using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Library grid card. Originally sample data mirroring the "covers" array from the "Paperbunkr
/// App" Claude Design wireframe (project 43c40b25); now also buildable from a real
/// <see cref="Series"/> record (docs/onboarding.md §5-6) via <see cref="FromSeries"/>. An
/// <see cref="ObservableObject"/> since Slice 3 (docs/superpowers/specs/2026-08-24-library-
/// multiselect-slice3-design.md) - same "was a plain init-only POCO, converted for live-notifying
/// IsSelected" treatment <see cref="IssueListRow"/> got in Slice 1.
/// </summary>
public sealed partial class SeriesCardSample : ObservableObject, ISelectableCard
{
    /// <summary>Explicit implementation - <see cref="SeriesId"/> is this model's real, long-established
    /// public name for the same value; <see cref="ISelectableCard"/> only needs an <c>Id</c> accessor
    /// reachable through the interface, not a second public property duplicating it.</summary>
    int ISelectableCard.Id => SeriesId;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Only set when this card represents a <see cref="Paperbunkr.Data.Entities.ContinuityMembership"/>
    /// on the Events &amp; Continuity screen (docs/superpowers/specs/2026-08-28-continuity-editing-
    /// design.md, Part D) - the membership's free-text note. Null/blank everywhere else.
    /// </summary>
    [ObservableProperty]
    private string? _membershipNote;

    /// <summary>True while the inline note editor for this continuity member is open.</summary>
    [ObservableProperty]
    private bool _isEditingNote;

    partial void OnMembershipNoteChanged(string? value) => OnPropertyChanged(nameof(HasMembershipNote));

    public bool HasMembershipNote => !string.IsNullOrWhiteSpace(MembershipNote);

    public const double PanoramaHeight = 146;
    public const double PanoramaMinWidth = 110;
    public const double PanoramaMaxWidth = 320;
    public const double DefaultCoverAspectRatio = 2.0 / 3.0; // standard portrait comic cover

    public int SeriesId { get; init; }
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required string Sub { get; init; }
    public string? Publisher { get; init; }
    public required string ContentTypeLabel { get; init; }

    /// <summary>Series-level current values, for the series-card context menu's radio ✓ marks
    /// (mirrors <see cref="ContentTypeLabel"/>). Raw enum names, e.g. "Ongoing" / "RightToLeft".</summary>
    public string? SeriesStatusLabel { get; init; }
    public string? ReadingStatusLabel { get; init; }
    public string? ReadingDirectionLabel { get; init; }

    /// <summary>Gates the series-card context menu's Reading Direction submenu (docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md §2) - only meaningful once a series is classified as manga-family.</summary>
    public bool IsMangaFamily => ContentTypeLabel is "Manga" or "Manhua" or "Manhwa";
    public int IssueCount { get; init; }

    /// <summary>Home screen's count-pill badge text (docs/superpowers/specs/2026-08-18-home-screen-
    /// design.md) - a plain <c>StringFormat</c> can't singularize "1 issue" on its own.</summary>
    public string IssueCountLabel => IssueCount == 1 ? "1 issue" : $"{IssueCount} issues";

    /// <summary>Home screen's Recently Added row badge (docs/superpowers/specs/2026-08-24-home-
    /// screen-design.md) - "New" when this series' newest issue was actually added recently (real
    /// signal from <see cref="LastAddedTime"/>, not a hardcoded label), falling back to
    /// <see cref="IssueCountLabel"/> once it ages out of that window. 7 days is a fresh, reasonable
    /// default picked for this - no existing "recent" threshold precedent elsewhere in this codebase
    /// to match (checked before picking a number).</summary>
    public string RecentAddBadgeLabel =>
        LastAddedTime is DateTime added && DateTime.UtcNow - added <= TimeSpan.FromDays(7) ? "New" : IssueCountLabel;

    public int UnreadCount { get; init; }
    public bool HasUnread => UnreadCount > 0;
    public bool Missing { get; init; }
    public required IBrush CoverBrush { get; init; }

