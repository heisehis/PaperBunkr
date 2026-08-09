namespace Paperbunkr.Data.Entities;

/// <summary>
/// Every condition field the smart-list rule builder can filter on, matching CE's
/// <c>ComicBook*Matcher</c> catalog field-for-field (docs/superpowers/specs/2026-08-06-smart-lists-design.md
/// §4) except <c>AllProperties</c> and <c>Week</c>, both deliberately deferred/omitted per that
/// spec. <see cref="SmartListCatalog"/> maps each value to its label, <see cref="SmartListDataType"/>,
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

    // ComicInfo text fields
    Title,
    Number,
    Volume,
    Count,
    AlternateSeries,
    AlternateNumber,
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
}
