using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Drives the app shell's navigation (rail nav, contextual sidebar, active screen).
/// Layout/tokens follow the "Paperbunkr App" wireframe (Claude Design project 43c40b25),
/// default variant: rail nav, pills toolbar, stacked detail layout, separate lists.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        Library = new LibraryScreenViewModel(GoDetailForSeries, GoReaderForIssue);
        Books = new BooksScreenViewModel(new FilePickerService(), new BookFolderScanService(), new BookCoverThumbnailService(), ShowToast);
        Detail = new DetailScreenViewModel(GoLibrary, GoReaderForIssue, GoIssuePropertiesForIssue, GoBulkIssuePropertiesForIssues);
        Reader = new ReaderScreenViewModel(GoDetail);
        IssueProperties = new IssuePropertiesScreenViewModel(GoDetailAfterIssueEdit);
        BulkIssueProperties = new BulkIssuePropertiesScreenViewModel(GoDetailAfterIssueEdit);
        Smart = new SmartScreenViewModel(GoDetailForSeries);
        Reading = new ReadingScreenViewModel(new FilePickerService());
        Plugin = new PluginScreenViewModel();
        Migration = new MigrationOverlayViewModel(new FilePickerService(), OpenSeriesDetailFromReview);
        Preferences = new PreferencesScreenViewModel(
            new SkinService(),
            new FilePickerService(),
            new LibraryFolderScanner(),
            new FileAssociationService(),
            new BackupService(),
            new KeyBindingService(),
            ShowToast,
            Migration,
            OpenMigrationOverlay,
            ShowProgressToast,
            CloseProgressToast);
    }

    /// <summary>
    /// Toast plumbing (P6 follow-up) - kept as a plain event on this shell ViewModel rather than a
    /// singleton/static service, since the actual <c>WindowNotificationManager</c> can only be
    /// created once a real <c>Window</c> exists, which happens after this ViewModel is constructed
    /// (see App.axaml.cs). <see cref="Views.MainWindow"/> subscribes once its own DataContext is set.
    /// </summary>
    public event Action<string, string>? ToastRequested;

    private void ShowToast(string title, string message) => ToastRequested?.Invoke(title, message);

    /// <summary>
    /// Live progress toast plumbing, same rationale as <see cref="ToastRequested"/> - a long-running
    /// library action (Generate Covers, Sync Metadata) shows one via <see cref="ShowProgressToast"/>,
    /// updates the same <see cref="ToastProgressViewModel"/> instance's Done/Total as it runs (the
    /// shown toast is live-bound to it, so no re-show needed), then calls
    /// <see cref="CloseProgressToast"/> when finished - typically followed by a normal
    /// <see cref="ShowToast"/> completion message.
    /// </summary>
    public event Action<ToastProgressViewModel>? ProgressToastRequested;

    public event Action<ToastProgressViewModel>? ProgressToastCloseRequested;

    private void ShowProgressToast(ToastProgressViewModel toast) => ProgressToastRequested?.Invoke(toast);

    private void CloseProgressToast(ToastProgressViewModel toast) => ProgressToastCloseRequested?.Invoke(toast);

    public LibraryScreenViewModel Library { get; }
    public BooksScreenViewModel Books { get; }
    public DetailScreenViewModel Detail { get; }
    public ReaderScreenViewModel Reader { get; }
    public IssuePropertiesScreenViewModel IssueProperties { get; }
    public BulkIssuePropertiesScreenViewModel BulkIssueProperties { get; }
    public SmartScreenViewModel Smart { get; }
    public ReadingScreenViewModel Reading { get; }
    public PluginScreenViewModel Plugin { get; }
    public PreferencesScreenViewModel Preferences { get; }
    public MigrationOverlayViewModel Migration { get; }

    [ObservableProperty]
    private bool _isMigrationOverlayOpen;

    [ObservableProperty]
    private string _currentScreen = "library";

    public bool IsLibrary => CurrentScreen == "library";
    public bool IsBooks => CurrentScreen == "books";
    public bool IsDetail => CurrentScreen == "detail";
    public bool IsSmart => CurrentScreen == "smart";
    public bool IsReading => CurrentScreen == "reading";
    public bool IsPlugin => CurrentScreen == "plugin";
    public bool IsPreferences => CurrentScreen == "preferences";
    public bool IsReader => CurrentScreen == "reader";
    public bool IsIssueProperties => CurrentScreen == "issueProperties";
    public bool IsBulkIssueProperties => CurrentScreen == "bulkIssueProperties";

    public bool ShowContextualSidebar => IsLibrary || IsSmart || IsReading;

    partial void OnCurrentScreenChanged(string value)
    {
        OnPropertyChanged(nameof(IsLibrary));
        OnPropertyChanged(nameof(IsBooks));
        OnPropertyChanged(nameof(IsDetail));
        OnPropertyChanged(nameof(IsSmart));
        OnPropertyChanged(nameof(IsReading));
        OnPropertyChanged(nameof(IsPlugin));
        OnPropertyChanged(nameof(IsPreferences));
        OnPropertyChanged(nameof(IsReader));
        OnPropertyChanged(nameof(IsIssueProperties));
        OnPropertyChanged(nameof(IsBulkIssueProperties));
        OnPropertyChanged(nameof(ShowContextualSidebar));
    }

    [RelayCommand]
    private void GoLibrary()
    {
        // Unlike Smart/Reading's EnsureListLoaded (guarded, load-once), Library genuinely reloads
        // every time - it's the one rail-nav screen with no lazy-load story yet, and the data can
        // change from several places while the user is elsewhere (Book Folders scan, CE migration
        // commit, Generate Covers). Reloading on every visit is simplest fix that covers all of
        // those instead of special-casing each caller to remember to refresh Library itself.
        Library.LoadFromDatabase();
        CurrentScreen = "library";
    }

    [RelayCommand]
    private void GoBooks()
    {
        Books.LoadFromDatabase();
        CurrentScreen = "books";
    }

    [RelayCommand]
    private void GoSmart()
    {
        Smart.EnsureListLoaded();
        CurrentScreen = "smart";
    }

    [RelayCommand]
    private void GoReading()
    {
        Reading.EnsureListLoaded();
        CurrentScreen = "reading";
    }

    [RelayCommand]
    private void GoPlugin() => CurrentScreen = "plugin";

    [RelayCommand]
    private void GoPreferences()
    {
        Preferences.EnsureLoaded();
        CurrentScreen = "preferences";
    }

    [RelayCommand]
    private void OpenMigrationOverlay()
    {
        Migration.Open();
        IsMigrationOverlayOpen = true;
    }

    [RelayCommand]
    private void CloseMigrationOverlay()
    {
        IsMigrationOverlayOpen = false;
        Library.LoadFromDatabase();
    }

    private void OpenSeriesDetailFromReview(int seriesId)
    {
        IsMigrationOverlayOpen = false;
        GoDetailForSeries(seriesId);
    }

    [RelayCommand]
    private void GoReader()
    {
        Reader.EnsureIssueLoaded();
        CurrentScreen = "reader";
    }

    private void GoDetail() => CurrentScreen = "detail";

    /// <summary>
    /// Distinct from the plain <see cref="GoDetail"/> Reader uses - the properties editor may have
    /// changed the currently-loaded series' data (e.g. an issue's <c>Number</c>, which the Issues
    /// tab tile label is derived from), so the Detail screen needs a real reload, not just a
    /// screen-visibility flip back to already-stale state.
    /// </summary>
    private void GoDetailAfterIssueEdit()
    {
        Detail.ReloadCurrentSeries();
        CurrentScreen = "detail";
    }

    private void GoDetailForSeries(int seriesId)
    {
        Detail.LoadSeries(seriesId);
        CurrentScreen = "detail";
    }

    private void GoReaderForIssue(int issueId)
    {
        Reader.LoadIssue(issueId);
        CurrentScreen = "reader";
    }

    private void GoIssuePropertiesForIssue(int issueId)
    {
        IssueProperties.Load(issueId);
        CurrentScreen = "issueProperties";
    }

    private void GoBulkIssuePropertiesForIssues(IReadOnlyList<int> issueIds)
    {
        BulkIssueProperties.Load(issueIds);
        CurrentScreen = "bulkIssueProperties";
    }

    /// <summary>
    /// Esc-to-close/cancel (P5, docs/alpha-roadmap.md), routed here rather than per-screen
    /// KeyDown handlers so there's exactly one place that knows what "the current dialog" is -
    /// none of Migration/Issue Properties/Bulk Editing are real Avalonia Windows/Popups (they're
    /// all screen-swaps or an overlay within the single MainWindow), so there's no native
    /// dialog-Escape behavior to inherit. Preferences has no Cancel concept (every toggle
    /// persists immediately, docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md)
    /// so it's deliberately not in this list - Esc there would have nothing meaningful to cancel.
    /// </summary>
    [RelayCommand]
    private void Escape()
    {
        if (IsMigrationOverlayOpen)
        {
            CloseMigrationOverlay();
        }
        else if (IsIssueProperties)
        {
            IssueProperties.CancelCommand.Execute(null);
        }
        else if (IsBulkIssueProperties)
        {
            BulkIssueProperties.CancelCommand.Execute(null);
        }
    }
}
