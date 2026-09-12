using System.Collections.Generic;
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
        float? volumeSortKey = null, string? penciller = null, bool isRead = false, float? bookPrice = null,
        string? contentTypeLabel = null, int seriesIssueCount = 0, int seriesUnreadCount = 0,
        bool? isFinalIssue = null, bool hasPendingProposal = false, int pendingProposalCount = 0,
        int openCount = 0, IReadOnlyDictionary<int, string>? virtualTagValues = null) => new()
    {
        SeriesName = seriesName, Title = title, Writer = writer, Publisher = publisher, Genre = genre,
        Year = year, IsMissing = isMissing, NumberSortKey = numberSortKey, CoverBrush = Brush,
        VolumeSortKey = volumeSortKey, Penciller = penciller, IsRead = isRead, BookPrice = bookPrice,
        ContentTypeLabel = contentTypeLabel, SeriesIssueCount = seriesIssueCount, SeriesUnreadCount = seriesUnreadCount,
        IsFinalIssue = isFinalIssue, HasPendingProposal = hasPendingProposal, PendingProposalCount = pendingProposalCount,
        OpenCount = openCount, VirtualTagValues = virtualTagValues ?? new Dictionary<int, string>(),
    };

    // --- Union members carried over from the retired LibraryFieldCatalog (2026-09-03) ---

    [Fact]
    public void SeriesIssueCount_And_SeriesUnreadCount_SortNumerically()
    {
        var s = IssueListFieldCatalog.SortFields[IssueListSortField.SeriesIssueCount];
        Assert.True(s.Compare(Row(seriesIssueCount: 2), Row(seriesIssueCount: 10)) < 0);
        var u = IssueListFieldCatalog.SortFields[IssueListSortField.SeriesUnreadCount];
        Assert.True(u.Compare(Row(seriesUnreadCount: 0), Row(seriesUnreadCount: 3)) < 0);
    }

    [Fact]
    public void ContentTypeGroup_UsesEnumDeclarationOrder_NotAlphabetical()
    {
        var d = IssueListFieldCatalog.GroupFields[IssueListGroupField.ContentType];
        Assert.Equal("Manga", d.GroupKey(Row(contentTypeLabel: "Manga")));
        Assert.Equal("Unknown", d.GroupKey(Row(contentTypeLabel: null)));
        // Declaration order: Comic before Manga (alphabetically the reverse).
        Assert.True(d.GroupOrder(nameof(ContentType.Comic), nameof(ContentType.Manga)) < 0);
    }

    [Theory]
    [InlineData("batman", "B")]
    [InlineData("100 Bullets", "#")]
    [InlineData("!Ultimate", "#")]
    [InlineData("", "#")]
    public void AlphabeticalGroup_BucketsSeriesNameFirstLetter(string name, string expected)
    {
        var d = IssueListFieldCatalog.GroupFields[IssueListGroupField.Alphabetical];
        Assert.Equal(expected, d.GroupKey(Row(seriesName: name)));
    }

    [Fact]
    public void SeriesIssueCountGroup_BucketsNumerically()
    {
        var d = IssueListFieldCatalog.GroupFields[IssueListGroupField.SeriesIssueCount];
        Assert.Equal("5", d.GroupKey(Row(seriesIssueCount: 5)));
        Assert.True(d.GroupOrder("2", "10") < 0);
    }

    [Fact]
    public void EverySortField_ExceptVirtualTag_HasACatalogEntry()
    {
        // VirtualTag is deliberately dynamic - no single fixed descriptor exists for the whole
        // family (docs/superpowers/specs/2026-09-12-library-sort-group-axes-design.md §1);
        // BuildVirtualTagSortDescriptor_/BuildVirtualTagGroupDescriptor_ tests below cover it instead.
        foreach (IssueListSortField field in Enum.GetValues<IssueListSortField>())
        {
            if (field == IssueListSortField.VirtualTag)
            {
                continue;
            }

            Assert.True(IssueListFieldCatalog.SortFields.ContainsKey(field), $"Missing sort descriptor for {field}");
        }
    }

    [Fact]
    public void EveryGroupField_ExceptNoneAndVirtualTag_HasACatalogEntry()
    {
        foreach (IssueListGroupField field in Enum.GetValues<IssueListGroupField>())
        {
            if (field is IssueListGroupField.None or IssueListGroupField.VirtualTag)
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

    // --- Phase 4b: configurable Details-table columns
    // (docs/superpowers/specs/2026-08-27-library-browsing-4b-toolbar-rework-design.md §8) ---

    [Fact]
    public void ColumnFields_EveryDescriptor_HasNonNullDisplay()
    {
        Assert.NotEmpty(IssueListFieldCatalog.ColumnFields);
        Assert.All(IssueListFieldCatalog.ColumnFields, d => Assert.NotNull(d.Display));
    }

    [Fact]
    public void Status_IsSortOnly_NotOfferedAsAColumn()
    {
        Assert.Null(IssueListFieldCatalog.SortFields[IssueListSortField.Status].Display);
        Assert.DoesNotContain(IssueListFieldCatalog.ColumnFields, d => d.Field == IssueListSortField.Status);
    }

    [Fact]
    public void ColumnFields_Display_NeverThrows_ForFullyNullAndFullyPopulatedRow()
    {
        var nullRow = new IssueListRow { SeriesName = "S", Title = "T", CoverBrush = Brush };
        var fullRow = new IssueListRow
        {
            SeriesName = "S", Title = "T", CoverBrush = Brush,
            Number = "12", Writer = "W", Publisher = "P", Genre = "G", Format = "Digital", Tags = "a, b",
            AddedTime = new DateTime(2024, 1, 2), OpenedTime = new DateTime(2024, 3, 4),
            ReleasedTime = new DateTime(2023, 12, 1), FileModifiedTime = new DateTime(2024, 5, 6),
            FileCreationTime = new DateTime(2024, 5, 5),
            Year = 2024, PageCount = 22, FileSize = 5_242_880, Rating = 4.5f, CommunityRating = 3.75f,
            ReadPercentage = 66.6, OpenCount = 3, BookmarkCount = 2, Count = 50, Month = 7, Day = 15,
            Volume = "2", Penciller = "Pe", Inker = "In", Colorist = "Co", Letterer = "Le",
            CoverArtist = "Ca", Editor = "Ed", Translator = "Tr", Characters = "Ch", Teams = "Te",
            Locations = "Lo", BookPrice = 3.99f, BookAge = "Modern", BookStore = "St", BookOwner = "Ow",
            BookCondition = "Mint", BookCollectionStatus = "Owned", BookLocation = "Shelf 1", ISBN = "123",
            IsRead = true, Imprint = "Im", LanguageIso = "en", AgeRating = "T", StoryArc = "Arc",
            SeriesGroup = "SG", FilePath = @"C:\c\x.cbz", FileName = "x.cbz", FileDirectory = @"C:\c",
            FileFormat = "CBZ", AlternateSeries = "AS", AlternateNumber = "AN", ScanInformation = "scan",
        };

        foreach (var descriptor in IssueListFieldCatalog.ColumnFields)
        {
            var ex1 = Record.Exception(() => descriptor.Display!(nullRow));
            Assert.Null(ex1);
            var ex2 = Record.Exception(() => descriptor.Display!(fullRow));
            Assert.Null(ex2);
        }

        Assert.Equal("5 MB", IssueListFieldCatalog.SortFields[IssueListSortField.FileSize].Display!(fullRow));
        Assert.Equal("67%", IssueListFieldCatalog.SortFields[IssueListSortField.ReadPercentage].Display!(fullRow));
        Assert.Equal("2024-01-02", IssueListFieldCatalog.SortFields[IssueListSortField.Added].Display!(fullRow));
        Assert.Equal("Read", IssueListFieldCatalog.SortFields[IssueListSortField.Read].Display!(fullRow));
        Assert.Equal("Unread", IssueListFieldCatalog.SortFields[IssueListSortField.Read].Display!(nullRow));
    }

    [Fact]
    public void DefaultDetailsColumns_AllResolve_AndAreColumnEligible()
    {
        Assert.NotEmpty(IssueListFieldCatalog.DefaultDetailsColumns);
        foreach (var field in IssueListFieldCatalog.DefaultDetailsColumns)
        {
            Assert.True(IssueListFieldCatalog.SortFields.TryGetValue(field, out var d), $"No descriptor for {field}");
            Assert.NotNull(d!.Display);
        }
    }

    // --- docs/superpowers/specs/2026-09-12-library-sort-group-axes-design.md ---

    [Fact]
    public void NeedsReviewSort_FalseSortsBeforeTrue_AndGroupLabelsCorrectly()
    {
        var sort = IssueListFieldCatalog.SortFields[IssueListSortField.NeedsReview];
        Assert.True(sort.Compare(Row(hasPendingProposal: false), Row(hasPendingProposal: true)) < 0);

        var group = IssueListFieldCatalog.GroupFields[IssueListGroupField.NeedsReview];
        Assert.Equal("Needs Review", group.GroupKey(Row(hasPendingProposal: true)));
        Assert.Equal("Up to Date", group.GroupKey(Row(hasPendingProposal: false)));
    }

    [Fact]
    public void PendingProposalCountSort_OrdersNumerically()
    {
        var sort = IssueListFieldCatalog.SortFields[IssueListSortField.PendingProposalCount];
        Assert.True(sort.Compare(Row(pendingProposalCount: 1), Row(pendingProposalCount: 5)) < 0);
    }

    [Fact]
    public void IsFinalIssueSort_OrdersUnknownThenNoThenYes_MatchingCEsYesNoOrdering()
    {
        var sort = IssueListFieldCatalog.SortFields[IssueListSortField.IsFinalIssue];
        var unknown = Row(isFinalIssue: null);
        var no = Row(isFinalIssue: false);
        var yes = Row(isFinalIssue: true);

        Assert.True(sort.Compare(unknown, no) < 0);
        Assert.True(sort.Compare(no, yes) < 0);
        Assert.True(sort.Compare(unknown, yes) < 0);
    }

    [Fact]
    public void IsFinalIssueGroup_HasThreeBuckets_OrderedUnknownThenNoThenYes()
    {
        var group = IssueListFieldCatalog.GroupFields[IssueListGroupField.IsFinalIssue];
        Assert.Equal("Final issue", group.GroupKey(Row(isFinalIssue: true)));
        Assert.Equal("Not final", group.GroupKey(Row(isFinalIssue: false)));
        Assert.Equal("Unknown", group.GroupKey(Row(isFinalIssue: null)));

        Assert.True(group.GroupOrder("Unknown", "Not final") < 0);
        Assert.True(group.GroupOrder("Not final", "Final issue") < 0);
    }

    [Theory]
    [InlineData(0, "0-20")]
    [InlineData(20, "0-20")]
    [InlineData(21, "21-50")]
    [InlineData(100, "51-100")]
    [InlineData(1000, "501-1000")]
    [InlineData(1001, ">1000")]
    public void OpenCountGroup_BucketsIntoCEsFixedRanges(int openCount, string expectedBucket)
    {
        var group = IssueListFieldCatalog.GroupFields[IssueListGroupField.OpenCount];
        Assert.Equal(expectedBucket, group.GroupKey(Row(openCount: openCount)));
    }

    [Fact]
    public void OpenCountGroup_OrdersByRange_NotAlphabetically()
    {
        // Alphabetically ">1000" < "0-20" (">" sorts before digits) - the range order must not be that.
        var group = IssueListFieldCatalog.GroupFields[IssueListGroupField.OpenCount];
        Assert.True(group.GroupOrder("0-20", ">1000") < 0);
        Assert.True(group.GroupOrder("21-50", "0-20") > 0);
    }

    [Fact]
    public void BuildVirtualTagSortDescriptor_SortsByEvaluatedValue_CaseInsensitively()
    {
        var tag = new VirtualTagDefinition { Id = 7, Name = "Reading Status", CaptionFormat = "{Status}" };
        var descriptor = IssueListFieldCatalog.BuildVirtualTagSortDescriptor(tag);

        Assert.Equal("Reading Status", descriptor.DisplayName);
        var a = Row(virtualTagValues: new Dictionary<int, string> { [7] = "alice" });
        var b = Row(virtualTagValues: new Dictionary<int, string> { [7] = "BOB" });
        Assert.True(descriptor.Compare(a, b) < 0);
    }

    [Fact]
    public void BuildVirtualTagGroupDescriptor_BucketsByExactValue_MissingFallsBackToUnspecified()
    {
        var tag = new VirtualTagDefinition { Id = 7, Name = "Reading Status", CaptionFormat = "{Status}" };
        var descriptor = IssueListFieldCatalog.BuildVirtualTagGroupDescriptor(tag);

        Assert.Equal("Reading Status", descriptor.DisplayName);
        Assert.Equal("Ongoing", descriptor.GroupKey(Row(virtualTagValues: new Dictionary<int, string> { [7] = "Ongoing" })));
        // No entry at all for this tag id (e.g. it wasn't enabled when this row was built).
        Assert.Equal("Unspecified", descriptor.GroupKey(Row()));
        // Different tag id present, but not this one.
        Assert.Equal("Unspecified", descriptor.GroupKey(Row(virtualTagValues: new Dictionary<int, string> { [99] = "Ongoing" })));
    }
}
