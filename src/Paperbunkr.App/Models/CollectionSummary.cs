using FluentIcons.Common;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Models;

/// <summary>
/// Sidebar row for one <c>Collection</c> (docs/superpowers/specs/2026-08-27-collections-design.md) -
/// name, total member count (series + issues + books), optional accent colour, and whether it's the
/// currently active Library filter. Identity fields (glyph, pin, progress) come from
/// docs/superpowers/specs/2026-11-06-collection-identity-design.md.
/// </summary>
public partial class CollectionSummary : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public int Count { get; init; }

    public string? AccentColor { get; init; }

    public bool IsActive { get; init; }

    /// <summary>True when this collection has at least one rule slot set (docs/superpowers/specs/2026-08-30-smart-collections-design.md) - shown as a badge next to the accent dot.</summary>
    public bool IsSmart { get; init; }

    /// <summary>The stored icon (a symbol name, <c>hero:</c>, <c>publisher:</c> or <c>file:</c>); null = the accent dot. See <see cref="CollectionIcon"/>.</summary>
    public string? IconGlyph { get; init; }

    /// <summary>True when the icon can be drawn; a deleted file or a missing logo shows the dot instead.</summary>
    public bool HasGlyph => Paperbunkr.App.Services.CollectionIconResolver.Resolve(IconGlyph) is not null;

    public bool ShowDot => !HasGlyph;

    public bool IsPinned { get; init; }

    /// <summary>True on the first unpinned row that follows at least one pinned row - the sidebar draws a hairline above it.</summary>
    public bool IsFirstUnpinned { get; init; }

    /// <summary>Read units out of total units, or null when the progress ring is off or the collection has no units.</summary>
    public double? ProgressFraction { get; init; }

    public bool HasProgress => ProgressFraction is not null;

    public double ProgressValue => ProgressFraction ?? 0;

    public string? ProgressText { get; init; }

    /// <summary>True while a member of this group is being dragged over this row (the drop line shows).</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isDropTarget;

    /// <summary>Null for a summary row that isn't wired for deletion yet (e.g. a design-time stub).</summary>
    public TwoStepConfirm? DeleteConfirm { get; init; }
}
