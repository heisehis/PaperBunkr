using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row in the Comic List screen (docs/superpowers/specs/2026-08-18-issue-list-pluggable-sort-
/// group-design.md) - a flat, cross-series projection of one <see cref="Paperbunkr.Data.Entities.Issue"/>,
/// same rationale as <see cref="SeriesCardSample"/>: avoid holding live EF-tracked entities for a
/// potentially-library-wide list, and precompute the one cross-entity field (<see cref="SeriesName"/>)
/// once instead of re-joining per sort click.
///
/// <para>
/// An <see cref="ObservableObject"/> (was a plain init-only POCO) since <see cref="IsSelected"/>
/// (docs/superpowers/specs/2026-08-24-library-multiselect-slice1-design.md) needs live change
/// notification for a tile's selected-state visuals - identical rationale to
/// <see cref="IssueCardSample"/>'s own earlier conversion. Every other property stays exactly as it
/// was: <c>init</c>-only, rebuilt fresh on every reload/re-sort/re-group by
/// <c>IssueListScreenViewModel.Render</c> - which is also why that method must re-stamp
/// <see cref="IsSelected"/> from the live selection set on every rebuild, or a re-sort would silently
/// wipe the visible selection.
/// </para>
/// </summary>
public sealed partial class IssueListRow : ObservableObject, ISelectableCard
{
    public int Id { get; init; }
    public int SeriesId { get; init; }
    public required string SeriesName { get; init; }
    public string? Number { get; init; }
    public float? NumberSortKey { get; init; }
    public required string Title { get; init; }
    public string? Writer { get; init; }
    public string? Publisher { get; init; }
    public string? Genre { get; init; }
    public string? Format { get; init; }
    public string? Tags { get; init; }
    public DateTime? AddedTime { get; init; }
    public DateTime? OpenedTime { get; init; }
    public DateTime? ReleasedTime { get; init; }
    public int? Year { get; init; }
    public int? PageCount { get; init; }
    public long? FileSize { get; init; }
    public float? Rating { get; init; }
    public float? CommunityRating { get; init; }
    public double ReadPercentage { get; init; }
    public int OpenCount { get; init; }
    public bool IsMissing { get; init; }
    public required IBrush CoverBrush { get; init; }

    // --- Slice 1 CE-catalog fields (docs/superpowers/specs/2026-08-18-library-book-centric-
    // redesign-design.md) - the remaining real CE comparer/grouper columns not covered above. ---
    public string? Volume { get; init; }
    public float? VolumeSortKey { get; init; }
    public string? Penciller { get; init; }
    public string? Inker { get; init; }
    public string? Colorist { get; init; }
    public string? Letterer { get; init; }
    public string? CoverArtist { get; init; }
    public string? Editor { get; init; }
    public string? Translator { get; init; }
    public string? Characters { get; init; }
    public string? Teams { get; init; }
    public string? Locations { get; init; }
    public float? BookPrice { get; init; }
    public string? BookAge { get; init; }
    public string? BookStore { get; init; }
    public string? BookOwner { get; init; }
    public string? BookCondition { get; init; }
    public string? BookCollectionStatus { get; init; }
    public string? BookLocation { get; init; }
    public string? ISBN { get; init; }
    public bool IsRead { get; init; }
    public string? Imprint { get; init; }
    public string? LanguageIso { get; init; }
    public string? AgeRating { get; init; }
    public string? StoryArc { get; init; }
    public string? SeriesGroup { get; init; }
    public string? FilePath { get; init; }
    public string? FileName { get; init; }
    public string? FileDirectory { get; init; }
    public DateTime? FileModifiedTime { get; init; }
    public DateTime? FileCreationTime { get; init; }
    public string? FileFormat { get; init; }
    public int? Count { get; init; }
    public string? AlternateSeries { get; init; }
    public string? AlternateNumber { get; init; }
    public int? Month { get; init; }
    public int? Day { get; init; }
    public string? ScanInformation { get; init; }
    public int BookmarkCount { get; init; }

    // --- Slice 3 fields (docs/superpowers/specs/2026-08-18-library-book-centric-redesign-design.md) -
    // Library's card templates retargeted from SeriesCardSample to this type need these. ---

    public string? ContentTypeLabel { get; init; }

    /// <summary>Series-level current values, for the tile context menu's radio ✓ marks (mirrors
    /// <see cref="ContentTypeLabel"/>). Raw enum names, e.g. "Ongoing" / "Reading" / "RightToLeft".</summary>
    public string? SeriesStatusLabel { get; init; }
    public string? ReadingStatusLabel { get; init; }
    public string? ReadingDirectionLabel { get; init; }

    /// <summary>Gates the tile context menu's Reading Direction submenu, same rationale as the old
    /// <c>SeriesCardSample.IsMangaFamily</c> - only meaningful once the issue's series is classified
    /// as manga-family.</summary>
    public bool IsMangaFamily => ContentTypeLabel is "Manga" or "Manhua" or "Manhwa";

    public bool HasFile => !string.IsNullOrEmpty(FilePath);

    /// <summary>Cache-file stem for this issue's current file identity - what
    /// <c>CoverImageConverter</c> binds against (docs/superpowers/specs/2026-08-27-cover-thumbnail-
    /// identity-validation-design.md).</summary>
    public string CoverKey => CoverFingerprint.Stem(Id, FilePath, FileSize);
    public bool HasPublisher => !string.IsNullOrWhiteSpace(Publisher);
    public bool HasLanguage => !string.IsNullOrWhiteSpace(LanguageIso);

    /// <summary>Panorama grid's per-tile width, same <see cref="SeriesCardSample.ComputePanoramaWidth"/>
    /// math as the old series-card version - just fed by this issue's own cover instead of a
    /// series' chosen "cover issue".</summary>
    public double PanoramaWidth { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}
