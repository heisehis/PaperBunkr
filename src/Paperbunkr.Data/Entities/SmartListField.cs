namespace Paperbunkr.Data.Entities;

/// <summary>
/// Every condition field the smart-list rule builder can filter on, matching CE's
/// <c>ComicBook*Matcher</c> catalog field-for-field (docs/superpowers/specs/2026-08-06-smart-lists-design.md
/// §4) except <c>Week</c>, deliberately omitted per that spec. <c>AllProperties</c> (deferred in the
/// original spec) is now present, wired to <see cref="SearchFieldBundleCatalog"/>
/// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §4).
/// <see cref="SmartListCatalog"/> maps each value to its label, <see cref="SmartListDataType"/>,
/// and the <see cref="Issue"/> column(s) it reads.
/// </summary>
public enum SmartListField
{
    // Series-level
    SeriesName,
    Genre,
    Publisher,
    ContentType,
    ReadingMode,
    SeriesComplete,
    ReadingStatus,

    /// <summary>Series-target only (docs/superpowers/specs/2026-08-30-smart-collections-design.md) - <see cref="Series.Status"/>, the publisher's completion state. Distinct from <see cref="ReadingStatus"/> (the reader's relationship) and the computed <see cref="SeriesComplete"/>.</summary>
    SeriesStatus,

    /// <summary>Series-target only - <see cref="Series.SortName"/>.</summary>
    SeriesSortName,

    /// <summary>
    /// The <c>Continuity</c> rows the issue's series belongs to, joined as text (docs/superpowers/
    /// specs/2026-08-27-metadata-model-phase4f-continuity-browse-design.md's deferred continuity-
    /// scoped Smart Lists). Not in CE - Continuity is a Paperbunkr-original grouping.
    /// </summary>
    Continuity,

    // ComicInfo text fields
    Title,
    Number,
    Volume,
    Count,
    AlternateSeries,
    AlternateNumber,
    AlternateCount,
    StoryArc,
    StoryArcNumber,
    SeriesGroup,
    Summary,
    Notes,
    Review,
    Writer,
    Penciller,
    Inker,
    Colorist,
    Letterer,
    CoverArtist,
    Editor,
    Translator,
    Imprint,
    Web,
    LanguageISO,
    Format,
    AgeRating,
    Characters,
    Teams,
    Locations,
    MainCharacterOrTeam,
    Tags,

    // Numeric
    Year,
    Month,
    Day,
    PageCount,
    Rating,
    CommunityRating,
    BookPrice,
    FileSize,
    ReadPercentage,
    BookmarkCount,
    NewPages,

    // Toggle
    Checked,
    IsMissing,
    IsLinked,
    BlackAndWhite,
    HasCustomValues,

    // Date
    Added,
    Opened,
    Released,
    Modified,
    Created,

    // File-derived text
    File,
    FullPath,
    Directory,
    FileFormat,

    // Book-collection text
    BookAge,
    BookCollectionStatus,
    BookCondition,
    BookLocation,
    BookNotes,
    BookOwner,
    BookStore,
    ISBN,
    ScanInformation,

    // Special-cased — not evaluated via SmartListCatalog's flat selector table
    CustomValue,
    Duplicate,

    /// <summary>
    /// Not in CE at all — CE's fixed VirtualTag01..20 WinForms-reflection-panel scheme was
    /// deliberately not ported (see <c>VirtualTagDefinition</c>). References one user-defined
    /// <c>VirtualTagDefinition</c> by id (<see cref="SmartListCondition.VirtualTagId"/>, same
    /// per-condition-argument shape as <see cref="CustomValue"/>) and matches its evaluated
    /// caption as text.
    /// </summary>
    VirtualTag,

