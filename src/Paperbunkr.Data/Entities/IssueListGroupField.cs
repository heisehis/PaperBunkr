namespace Paperbunkr.Data.Entities;

/// <summary>
/// Comic List screen's grouping dimensions (docs/superpowers/specs/2026-08-18-issue-list-pluggable-
/// sort-group-design.md, extended by docs/superpowers/specs/2026-08-18-library-book-centric-
/// redesign-design.md's Slice 1) - deliberately a subset of <see cref="IssueListSortField"/>,
/// matching CE's own precedent that not every sortable field is a sensible group-by field (CE has
/// no grouper for e.g. ISBN, matched here by <see cref="IssueListSortField.ISBN"/> having no
/// corresponding value here either).
/// </summary>
public enum IssueListGroupField
{
    None,
    Series,
    Publisher,
    Genre,
    Format,
    Year,
    Status,
    Opened,
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
    Read,
    Imprint,
    Language,
    AgeRating,
    StoryArc,
    SeriesGroup,
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
}