    /// <summary>Series-level sort aggregates (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase C) - computed once here, not recomputed per sort click.</summary>
    public DateTime? LastAddedTime { get; init; }
    public DateTime? LastOpenedTime { get; init; }
    public long TotalFileSize { get; init; }

    /// <summary>Cover issue's <c>LanguageISO</c> (docs/superpowers/specs/2026-08-09-library-toolbar-design.md Phase D overlay toggle) - raw ISO code, e.g. "en"/"ja".</summary>
    public string? LanguageIso { get; init; }

    /// <summary>First unread issue in reading order, or <see langword="null"/> if every issue is read - backs the Continue Reading overlay button.</summary>
    public int? ContinueReadingIssueId { get; init; }

    public bool HasContinueReading => ContinueReadingIssueId is not null;
    public bool HasPublisher => !string.IsNullOrWhiteSpace(Publisher);
    public bool HasLanguage => !string.IsNullOrWhiteSpace(LanguageIso);

    /// <summary>Drives "Show in Explorer"'s IsEnabled (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §1) - false only when every issue in the series is a fileless placeholder.</summary>
    public bool HasFile { get; init; }

    /// <summary>
    /// Panorama grid's per-series tile width (docs/superpowers/specs/
    /// 2026-08-09-library-toolbar-design.md Phase A) - computed from the real cover bitmap's
    /// aspect ratio at a fixed <see cref="PanoramaHeight"/>, clamped so no cover renders absurdly
    /// thin or wide. Landscape covers render wide, portrait covers render narrow, side by side in
    /// the same row - not a single fixed crop box. Falls back to a standard portrait ratio before
    /// a real cover's been generated; re-tunes automatically once one exists.
    /// </summary>
    public double PanoramaWidth { get; init; }

    /// <summary>
    /// Cover issue id, resolved to a <see cref="Bitmap"/> lazily via <c>CoverImageConverter</c>
    /// (docs/superpowers/specs/2026-08-22-cover-memory-virtualization-design.md) rather than eagerly
    /// decoded here - the whole point of <c>VirtualizingWrapPanel</c> is that a card not currently
    /// realized never triggers a decode at all. Null if the series has no issues to derive a cover
    /// from. <see cref="CoverBrush"/> is the fallback the UI shows underneath while a cover is
    /// missing/not yet generated.
    /// </summary>
    public int? CoverIssueId { get; init; }

    /// <summary>
    /// A flat <see cref="IssueListRow"/> standing in for this series when Library's <b>single</b>
    /// sort/group field pool (2026-09-03 unification - <c>IssueListFieldCatalog</c>) is applied to
    /// series cards. Built from the cover issue (else the first by number), so "sort series by
    /// Writer" means "by the cover issue's Writer"; <c>SeriesIssueCount</c>/<c>SeriesUnreadCount</c>
    /// on it carry the whole-series aggregates. Never null - an issue-less series gets a minimal
    /// row carrying just the series-level fields.
    /// </summary>
    public required IssueListRow RepresentativeRow { get; init; }

