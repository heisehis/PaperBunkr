using System.Diagnostics;
using System.Globalization;
using System.Text;
using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.PanelHarness.Common;
using Paperbunkr.PanelHarness.Scoring;
using SkiaSharp;

namespace Paperbunkr.PanelHarness.Modes;

/// <summary>
/// Scores the app's panel pipeline against hand-labelled pages (docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md, Scorer).
/// Usage: --score SPLIT_DIR [--model PATH] [--label NAME] [--labels DIR] [--manifest PATH] [--webtoon-model] [--boxes-only] [--overlays]
/// SPLIT_DIR holds <c>images/</c>; labels default to SPLIT_DIR/images/labels (where X-AnyLabeling exports YOLO); the manifest defaults to SPLIT_DIR/../manifest.csv.
/// Writes DATA/scores/NAME.csv (one row per page) and NAME.md (the table for the design's Results). Read-only apart from that.
/// </summary>
internal static class ScoreMode
{
    private sealed record Scored(ManifestRow Page, PageScore Score, double Ms, string Variant);

    public static int Run(Args a)
    {
        string? split = a.Required("--score");
        if (split is null)
        {
            return 2;
        }

        string images = Path.Combine(split, "images");
        string labels = a.Str("--labels", Labels.DefaultDir(split));
        string manifestPath = a.Str("--manifest", Path.Combine(split, "..", "manifest.csv"));
        string label = a.Str("--label", "score");
        bool boxesOnly = a.Has("--boxes-only");
        bool webtoonModel = a.Has("--webtoon-model");
        string scoresDir = Path.Combine(split, "..", "scores");
        Directory.CreateDirectory(scoresDir);
        string? overlayDir = a.Has("--overlays") ? Path.Combine(scoresDir, label + "-wrong") : null;
        if (overlayDir is not null)
        {
            Directory.CreateDirectory(overlayDir);
        }

        var manifest = Manifest.Load(manifestPath).Rows();
        if (!PanelDetectionService.OnnxAvailable)
        {
            Console.WriteLine($"warning: no model at {PanelDetectionService.ModelPath}; scoring the heuristic only");
        }

        using var tiler = webtoonModel && File.Exists(PanelDetectionService.ModelPath) ? new OnnxPanelDetector(PanelDetectionService.ModelPath) : null;

        var results = new List<Scored>();
        var unlabelled = new List<string>();
        var noOrder = new List<string>();
        bool warmedUp = false;
        foreach (var file in Images.InFolder(images))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            if (!manifest.TryGetValue(id, out var row))
            {
                Console.WriteLine($"skip {id}: not in the manifest");
                continue;
            }

            var truth = PageTruth.Load(labels, id);
            if (truth is null)
            {
                unlabelled.Add(id);
                continue;
            }

            if (truth.Order is null)
            {
                // Still scored on its boxes; it just doesn't count towards page-right or order-right.
                noOrder.Add(id);
            }

            using var page = Images.Load(file);
            if (page is null)
            {
                Console.WriteLine($"skip {id}: could not decode");
                continue;
            }

            if (!warmedUp)
            {
                // The first call loads the model (about a second); keep it out of the timings.
                PanelDetectionService.Detect(page, row.RightToLeft);
                warmedUp = true;
            }

            var sw = Stopwatch.StartNew();
            var result = PanelDetectionService.Detect(page, row.RightToLeft);
            sw.Stop();
            var score = PanelMatcher.Score(truth.Boxes, boxesOnly ? null : truth.Order, result);
            results.Add(new Scored(row, score, sw.Elapsed.TotalMilliseconds, "pipeline"));
            if (overlayDir is not null && !(boxesOnly ? score.BoxesRight : score.PageRight))
            {
                WriteWrongOverlay(page, truth, result, Path.Combine(overlayDir, id + ".png"));
            }

            if (tiler is not null && row.Tall)
            {
                sw.Restart();
                var tiled = StripTiler.Detect(tiler, page, row.RightToLeft);
                sw.Stop();
                results.Add(new Scored(row, PanelMatcher.Score(truth.Boxes, boxesOnly ? null : truth.Order, tiled), sw.Elapsed.TotalMilliseconds, "model-tiled"));
            }
        }

        if (unlabelled.Count > 0)
        {
            Console.WriteLine($"{unlabelled.Count} pages have no labels yet (skipped): {string.Join(' ', unlabelled.Take(10))}{(unlabelled.Count > 10 ? " ..." : "")}");
        }

        if (noOrder.Count > 0 && !boxesOnly)
        {
            Console.WriteLine($"{noOrder.Count} pages have no recorded order (scored on boxes only; record it with --order-sheet --apply): {string.Join(' ', noOrder.Take(10))}{(noOrder.Count > 10 ? " ..." : "")}");
        }

