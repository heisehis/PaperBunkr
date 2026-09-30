using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Paperbunkr.App.Services.Reader.Panels;
using SkiaSharp;

if (args.Length > 1 && args[0] == "--probe")
{
    using var probe = new Microsoft.ML.OnnxRuntime.InferenceSession(args[1]);
    foreach (var i in probe.InputMetadata) Console.WriteLine($"in  {i.Key}: {i.Value.ElementDataType} [{string.Join(",", i.Value.Dimensions)}]");
    foreach (var o in probe.OutputMetadata) Console.WriteLine($"out {o.Key}: {o.Value.ElementDataType} [{string.Join(",", o.Value.Dimensions)}]");
    return;
}

// Measures how often PanelDetector finds panels on a real comics folder. Read-only: opens archives for reading,
// writes only into --out. Usage: PanelHarness COMICS_ROOT [--issues N] [--pages K] [--seed S] [--out DIR]
string root = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : @"D:\Users\Ehis\Documents\Comics";
int issueCount = IntArg("--issues", 150);
int pagesPer = IntArg("--pages", 8);
int seed = IntArg("--seed", 1);
string outDir = StrArg("--out", Path.Combine(Path.GetTempPath(), "panel-harness"));
string detector = StrArg("--detector", "heuristic");
string modelPath = StrArg("--model", "");
int sampleEvery = IntArg("--sample-every", 8);
if (modelPath.Length > 0)
{
    PanelDetectionService.ModelPath = modelPath;
}

if (detector != "heuristic" && !PanelDetectionService.OnnxAvailable)
{
    Console.WriteLine($"ONNX model not available at {PanelDetectionService.ModelPath}");
    return;
}
Directory.CreateDirectory(outDir);

