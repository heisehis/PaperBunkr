using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using cYo.Common.Runtime;
using cYo.Projects.ComicRack.Engine.Database;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.CeMigration;

namespace Paperbunkr.App.ViewModels;

public enum MigrationStage
{
    Locate,
    Preview,
    Conflicts,
    Commit,
    Results,
}

/// <summary>
/// Drives the migration overlay's Locate -&gt; Preview -&gt; Conflicts -&gt; Commit -&gt; Results flow
/// (docs/onboarding.md §14, docs/superpowers/specs/2026-08-06-migration-ux-design.md §B). One
/// <see cref="Stage"/> selects which panel the view shows, matching the existing pattern of one
/// ViewModel driving Is*-flag visibility (see <see cref="MainViewModel"/>) rather than separate
/// routed screens.
/// </summary>
public partial class MigrationViewModel : ViewModelBase
{
    private readonly IFilePickerService _filePicker;
    private readonly Action? _onCompleted;

    private ComicDatabase? _loadedDatabase;
    private readonly HashSet<SeriesConflictCandidate> _mergeDecisions = new();
    private readonly HashSet<SeriesConflictCandidate> _keepSeparateDecisions = new();

    public MigrationViewModel(IFilePickerService filePicker, Action? onCompleted = null)
    {
        _filePicker = filePicker;
        _onCompleted = onCompleted;
        Conflicts = new ObservableCollection<SeriesConflictRowViewModel>();
        ResetToLocate();
    }

    public ObservableCollection<SeriesConflictRowViewModel> Conflicts { get; }

    [ObservableProperty]
    private MigrationStage _stage = MigrationStage.Locate;

    public bool IsLocate => Stage == MigrationStage.Locate;
    public bool IsPreview => Stage == MigrationStage.Preview;
    public bool IsConflicts => Stage == MigrationStage.Conflicts;
    public bool IsCommit => Stage == MigrationStage.Commit;
    public bool IsResults => Stage == MigrationStage.Results;

    partial void OnStageChanged(MigrationStage value)
    {
        OnPropertyChanged(nameof(IsLocate));
        OnPropertyChanged(nameof(IsPreview));
        OnPropertyChanged(nameof(IsConflicts));
        OnPropertyChanged(nameof(IsCommit));
        OnPropertyChanged(nameof(IsResults));
    }

    [ObservableProperty]
    private string _selectedPath = string.Empty;

