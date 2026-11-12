using System.Globalization;
using Paperbunkr.App.Services.Reader.Panels;

namespace Paperbunkr.PanelHarness.Scoring;

/// <summary>
/// A labelled page: its true panel boxes (<c>&lt;id&gt;.txt</c>, YOLO) and, for test pages, its true reading order (<c>&lt;id&gt;.order</c>: one line of
/// 0-based box indices, i.e. line numbers in the label file, in reading order). The order file is written by <c>--order-sheet --apply</c>.
/// </summary>
internal sealed record PageTruth(List<PanelRect> Boxes, List<int>? Order)
{
    public static string LabelPath(string labelsDir, string id) => Path.Combine(labelsDir, id + ".txt");

    public static string OrderPath(string labelsDir, string id) => Path.Combine(labelsDir, id + ".order");

    /// <summary>Null when the page has no label file yet (not labelled).</summary>
    public static PageTruth? Load(string labelsDir, string id)
    {
        string labels = LabelPath(labelsDir, id);
        if (!File.Exists(labels))
        {
            return null;
        }

        var boxes = YoloLabels.Read(labels);
        string orderFile = OrderPath(labelsDir, id);
        List<int>? order = File.Exists(orderFile) ? ParseOrder(File.ReadAllText(orderFile)) : null;
        if (order is not null && !IsPermutation(order, boxes.Count))
        {
            throw new InvalidDataException($"{orderFile} does not list each of the {boxes.Count} boxes exactly once; the labels changed after the order was recorded. Re-run --order-sheet.");
        }

        return new PageTruth(boxes, order);
    }

    public static List<int> ParseOrder(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList();

    public static void WriteOrder(string labelsDir, string id, IEnumerable<int> order) =>
        File.WriteAllText(OrderPath(labelsDir, id), string.Join(' ', order) + "\n");

    public static bool IsPermutation(IReadOnlyList<int> order, int count) =>
        order.Count == count && order.All(i => i >= 0 && i < count) && order.Distinct().Count() == count;
}
