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
    /// Real source of truth as of docs/superpowers/specs/2026-08-17-metadata-model-phase1-canonical-
    /// metadata-design.md - has no CE precedent at all (a deliberate new feature, not parity).
    /// </summary>
    public SeriesStatus Status { get; set; } = SeriesStatus.Unknown;

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

    /// <summary>Issue whose cover thumbnail represents the series (e.g. in library grid views).</summary>
    public int? CoverIssueId { get; set; }

    public Issue? CoverIssue { get; set; }

    public List<Issue> Issues { get; set; } = new();

    public List<Category> Categories { get; set; } = new();

    public List<TrackingLink> TrackingLinks { get; set; } = new();

    /// <summary>M:M with <see cref="Continuity"/> (docs/superpowers/specs/2026-08-17-metadata-model-phase4a-continuity-design.md) - a series can belong to more than one continuity.</summary>
    public List<Continuity> Continuities { get; set; } = new();
}