    [ObservableProperty]
    private bool _defaultPathFound;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasErrorMessage));

    public bool HasConflicts => Conflicts.Count > 0;

    [ObservableProperty]
    private bool _isBusy;

    // --- Preview stage ---
    [ObservableProperty]
    private string _seriesCountLabel = "0";

    [ObservableProperty]
    private string _issueCountLabel = "0";

    [ObservableProperty]
    private string _guessedContentTypeLabel = "0";

    // --- Results stage ---
    [ObservableProperty]
    private string _seriesCreatedLabel = "0";

    [ObservableProperty]
    private string _issuesCreatedLabel = "0";

    [ObservableProperty]
    private string _seriesMergedLabel = "0";

    [ObservableProperty]
    private string _conflictsPendingLabel = "0";

    /// <summary>Resets the flow back to Locate, re-checking for the default CE install path. Called on first open and whenever the overlay reopens in Migrate mode.</summary>
    public void ResetToLocate()
    {
        Stage = MigrationStage.Locate;
        ErrorMessage = null;
        _loadedDatabase = null;
        Conflicts.Clear();
        _mergeDecisions.Clear();
        _keepSeparateDecisions.Clear();
        OnPropertyChanged(nameof(HasConflicts));

        string defaultPath = GetDefaultCePath();
        DefaultPathFound = File.Exists(defaultPath);
        SelectedPath = DefaultPathFound ? defaultPath : string.Empty;
    }

    /// <summary>Default CE install path: %AppData%\cYo\ComicRack Community Edition\ComicDb.xml, built from the shared ApplicationInfo constants rather than a second hardcoded string.</summary>
    public static string GetDefaultCePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, ApplicationInfo.CompanyName, ApplicationInfo.ProductName, "ComicDb.xml");
    }

    [RelayCommand]
    private async Task Browse()
    {
        string? path = await _filePicker.PickOpenFileAsync("Locate ComicDb.xml", "xml", "ComicRack CE Library");
        if (path is not null)
        {
            SelectedPath = path;
        }
    }

    [RelayCommand]
    private void Scan()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(SelectedPath) || !File.Exists(SelectedPath))
        {
            ErrorMessage = "That file couldn't be found.";
            return;
        }

        try
        {
            _loadedDatabase = CeLibraryMigrator.LoadFromXml(SelectedPath);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't read that file: {FlattenMessages(ex)}";
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var preview = new CeLibraryMigrator().Preview(_loadedDatabase, context);

        SeriesCountLabel = preview.SeriesCount.ToString();
        IssueCountLabel = preview.IssueCount.ToString();
        GuessedContentTypeLabel = preview.SeriesWithGuessedContentType.ToString();

        Conflicts.Clear();
        _mergeDecisions.Clear();
        _keepSeparateDecisions.Clear();
        foreach (var candidate in preview.ConflictCandidates)
        {
            Conflicts.Add(new SeriesConflictRowViewModel(
                candidate.IncomingName,
                candidate.MatchedName,
                candidate.Similarity,
                onMerge: _ => _mergeDecisions.Add(candidate),
                onKeepSeparate: _ => _keepSeparateDecisions.Add(candidate)));
        }

        OnPropertyChanged(nameof(HasConflicts));
        Stage = MigrationStage.Preview;
    }

    /// <summary>From Preview: skips straight to Commit if no conflicts were found (§14 step 3 is a no-op then).</summary>
    [RelayCommand]
    private void ContinueFromPreview()
    {
        Stage = Conflicts.Count > 0 ? MigrationStage.Conflicts : MigrationStage.Commit;
    }

    [RelayCommand]
    private void ContinueFromConflicts() => Stage = MigrationStage.Commit;

    [RelayCommand]
    private void KeepAllSeparate() => SeriesConflictRowViewModel.ApplyBulkAction(Conflicts, ConflictBulkAction.KeepAllSeparate);

    [RelayCommand]
    private void MergeAllAboveNinety() => SeriesConflictRowViewModel.ApplyBulkAction(Conflicts, ConflictBulkAction.MergeAboveNinetyPercent);

    [RelayCommand]
    private async Task Commit()
    {
        if (_loadedDatabase is null)
        {
            return;
        }

        IsBusy = true;
        var database = _loadedDatabase;

        var result = await Task.Run(() =>
        {
            using var context = PaperbunkrDb.CreateContext();
            var options = BuildMigrationOptions(context);
            return new CeLibraryMigrator().Migrate(database, context, options);
        });

        IsBusy = false;

        SeriesCreatedLabel = result.SeriesCreated.ToString();
        IssuesCreatedLabel = result.IssuesCreated.ToString();
        SeriesMergedLabel = result.SeriesMerged.ToString();
        ConflictsPendingLabel = result.ConflictsPending.ToString();

        Stage = MigrationStage.Results;
        _onCompleted?.Invoke();
    }

    /// <summary>
    /// Translates the Conflicts stage's per-row decisions into a <see cref="MigrationOptions"/>.
    /// A "Merge" decision targets an existing library series (MergeIntoExisting) when its
    /// MatchedName exactly matches one already in <paramref name="context"/> - that's how
    /// against-existing candidates are constructed (see <see cref="SeriesNameMatcher.FindAgainstExistingCandidates"/>)
    /// - otherwise it's an intra-import pair, folded into a MergeGroups set instead.
    /// </summary>
    /// <summary>
    /// CeLibraryMigrator.LoadFromXml wraps every failure in a generic FileLoadException("Could
    /// not open the Database") - walks the InnerException chain so the actual cause (a missing
    /// dependency, a malformed element, etc.) reaches the user instead of that one opaque line.
    /// </summary>
    private static string FlattenMessages(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" - ", messages);
    }

    private MigrationOptions BuildMigrationOptions(Paperbunkr.Data.PaperbunkrDbContext context)
    {
        var options = new MigrationOptions();
        var mergeGroupsBySeed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in _mergeDecisions)
        {
            var existingMatch = context.Series.FirstOrDefault(s => s.Name == candidate.MatchedName);
            if (existingMatch is not null)
            {
                options.MergeIntoExisting[candidate.IncomingName] = existingMatch.Id;
                continue;
            }

            if (!mergeGroupsBySeed.TryGetValue(candidate.IncomingName, out var group)
                && !mergeGroupsBySeed.TryGetValue(candidate.MatchedName, out group))
            {
                group = new List<string> { candidate.IncomingName };
                mergeGroupsBySeed[candidate.IncomingName] = group;
            }

            if (!group.Contains(candidate.MatchedName, StringComparer.OrdinalIgnoreCase))
            {
                group.Add(candidate.MatchedName);
            }

            mergeGroupsBySeed[candidate.MatchedName] = group;
        }

        foreach (var group in mergeGroupsBySeed.Values.Distinct())
        {
            options.MergeGroups.Add(group);
        }

        foreach (var candidate in _keepSeparateDecisions)
        {
            options.ResolvedKeepSeparate.Add(candidate);
        }

        return options;
    }
}
