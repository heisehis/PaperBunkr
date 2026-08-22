using System.Linq;
using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Exercises <see cref="IssueListFieldCatalog"/> (docs/superpowers/specs/2026-08-18-issue-list-pluggable-sort-group-design.md).</summary>
public class IssueListFieldCatalogTests
{
    private static readonly IBrush Brush = Brushes.Black;

    private static IssueListRow Row(
        string seriesName = "S", string title = "T", string? writer = null, string? publisher = null,
        string? genre = null, int? year = null, bool isMissing = false, float? numberSortKey = null,
        float? volumeSortKey = null, string? penciller = null, bool isRead = false, float? bookPrice = null) => new()
    {
        SeriesName = seriesName, Title = title, Writer = writer, Publisher = publisher, Genre = genre,
        Year = year, IsMissing = isMissing, NumberSortKey = numberSortKey, CoverBrush = Brush,
        VolumeSortKey = volumeSortKey, Penciller = penciller, IsRead = isRead, BookPrice = bookPrice,
    };

    [Fact]
    public void EverySortField_HasACatalogEntry()
    {
        foreach (IssueListSortField field in Enum.GetValues<IssueListSortField>())
        {
            Assert.True(IssueListFieldCatalog.SortFields.ContainsKey(field), $"Missing sort descriptor for {field}");
        }
    }

    [Fact]
    public void EveryGroupField_ExceptNone_HasACatalogEntry()
    {
        foreach (IssueListGroupField field in Enum.GetValues<IssueListGroupField>())
        {
            if (field == IssueListGroupField.None)
            {
                continue;
            }

            Assert.True(IssueListFieldCatalog.GroupFields.ContainsKey(field), $"Missing group descriptor for {field}");
        }
    }

    [Fact]
    public void SeriesSort_PrimarilyByName_TieBreaksByNumber()
    {
        var descriptor = IssueListFieldCatalog.SortFields[IssueListSortField.Series];
        var rows = new[]
        {
            Row(seriesName: "Zeta", numberSortKey: 1),
            Row(seriesName: "Alpha", numberSortKey: 2),
            Row(seriesName: "Alpha", numberSortKey: 1),
        };

        var sorted = rows.ToList();
        sorted.Sort(descriptor.Compare);

        Assert.Equal("Alpha", sorted[0].SeriesName);
        Assert.Equal(1, sorted[0].NumberSortKey);
        Assert.Equal("Alpha", sorted[1].SeriesName);
        Assert.Equal(2, sorted[1].NumberSortKey);
        Assert.Equal("Zeta", sorted[2].SeriesName);
    }

    [Fact]
    public void PublisherGroup_BucketsByPublisher_BlankFallsBackToUnknown()
    {
        var descriptor = IssueListFieldCatalog.GroupFields[IssueListGroupField.Publisher];
        Assert.Equal("Marvel", descriptor.GroupKey(Row(publisher: "Marvel")));
        Assert.Equal("Unknown", descriptor.GroupKey(Row(publisher: null)));
    }

    [Fact]
    public void YearGroup_UsesNumericOrdering()
    {
        var descriptor = IssueListFieldCatalog.GroupFields[IssueListGroupField.Year];
        Assert.Equal("2020", descriptor.GroupKey(Row(year: 2020)));
        Assert.True(descriptor.GroupOrder("5", "2020") < 0); // numeric: not "5" > "2020" lexically
    }

    [Fact]
    public void StatusGroup_LabelsMissingVsAvailable()
    {
        var descriptor = IssueListFieldCatalog.GroupFields[IssueListGroupField.Status];
        Assert.Equal("Missing", descriptor.GroupKey(Row(isMissing: true)));
        Assert.Equal("Available", descriptor.GroupKey(Row(isMissing: false)));
    }

