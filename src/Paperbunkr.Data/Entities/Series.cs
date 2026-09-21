namespace Paperbunkr.Data.Entities;

/// <summary>
/// A comic/manga series — new first-class entity (docs/onboarding.md §6). CE has no equivalent:
/// its <c>ComicBook</c>/<c>ComicInfo</c> only carries a flat <c>Series</c> string field, with
/// series-level facts (like <c>SeriesComplete</c>) duplicated onto every issue for lack of
/// anywhere else to put them. Elevating Series to a real entity fixes that, matching how Mihon
/// separates Manga from Chapter.
/// </summary>
public class Series
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? SortName { get; set; }

    public ContentType ContentType { get; set; } = ContentType.Unknown;

    public ReadingMode ReadingMode { get; set; } = ReadingMode.LeftToRight;

    /// <summary>
    /// Series-level double-page spread default (docs/superpowers/specs/2026-08-15-reader-double-page-
    /// spread-design.md §2). Nullable, unlike <see cref="ReadingMode"/> above - <see langword="null"/>
    /// means "inherit <c>AppSettings.DefaultPageLayoutMode</c>," needed so that global layer can act
    /// as a live fallback rather than only ever seeding a new row once. Set via the Reader's own
    /// double-page toggle (mirrors <see cref="ReadingMode"/>'s own toggle shape) - not editable from
    /// any metadata-editing screen, matching how <see cref="ReadingMode"/> itself is written only from
    /// the Reader.
    /// </summary>
    public PageLayoutMode? PageLayoutMode { get; set; }

    /// <summary>
    /// Series-level default page-fit mode (docs/superpowers/specs/2026-09-21-comic-reader-flow-and-defaults-
    /// design.md §2). <see langword="null"/> falls through to <c>AppSettings.DefaultPageFitMode</c>; an
    /// issue's own <see cref="Issue.PageFitModeOverride"/> still wins. Written only by the Reader's
    /// "Apply to series" action, cleared from the detail screens.
    /// </summary>
    public ImageFitMode? PageFitModeOverride { get; set; }

    /// <summary>Series-level default for the auto-rotate-landscape-pages toggle, same shape as <see cref="PageFitModeOverride"/>.</summary>
    public bool? AutoRotateOverride { get; set; }

    /// <summary>
    /// Real source of truth as of docs/superpowers/specs/2026-08-17-metadata-model-phase1-canonical-
    /// metadata-design.md - has no CE precedent at all (a deliberate new feature, not parity).
    /// </summary>
    public SeriesStatus Status { get; set; } = SeriesStatus.Unknown;

    /// <summary>
    /// The user's own reading-progress relationship with this series (Planned/Reading/Completed/
    /// Paused/Dropped/ReReading) - independent of <see cref="Status"/> above, which is the
    /// publisher's status, not the reader's. New (docs/superpowers/specs/2026-08-19-metadata-model-
    /// reading-status-design.md). Set automatically to <see cref="Entities.ReadingStatus.Reading"/>
    /// on an issue's first open (<c>ReaderScreenViewModel.Load</c>) when still
    /// <see cref="Entities.ReadingStatus.Unknown"/> or <see cref="Entities.ReadingStatus.Planned"/>;
    /// every other transition is user-driven (Library context menu, Bulk Edit) - matching this
    /// codebase's existing minimal-automation stance for series-level state.
    /// </summary>
    public ReadingStatus ReadingStatus { get; set; } = ReadingStatus.Unknown;

    /// <summary>
    /// Computed, not stored - <c>Status == Completed</c>. Kept as a property (not removed outright)
    /// so every existing reader (Smart Lists' <c>SeriesComplete</c> selector, Detail screen) keeps
    /// working unchanged; avoids a second source of truth per this phase's design doc.
    /// </summary>
    public bool IsComplete => Status == SeriesStatus.Completed;

    /// <summary>Populated once at CE-migration time. Not the current source of truth for filtering/display — see <see cref="Issue.Publisher"/>.</summary>
    public string? Publisher { get; set; }

    /// <summary>Populated once at CE-migration time. Not the current source of truth for filtering/display — see <see cref="Issue.Genre"/>.</summary>
    public string? Genre { get; set; }

    public string? Summary { get; set; }

    /// <summary>Average of the series' rated <see cref="Issue.Rating"/> values (docs/superpowers/
    /// specs/2026-09-18-per-tracker-score-and-finish-date-design.md), recomputed by
    /// <c>SeriesRatingResolver.Recompute</c> - distinct from any single issue's own rating, and
    /// distinct from a tracker's remote score (which is display-only unless explicitly pulled in
    /// via that tracker's own "Use this score" action, per the same design doc).</summary>
    public float? Rating { get; set; }

    /// <summary>Provider-sourced creator credit (dedup-joined author/artist/staff names, docs/
    /// superpowers/specs/2026-09-18-external-metadata-full-extraction-design.md §3) - new, no CE
    /// precedent (CE has no series-level creator concept, only the per-issue ComicInfo.xml credit
    /// fields on <see cref="Issue"/>). Not a fallback source for those per-issue fields.</summary>
    public string? Creator { get; set; }

    /// <summary>Non-null for a series mirrored from another instance (see <see cref="RemoteSource"/>). Name-based series lookups for local work must exclude these, or a local scan could attach files to a remote series of the same name.</summary>
    public int? RemoteSourceId { get; set; }

    public RemoteSource? RemoteSource { get; set; }

    /// <summary>The host's own <see cref="Id"/> for this series.</summary>
    public int? RemoteSeriesId { get; set; }

    /// <summary>Issue whose cover thumbnail represents the series (e.g. in library grid views).</summary>
    public int? CoverIssueId { get; set; }

    public Issue? CoverIssue { get; set; }

    /// <summary>Alternate (native/romanized/localized) titles - see <see cref="SeriesTitle"/>'s own doc comment. <see cref="Name"/> above remains the primary/sort title.</summary>
    public List<SeriesTitle> Titles { get; set; } = new();

    public List<Issue> Issues { get; set; } = new();

    /// <summary>
    /// True once the user has dismissed this series's "Empty Rows" listing in Library Health
    /// (docs/superpowers/specs/2026-09-17-series-name-matching-and-empty-row-cleanup-design.md) -
    /// a zero-<see cref="Issue"/> series they've confirmed is fine to keep (e.g. a deliberate
    /// placeholder). Same review-queue-only contract as <see cref="Issue.MissingAcknowledged"/>.
    /// </summary>
    public bool EmptyRowAcknowledged { get; set; }

    /// <summary>
    /// True once the "Open the tracker link panel automatically" behavior has fired for this series
    /// (docs/superpowers/specs/2026-09-18-tracker-behavior-settings-design.md §3.1) - one-shot per
    /// series ever, set only after the panel actually opened.
    /// </summary>
    public bool TrackerPromptShown { get; set; }

    /// <summary>
    /// Collections this series belongs to, reached via the polymorphic <see cref="CollectionItem"/>
    /// join (docs/superpowers/specs/2026-08-27-collections-design.md) — replaces the old implicit
    /// <c>Category</c> M:M skip-navigation.
    /// </summary>
    public List<CollectionItem> CollectionItems { get; set; } = new();

    public List<TrackingLink> TrackingLinks { get; set; } = new();

    /// <summary>M:M with <see cref="Continuity"/> (docs/superpowers/specs/2026-08-17-metadata-model-phase4a-continuity-design.md) - a series can belong to more than one continuity. Through the explicit <see cref="ContinuityMembership"/> join entity since docs/superpowers/specs/2026-08-28-continuity-editing-design.md.</summary>
    public List<ContinuityMembership> ContinuityMemberships { get; set; } = new();

    /// <summary>Series-scoped proposals (Summary/Status/Genre) - see <see cref="MetadataProposal.SeriesId"/>'s own doc comment (docs/superpowers/specs/2026-08-23-apply-from-provider-design.md). Mirrors <see cref="Issue.MetadataProposals"/>.</summary>
    public List<MetadataProposal> MetadataProposals { get; set; } = new();
}
