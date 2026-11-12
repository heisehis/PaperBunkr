using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.PanelHarness.Common;
using Paperbunkr.PanelHarness.Scoring;

namespace Paperbunkr.PanelHarness.Modes;

/// <summary>
/// Pre-draws boxes for the labeller (docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md, Pre-labeller): runs the app's own pipeline on
/// every image of a split and writes YOLO label files for X-AnyLabeling to import. An unconfident page gets an empty file (no panels).
/// Never overwrites an existing label file unless --force, so corrected labels are safe.
/// Usage: --prelabel SPLIT_DIR [--model PATH] [--labels DIR] [--manifest PATH] [--force]
/// </summary>
internal static class PrelabelMode
{
    public static int Run(Args a)
    {
        string? split = a.Required("--prelabel");
        if (split is null)
        {
            return 2;
        }

        string images = Path.Combine(split, "images");
        string labels = a.Str("--labels", Labels.DefaultDir(split));
        var manifest = Manifest.Load(a.Str("--manifest", Path.Combine(split, "..", "manifest.csv"))).Rows();
        bool force = a.Has("--force");
        Directory.CreateDirectory(labels);
        Labels.WriteClasses(labels);
        Console.WriteLine(PanelDetectionService.OnnxAvailable ? $"model: {PanelDetectionService.ModelPath}" : "warning: no model, pre-labelling with the heuristic");

        int written = 0, kept = 0, empty = 0;
        foreach (var file in Images.InFolder(images))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            string target = PageTruth.LabelPath(labels, id);
            if (File.Exists(target) && !force)
            {
                kept++;
                continue;
            }

            bool rtl = manifest.TryGetValue(id, out var row) && row.RightToLeft;
            using var page = Images.Load(file);
            if (page is null)
            {
                Console.WriteLine($"skip {id}: could not decode");
                continue;
            }

            var result = PanelDetectionService.Detect(page, rtl);
            YoloLabels.Write(target, result.Confident ? result.Rects : []);
            written++;
            if (!result.Confident)
            {
                empty++;
            }
        }

        Console.WriteLine($"{written} label files written ({empty} empty: no confident panels), {kept} existing kept -> {labels}");
        return 0;
    }
}
