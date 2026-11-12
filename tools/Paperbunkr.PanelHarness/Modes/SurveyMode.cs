using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.PanelHarness.Common;
using SkiaSharp;

namespace Paperbunkr.PanelHarness.Modes;

/// <summary>
/// Measures how often the pipeline finds panels on a real comics folder (docs/superpowers/specs/2026-09-28-guided-view-detection-upgrade-design.md).
/// Read-only: opens archives for reading, writes only into --out.
/// Usage: PanelHarness [COMICS_ROOT] [--issues N] [--pages K] [--seed S] [--out DIR] [--detector heuristic|onnx|both] [--model PATH] [--tmp] [--only FOLDER]
/// </summary>
internal static class SurveyMode
{
    public static int Run(Args a)
    {
        string root = a.Raw.Length > 0 && !a.Raw[0].StartsWith("--", StringComparison.Ordinal) ? a.Raw[0] : @"D:\Users\Ehis\Documents\Comics";
        int issueCount = a.Int("--issues", 150);
        int pagesPer = a.Int("--pages", 8);
        int seed = a.Int("--seed", 1);
        string outDir = a.Str("--out", Path.Combine(Path.GetTempPath(), "panel-harness"));
        string detector = a.Str("--detector", "heuristic");
        int sampleEvery = a.Int("--sample-every", 8);
        if (detector != "heuristic" && !PanelDetectionService.OnnxAvailable)
        {
            Console.WriteLine($"ONNX model not available at {PanelDetectionService.ModelPath}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        // --tmp also reads unfinished "*.cbz.tmp" downloads (they are often complete zips; entries that fail are skipped).
        var all = Directory.EnumerateFiles(root, "*.cbz", SearchOption.AllDirectories).ToList();
        if (a.Has("--tmp"))
        {
            all.AddRange(Directory.EnumerateFiles(root, "*.cbz.tmp", SearchOption.AllDirectories));
        }

        Console.WriteLine($"{all.Count} cbz under {root}");

        // Stratify by top-level folder (loose files at the root form their own group) so small folders like Manga are represented.
        string only = a.Str("--only", "");
        var groups = all.GroupBy(f => TopFolder(root, f)).Where(g => only.Length == 0 || g.Key.Equals(only, StringComparison.OrdinalIgnoreCase)).OrderBy(g => g.Key).ToList();
        if (groups.Count == 0)
        {
            Console.WriteLine("nothing to sample");
            return 1;
        }

        var rng = new Random(seed);
        var sample = new List<string>();
        int perGroup = Math.Max(1, issueCount / groups.Count);
        foreach (var g in groups)
        {
            sample.AddRange(g.OrderBy(_ => rng.Next()).Take(perGroup));
        }

        Console.WriteLine($"sampling {sample.Count} issues from {groups.Count} groups, up to {pagesPer} pages each -> {outDir}");

        var rows = new List<Row>();
        var sw = new Stopwatch();
        int overlays = 0;
        foreach (var file in sample)
        {
            string group = TopFolder(root, file);
            bool rtl = file.Contains("Manga", StringComparison.OrdinalIgnoreCase);
            try
            {
                using var zip = ZipFile.OpenRead(file);
                var entries = zip.Entries
                    .Where(e => e.Length > 0 && Images.IsImage(e.Name))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (entries.Count < 6)
                {
                    continue;
                }

                var inner = entries.Skip(2).Take(entries.Count - 4).ToList();
                var picks = Enumerable.Range(0, Math.Min(pagesPer, inner.Count))
                    .Select(i => inner[(int)((long)i * inner.Count / Math.Min(pagesPer, inner.Count))])
                    .Distinct()
                    .ToList();
                foreach (var entry in picks)
                {
                    using var ms = new MemoryStream();
                    using (var s = entry.Open())
                    {
                        s.CopyTo(ms);
                    }

                    ms.Position = 0;
                    using var page = SKBitmap.Decode(ms);
                    if (page is null)
                    {
                        continue;
                    }

                    sw.Restart();
                    // The exact pipeline the reader runs: model, then heuristic, then whole page; tall strips take the strip detector.
                    PanelDetectionService.UseOnnx = detector != "heuristic";
                    var result = PanelDetectionService.Detect(page, rtl);
                    sw.Stop();
                    int heuristicCount = -1;
                    if (detector == "both")
                    {
                        PanelDetectionService.UseOnnx = false;
                        heuristicCount = PanelDetectionService.Detect(page, rtl) is { Confident: true } hp ? hp.Count : 0;
                        PanelDetectionService.UseOnnx = true;
                    }

                    bool tall = page.Height > 3.0 * page.Width;
                    var row = new Row(group, Path.GetFileName(file), entry.FullName, page.Width, page.Height, tall, result.Confident, result.Count, sw.Elapsed.TotalMilliseconds, heuristicCount);
                    rows.Add(row);

                    if ((!result.Confident || result.Count > 12 || rows.Count % sampleEvery == 0) && overlays < 400)
                    {
                        Overlay.WriteResult(page, result, Path.Combine(outDir, $"{overlays:D3}_{Safe(group)}_{Safe(Path.GetFileNameWithoutExtension(file))}_{Safe(Path.GetFileNameWithoutExtension(entry.Name))}.png"));
                        overlays++;
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"skip {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        var csv = new StringBuilder("group,issue,page,width,height,tall,confident,panels,ms\n");
        foreach (var r in rows)
        {
            csv.AppendLine(string.Join(',', Q(r.Group), Q(r.Issue), Q(r.Page), r.Width, r.Height, r.Tall, r.Confident, r.Panels, r.Ms.ToString("F1", CultureInfo.InvariantCulture), r.HeuristicPanels));
        }

        File.WriteAllText(Path.Combine(outDir, "results.csv"), csv.ToString());

        Console.WriteLine();
        Console.WriteLine($"{"group",-24}{"pages",7}{"confident",11}{"whole",8}{"tall",7}{"avg ms",8}");
        foreach (var g in rows.GroupBy(r => r.Group).OrderBy(g => g.Key))
        {
            Console.WriteLine(Line(g.Key, g.ToList()));
        }

        Console.WriteLine(Line("ALL", rows));
        Console.WriteLine(Line("ALL non-tall", rows.Where(r => !r.Tall).ToList()));
        Console.WriteLine(Line("ALL tall", rows.Where(r => r.Tall).ToList()));
        Console.WriteLine();
        Console.WriteLine("panel count histogram (confident pages): " + string.Join("  ", rows.Where(r => r.Confident).GroupBy(r => r.Panels).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
        if (detector == "both")
        {
            Console.WriteLine($"same panel count as heuristic: {rows.Count(r => r.HeuristicPanels > 0 && r.HeuristicPanels == r.Panels)} of {rows.Count(r => r.HeuristicPanels > 0)} pages where the heuristic was confident");
        }

        Console.WriteLine($"overlays written: {overlays}; csv: {Path.Combine(outDir, "results.csv")}");
        return 0;
    }

    private static string TopFolder(string root, string file)
    {
        var rel = Path.GetRelativePath(root, file);
        int sep = rel.IndexOfAny(['\\', '/']);
        return sep < 0 ? "(root)" : rel[..sep];
    }

    private static string Line(string name, List<Row> rs)
    {
        if (rs.Count == 0)
        {
            return $"{name,-24}{0,7}";
        }

        int conf = rs.Count(r => r.Confident);
        return $"{name,-24}{rs.Count,7}{100.0 * conf / rs.Count,10:F1}%{rs.Count - conf,8}{rs.Count(r => r.Tall),7}{rs.Average(r => r.Ms),8:F1}";
    }

    private static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray())[..Math.Min(30, s.Length)];

    private static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    private sealed record Row(string Group, string Issue, string Page, int Width, int Height, bool Tall, bool Confident, int Panels, double Ms, int HeuristicPanels);
}