int IntArg(string name, int fallback) => int.TryParse(StrArg(name, ""), out var v) ? v : fallback;
string StrArg(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

// --tmp also reads unfinished "*.cbz.tmp" downloads (they are often complete zips; entries that fail are skipped).
var all = Directory.EnumerateFiles(root, "*.cbz", SearchOption.AllDirectories).ToList();
if (args.Contains("--tmp"))
{
    all.AddRange(Directory.EnumerateFiles(root, "*.cbz.tmp", SearchOption.AllDirectories));
}
Console.WriteLine($"{all.Count} cbz under {root}");

// Stratify by top-level folder (loose files at the root form their own group) so small folders like Manga are represented.
string only = StrArg("--only", "");
var groups = all.GroupBy(f => TopFolder(f)).Where(g => only.Length == 0 || g.Key.Equals(only, StringComparison.OrdinalIgnoreCase)).OrderBy(g => g.Key).ToList();
var rng = new Random(seed);
var sample = new List<string>();
int perGroup = Math.Max(1, issueCount / groups.Count);
foreach (var g in groups)
{
    sample.AddRange(g.OrderBy(_ => rng.Next()).Take(perGroup));
}

Console.WriteLine($"sampling {sample.Count} issues from {groups.Count} groups, up to {pagesPer} pages each -> {outDir}");

string TopFolder(string file)
{
    var rel = Path.GetRelativePath(root, file);
    int sep = rel.IndexOfAny(['\\', '/']);
    return sep < 0 ? "(root)" : rel[..sep];
}

var rows = new List<Row>();
var sw = new Stopwatch();
int overlays = 0;
foreach (var file in sample)
{
    string group = TopFolder(file);
    bool rtl = file.Contains("Manga", StringComparison.OrdinalIgnoreCase);
    try
    {
        using var zip = ZipFile.OpenRead(file);
        var entries = zip.Entries
            .Where(e => e.Length > 0 && IsImage(e.Name))
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
            // The exact pipeline the reader runs: model, then heuristic, then whole page; tall strips tiled.
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
                WriteOverlay(page, result, Path.Combine(outDir, $"{overlays:D3}_{Safe(group)}_{Safe(Path.GetFileNameWithoutExtension(file))}_{Safe(Path.GetFileNameWithoutExtension(entry.Name))}.png"));
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

static string Line(string name, List<Row> rs)
{
    if (rs.Count == 0)
    {
        return $"{name,-24}{0,7}";
    }

    int conf = rs.Count(r => r.Confident);
    return $"{name,-24}{rs.Count,7}{100.0 * conf / rs.Count,10:F1}%{rs.Count - conf,8}{rs.Count(r => r.Tall),7}{rs.Average(r => r.Ms),8:F1}";
}

static bool IsImage(string name) => name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
    || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray())[..Math.Min(30, s.Length)];

static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

static void WriteOverlay(SKBitmap page, PagePanels result, string path)
{
    if (page.Height > 3.0 * page.Width)
    {
        WriteTallOverlay(page, result, path);
        return;
    }

    double scale = Math.Min(1.0, 1100.0 / Math.Max(page.Width, page.Height));
    int w = Math.Max(1, (int)(page.Width * scale));
    int h = Math.Max(1, (int)(page.Height * scale));
    using var surface = SKSurface.Create(new SKImageInfo(w, h));
    var canvas = surface.Canvas;
    canvas.DrawBitmap(page, new SKRect(0, 0, w, h));
    using var stroke = new SKPaint { Color = result.Confident ? SKColors.LimeGreen : SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
    using var label = new SKPaint { Color = SKColors.Yellow, TextSize = 28, IsAntialias = true };
    int n = 1;
    foreach (var r in result.Rects)
    {
        var rect = new SKRect((float)(r.X * w), (float)(r.Y * h), (float)(r.Right * w), (float)(r.Bottom * h));
        canvas.DrawRect(rect, stroke);
        canvas.DrawText(n++.ToString(), rect.Left + 6, rect.Top + 30, label);
    }

    using var img = surface.Snapshot();
    using var data = img.Encode(SKEncodedImageFormat.Png, 80);
    using var fs = File.Create(path);
    data.SaveTo(fs);
}

// A webtoon strip is drawn at width 300 and cut into side-by-side columns of 1500 px so it can be read on one image.
static void WriteTallOverlay(SKBitmap page, PagePanels result, string path)
{
    const int colW = 300, colH = 1500, gap = 12;
    double scale = colW / (double)page.Width;
    int h = Math.Max(1, (int)(page.Height * scale));
    using var strip = SKSurface.Create(new SKImageInfo(colW, h));
    var sc = strip.Canvas;
    sc.DrawBitmap(page, new SKRect(0, 0, colW, h));
    using var stroke = new SKPaint { Color = result.Confident ? SKColors.LimeGreen : SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = 3 };
    using var label = new SKPaint { Color = SKColors.Yellow, TextSize = 24, IsAntialias = true };
    int n = 1;
    foreach (var r in result.Rects)
    {
        var rect = new SKRect((float)(r.X * colW), (float)(r.Y * h), (float)(r.Right * colW), (float)(r.Bottom * h));
        sc.DrawRect(rect, stroke);
        sc.DrawText(n++.ToString(), rect.Left + 4, rect.Top + 24, label);
    }

    using var strip2 = strip.Snapshot();
    int cols = (h + colH - 1) / colH;
    using var surface = SKSurface.Create(new SKImageInfo((cols * (colW + gap)) - gap, Math.Min(colH, h)));
    surface.Canvas.Clear(SKColors.DimGray);
    for (int c = 0; c < cols; c++)
    {
        var src = new SKRect(0, c * colH, colW, Math.Min(h, (c + 1) * colH));
        surface.Canvas.DrawImage(strip2, src, new SKRect(c * (colW + gap), 0, (c * (colW + gap)) + colW, src.Height));
    }

    using var img = surface.Snapshot();
    using var data = img.Encode(SKEncodedImageFormat.Png, 80);
    using var fs = File.Create(path);
    data.SaveTo(fs);
}

internal sealed record Row(string Group, string Issue, string Page, int Width, int Height, bool Tall, bool Confident, int Panels, double Ms, int HeuristicPanels);
