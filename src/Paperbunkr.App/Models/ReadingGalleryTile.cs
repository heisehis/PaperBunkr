using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// One tile in the Reading Lists gallery (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §2, decision Q17): a folder
/// (mosaic of its lists' covers, list count, read % over everything below) or a list (arc cover, else a 2×2 mosaic, plus a progress bar).
/// </summary>
public sealed partial class ReadingGalleryTile : ObservableObject
{
    public bool IsFolder { get; init; }

    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>"12 / 31 read" or "5 lists · 38%".</summary>
    public string SubLine { get; init; } = string.Empty;

    /// <summary>0..1 read fraction.</summary>
    public double Progress { get; init; }

    /// <summary>The containing folder (the gallery level this tile sits on).</summary>
    public int? ParentFolderId { get; init; }

    public int SortOrder { get; init; }

    /// <summary>Up to four cover keys for the mosaic (<c>AsyncCoverImage.SourceId</c>).</summary>
    public IReadOnlyList<string> CoverKeys { get; init; } = Array.Empty<string>();

    public int CoverColumns => CoverKeys.Count > 1 ? 2 : 1;

    /// <summary>A downloaded arc cover - wins over the mosaic for a list tile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMosaic))]
    [NotifyPropertyChangedFor(nameof(ShowGlyph))]
    private Bitmap? _arcCover;

    public bool ShowMosaic => ArcCover is null && CoverKeys.Count > 0;

    public bool ShowGlyph => ArcCover is null && CoverKeys.Count == 0;

    public bool ShowProgress => !IsFolder;

    /// <summary>Tag values on the list (empty for folders) - for the gallery's tag chips.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = string.Empty;
}

/// <summary>A card in the gallery's "Continue reading" strip.</summary>
public sealed class ReadingContinueCard
{
    public int ListId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? CoverKey { get; init; }

    /// <summary>"next: Infinite Crisis #4 · 12/31".</summary>
    public string NextLine { get; init; } = string.Empty;
}

/// <summary>One segment of the gallery breadcrumb ("Reading Lists › Crisis Events › Tie-ins").</summary>
public sealed record ReadingBreadcrumb(int? FolderId, string Name, bool IsLast);

/// <summary>A tag chip on the gallery header.</summary>
public sealed record ReadingTagChip(string? Value, string Label, bool IsActive);