        if (results.Count == 0)
        {
            Console.WriteLine("nothing scored");
            return 1;
        }

        File.WriteAllText(Path.Combine(scoresDir, label + ".csv"), Csv(results));
        string table = Table(label, results, boxesOnly);
        File.WriteAllText(Path.Combine(scoresDir, label + ".md"), table);
        Console.WriteLine(table);
        return 0;
    }

    private static string Csv(List<Scored> results)
    {
        var sb = new StringBuilder("id,group,tall,variant,truth,detected,matched,missed,extra,boxes_right,order_right,page_right,ms\n");
        foreach (var r in results)
        {
            var s = r.Score;
            sb.AppendLine(string.Join(',', r.Page.Id, r.Page.Group, r.Page.Tall, r.Variant, s.Truth, s.Detected, s.Matched, s.Missed, s.Extra,
                s.BoxesRight, s.OrderRight?.ToString() ?? "", s.PageRight, r.Ms.ToString("F1", CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    /// <summary>The Markdown table: one row per scoring group, plus a row for the model on tiled strips when it was run.</summary>
    public static string Table(string label, IReadOnlyList<(string Group, PageScore Score, double Ms)> rows, bool boxesOnly)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Score `{label}`, {DateTime.Now:yyyy-MM-dd}, model `{Path.GetFileName(PanelDetectionService.ModelPath)}`{(boxesOnly ? ", boxes only (order not scored)" : "")}.");
        sb.AppendLine();
        sb.AppendLine("| Group | Pages | Page right | Boxes right | Order right (of boxes-right) | Panel precision | Panel recall | Median ms | p95 ms |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var g in rows.GroupBy(r => r.Group).OrderBy(g => Rank(g.Key)))
        {
            var list = g.ToList();
            int n = list.Count;
            int boxesRight = list.Count(r => r.Score.BoxesRight);
            int withOrder = list.Count(r => r.Score.OrderRight is not null);
            int boxesRightWithOrder = list.Count(r => r.Score.BoxesRight && r.Score.OrderRight is not null);
            int orderRight = list.Count(r => r.Score.BoxesRight && r.Score.OrderRight == true);
            int matched = list.Sum(r => r.Score.Matched);
            int truth = list.Sum(r => r.Score.Truth);
            int extra = list.Sum(r => r.Score.Extra);
            double precision = matched + extra == 0 ? 1 : matched / (double)(matched + extra);
            double recall = truth == 0 ? 1 : matched / (double)truth;
            var ms = list.Select(r => r.Ms).OrderBy(v => v).ToList();
            // Pages whose true order isn't recorded yet count for boxes only, so page-right and order-right are over the pages that have one.
            string page = boxesOnly || withOrder == 0 ? "-" : Pct(list.Count(r => r.Score.PageRight), withOrder) + (withOrder < n ? $" (of {withOrder})" : "");
            string order = boxesOnly || boxesRightWithOrder == 0 ? "-" : $"{Pct(orderRight, boxesRightWithOrder)} ({orderRight}/{boxesRightWithOrder})";
            sb.AppendLine($"| {g.Key} | {n} | {page} | {Pct(boxesRight, n)} | {order} | {Pct(precision)} | {Pct(recall)} | {Percentile(ms, 0.5):F0} | {Percentile(ms, 0.95):F0} |");
        }

        return sb.ToString();
    }

    private static string Table(string label, List<Scored> results, bool boxesOnly) =>
        Table(label, results.Select(r => (r.Variant == "pipeline" ? r.Page.Group : $"{r.Page.Group} (model on tiles)", r.Score, r.Ms)).ToList(), boxesOnly);

    private static int Rank(string group) => group.StartsWith("Western") ? 0 : group.StartsWith("Manga") ? 1 : 2;

    private static string Pct(int part, int whole) => whole == 0 ? "-" : Pct(part / (double)whole);

    private static string Pct(double share) => (share * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";

    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        double rank = p * (sorted.Count - 1);
        int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
        return sorted[lo] + ((sorted[hi] - sorted[lo]) * (rank - lo));
    }

    /// <summary>True boxes in green (numbered in true order), detected boxes in red (numbered in detected order).</summary>
    private static void WriteWrongOverlay(SKBitmap page, PageTruth truth, PagePanels result, string path)
    {
        var boxes = new List<OverlayBox>();
        var order = truth.Order ?? Enumerable.Range(0, truth.Boxes.Count).ToList();
        for (int i = 0; i < order.Count; i++)
        {
            boxes.Add(new OverlayBox(truth.Boxes[order[i]], SKColors.LimeGreen, $"T{i + 1}"));
        }

        for (int i = 0; i < result.Rects.Count; i++)
        {
            boxes.Add(new OverlayBox(result.Rects[i], SKColors.Red, $"D{i + 1}"));
        }

        Overlay.Write(page, boxes, path);
    }
}
