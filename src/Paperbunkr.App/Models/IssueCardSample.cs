using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// Sample data for an issue cover tile (Detail screen's Issues tab). An <see cref="ObservableObject"/>
/// (was a plain init-only POCO) since <see cref="IsSelected"/> (docs/superpowers/specs/
/// 2026-08-07-bulk-issue-editing-design.md §1) needs live change notification for the tile's
/// selected-border visual state.
/// </summary>
public sealed partial class IssueCardSample : ObservableObject, ISelectableCard
{
    public ObservableCollection<DetailListCell> ListCells { get; } = new();

    // --- Detail layout v2 (docs/superpowers/specs/2026-10-19-detail-layouts-v2-design.md). Set by DetailTabsViewModel.ConfigureIssueCard
    // from the layout; the tile templates bind them. Unused by the Smart Lists grid. ---

    /// <summary>The Library's own row for this issue, so captions, columns and card text use the Library's field projections. Null outside the detail screen.</summary>
    public IssueListRow? Row { get; init; }

    /// <summary>Width / height of the cover's aspect ratio, for the Panorama cover fit.</summary>
    public double CoverAspect { get; init; } = SeriesCardSample.DefaultCoverAspectRatio;

    /// <summary>The Poster tile's caption lines, top to bottom (the first one bold). Empty when captions are hidden.</summary>
    public IReadOnlyList<string> CaptionLines { get; set; } = System.Array.Empty<string>();

    public bool ShowPosterStatus { get; set; } = true;
    public bool ShowPosterEdition { get; set; } = true;
    public bool ShowCardStatus { get; set; } = true;
    public bool ShowPosterReadStatus => ShowPosterStatus && IsRead;
    public bool ShowPosterProgressStatus => ShowPosterStatus && IsInProgress;
    public bool ShowPosterEditionRibbon => ShowPosterEdition && HasEditionRibbon;
    public bool ShowCardReadStatus => ShowCardStatus && IsRead;
    public bool ShowCardProgressStatus => ShowCardStatus && IsInProgress;
    public bool ShowCardUnreadStatus => ShowCardStatus && !IsRead && !IsInProgress;

    /// <summary>Card headline (the Title tile element). Null hides it.</summary>
    public string? CardHeadline { get; set; }

    public bool ShowCardTitle => !string.IsNullOrWhiteSpace(CardHeadline);

    /// <summary>Card second line: every other tile element, joined with " · ".</summary>
    public string CardSecondLine { get; set; } = string.Empty;

    public bool ShowCardProgress => !string.IsNullOrWhiteSpace(CardSecondLine);

    public double PosterCoverWidth { get; set; } = 92;
    public double PosterCoverHeight { get; set; } = 131;
    public double CardCoverWidth { get; set; } = 54;
    public double CardCoverHeight { get; set; } = 80;

    /// <summary>Tells already-realized tiles that the detail screen re-dressed this card for a new layout.</summary>
    public void RefreshLayoutBindings()
    {
        foreach (string name in new[]
        {
            nameof(CaptionLines), nameof(ShowPosterReadStatus), nameof(ShowPosterProgressStatus), nameof(ShowPosterEditionRibbon),
            nameof(ShowCardReadStatus), nameof(ShowCardProgressStatus), nameof(ShowCardUnreadStatus),
            nameof(CardHeadline), nameof(ShowCardTitle), nameof(CardSecondLine), nameof(ShowCardProgress),
            nameof(PosterCoverWidth), nameof(PosterCoverHeight), nameof(CardCoverWidth), nameof(CardCoverHeight),
        })
        {
            OnPropertyChanged(name);
        }
    }

    public int Id { get; init; }

    [ObservableProperty]
    private bool _isReadingNow;

    /// <summary>
    /// Owning series id. Unused by Detail's own Issues tab (already scoped to a single series when
    /// it constructs these), but needed by the Smart Lists results grid (docs/superpowers/specs/
    /// 2026-08-09-smart-lists-results-view-design.md), whose results span many series.
    /// </summary>
    public int SeriesId { get; init; }

    /// <summary>Issue number label, e.g. "#5" (or "#?" when unnumbered).</summary>
    public required string Title { get; init; }
    public bool IsUnread { get; init; }
    public required IBrush CoverBrush { get; init; }

