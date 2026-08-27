namespace Paperbunkr.Data.Entities;

/// <summary>
/// A single comic/manga issue file — ported concept from ComicRackCE's <c>ComicBook</c> (which
/// itself extends <c>ComicInfo</c>, the ComicInfo.xml-standard metadata carrier; see
/// src/Paperbunkr.Engine/ComicBook.cs and ComicInfo.cs). Retargeted per docs/onboarding.md §6:
/// gains a <see cref="SeriesId"/> FK now that Series is a first-class entity, and drops the
/// per-issue <c>SeriesComplete</c>/<c>Manga</c> flags (genuinely series-only, no per-issue
/// equivalent — see <see cref="Series"/>) plus adds <see cref="StoryArcNumber"/> and
/// <see cref="ReadingModeOverride"/>, both new.
///
/// <see cref="Publisher"/> below was originally meant to be dropped as a series-level duplicate
/// too, per that same onboarding.md §6 plan — reintroduced once the Issue Properties/Bulk editors
/// (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md) made per-issue editing of it a
/// real feature, matching CE's own <c>ComicBook</c> shape (a real per-issue ComicInfo.xml field
/// there too). <see cref="Series.Publisher"/> still exists (populated once at CE-migration time)
/// but is no longer the source of truth once an issue has its own value — see
/// <c>SmartListCatalog.TextSelectors</c>. Genre followed the same path but has since moved again —
/// see <see cref="Tags"/>.
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

    /// <summary>
    /// String, not int - preserves the original display representation (docs/superpowers/specs/
    /// 2026-08-17-metadata-model-phase1-canonical-metadata-design.md), same treatment as
    /// <see cref="Number"/>. Use <c>IssueMetadataExtensions.VolumeSortKey(this Issue)</c> for
    /// sorting, not this raw string.
    /// </summary>
    public string? Volume { get; set; }

    public string? AlternateSeries { get; set; }

    public string? AlternateNumber { get; set; }

    public string? StoryArc { get; set; }

    /// <summary>
    /// Replaces the never-ported per-issue <c>SeriesComplete</c> (docs/superpowers/specs/2026-08-17-
    /// metadata-model-phase1-canonical-metadata-design.md) - "this issue is the one that completes
    /// the series," distinct from <see cref="Series.Status"/> ("this series is known to be
    /// complete"). Schema-present, deliberately dormant this phase - no source to backfill from
    /// (CE's flag was never carried into Paperbunkr's schema) and no editor UI yet.
    /// </summary>
    public bool IsFinalIssue { get; set; }

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

    public string? Web { get; set; }

    public int? PageCount { get; set; }

    public string? LanguageISO { get; set; }

    public string? Format { get; set; }

    public string? AgeRating { get; set; }

    public string? Characters { get; set; }

    public string? Teams { get; set; }

    public string? Locations { get; set; }

    /// <summary>
    /// Weighted, categorized tag values (docs/superpowers/specs/2026-08-23-weighted-categorized-
    /// tags-design.md) - replaces the old flat <c>Genre</c>/<c>Tags</c> CSV strings. Holds both
    /// the old Genre-field and Tags-field values, distinguished by <see cref="IssueTag.Field"/>.
    /// </summary>
    public List<IssueTag> Tags { get; set; } = new();

    /// <summary>
    /// Nullable escape hatch for a single issue (e.g. a one-shot) that reads differently from its
    /// series' default <see cref="Entities.ReadingMode"/>. New (docs/onboarding.md §6).
    /// </summary>
    public ReadingMode? ReadingModeOverride { get; set; }

    /// <summary>
    /// Nullable escape hatch for a single issue that displays best with a different fit mode than
    /// the fixed code default (docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-
    /// controls-design.md §3) - page dimensions/scan quality genuinely vary issue-to-issue in a way
    /// reading direction doesn't, unlike <see cref="ReadingModeOverride"/> which is Series-scoped.
    /// </summary>
    public ImageFitMode? PageFitModeOverride { get; set; }

    /// <summary>
    /// Nullable escape hatch for a single issue that should override the series' double-page spread
    /// default (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md §2) - same
    /// shape as <see cref="PageFitModeOverride"/>. Schema-present but has no writer UI this pass
    /// (dormant), same accepted precedent <see cref="ReadingModeOverride"/> already has here.
    /// </summary>
    public PageLayoutMode? PageLayoutModeOverride { get; set; }

    /// <summary>Same override shape as <see cref="PageFitModeOverride"/>, for the auto-rotate-landscape-pages toggle.</summary>
    public bool? AutoRotateOverride { get; set; }

    /// <summary>
    /// Nullable per-issue escape hatches for live image adjustment (docs/superpowers/specs/
    /// 2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §9), same override
    /// shape as <see cref="PageFitModeOverride"/>. Effective value is additive with the global
    /// <c>AppSettings.Default*</c> counterpart (<c>global + (override ?? 0)</c>), matching
    /// ComicRackCE's own <c>BitmapAdjustment.Add(BaseColorAdjustment, Comic.ColorAdjustment)</c>.
    /// </summary>
    public float? BrightnessOverride { get; set; }

    /// <summary>See <see cref="BrightnessOverride"/>.</summary>
    public float? ContrastOverride { get; set; }

    /// <summary>See <see cref="BrightnessOverride"/>.</summary>
    public float? SaturationOverride { get; set; }

    /// <summary>See <see cref="BrightnessOverride"/>.</summary>
    public float? GammaOverride { get; set; }

    // --- read-state / file fields carried over from ComicBook.cs ---

    public string? FilePath { get; set; }

    public DateTime? AddedTime { get; set; }

    public DateTime? ReleasedTime { get; set; }

    public DateTime? OpenedTime { get; set; }

    /// <summary>
    /// Real counter (docs/superpowers/specs/2026-08-17-metadata-model-phase1-canonical-metadata-
    /// design.md), unlike CE's own <c>OpenedCount</c> which only ever flips 0→1 on mark-as-read.
    /// Incremented alongside <see cref="OpenedTime"/> wherever an issue is actually opened for
    /// reading (<c>ReaderScreenViewModel.Load</c>).
    /// </summary>
    public int OpenCount { get; set; }

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

    /// <summary>
    /// Replaces the old <c>bool BlackAndWhite</c> (docs/superpowers/specs/2026-08-17-metadata-
    /// model-phase1-canonical-metadata-design.md) - see <see cref="Entities.ColorMode"/>'s own doc
    /// comment for the CE-deviation note.
    /// </summary>
    public ColorMode ColorMode { get; set; } = ColorMode.Unknown;

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

    /// <summary>Page-labeled bookmarks (docs/superpowers/specs/2026-08-17-metadata-model-phase1-canonical-metadata-design.md), mirrors <see cref="BookBookmark"/>'s shape. BookmarkCount is computed as <c>Bookmarks.Count</c>, never stored.</summary>
    public List<IssueBookmark> Bookmarks { get; set; } = new();

    /// <summary>
    /// Filename/embedded-metadata-inferred field proposals (docs/superpowers/specs/2026-08-17-
    /// metadata-model-phase2a-metadata-proposals-design.md), CE's <c>Shadow*</c>/<c>Proposed*</c>
    /// pattern made real and auditable. Use <c>IssueMetadataExtensions.Effective*(this Issue)</c>
    /// to read a field with proposals factored in, not this collection or the raw field directly.
    /// </summary>
    public List<MetadataProposal> MetadataProposals { get; set; } = new();

    /// <summary>
    /// True when this row was auto-created to stand in for an unmatched Reading List import row
    /// (docs/superpowers/specs/2026-08-06-reading-lists-design.md §2), not a real book the user
    /// added — distinguishes that from a real book the user separately flagged
    /// <see cref="FileIsMissing"/> for. Adapted from CBLManager's <c>PlaceholderTagKey</c> custom-
    /// value workaround (needed there only because CE's plugin API had no free metadata slot on a
    /// book); a real column here since Paperbunkr owns its schema.
    /// </summary>
    public bool IsPlaceholder { get; set; }

    /// <summary>
    /// True once the user has dismissed this issue's missing-file state from the Needs Review
    /// queue ("I know this one's missing, stop asking") without relinking or removing it
    /// (docs/superpowers/specs/2026-08-06-migration-ux-polish-design.md §2). Review-queue concept
    /// only - the system "Missing Files" smart list ignores this and still shows every missing
    /// file when browsed directly.
    /// </summary>
    public bool MissingAcknowledged { get; set; }
}
