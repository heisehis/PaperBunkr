using Paperbunkr.App.Services.Reader.Panels;

namespace Paperbunkr.PanelHarness.Scoring;

/// <summary>How one detected page compares with its hand-labelled truth (docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md, Scorer).</summary>
/// <param name="OrderRight">Whether the detected order equals the true order. Only meaningful when <paramref name="BoxesRight"/>; null when the true order is unknown.</param>
internal sealed record PageScore(int Truth, int Detected, int Matched, int Missed, int Extra, bool BoxesRight, bool? OrderRight)
{
    public bool PageRight => BoxesRight && OrderRight == true;
}

internal static class PanelMatcher
{
    public const double IouThreshold = 0.5;

    public static double Iou(PanelRect a, PanelRect b)
    {
        double w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        double h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        if (w <= 0 || h <= 0)
        {
            return 0;
        }

        double inter = w * h;
        double union = (a.Width * a.Height) + (b.Width * b.Height) - inter;
        return union <= 0 ? 0 : inter / union;
    }

    /// <summary>
    /// Greedy one-to-one matching, highest overlap first. Returns, for each detected box, the index of the true box it matched, or -1 for an extra box.
    /// </summary>
    public static int[] Match(IReadOnlyList<PanelRect> truth, IReadOnlyList<PanelRect> detected, double threshold = IouThreshold)
    {
        var pairs = new List<(double Iou, int T, int D)>();
        for (int t = 0; t < truth.Count; t++)
        {
            for (int d = 0; d < detected.Count; d++)
            {
                double iou = Iou(truth[t], detected[d]);
                if (iou >= threshold)
                {
                    pairs.Add((iou, t, d));
                }
            }
        }

        var detectedToTruth = Enumerable.Repeat(-1, detected.Count).ToArray();
        var truthUsed = new bool[truth.Count];
        foreach (var (_, t, d) in pairs.OrderByDescending(p => p.Iou))
        {
            if (detectedToTruth[d] < 0 && !truthUsed[t])
            {
                detectedToTruth[d] = t;
                truthUsed[t] = true;
            }
        }

        return detectedToTruth;
    }

    /// <summary>
    /// Scores one page. <paramref name="trueOrder"/> lists the true boxes' indices in reading order (null when unknown).
    /// A page with no true panels (a text page, an ad) is right only when the pipeline gave up and shows the whole page.
    /// An unconfident result is scored as its whole-page box, so a splash page labelled as one box around the art is right when the pipeline falls back.
    /// </summary>
    public static PageScore Score(IReadOnlyList<PanelRect> truth, IReadOnlyList<int>? trueOrder, PagePanels result)
    {
        if (truth.Count == 0)
        {
            bool right = !result.Confident;
            return new PageScore(0, result.Confident ? result.Rects.Count : 0, 0, 0, result.Confident ? result.Rects.Count : 0, right, true);
        }

        var detected = result.Rects;
        var map = Match(truth, detected);
        int matched = map.Count(t => t >= 0);
        int extra = map.Length - matched;
        int missed = truth.Count - matched;
        bool boxesRight = missed == 0 && extra == 0;

        bool? orderRight = null;
        if (trueOrder is not null)
        {
            orderRight = boxesRight && map.SequenceEqual(trueOrder);
        }

        return new PageScore(truth.Count, detected.Count, matched, missed, extra, boxesRight, orderRight);
    }
}
