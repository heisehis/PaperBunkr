using System.Globalization;
using System.Text;
using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.PanelHarness.Common;
using Paperbunkr.PanelHarness.Scoring;
using SkiaSharp;

namespace Paperbunkr.PanelHarness.Modes;

/// <summary>
/// Records the true reading order of the test pages (docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md, Q13a).
/// Step 1, <c>--order-sheet SPLIT_DIR</c>: numbers each page's corrected boxes in the app's order (<see cref="PanelOrdering"/>), draws them into
/// <c>order-review/&lt;id&gt;.png</c>, and writes <c>order.csv</c> with every page marked <c>ok</c>. Edit a wrong row's <c>order</c> to the right sequence of
/// overlay numbers (e.g. <c>1 3 2 4</c>), or its <c>direction</c> to flip the page between ltr and rtl.
/// Step 2, <c>--order-sheet SPLIT_DIR --apply</c>: writes one <c>&lt;id&gt;.order</c> per page and copies changed directions into the manifest.
/// An existing order.csv is never overwritten without --force, so typed corrections are safe.
/// </summary>
internal static class OrderSheetMode
{
    private const string Header = "id,sheet_direction,direction,boxes,order";

    public static int Run(Args a)
    {
        string? split = a.Required("--order-sheet");
        if (split is null)
        {
            return 2;
        }

        string labels = a.Str("--labels", Labels.DefaultDir(split));
        string manifestPath = a.Str("--manifest", Path.Combine(split, "..", "manifest.csv"));
        string sheet = Path.Combine(split, "order.csv");
        return a.Has("--apply") ? Apply(sheet, labels, manifestPath) : MakeSheet(split, labels, manifestPath, sheet, a.Has("--force"));
    }

    /// <summary>The true boxes' indices in the order the app would read them.</summary>
    public static List<int> AppOrder(IReadOnlyList<PanelRect> boxes, bool rightToLeft)
    {
        var used = new bool[boxes.Count];
        var order = new List<int>();
        foreach (var r in PanelOrdering.Sort(boxes, rightToLeft))
        {
            int i = Enumerable.Range(0, boxes.Count).First(k => !used[k] && boxes[k].Equals(r));
            used[i] = true;
            order.Add(i);
        }

        return order;
    }

    /// <summary>
    /// Turns an order.csv entry into true box indices: <c>ok</c> keeps the app order; otherwise each overlay number (1-based, as drawn from
    /// <paramref name="sheetOrder"/>) names a box. Null when the entry is not a permutation of the page's boxes.
    /// </summary>
    public static List<int>? Resolve(string entry, IReadOnlyList<int> sheetOrder)
    {
        if (entry.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            return sheetOrder.ToList();
        }

        var numbers = new List<int>();
        foreach (var part in entry.Split([' ', ',', ';', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1 || n > sheetOrder.Count)
            {
                return null;
            }

            numbers.Add(n);
        }

        if (numbers.Count != sheetOrder.Count || numbers.Distinct().Count() != numbers.Count)
        {
            return null;
        }

        return numbers.Select(n => sheetOrder[n - 1]).ToList();
    }

    private static int MakeSheet(string split, string labels, string manifestPath, string sheet, bool force)
    {
        if (File.Exists(sheet) && !force)
        {
            Console.WriteLine($"{sheet} already exists (it may hold your corrections); pass --force to rebuild it, or --apply to use it");
            return 1;
        }

        var manifest = Manifest.Load(manifestPath).Rows();
        string review = Path.Combine(split, "order-review");
        Directory.CreateDirectory(review);
        var csv = new StringBuilder(Header + "\n");
        int pages = 0, unlabelled = 0;
        foreach (var file in Images.InFolder(Path.Combine(split, "images")))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            var truth = PageTruth.Load(labels, id);
            if (truth is null)
            {
                unlabelled++;
                continue;
            }

            string direction = manifest.TryGetValue(id, out var row) ? row.Direction : "ltr";
            var order = AppOrder(truth.Boxes, direction == "rtl");
            using var page = Images.Load(file);
            if (page is not null)
            {
                Overlay.Write(page, order.Select((box, n) => new OverlayBox(truth.Boxes[box], SKColors.DeepSkyBlue, (n + 1).ToString())).ToList(), Path.Combine(review, id + ".png"));
            }

            csv.Append(id).Append(',').Append(direction).Append(',').Append(direction).Append(',').Append(truth.Boxes.Count).Append(",ok\n");
            pages++;
        }

        File.WriteAllText(sheet, csv.ToString());
        Console.WriteLine($"{pages} pages -> {review} and {sheet}{(unlabelled > 0 ? $"; {unlabelled} pages have no labels yet and were left out" : "")}");
        Console.WriteLine("Look through order-review; where the numbers are wrong, type the right sequence in that row's 'order' column (e.g. 1 3 2 4). Then run with --apply.");
        return 0;
    }

    private static int Apply(string sheet, string labels, string manifestPath)
    {
        if (!File.Exists(sheet))
        {
            Console.WriteLine($"no {sheet}; run --order-sheet first");
            return 1;
        }

        var manifest = Manifest.Load(manifestPath);
        var rows = Manifest.ParseCsv(Manifest.ReadShared(sheet));
        if (rows.Count == 0 || string.Join(',', rows[0]) != Header)
        {
            Console.WriteLine($"{sheet} does not start with the header '{Header}'");
            return 1;
        }

        int applied = 0, flipped = 0, pending = 0;
        var rejected = new List<string>();
        foreach (var r in rows.Skip(1).Where(r => r.Count >= 5))
        {
            string id = r[0], sheetDirection = r[1].Trim(), direction = r[2].Trim().ToLowerInvariant(), entry = r[4];

            // "todo": the order shown is wrong and the right one isn't typed yet. No .order is written (an old one is removed), so the
            // page is scored on boxes only until it is.
            if (entry.Trim().Equals("todo", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(PageTruth.OrderPath(labels, id));
                pending++;
                continue;
            }
            if (direction is not ("ltr" or "rtl"))
            {
                rejected.Add($"{id}: direction '{r[2]}' is not ltr or rtl");
                continue;
            }

            string labelFile = PageTruth.LabelPath(labels, id);
            if (!File.Exists(labelFile))
            {
                rejected.Add($"{id}: no label file");
                continue;
            }

            var boxes = YoloLabels.Read(labelFile);
            bool isOk = entry.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase);

            // "ok" means the app's order in the page's (possibly flipped) direction; typed numbers refer to the overlay that was drawn.
            var reference = AppOrder(boxes, (isOk ? direction : sheetDirection) == "rtl");
            var order = boxes.Count.ToString(CultureInfo.InvariantCulture) == r[3].Trim() ? Resolve(entry, reference) : null;
            if (order is null)
            {
                rejected.Add($"{id}: '{entry}' is not an order of its {boxes.Count} boxes (or the labels changed since the sheet was made)");
                continue;
            }

            PageTruth.WriteOrder(labels, id, order);
            applied++;
            if (direction != sheetDirection)
            {
                manifest.SetDirection(id, direction);
                flipped++;
            }
        }

        if (flipped > 0)
        {
            manifest.Save(manifestPath);
        }

        Console.WriteLine($"{applied} orders written, {pending} still 'todo', {flipped} directions changed in the manifest, {rejected.Count} rejected");
        foreach (var line in rejected)
        {
            Console.WriteLine("  " + line);
        }

        return rejected.Count == 0 ? 0 : 1;
    }
}
