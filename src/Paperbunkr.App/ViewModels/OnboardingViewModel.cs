using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

public enum OnboardingStage
{
    Choice,
    Scanning,
    Done,
}

/// <summary>
/// Drives the general first-run welcome flow. Replaces the old behavior of auto-opening
/// <see cref="MigrationOverlayViewModel"/> on every fresh install with a detected CE library -
/// that assumed everyone arriving at Paperbunkr is a ComicRack CE migrant, which stopped being a
/// safe assumption once the app has users who never touched CE. Choice -&gt; (Scanning) -&gt; Done,
/// same one-Stage-selects-the-panel shape as <see cref="MigrationViewModel"/>. CE import stays
/// available as one option among several, offered only when a CE install is actually detected
/// (<see cref="CeDetected"/>), never assumed.
/// </summary>
public partial class OnboardingViewModel : ViewModelBase
{
    private readonly IFilePickerService _filePicker;
    private readonly LibraryFolderScanner _scanner;
    private readonly Action _reloadFolderWatch;
    private readonly Action _onImportFromCe;
    private readonly Action _onFinished;

    public OnboardingViewModel(IFilePickerService filePicker, LibraryFolderScanner scanner, Action reloadFolderWatch, Action onImportFromCe, Action onFinished)
    {
        _filePicker = filePicker;
        _scanner = scanner;
        _reloadFolderWatch = reloadFolderWatch;
        _onImportFromCe = onImportFromCe;
        _onFinished = onFinished;
    }

    [ObservableProperty]
    private OnboardingStage _stage = OnboardingStage.Choice;

    public bool IsChoice => Stage == OnboardingStage.Choice;
    public bool IsScanning => Stage == OnboardingStage.Scanning;
    public bool IsDone => Stage == OnboardingStage.Done;

    partial void OnStageChanged(OnboardingStage value)
    {
        OnPropertyChanged(nameof(IsChoice));
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(IsDone));
    }

    /// <summary>Whether a ComicRack CE install was found on this machine - gates the "Import from ComicRack CE" card, re-checked every time the overlay opens (installs/uninstalls can happen between launches).</summary>
    [ObservableProperty]
    private bool _ceDetected;

    [ObservableProperty]
    private string _scanStatusLabel = "Adding your comics…";

    [ObservableProperty]
    private bool _hasIssuesAdded;

    [ObservableProperty]
    private string _resultSummaryLabel = string.Empty;

    /// <summary>Resets to the Choice stage and re-checks CE detection. Called each time the overlay opens.</summary>
    public void Open()
    {
        Stage = OnboardingStage.Choice;
        CeDetected = File.Exists(MigrationViewModel.GetDefaultCePath());
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        string? path = await _filePicker.PickFolderAsync("Choose your comics or manga folder");
        if (path is null)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            if (!context.WatchedFolders.Any(w => w.Path == path))
            {
                context.WatchedFolders.Add(new WatchedFolder { Path = path });
                context.SaveChanges();
            }
        }

        _reloadFolderWatch();

        Stage = OnboardingStage.Scanning;
        ScanStatusLabel = "Adding your comics…";

        var progress = new Progress<(int Done, int Total)>(p => ScanStatusLabel = $"Adding your comics… {p.Done}/{p.Total}");
        var result = await _scanner.ScanAllAsync(progress);

        HasIssuesAdded = result.IssuesAdded > 0;
        if (HasIssuesAdded)
        {
            ScanStatusLabel = "Generating covers…";
            var coverProgress = new Progress<(int Done, int Total)>(p => ScanStatusLabel = $"Generating covers… {p.Done}/{p.Total}");
            await new CoverThumbnailService().GenerateAllAsync(coverProgress);

            ResultSummaryLabel = $"Added {result.IssuesAdded} issue{(result.IssuesAdded == 1 ? "" : "s")} across {result.SeriesTouched} series.";
        }
        else
        {
            ResultSummaryLabel = "No comics found in that folder yet - you can add more anytime from Preferences → Library.";
        }

        Stage = OnboardingStage.Done;
    }

    [RelayCommand]
    private void AddAnotherFolder() => Stage = OnboardingStage.Choice;

    [RelayCommand]
    private void ImportFromCe() => _onImportFromCe();

    /// <summary>"I'll do this later" from Choice, and "Start Reading" from Done - both just hand control back to the shell.</summary>
    [RelayCommand]
    private void Finish() => _onFinished();
}
