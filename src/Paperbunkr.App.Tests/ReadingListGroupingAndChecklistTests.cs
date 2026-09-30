using System.Text;
using Paperbunkr.App.Services;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="ReadingListGrouping"/> (docs/superpowers/specs/2026-09-28-reading-lists-build-from-events-design.md §1) and
/// <see cref="ReadingListChecklistPdf"/> (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §6).
/// </summary>
public class ReadingListGroupingAndChecklistTests
{
    [Fact]
    public void Runs_GroupOnlyConsecutiveLabels()
    {
        var items = new[] { ("a", "X"), ("b", "X"), ("c", "Y"), ("d", "X"), ("e", null), ("f", ""), ("g", "Y") };
        var runs = ReadingListGrouping.Runs(items, i => i.Item2);

        Assert.Equal(new[] { "X", "Y", "X", "", "Y" }, runs.Select(r => r.Label));
        Assert.Equal(new[] { "ab", "c", "d", "ef", "g" }, runs.Select(r => string.Concat(r.Items.Select(i => i.Item1))));
        Assert.Empty(ReadingListGrouping.Runs(Array.Empty<(string, string?)>(), i => i.Item2));
    }

    private static ChecklistModel Model(int rows, Func<int, string?>? group = null, Func<int, string?>? note = null) =>
        new("Crisis", rows, 0, 0, new DateTime(2026, 9, 28),
            Enumerable.Range(1, rows).Select(i => new ChecklistRow(i, group?.Invoke(i), $"Crisis #{i}", 1985, i % 2 == 0, i % 5 != 0, note?.Invoke(i))).ToList());

    [Fact]
    public void Pdf_IsAPdf_AndSpansSeveralPagesForAHundredRows()
    {
        using var stream = new MemoryStream();
        ReadingListChecklistPdf.Write(stream, Model(100, note: i => i % 7 == 0 ? "read after the prelude" : null), ChecklistPaperSize.Letter);

        var bytes = stream.ToArray();
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.True(ReadingListChecklistPdf.Paginate(Model(100), ChecklistPaperSize.Letter).Count >= 3);
    }

    [Fact]
    public void Paginate_NeverLeavesAGroupRowLastOnAPage()
    {
        // A new group every 3 rows guarantees group rows land near every page boundary.
        var pages = ReadingListChecklistPdf.Paginate(Model(200, group: i => $"Part {(i - 1) / 3}"), ChecklistPaperSize.A4);

        Assert.True(pages.Count > 3);
        Assert.All(pages.Take(pages.Count - 1), page => Assert.Null(page[^1].Group));
        Assert.Equal(200, pages.SelectMany(p => p).Count(l => l.Row is not null));
    }

    [Fact]
    public void Pdf_HandlesNonLatinText()
    {
        using var stream = new MemoryStream();
        var model = new ChecklistModel("ワンピース", 1, 0, 1, DateTime.Today,
            new[] { new ChecklistRow(1, "東の海", "ワンピース #1", 1997, false, false, "Ранний выпуск") });
        ReadingListChecklistPdf.Write(stream, model, ChecklistPaperSize.A4);
        Assert.True(stream.Length > 0);
    }
}