    [Fact]
    public void TagsAndNumberFields_AreSortOnly_NotInGroupCatalog()
    {
        // Matches CE's own column table precedent (7 of 73 real fields sortable-but-not-groupable).
        Assert.True(IssueListFieldCatalog.SortFields.ContainsKey(IssueListSortField.Tags));
        Assert.DoesNotContain(IssueListFieldCatalog.GroupFields.Keys, f => f.ToString() == "Tags");
    }

    // --- Slice 1 additions (docs/superpowers/specs/2026-08-18-library-book-centric-redesign-
    // design.md) ---

    [Fact]
    public void ISBN_IsSortOnly_NotInGroupCatalog_MatchingCEsRealColumnTable()
    {
        Assert.True(IssueListFieldCatalog.SortFields.ContainsKey(IssueListSortField.ISBN));
        Assert.DoesNotContain(IssueListFieldCatalog.GroupFields.Keys, f => f.ToString() == "ISBN");
    }

    [Fact]
    public void VolumeSort_OrdersByVolumeSortKey_NotSeriesOrNumber()
    {
        var descriptor = IssueListFieldCatalog.SortFields[IssueListSortField.Volume];
        var rows = new[] { Row(volumeSortKey: 3), Row(volumeSortKey: 1), Row(volumeSortKey: 2) };

        var sorted = rows.ToList();
        sorted.Sort(descriptor.Compare);

        Assert.Equal(new float?[] { 1, 2, 3 }, sorted.Select(r => r.VolumeSortKey));
    }

    [Fact]
    public void PencillerSort_OrdersCaseInsensitively()
    {
        var descriptor = IssueListFieldCatalog.SortFields[IssueListSortField.Penciller];
        var rows = new[] { Row(penciller: "zeb"), Row(penciller: "Adam") };

        var sorted = rows.ToList();
        sorted.Sort(descriptor.Compare);

        Assert.Equal("Adam", sorted[0].Penciller);
    }

    [Fact]
    public void ReadGroup_LabelsReadVsUnread()
    {
        var descriptor = IssueListFieldCatalog.GroupFields[IssueListGroupField.Read];
        Assert.Equal("Read", descriptor.GroupKey(Row(isRead: true)));
        Assert.Equal("Unread", descriptor.GroupKey(Row(isRead: false)));
    }

    [Fact]
    public void BookPriceGroup_BucketsByFormattedPrice_BlankFallsBackToUnknown()
    {
        var descriptor = IssueListFieldCatalog.GroupFields[IssueListGroupField.BookPrice];
        Assert.Equal("3.99", descriptor.GroupKey(Row(bookPrice: 3.99f)));
        Assert.Equal("Unknown", descriptor.GroupKey(Row(bookPrice: null)));
    }

    /// <summary>
    /// The concrete real-world case this pair of fields exists for: an anthology/imprint series
    /// (e.g. "Warhammer 40") where every issue shares one generic Series name but each really
    /// belongs to a distinct mini-series/story - grouping by Series alone can't separate them
    /// (see docs/superpowers/specs/2026-08-18-library-book-centric-redesign-design.md), but Story
    /// Arc can.
    /// </summary>
    [Fact]
    public void StoryArcGroup_SeparatesIssuesWithinOneGenericSeries()
    {
        var descriptor = IssueListFieldCatalog.GroupFields[IssueListGroupField.StoryArc];
        var damnationCrusade = new IssueListRow
        {
            SeriesName = "Warhammer 40", Title = "Damnation Crusade #1", StoryArc = "Damnation Crusade", CoverBrush = Brush,
        };
        var dawnOfWar = new IssueListRow
        {
            SeriesName = "Warhammer 40", Title = "Dawn of War III #1", StoryArc = "Dawn of War III", CoverBrush = Brush,
        };

        Assert.Equal("Damnation Crusade", descriptor.GroupKey(damnationCrusade));
        Assert.Equal("Dawn of War III", descriptor.GroupKey(dawnOfWar));
        Assert.NotEqual(descriptor.GroupKey(damnationCrusade), descriptor.GroupKey(dawnOfWar));
    }
}
