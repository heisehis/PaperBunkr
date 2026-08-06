namespace Paperbunkr.Data.Entities;

/// <summary>
/// A single comic/manga issue file — ported concept from ComicRackCE's <c>ComicBook</c> (which
/// itself extends <c>ComicInfo</c>, the ComicInfo.xml-standard metadata carrier; see
/// src/Paperbunkr.Engine/ComicBook.cs and ComicInfo.cs). Retargeted per docs/onboarding.md §6:
/// gains a <see cref="SeriesId"/> FK now that Series is a first-class entity, drops the fields
/// promoted to series-level (Series name itself, Publisher/Genre/Summary duplicate, and the
/// per-issue <c>SeriesComplete</c>/<c>Manga</c> flags — see <see cref="Series"/>), and adds
/// <see cref="StoryArcNumber"/> and <see cref="ReadingModeOverride"/>, both new.
/// </summary>
public class Issue
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    // --- ComicInfo.xml-standard fields carried over from ComicInfo.cs ---

    public string? Title { get; set; }

    public string? Number { get; set; }

    public int? Count { get; set; }

    public int? Volume { get; set; }

    public string? AlternateSeries { get; set; }

    public string? AlternateNumber { get; set; }

    public string? StoryArc { get; set; }

    /// <summary>
    /// New field — confirmed absent from CE's ComicInfo entirely (docs/onboarding.md §6). This is
    /// what the Comic Vine Scraper fork's <c>comicinfo_patch.py</c> is designed to write.
    /// </summary>
    public string? StoryArcNumber { get; set; }

    public string? SeriesGroup { get; set; }

    public string? Summary { get; set; }

    public string? Notes { get; set; }

    public string? Review { get; set; }

    public int? Year { get; set; }

    public int? Month { get; set; }

    public int? Day { get; set; }

    // --- credits (ComicInfo.cs) ---

    public string? Writer { get; set; }

    public string? Penciller { get; set; }

    public string? Inker { get; set; }

    public string? Colorist { get; set; }

    public string? Letterer { get; set; }

    public string? CoverArtist { get; set; }

    public string? Editor { get; set; }

    public string? Translator { get; set; }

    public string? Publisher { get; set; }

    public string? Imprint { get; set; }

    public string? Genre { get; set; }

    public string? Web { get; set; }

    public int? PageCount { get; set; }

    public string? LanguageISO { get; set; }

    public string? Format { get; set; }

    public string? AgeRating { get; set; }

    public string? Characters { get; set; }

    public string? Teams { get; set; }

    public string? Locations { get; set; }

    public string? Tags { get; set; }

    /// <summary>
    /// Nullable escape hatch for a single issue (e.g. a one-shot) that reads differently from its
    /// series' default <see cref="Entities.ReadingMode"/>. New (docs/onboarding.md §6).
    /// </summary>
    public ReadingMode? ReadingModeOverride { get; set; }

    // --- read-state / file fields carried over from ComicBook.cs ---

    public string? FilePath { get; set; }

    public DateTime? AddedTime { get; set; }

    public DateTime? ReleasedTime { get; set; }

    public DateTime? OpenedTime { get; set; }

    public int? LastPageRead { get; set; }

    public bool FileIsMissing { get; set; }

    public string? CustomThumbnailKey { get; set; }

    // --- fields added for Smart Lists CE-parity (docs/superpowers/specs/2026-08-06-smart-lists-design.md) ---

    /// <summary>CE: <c>ComicBook.Checked</c> — arbitrary user-toggleable mark, unrelated to read state.</summary>
    public bool Checked { get; set; }

    /// <summary>
    /// CE: <c>ComicInfo.MainCharacterOrTeam</c>. A genuine ComicInfo.xml-standard field that was
    /// missed in the original port of this class — not a new-for-Paperbunkr concept.
    /// </summary>
    public string? MainCharacterOrTeam { get; set; }

    /// <summary>CE: <c>ComicBook.Rating</c> ("My Rating"). Drives the Favorites smart list (Rating &gt; 3).</summary>
    public float? Rating { get; set; }

    public float? CommunityRating { get; set; }

    public string? ISBN { get; set; }

    public string? ScanInformation { get; set; }

    public bool BlackAndWhite { get; set; }

    /// <summary>CE: <c>ComicBook.FileSize</c>, bytes.</summary>
    public long? FileSize { get; set; }

    public DateTime? FileModifiedTime { get; set; }

    public DateTime? FileCreationTime { get; set; }

    /// <summary>CE: <c>ComicBook.NewPages</c> — count of pages added since last read.</summary>
    public int? NewPages { get; set; }

    // --- "book collection" fields (CE: ComicBook.Book*) — no editor UI yet, smart-list-only
    // until a dedicated Book Collection panel is built as a separate future feature. ---

    public string? BookAge { get; set; }

    public string? BookCollectionStatus { get; set; }

    public string? BookCondition { get; set; }

    public string? BookLocation { get; set; }

    public string? BookNotes { get; set; }

    public string? BookOwner { get; set; }

    public float? BookPrice { get; set; }

    public string? BookStore { get; set; }

    public List<IssueCustomValue> CustomValues { get; set; } = new();

    /// <summary>
    /// True when this row was auto-created to stand in for an unmatched Reading List import row
    /// (docs/superpowers/specs/2026-08-06-reading-lists-design.md §2), not a real book the user
    /// added — distinguishes that from a real book the user separately flagged
    /// <see cref="FileIsMissing"/> for. Adapted from CBLManager's <c>PlaceholderTagKey</c> custom-
    /// value workaround (needed there only because CE's plugin API had no free metadata slot on a
    /// book); a real column here since Paperbunkr owns its schema.
    /// </summary>
    public bool IsPlaceholder { get; set; }
}