    /// <summary>Real decoded cover art, null until "Generate Covers" has processed this issue.</summary>
    public Bitmap? CoverImage { get; init; }

    /// <summary>
    /// Cover issue id for the lazy <c>views:AsyncCoverImage.SourceId</c> binding path - set instead
    /// of <see cref="CoverImage"/> where the grid can be large and virtualized (Smart Lists results:
    /// a list matching hundreds of issues would otherwise decode every JPEG synchronously on the UI
    /// thread while building these). Detail's own Issues tab keeps using <see cref="CoverImage"/> -
    /// it's always scoped to one series' worth of issues.
    /// </summary>
    public int? CoverIssueId { get; init; }

    /// <summary>
    /// <see cref="Paperbunkr.App.Services.CoverFingerprint.Stem"/> for <see cref="CoverIssueId"/> -
    /// what <c>views:AsyncCoverImage.SourceId</c> actually keys its lookup on (docs/superpowers/
    /// specs/2026-08-27-cover-thumbnail-identity-validation-design.md), so a library rebuild that
    /// reassigns <see cref="CoverIssueId"/> can't serve a stale cover for the reused id. Null
    /// whenever <see cref="CoverIssueId"/> is null.
    /// </summary>
    public string? CoverKey { get; init; }

    /// <summary>Null/empty for a fileless placeholder entry (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md).</summary>
    public string? FilePath { get; init; }

    /// <summary>Drives "Show in Explorer"'s IsEnabled without a converter - see FilePath's own doc comment.</summary>
    public bool HasFile => !string.IsNullOrEmpty(FilePath);

    /// <summary>Issue.Format (e.g. "Omnibus", "One Shot", "TPB") - drives the Poster-view tile's
    /// Format badge (docs/superpowers/specs/2026-09-13-detail-screens-redesign-design.md §2). Blank
    /// for the overwhelming majority of tiles (a plain numbered issue), so normal runs stay quiet.</summary>
    public string? Format { get; init; }

    public bool HasFormat => !string.IsNullOrWhiteSpace(Format);

    /// <summary>Only known structured edition/format values receive a small cover ribbon.</summary>
    public bool HasEditionRibbon => EditionRibbonLabel is not null;

    public string? EditionRibbonLabel => NormalizeEditionLabel(Format);

    private static string? NormalizeEditionLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "annual" => "ANNUAL",
            "special" or "one shot" or "oneshot" => "SPECIAL",
            "omnibus" => "OMNIBUS",
            "trade paperback" or "tpb" => "TPB",
            "hardcover" or "hc" => "HC",
            "deluxe" => "DELUXE",
            "absolute" => "ABSOLUTE",
            _ => null,
        };
    }

    // --- Detail-screen Issues-tab List / Card view-mode columns (docs/superpowers/specs/
    //     2026-08-28-detail-screens-streaming-redesign-design.md). Unused by the Smart Lists grid. ---

    /// <summary>The issue's own title (not the number) - Card view's headline.</summary>
    public string? FullTitle { get; init; }

    public string? ArcTitle { get; init; }

    public System.DateTime? CoverDate { get; init; }

    public float? Rating { get; init; }

    /// <summary>Fully read - dims the tile.</summary>
    public bool IsRead { get; init; }

    /// <summary>0..1 read progress - drives the thin in-progress bar.</summary>
    public double ReadFraction { get; init; }

    public bool IsInProgress => ReadFraction is > 0 and < 1;

    /// <summary>Read-state badge for the detail-screen tile (docs/superpowers/specs/2026-09-04-
    /// detail-screen-icons-and-glyphs-design.md §4).</summary>
    public IssueTileGlyph TileGlyph => IssueTileGlyphs.Resolve(IsRead, IsInProgress);

    public string CoverDateLabel => CoverDate?.ToString("MMM yyyy") ?? string.Empty;

    public string RatingLabel => Rating is > 0 ? new string('★', System.Math.Clamp((int)System.Math.Round(Rating.Value), 1, 5)) : "—";

    public string CardProgressLabel => IsInProgress ? $"{Title} · {(int)System.Math.Round(ReadFraction * 100)}%" : Title;

    public string CardActionLabel => IsInProgress ? "Continue" : IsRead ? "Re-read" : "Read";

    [ObservableProperty]
    private bool _isSelected;
}
