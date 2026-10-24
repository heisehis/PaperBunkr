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

    /// <summary>Ordered read-state path. Long lists are compressed into contiguous buckets.</summary>
    public IReadOnlyList<ReadingProgressSegment> ProgressSegments { get; init; } = Array.Empty<ReadingProgressSegment>();

    public string ProgressLabel => $"{SubLine}";

    /// <summary>The caps type word (ARC, EVENT, CONTINUITY...) a list card wears, or null for a plain user list and for folders.</summary>
    public string? TypeCaption { get; init; }

    /// <summary>The muted part after the caption: "METRON · 29 ISSUES".</summary>
    public string CaptionLine { get; init; } = string.Empty;

    /// <summary>The cleaned description; the card shows two lines of it, or none when empty.</summary>
    public string Synopsis { get; init; } = string.Empty;

    public bool ShowSynopsis => Synopsis.Length > 0;

    public bool ShowCaption => !IsFolder;

    public bool HasTypeCaption => !string.IsNullOrEmpty(TypeCaption);

    public bool IsList => !IsFolder;

    /// <summary>"Not started", "13 / 38 read" or "✓ Finished" (a list card's footer, left).</summary>
    public string FooterLeft { get; init; } = string.Empty;

    /// <summary>The forecast ("~ Nov 2026"), empty when there is no pace to project from or nothing left to read.</summary>
    public string FooterRight { get; init; } = string.Empty;

    public bool IsFinished { get; init; }

    /// <summary>The containing folder (the gallery level this tile sits on).</summary>
    public int? ParentFolderId { get; init; }

    public int SortOrder { get; init; }

    /// <summary>Up to four cover keys for the mosaic (<c>AsyncCoverImage.SourceId</c>).</summary>
    public IReadOnlyList<string> CoverKeys { get; init; } = Array.Empty<string>();

    public int CoverColumns => CoverKeys.Count > 1 ? 2 : 1;

    /// <summary>The fanned stack on a card: the front cover, the one behind it and the one behind that (null when the list has fewer).</summary>
    public string? FrontCoverKey => CoverKeys.Count > 0 ? CoverKeys[0] : null;

    public string? MidCoverKey => CoverKeys.Count > 1 ? CoverKeys[1] : null;

    public string? BackCoverKey => CoverKeys.Count > 2 ? CoverKeys[2] : null;

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

public sealed record ReadingProgressSegment(bool IsRead);

/// <summary>A card in the gallery's "Continue reading" strip.</summary>
public sealed class ReadingContinueCard
{
    public int ListId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? CoverKey { get; init; }

    /// <summary>"next: Infinite Crisis #4 · 12/31".</summary>
    public string NextLine { get; init; } = string.Empty;

    /// <summary>0..1 read fraction, for the card's thin progress line.</summary>
    public double Progress { get; init; }
}

/// <summary>One segment of the gallery breadcrumb ("Reading Lists › Crisis Events › Tie-ins").</summary>
public sealed record ReadingBreadcrumb(int? FolderId, string Name, bool IsLast);

/// <summary>A tag chip on the gallery header.</summary>
public sealed record ReadingTagChip(string? Value, string Label, bool IsActive);
