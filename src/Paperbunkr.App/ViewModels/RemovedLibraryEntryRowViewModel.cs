using System;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in Library Health's "Recently Removed" list (docs/superpowers/specs/2026-09-06-missing-
/// files-library-health-design.md) - a <see cref="Data.Entities.RemovedLibraryEntry"/> from the last
/// 30 days, with a single Restore action. Populated by both single-item Remove and bulk "Remove All
/// Confirmed Missing" alike.
/// </summary>
public partial class RemovedLibraryEntryRowViewModel : ViewModelBase
{
    private readonly Action<RemovedLibraryEntryRowViewModel> _onRestore;

    public RemovedLibraryEntryRowViewModel(
        int entryId,
        string displayLabel,
        string? filePath,
        DateTime removedAtUtc,
        Action<RemovedLibraryEntryRowViewModel> onRestore)
    {
        EntryId = entryId;
        DisplayLabel = displayLabel;
        FilePath = filePath;
        RemovedAtUtc = removedAtUtc;
        _onRestore = onRestore;
    }

    public int EntryId { get; }

    public string DisplayLabel { get; }

    public string? FilePath { get; }

    public DateTime RemovedAtUtc { get; }

    public string RemovedWhenLabel => RemovedAtUtc.ToLocalTime().ToString("MMM d, yyyy h:mm tt");

    [RelayCommand]
    private void Restore() => _onRestore(this);
}