    public static IBrush Gradient(string fromHex, string toHex) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(fromHex), 0),
            new GradientStop(Color.Parse(toHex), 1),
        },
    };

    // Same palette used throughout the wireframe's own sample covers - picked deterministically
    // per series (by name hash) since there's no real cover-art decode pipeline yet (that's the
    // reader canvas work in docs/onboarding.md §8), just to keep the grid visually varied.
    private static readonly (string From, string To)[] s_palette =
    {
        ("#3a2f45", "#8a4a2e"),
        ("#1e3a3f", "#2f7d6a"),
        ("#442a1c", "#c9803f"),
        ("#26313f", "#4a6b8a"),
        ("#3f2130", "#a34a5c"),
        ("#1f2a1c", "#5c8a4a"),
        ("#2a2333", "#6a5ca3"),
        ("#332118", "#8a5a2e"),
    };

    // string.GetHashCode() is randomized per process in .NET Core - not stable across app
    // restarts, which would make the "same series, same color" property this palette pick
    // relies on flip every launch. FNV-1a is a plain, stable, non-cryptographic hash.
    private static uint StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (char c in value)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }

    /// <summary>The same deterministic per-series-name cover gradient <see cref="FromSeries"/> uses,
    /// exposed standalone for screens (e.g. Reader) that need just the color, not a full card.</summary>
    public static IBrush CoverBrushFor(string seriesName)
    {
        var (from, to) = s_palette[StableHash(seriesName) % (uint)s_palette.Length];
        return Gradient(from, to);
    }

    /// <summary>
    /// Pure clamp math backing <see cref="PanoramaWidth"/> (docs/superpowers/specs/
    /// 2026-08-09-library-toolbar-design.md Phase A), extracted for direct testing the same way
    /// <c>ZoomPanMath</c>/<c>GridKeyboardNavigation</c> already separate their pure geometry from
    /// the Avalonia-bitmap-touching caller.
    /// </summary>
    public static double ComputePanoramaWidth(double aspectRatio) =>
        Math.Clamp(aspectRatio * PanoramaHeight, PanoramaMinWidth, PanoramaMaxWidth);

    public static SeriesCardSample FromSeries(Series series)
    {
        int unreadCount = series.Issues.Count(i => i.LastPageRead is null or 0);

        var coverIssue = series.Issues.FirstOrDefault(i => i.Id == series.CoverIssueId)
            ?? series.Issues.OrderByNumber().FirstOrDefault();

        // Always the default ratio, never a real decoded aspect ratio - Panorama's per-cover
        // variable width relied on eagerly decoding every card's cover just to measure it, which is
        // exactly the eager-decode-regardless-of-visibility behavior this lazy CoverIssueId design
        // exists to eliminate. Traded off deliberately (confirmed with the user): Panorama cards
        // render at a uniform default width now instead of shape-adapting per cover.
        double aspectRatio = DefaultCoverAspectRatio;

        return new SeriesCardSample
        {
            SeriesId = series.Id,
            Title = series.Name.ToUpperInvariant(),
            Name = series.Name,
            RepresentativeRow = coverIssue is not null
                ? IssueListRow.FromIssue(coverIssue, series)
                : new IssueListRow
                {
                    SeriesId = series.Id,
                    SeriesName = series.Name,
                    Title = series.Name,
                    CoverBrush = CoverBrushFor(series.Name),
                    ContentTypeLabel = series.ContentType.ToString(),
                    SeriesStatusLabel = series.Status.ToString(),
                    ReadingStatusLabel = series.ReadingStatus.ToString(),
                    ReadingDirectionLabel = series.ReadingMode.ToString(),
                    Publisher = series.Publisher,
                    PanoramaWidth = ComputePanoramaWidth(aspectRatio),
                },
            Sub = $"{series.ContentType} · {series.Issues.Count} issues",
            Publisher = series.Publisher,
            ContentTypeLabel = series.ContentType.ToString(),
            SeriesStatusLabel = series.Status.ToString(),
            ReadingStatusLabel = series.ReadingStatus.ToString(),
            ReadingDirectionLabel = series.ReadingMode.ToString(),
            IssueCount = series.Issues.Count,
            UnreadCount = unreadCount,
            Missing = series.Issues.Any(i => i.FileIsMissing),
            HasFile = series.Issues.Any(i => !string.IsNullOrEmpty(i.FilePath)),
            CoverBrush = CoverBrushFor(series.Name),
            CoverIssueId = coverIssue?.Id,
            PanoramaWidth = ComputePanoramaWidth(aspectRatio),
            // LINQ's nullable Max() returns null for an all-null or empty sequence rather than throwing.
            LastAddedTime = series.Issues.Select(i => i.AddedTime).Max(),
            LastOpenedTime = series.Issues.Select(i => i.OpenedTime).Max(),
            TotalFileSize = series.Issues.Sum(i => i.FileSize ?? 0),
            LanguageIso = coverIssue?.LanguageISO,
            ContinueReadingIssueId = series.Issues.OrderByNumber().FirstOrDefault(i => i.LastPageRead is null or 0)?.Id,
        };
    }
}
