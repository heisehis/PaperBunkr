using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Models;

/// <summary>One row in the Preferences Advanced tab's File Types list: its "Open with" association and, for the
/// types Paperbunkr can draw, its File Explorer "Thumbnail" toggle
/// (docs/superpowers/specs/2026-09-30-explorer-cover-thumbnails-design.md, layout B).</summary>
public class FileAssociationSummary
{
    public string Name { get; init; } = string.Empty;

    public string ExtensionList { get; init; } = string.Empty;

    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();

    public bool IsAssociated { get; init; }

    /// <summary>The row's extensions the thumbnail handler covers; empty → the Thumbnail column shows "—" (decision 19).</summary>
    public IReadOnlyList<string> ThumbnailExtensions { get; init; } = Array.Empty<string>();

    public bool HasThumbnail => ThumbnailExtensions.Count > 0;

    public bool IsThumbnailEnabled { get; init; }

    /// <summary>Another program's name when it, not Paperbunkr, draws this type's thumbnails (decision 22).</summary>
    public string? ThumbnailOwner { get; init; }

    public bool HasThumbnailOwner => ThumbnailOwner is not null;

    public string ThumbnailOwnerLabel => "Thumbnail: " + ThumbnailOwner;
}
