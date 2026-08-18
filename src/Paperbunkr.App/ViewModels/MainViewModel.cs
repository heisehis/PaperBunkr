using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

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
        Library = new LibraryScreenViewModel(GoDetailForSeries, GoReaderForIssue, GoNewIssuePropertiesForPlaceholder);
        Books = new BooksScreenViewModel(new FilePickerService(), new BookFolderScanService(), new BookCoverThumbnailService(), GoBookReaderForBook);
        BookReader = new BookReaderScreenViewModel(GoBooks);
        PdfReader = new PdfPageReaderScreenViewModel(GoBooks);
        Detail = new DetailScreenViewModel(GoLibrary, GoReaderForIssue, GoIssuePropertiesForIssue, GoBulkIssuePropertiesForIssues);
        Reader = new ReaderScreenViewModel(GoDetail);
        IssueProperties = new IssuePropertiesScreenViewModel(GoDetailAfterIssueEdit);
        BulkIssueProperties = new BulkIssuePropertiesScreenViewModel(GoDetailAfterIssueEdit);
        Smart = new SmartScreenViewModel(GoDetailForSeries);
        Reading = new ReadingScreenViewModel(new FilePickerService());
        Events = new EventsScreenViewModel();
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

        // Real bug, found via manual testing: Reader.CanvasBackgroundBrush/PageMarginMultiplier
        // (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md
        // §10) were only ever re-read inside ReaderScreenViewModel.Load - fine for a value read once
        // per book, but background/margin are edited from Preferences while a book may already be
        // open and staying open (the rail-nav screen switcher never destroys/recreates the Reader),
        // so cycling through colors there appeared to "get stuck" on whatever was set the last time
        // Load happened to run. Wired the same way as the toast plumbing above - Preferences raises a
        // plain event, the Reader refreshes its own snapshot in response, no shared mutable state.
        Preferences.ReaderDisplaySettingsChanged += Reader.RefreshDisplaySettings;
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
    public BookReaderScreenViewModel BookReader { get; }
    public PdfPageReaderScreenViewModel PdfReader { get; }
    public DetailScreenViewModel Detail { get; }
    public ReaderScreenViewModel Reader { get; }
    public IssuePropertiesScreenViewModel IssueProperties { get; }
    public BulkIssuePropertiesScreenViewModel BulkIssueProperties { get; }
    public SmartScreenViewModel Smart { get; }
    public ReadingScreenViewModel Reading { get; }
    public EventsScreenViewModel Events { get; }
    public PluginScreenViewModel Plugin { get; }
    public PreferencesScreenViewModel Preferences { get; }
    public MigrationOverlayViewModel Migration { get; }

    [ObservableProperty]
    private bool _isMigrationOverlayOpen;

    [ObservableProperty]
    private string _currentScreen = "library";

    public bool IsLibrary => CurrentScreen == "library";
    public bool IsBooks => CurrentScreen == "books";
    public bool IsBookReader => CurrentScreen == "bookReader";
    public bool IsPdfReader => CurrentScreen == "pdfReader";
    public bool IsDetail => CurrentScreen == "detail";
    public bool IsSmart => CurrentScreen == "smart";
    public bool IsReading => CurrentScreen == "reading";
    public bool IsEvents => CurrentScreen == "events";
    public bool IsPlugin => CurrentScreen == "plugin";
    public bool IsPreferences => CurrentScreen == "preferences";
    public bool IsReader => CurrentScreen == "reader";
    public bool IsIssueProperties => CurrentScreen == "issueProperties";
    public bool IsBulkIssueProperties => CurrentScreen == "bulkIssueProperties";

    public bool ShowContextualSidebar => IsLibrary || IsSmart || IsReading || IsEvents;

    partial void OnCurrentScreenChanged(string value)
    {
        OnPropertyChanged(nameof(IsLibrary));
        OnPropertyChanged(nameof(IsBooks));
        OnPropertyChanged(nameof(IsBookReader));
        OnPropertyChanged(nameof(IsPdfReader));
        OnPropertyChanged(nameof(IsDetail));
        OnPropertyChanged(nameof(IsSmart));
        OnPropertyChanged(nameof(IsReading));
        OnPropertyChanged(nameof(IsEvents));
        OnPropertyChanged(nameof(IsPlugin));
        OnPropertyChanged(nameof(IsPreferences));
        OnPropertyChanged(nameof(IsReader));
        OnPropertyChanged(nameof(IsIssueProperties));
        OnPropertyChanged(nameof(IsBulkIssueProperties));
        OnPropertyChanged(nameof(ShowContextualSidebar));
    }

    [RelayCommand]
    private void GoLibrary() => TryLeaveCurrentEditor(() =>
    {
        // Unlike Smart/Reading's EnsureListLoaded (guarded, load-once), Library genuinely reloads
        // every time - it's the one rail-nav screen with no lazy-load story yet, and the data can
        // change from several places while the user is elsewhere (Book Folders scan, CE migration
        // commit, Generate Covers). Reloading on every visit is simplest fix that covers all of
        // those instead of special-casing each caller to remember to refresh Library itself.
        Library.LoadFromDatabase();
        CurrentScreen = "library";
    });

    [RelayCommand]
    private void GoBooks() => TryLeaveCurrentEditor(() =>
    {
        Books.LoadFromDatabase();
        CurrentScreen = "books";
    });

    [RelayCommand]
    private void GoSmart() => TryLeaveCurrentEditor(() =>
    {
        Smart.EnsureListLoaded();
        CurrentScreen = "smart";
    });

    [RelayCommand]
    private void GoReading() => TryLeaveCurrentEditor(() =>
    {
        Reading.EnsureListLoaded();
        CurrentScreen = "reading";
    });

    [RelayCommand]
    private void GoEvents() => TryLeaveCurrentEditor(() =>
    {
        Events.EnsureEventLoaded();
        CurrentScreen = "events";
    });

    [RelayCommand]
    private void GoPlugin() => TryLeaveCurrentEditor(() => CurrentScreen = "plugin");

    [RelayCommand]
    private void GoPreferences() => TryLeaveCurrentEditor(() =>
    {
        Preferences.EnsureLoaded();
        CurrentScreen = "preferences";
    });

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
    private void GoReader() => TryLeaveCurrentEditor(() =>
    {
        Reader.EnsureIssueLoaded();
        CurrentScreen = "reader";
    });

    /// <summary>
    /// Guards the seven rail-nav destinations against silently discarding an in-progress Issue
    /// Properties/Bulk Editing edit (P6 follow-up, docs/alpha-todo.md) - CE's equivalent
    /// (<c>ComicBookDialog</c>) is a true modal Windows dialog that blocks all other interaction by
    /// construction, but these screens are just screen-swaps within one window, so without this the
    /// rail nav stays fully clickable mid-edit with no warning. Stashes <paramref name="navigate"/>
    /// and opens the confirm banner instead of running it immediately when the active editor
    /// reports unsaved changes; runs it straight through otherwise. Deliberately NOT applied to
    /// <see cref="Escape"/> - pressing Escape is already an explicit "cancel this" gesture, not an
    /// ambiguous navigation away from it.
    /// </summary>
    private void TryLeaveCurrentEditor(Action navigate)
    {
        bool hasUnsavedChanges = (IsIssueProperties && IssueProperties.HasUnsavedChanges())
            || (IsBulkIssueProperties && BulkIssueProperties.HasUnsavedChanges());

        if (!hasUnsavedChanges)
        {
            navigate();
            return;
        }

        _pendingNavigation = navigate;
        IsDiscardConfirmOpen = true;
    }

    private Action? _pendingNavigation;

    [ObservableProperty]
    private bool _isDiscardConfirmOpen;

    [RelayCommand]
    private void ConfirmDiscard()
    {
        IsDiscardConfirmOpen = false;
        var navigate = _pendingNavigation;
        _pendingNavigation = null;
        navigate?.Invoke();
    }

    [RelayCommand]
    private void CancelDiscard()
    {
        IsDiscardConfirmOpen = false;
        _pendingNavigation = null;
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

    /// <summary>
    /// Manual "add a physical book" hand-off (docs/superpowers/specs/2026-08-16-reveal-in-explorer-
    /// and-fileless-entries-design.md §2/§3) - loads Detail's target series first, same shape as
    /// <see cref="GoDetailForSeries"/>, so IssueProperties' shared _goBack (GoDetailAfterIssueEdit)
    /// lands the user on the right series' Detail screen once they Save/Cancel out of the editor,
    /// not whatever series they last viewed.
    /// </summary>
    private void GoNewIssuePropertiesForPlaceholder(int issueId, int seriesId, bool deleteIfUnedited)
    {
        Detail.LoadSeries(seriesId);
        IssueProperties.Load(issueId, deleteIfUnedited);
        CurrentScreen = "issueProperties";
    }

    private void GoReaderForIssue(int issueId)
    {
        Reader.LoadIssue(issueId);
        CurrentScreen = "reader";
    }

    private void GoBookReaderForBook(int bookId, BookFormat format)
    {
        if (format == BookFormat.Pdf)
        {
            PdfReader.LoadBook(bookId);
            CurrentScreen = "pdfReader";
        }
        else
        {
            BookReader.LoadBook(bookId);
            CurrentScreen = "bookReader";
        }
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
