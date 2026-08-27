using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row in the comic Reader's Bookmarks flyout (docs/superpowers/specs/2026-08-18-metadata-model-
/// ui-gaps-status-and-bookmarks-design.md). Gained inline-rename state (docs/ce-feature-inventory.md
/// §A "Named bookmarks") - an <see cref="ObservableObject"/> now rather than a plain POCO so the row's
/// own edit mode can be toggled without rebuilding the whole <c>Bookmarks</c> collection.
/// </summary>
public sealed partial class IssueBookmarkSummary : ObservableObject
{
    public int Id { get; init; }

    public int PageNumber { get; init; }

    [ObservableProperty]
    private string _label = string.Empty;

    /// <summary>True while this row shows a TextBox instead of its label Button.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Scratch buffer for the in-progress rename - seeded from <see cref="Label"/> when
    /// editing begins, discarded (not written back) if the user never commits it.</summary>
    [ObservableProperty]
    private string _editText = string.Empty;
}
