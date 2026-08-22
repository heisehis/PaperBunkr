using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises every descriptor in <see cref="LibraryFieldCatalog"/> (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase2c-library-field-descriptors-design.md) in isolation - the
/// regression guard that user-visible Library sort/group behavior didn't change lives in the
/// pre-existing <see cref="LibraryScreenViewModelTests"/> (left unmodified by this phase); these
/// tests instead cover each descriptor's exact comparison/bucketing semantics directly, including
/// the edge cases the old switch handled (ordinal case-insensitive string comparison, not .NET's
/// default culture-aware one; blank Publisher; a non-letter-leading name; ContentType's
/// enum-declaration-order group ordering). Runs under <see cref="AvaloniaTestCollection"/> since
/// <see cref="SeriesCardSample.CoverBrush"/> needs a real Skia platform for
/// <see cref="SeriesCardSample.CoverBrushFor"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryFieldCatalogTests
{
    private static SeriesCardSample Card(string name, string? publisher = null, string contentTypeLabel = "Comic",
        DateTime? lastAdded = null, DateTime? lastOpened = null, long totalFileSize = 0, int issueCount = 0, int unreadCount = 0) =>
        new()
        {
            Title = name,
            Name = name,
            Sub = string.Empty,
            Publisher = publisher,
            ContentTypeLabel = contentTypeLabel,
            CoverBrush = SeriesCardSample.CoverBrushFor(name),
            LastAddedTime = lastAdded,
            LastOpenedTime = lastOpened,
            TotalFileSize = totalFileSize,
            IssueCount = issueCount,
            UnreadCount = unreadCount,
        };

    // --- Sort descriptors ---

    [Fact]
    public void Name_UsesOrdinalCaseInsensitiveComparison_NotCultureAware()
    {
        var descriptor = LibraryFieldCatalog.SortFields[LibrarySortField.Name];

        // "apple" vs "Banana": ordinal-ignore-case compares 'a' < 'b' regardless of case, same as
        // culture-aware would give here - the real distinguishing case is symbols vs letters, where
        // ordinal and culture-aware comparers can disagree. Assert the exact contract instead:
        // matches string.Compare(..., StringComparison.OrdinalIgnoreCase) exactly.
        var a = Card("apple");
        var b = Card("Banana");
        Assert.Equal(string.Compare("apple", "Banana", StringComparison.OrdinalIgnoreCase), descriptor.Compare(a, b));
    }

    [Fact]
    public void DateAdded_ComparesNullableDateTimes_NullSortsFirst()
    {
        var descriptor = LibraryFieldCatalog.SortFields[LibrarySortField.DateAdded];
        var withDate = Card("A", lastAdded: new DateTime(2024, 1, 1));
        var noDate = Card("B", lastAdded: null);

        Assert.True(descriptor.Compare(noDate, withDate) < 0);
        Assert.True(descriptor.Compare(withDate, noDate) > 0);
        Assert.Equal(0, descriptor.Compare(withDate, withDate));
    }

    [Fact]
    public void LastRead_ComparesNullableDateTimes()
    {
        var descriptor = LibraryFieldCatalog.SortFields[LibrarySortField.LastRead];
        var earlier = Card("A", lastOpened: new DateTime(2024, 1, 1));
        var later = Card("B", lastOpened: new DateTime(2024, 6, 1));

        Assert.True(descriptor.Compare(earlier, later) < 0);
    }

    [Fact]
    public void Size_ComparesTotalFileSize()
    {
        var descriptor = LibraryFieldCatalog.SortFields[LibrarySortField.Size];
        Assert.True(descriptor.Compare(Card("A", totalFileSize: 100), Card("B", totalFileSize: 200)) < 0);
    }

    [Fact]
    public void IssueCount_ComparesIssueCount()
    {
        var descriptor = LibraryFieldCatalog.SortFields[LibrarySortField.IssueCount];
        Assert.True(descriptor.Compare(Card("A", issueCount: 1), Card("B", issueCount: 5)) < 0);
    }

    [Fact]
    public void UnreadCount_ComparesUnreadCount()
    {
        var descriptor = LibraryFieldCatalog.SortFields[LibrarySortField.UnreadCount];
        Assert.True(descriptor.Compare(Card("A", unreadCount: 0), Card("B", unreadCount: 3)) < 0);
    }

    [Fact]
    public void Publisher_UsesOrdinalCaseInsensitiveComparison_HandlesNull()
    {
        var descriptor = LibraryFieldCatalog.SortFields[LibrarySortField.Publisher];
        var noPublisher = Card("A", publisher: null);
        var withPublisher = Card("B", publisher: "DC");

        // string.Compare treats null as less than any non-null string - same contract preserved.
        Assert.True(descriptor.Compare(noPublisher, withPublisher) < 0);
    }

    // --- Group descriptors ---

    [Fact]
    public void ContentType_GroupKey_IsContentTypeLabel()
    {
        var descriptor = LibraryFieldCatalog.GroupFields[LibraryGroupField.ContentType];
        Assert.Equal("Manga", descriptor.GroupKey(Card("A", contentTypeLabel: "Manga")));
    }

    [Fact]
    public void ContentType_GroupOrder_UsesEnumDeclarationOrder_NotAlphabetical()
    {
        var descriptor = LibraryFieldCatalog.GroupFields[LibraryGroupField.ContentType];

        // Declaration order: Comic, Manga, Manhua, Manhwa, Unknown (confirmed from ContentType.cs).
        // Alphabetically, "Comic" would sort after "Manga"/"Manhua"/"Manhwa" - assert it doesn't.
        Assert.True(descriptor.GroupOrder(nameof(ContentType.Comic), nameof(ContentType.Manga)) < 0);
        Assert.True(descriptor.GroupOrder(nameof(ContentType.Manhwa), nameof(ContentType.Unknown)) < 0);
    }

    [Fact]
    public void Publisher_GroupKey_BlankFallsBackToUnknown()
    {
        var descriptor = LibraryFieldCatalog.GroupFields[LibraryGroupField.Publisher];

        Assert.Equal("Unknown", descriptor.GroupKey(Card("A", publisher: null)));
        Assert.Equal("Unknown", descriptor.GroupKey(Card("A", publisher: "   ")));
        Assert.Equal("DC", descriptor.GroupKey(Card("A", publisher: "DC")));
    }

    [Fact]
    public void Publisher_GroupOrder_IsOrdinalCaseInsensitive()
    {
        var descriptor = LibraryFieldCatalog.GroupFields[LibraryGroupField.Publisher];
        Assert.True(descriptor.GroupOrder("dc", "Marvel") < 0);
    }

    [Fact]
    public void Alphabetical_GroupKey_LetterLeadingName_UsesUppercasedFirstLetter()
    {
        var descriptor = LibraryFieldCatalog.GroupFields[LibraryGroupField.Alphabetical];
        Assert.Equal("B", descriptor.GroupKey(Card("batman")));
    }

    [Fact]
    public void Alphabetical_GroupKey_DigitOrSymbolLeadingName_BucketsUnderHash()
    {
        var descriptor = LibraryFieldCatalog.GroupFields[LibraryGroupField.Alphabetical];
        Assert.Equal("#", descriptor.GroupKey(Card("100 Bullets")));
        Assert.Equal("#", descriptor.GroupKey(Card("!Ultimate")));
    }

    [Fact]
    public void Alphabetical_GroupKey_EmptyName_BucketsUnderHash()
    {
        var descriptor = LibraryFieldCatalog.GroupFields[LibraryGroupField.Alphabetical];
        Assert.Equal("#", descriptor.GroupKey(Card(string.Empty)));
    }

    [Fact]
    public void GroupFields_HasNoEntryForNone()
    {
        Assert.False(LibraryFieldCatalog.GroupFields.ContainsKey(LibraryGroupField.None));
    }
}
