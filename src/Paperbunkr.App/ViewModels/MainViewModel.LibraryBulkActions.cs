using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Shell wiring for the Library bulk actions (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md): the Paste Data and Merge
/// series overlays, the confirmations, the clipboard/navigation hooks. Kept out of the main file, which other work also edits.
/// </summary>
public partial class MainViewModel
{
    public PasteDataScreenViewModel PasteData { get; private set; } = null!;

    public MergeSeriesScreenViewModel MergeSeries { get; private set; } = null!;

    [ObservableProperty]
    private bool _isPasteDataOverlayOpen;

    [ObservableProperty]
    private bool _isMergeSeriesOverlayOpen;

    /// <summary>Called once from the constructor, after <see cref="Library"/> and <see cref="Dialogs"/> exist.</summary>
    private void WireLibraryBulkActions()
    {
        PasteData = new PasteDataScreenViewModel(ClosePasteDataOverlay, (ids, fields) => Library.ApplyPastedData(ids, fields));
        MergeSeries = new MergeSeriesScreenViewModel(CloseMergeSeriesOverlay, (target, sources) => Library.ApplySeriesMerge(target, sources));
        Library.Dialogs = Dialogs;
        Library.OpenPasteData = OpenPasteDataOverlay;
        Library.OpenMergeSeries = OpenMergeSeriesOverlay;
        Library.GoReadingList = GoReadingWithList;
    }

    private void OpenPasteDataOverlay(IReadOnlyList<int> issueIds, MetadataClipboardContent content)
    {
        PasteData.Load(issueIds, content);
        IsPasteDataOverlayOpen = true;
    }

    [RelayCommand]
    private void ClosePasteDataOverlay() => IsPasteDataOverlayOpen = false;

    private void OpenMergeSeriesOverlay(IReadOnlyList<int> seriesIds, IReadOnlyDictionary<int, IBrush> covers)
    {
        MergeSeries.Load(seriesIds, covers);
        IsMergeSeriesOverlayOpen = true;
    }

    [RelayCommand]
    private void CloseMergeSeriesOverlay() => IsMergeSeriesOverlayOpen = false;
}
