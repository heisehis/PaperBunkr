using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises the pure hover-tooltip content helpers on <see cref="IssueListRow"/> (docs/superpowers/
/// specs/2026-09-13-preferences-cosmetic-toggles-design.md's <c>ShowToolTips</c>) - the actual
/// hover/popup mechanics live in <c>LibraryScreen.axaml.cs</c> and aren't headlessly testable
/// (standing no-computer-use caveat), but the content these helpers compute is plain string logic.
/// </summary>
public class IssueListRowHoverTooltipTests
{
    private static IssueListRow Row(string? writer = null, string? penciller = null, string? summary = null, long? fileSize = null, string? format = null) => new()
    {
        Id = 1,
        SeriesId = 1,
        SeriesName = "Kilo Station",
        Title = "Kilo Station #1",
        CoverBrush = Avalonia.Media.Brushes.Gray,
        Writer = writer,
        Penciller = penciller,
        Summary = summary,
        FileSize = fileSize,
        Format = format,
    };

    [Fact]
    public void WriterAndPenciller_JoinsBothWhenPresent()
    {
        var row = Row(writer: "Alice", penciller: "Bob");
        Assert.Equal("Alice, Bob", row.WriterAndPenciller);
        Assert.True(row.HasWriterAndPenciller);
    }

    [Fact]
    public void WriterAndPenciller_OmitsTheMissingOne()
    {
        var row = Row(writer: "Alice", penciller: null);
        Assert.Equal("Alice", row.WriterAndPenciller);
    }

    [Fact]
    public void WriterAndPenciller_BothMissing_IsNullEquivalent()
    {
        var row = Row();
        Assert.False(row.HasWriterAndPenciller);
    }

    [Fact]
    public void SummaryExcerpt_ShortSummary_IsUnchanged()
    {
        var row = Row(summary: "A short summary.");
        Assert.Equal("A short summary.", row.SummaryExcerpt);
        Assert.True(row.HasSummaryExcerpt);
    }

    [Fact]
    public void SummaryExcerpt_LongSummary_TruncatesTo150CharsWithEllipsis()
    {
        string longSummary = new string('x', 200);
        var row = Row(summary: longSummary);

        Assert.Equal(151, row.SummaryExcerpt!.Length); // 150 chars + ellipsis
        Assert.EndsWith("…", row.SummaryExcerpt);
        Assert.StartsWith(new string('x', 150), row.SummaryExcerpt);
    }

    [Fact]
    public void SummaryExcerpt_NoSummary_IsNull()
    {
        Assert.False(Row().HasSummaryExcerpt);
    }

    [Fact]
    public void FileSizeDisplay_ReusesIssueListFieldCatalogFormatting()
    {
        var row = Row(fileSize: 1024 * 1024 * 3);
        Assert.Equal(IssueListFieldCatalog.FormatFileSize(row.FileSize), row.FileSizeDisplay);
        Assert.True(row.HasFileSizeDisplay);
    }

    [Fact]
    public void HasFormat_ReflectsWhetherFormatIsSet()
    {
        Assert.True(Row(format: "CBZ").HasFormat);
        Assert.False(Row().HasFormat);
    }
}
