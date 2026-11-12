using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.PanelHarness.Scoring;

namespace Paperbunkr.PanelHarness.Modes;

/// <summary>
/// Checks the scoring rules on hand-built cases (the harness has no test project). Exits non-zero when any case fails.
/// Run after any change under Scoring/: PanelHarness --selftest
/// </summary>
internal static class SelfTest
{
    private static int _failed;
    private static int _passed;

    public static int Run()
    {
        var left = new PanelRect(0.05, 0.05, 0.4, 0.4);
        var right = new PanelRect(0.55, 0.05, 0.4, 0.4);
        var bottom = new PanelRect(0.05, 0.55, 0.9, 0.4);
        List<PanelRect> truth = [left, right, bottom];
        List<int> order = [0, 1, 2];

        var exact = PanelMatcher.Score(truth, order, new PagePanels([left, right, bottom], true));
        Check("exact match is page-right", exact.PageRight && exact.Matched == 3 && exact.Extra == 0 && exact.Missed == 0);

        var swapped = PanelMatcher.Score(truth, order, new PagePanels([right, left, bottom], true));
        Check("swapped order: boxes right, order wrong", swapped.BoxesRight && swapped.OrderRight == false && !swapped.PageRight);

        var missed = PanelMatcher.Score(truth, order, new PagePanels([left, right], true));
        Check("missed box", !missed.BoxesRight && missed.Missed == 1 && missed.Extra == 0 && missed.OrderRight == false);

        var extra = PanelMatcher.Score(truth, order, new PagePanels([left, right, bottom, new PanelRect(0.4, 0.4, 0.2, 0.2)], true));
        Check("extra box", !extra.BoxesRight && extra.Extra == 1 && extra.Missed == 0);

        // IoU against a 0.4 x 0.4 box: shifting it right by s keeps an overlap of (0.4 - s) x 0.4, so IoU = (0.4 - s) / (0.4 + s).
        var justOver = new PanelRect(left.X + 0.1305, left.Y, 0.4, 0.4); // IoU ~0.508
        var justUnder = new PanelRect(left.X + 0.1395, left.Y, 0.4, 0.4); // IoU ~0.483
        Check("IoU just over 0.5 matches", PanelMatcher.Iou(left, justOver) > 0.5 && PanelMatcher.Match([left], [justOver])[0] == 0);
        Check("IoU just under 0.5 does not match", PanelMatcher.Iou(left, justUnder) < 0.5 && PanelMatcher.Match([left], [justUnder])[0] == -1);

        var noPanelsWhole = PanelMatcher.Score([], [], PagePanels.Whole);
        Check("no-panel page with whole-page fallback is right", noPanelsWhole.PageRight);

        var noPanelsBoxed = PanelMatcher.Score([], [], new PagePanels([left], true));
        Check("no-panel page with a confident box is wrong", !noPanelsBoxed.PageRight && noPanelsBoxed.Extra == 1);

        var splash = new PanelRect(0.02, 0.02, 0.96, 0.96);
        Check("splash labelled as one box is right on whole-page fallback", PanelMatcher.Score([splash], [0], PagePanels.Whole).PageRight);

        Check("unknown order leaves page-right false", PanelMatcher.Score(truth, null, new PagePanels([left, right, bottom], true)) is { BoxesRight: true, OrderRight: null, PageRight: false });

        // Two tile halves of one tall panel, overlapping in the seam band, merge into one; a panel beside it in another column does not.
        var top = new PanelRect(0.1, 0.10, 0.5, 0.20);
        var lower = new PanelRect(0.12, 0.25, 0.48, 0.15);
        var aside = new PanelRect(0.7, 0.12, 0.25, 0.2);
        var merged = StripTiler.Merge([top, lower, aside]);
        Check("tile halves merge, other column kept", merged.Count == 2 && merged.Any(m => Math.Abs(m.Y - 0.10) < 1e-9 && Math.Abs(m.Bottom - 0.40) < 1e-9));

        var tiles = StripTiler.Tiles(800, 6000);
        Check("tiles cover the strip with overlap", tiles[0].Top == 0 && tiles[^1].Top + tiles[^1].Height == 6000 && tiles.Zip(tiles.Skip(1)).All(p => p.Second.Top < p.First.Top + p.First.Height));

        Check("order permutation check", PageTruth.IsPermutation([2, 0, 1], 3) && !PageTruth.IsPermutation([0, 0, 1], 3) && !PageTruth.IsPermutation([0, 1], 3));

        string dir = Path.Combine(Path.GetTempPath(), $"panel-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            YoloLabels.Write(Path.Combine(dir, "p.txt"), truth);
            var back = YoloLabels.Read(Path.Combine(dir, "p.txt"));
            Check("YOLO round trip", back.Count == 3 && back.Zip(truth).All(p => PanelMatcher.Iou(p.First, p.Second) > 0.999));

            File.WriteAllText(Path.Combine(dir, "q.txt"), "1 0.5 0.5 0.2 0.2\n0 0.5 0.5 0.4 0.4\n");
            Check("non-panel classes ignored", YoloLabels.Read(Path.Combine(dir, "q.txt")).Count == 1);

            ApplyOrderCases(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        Console.WriteLine($"selftest: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    /// <summary>The order sheet's typed sequences: overlay numbers (1-based, in the app's order) become true box indices.</summary>
    private static void ApplyOrderCases(string dir)
    {
        List<int> appOrder = [2, 0, 1]; // the app reads box 2 first, then 0, then 1
        Check("'ok' keeps the app order", OrderSheetMode.Resolve("ok", appOrder) is [2, 0, 1]);
        Check("typed sequence maps overlay numbers to boxes", OrderSheetMode.Resolve("2 1 3", appOrder) is [0, 2, 1]);
        Check("a sequence that is not a permutation is rejected", OrderSheetMode.Resolve("1 1 3", appOrder) is null && OrderSheetMode.Resolve("1 2", appOrder) is null);
        _ = dir;
    }

    private static void Check(string name, bool ok)
    {
        if (ok)
        {
            _passed++;
            return;
        }

        _failed++;
        Console.WriteLine($"FAIL: {name}");
    }
}