    /// <summary>
    /// CE's <c>ComicBookAllPropertiesMatcher</c> — match against a curated bundle of fields at once,
    /// scoped by <see cref="SmartListCondition.SearchMode"/> (docs/superpowers/specs/2026-08-28-
    /// smartlist-engine-v2-design.md §4). Special-cased in <see cref="SmartListQueryBuilder"/> (like
    /// <see cref="CustomValue"/>/<see cref="VirtualTag"/>) rather than having a flat
    /// <see cref="SmartListCatalog"/> selector; the same <see cref="SearchFieldBundleCatalog"/> the
    /// Library search box uses.
    /// </summary>
    AllProperties,

    // Novel-target fields (docs/superpowers/specs/2026-08-30-smart-collections-design.md). Prefixed
    // "Novel" rather than "Book" to avoid colliding with the "Book-collection text" group above,
    // which is CE's physical-book-collector metadata on a comic Issue, unrelated to Paperbunkr's
    // Book entity (an EPUB/PDF novel).
    NovelTitle,
    NovelAuthor,
    NovelSeries,
    NovelFormat,
    NovelSummary,
    NovelFinished,
    NovelChapterCount,
    NovelAdded,
    NovelOpened,
    NovelPublished,

    // Smart features S1 (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.1). Stored by name (string
    // conversion), so appending here needs no migration. Not in CE, which has only a series gap count.

    /// <summary>Series-target only - issues not yet read (95% rule; in-progress counts as unread), placeholders excluded.</summary>
    UnreadCount,

    /// <summary>Series-target only - issues read (95% rule), placeholders excluded.</summary>
    ReadCount,

    /// <summary>Series-target only - whole days since the newest open/reading event across the series; a never-opened series has no value and never matches.</summary>
    DaysSinceLastRead,

    /// <summary>Issue-target only - the issue has at least one Pending <see cref="MetadataProposal"/>.</summary>
    HasPendingProposal,

    // Appended, never inserted: PluginCondition (and so OrganizerProfile.ExcludeRuleJson) serializes this
    // enum as a number. Added for ComicRack smart-list import (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md).

    /// <summary>CE's <c>ComicBookNumberMatcher</c>: the issue number parsed to a float, -1 when it is not a number. <see cref="Number"/> stays Text on purpose, so this is the numeric view of it.</summary>
    NumberValue,

    /// <summary>CE's <c>ComicBookPublishedMatcher</c>: the date built from Year/Month/Day (<c>ComicBook.Published</c>). Distinct from <see cref="Released"/>.</summary>
    Published,

    /// <summary>CE's <c>SmartListSeriesLastNumberMatcher</c> ("Series: Last Number"): the highest numeric issue number among every comic of the same series and volume.</summary>
    SeriesLastNumber,

    /// <summary>CE's <c>SmartListSeriesMaxYearMatcher</c> ("Series: Last Year").</summary>
    SeriesMaxYear,

    /// <summary>CE's <c>SmartListSeriesMaxCountMatcher</c> ("Series: Highest Count").</summary>
    SeriesMaxCount,

    /// <summary>CE's <c>SmartListSeriesMaxGapSizeMatcher</c> ("Series: Biggest Gap").</summary>
    SeriesMaxGapSize,

    /// <summary>
    /// The books that have a proposed value (an accepted, filename-inferred <c>MetadataProposal</c>) for a field whose own stored value is empty - ComicRack's "Proposed Values"
    /// script (docs/superpowers/specs/2026-10-11-proposed-values-design.md). Special-cased in <see cref="SmartLists.SmartListQueryBuilder"/> like <see cref="Duplicate"/>: operator
    /// Is = has one, IsNot = has none; <see cref="SmartListCondition.Value"/> is the field list (<c>number, year</c>; blank = every field).
    /// </summary>
    HasProposedValue,

    /// <summary>
    /// ComicRack's "Same X and different Y" script (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §1): the books in a group that share the values of the X attributes
    /// (<see cref="SmartListCondition.Value"/>, <c>|</c>-separated) and, when Y (<see cref="SmartListCondition.Value2"/>) is given, differ in one of the Y attributes. Special-cased in
    /// <see cref="SmartLists.SmartListQueryBuilder"/> like <see cref="Duplicate"/>; the operator Is = flagged, IsNot = not.
    /// </summary>
    SameXDifferentY,
}
