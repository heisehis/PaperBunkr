namespace Paperbunkr.Data.Entities;

/// <summary>
/// Library's single sort-key pool (docs/superpowers/specs/2026-08-18-issue-list-pluggable-sort-
/// group-design.md's original Slice 1, extended by docs/superpowers/specs/
/// 2026-08-18-library-book-centric-redesign-design.md's Slice 1 - the full CE field-coverage table
/// lives there, including what's deliberately excluded as CE bugs/UI-only concepts).
///
/// 2026-09-03: unified - this is now the sort pool for <b>both</b> per-issue cards and per-series
/// cards (the old series-only <c>LibrarySortField</c> is gone). A per-issue field applied to a
/// series card resolves against that series' representative issue (see
/// <c>SeriesCardSample.RepresentativeRow</c>); <see cref="SeriesIssueCount"/> /
/// <see cref="SeriesUnreadCount"/> are the two series-level aggregates carried over from
/// <c>LibrarySortField</c>.
/// </summary>
public enum IssueListSortField
{
    Number,
    Series,
    Title,
    Writer,
    Publisher,
    Genre,
    Format,
    Added,
    Opened,
    Released,
    Year,
    PageCount,
    FileSize,
    Rating,
    CommunityRating,
    ReadPercentage,
    OpenCount,
    Tags,
    Status,
    Volume,
    Penciller,
    Inker,
    Colorist,
    Letterer,
    CoverArtist,
    Editor,
    Translator,
    Characters,
    Teams,
    Locations,
    BookPrice,
    BookAge,
    BookStore,
    BookOwner,
    BookCondition,
    BookCollectionStatus,
    BookLocation,
    ISBN,
    Read,
    Imprint,
    Language,
    AgeRating,
    StoryArc,
    SeriesGroup,
    FilePath,
    FileName,
    FileDirectory,
    FileModified,
    FileCreated,
    FileFormat,
    Count,
    AlternateSeries,
    AlternateNumber,
    Month,
    Day,
    ScanInformation,
    BookmarkCount,

    // --- Series-level aggregates, carried over from the retired LibrarySortField (2026-09-03).
    // On a per-issue card these read the issue's own series' totals. ---
    SeriesIssueCount,
    SeriesUnreadCount,
}
