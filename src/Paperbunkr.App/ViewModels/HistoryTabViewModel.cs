using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.History;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Insights screen's History tab (docs/superpowers/specs/2026-09-29-insights-reading-history-design.md §3):
/// a Mihon-style list of what you've been reading - one row per series at its latest read, under day headers,
/// with resume / open / remove. Owned by <see cref="InsightsScreenViewModel"/> as <c>History</c>, same pattern
/// as its Stats / Recap / Goals children. All row computation is <see cref="ReadingHistoryResolver"/>; search and
/// the type chips filter the cached result in memory, so typing never re-queries.
/// </summary>
public partial class HistoryTabViewModel : ViewModelBase, IContextMenuProvider
{
    private readonly Action<int> _goReaderForIssue;
    private readonly Action<int> _goDetailForSeries;
    private readonly Action<int, BookFormat> _goReaderForBook;
    private readonly Action<int> _goBookDetailForBook;
    private readonly IDialogService _dialogs;
    private readonly IReadingEventRecorder _recorder;
    private readonly IActivityService? _activity;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<DateTime> _nowUtc;
    private readonly TimeZoneInfo _timeZone;
    private readonly Func<Func<IReadOnlyList<ReadingHistoryRow>>, Task<IReadOnlyList<ReadingHistoryRow>>> _runInBackground;
    private readonly Action<Action> _post;

    private IReadOnlyList<ReadingHistoryRow> _rows = Array.Empty<ReadingHistoryRow>();
    private bool _stale = true;
    private int _loadGeneration;

    /// <param name="runInBackground">Runs the resolver off the UI thread. Tests pass a synchronous runner - the
    /// pinned-thread headless framework can't survive a real <c>Task.Run</c> await (Event Map memory).</param>
    /// <param name="post">Defers work one UI-dispatcher tick - reloads triggered from a row's own Remove click
    /// must not rebuild <see cref="Items"/> while that click is still routing (CLAUDE.md routed-event rule).</param>
    public HistoryTabViewModel(
        Action<int> goReaderForIssue,
        Action<int> goDetailForSeries,
        Action<int, BookFormat> goReaderForBook,
        Action<int> goBookDetailForBook,
        IDialogService dialogs,
        IReadingEventRecorder? recorder = null,
        IActivityService? activity = null,
        Func<PaperbunkrDbContext>? contextFactory = null,
        Func<DateTime>? nowUtc = null,
        TimeZoneInfo? timeZone = null,
        Func<Func<IReadOnlyList<ReadingHistoryRow>>, Task<IReadOnlyList<ReadingHistoryRow>>>? runInBackground = null,
        Action<Action>? post = null)
    {
        _goReaderForIssue = goReaderForIssue;
        _goDetailForSeries = goDetailForSeries;
        _goReaderForBook = goReaderForBook;
        _goBookDetailForBook = goBookDetailForBook;
        _dialogs = dialogs;
        _activity = activity;
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _recorder = recorder ?? new ReadingEventRecorder(_contextFactory);
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _runInBackground = runInBackground ?? (work => Task.Run(work));
        _post = post ?? (action => Dispatcher.UIThread.Post(action));

        // Handlers may fire on a background thread, and - for Remove / Clear all - synchronously inside the
        // clicked row's routed event. Posting covers both.
        _recorder.ReadingEventRecorded += () => _post(() =>
        {
            _stale = true;
            if (IsActive)
            {
                Refresh();
            }
        });
    }

    /// <summary>Set while the History tab is the selected Insights tab (by <see cref="InsightsScreenViewModel"/>).</summary>
    public bool IsActive { get; set; }

    /// <summary>Day headers (<see cref="HistoryDayHeader"/>) interleaved with rows (<see cref="HistoryRowItem"/>).</summary>
    public ObservableCollection<object> Items { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private HistoryTypeFilter _typeFilter = HistoryTypeFilter.All;

    [ObservableProperty]
    private bool _isLoaded;

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnTypeFilterChanged(HistoryTypeFilter value)
    {
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsComicsFilter));
        OnPropertyChanged(nameof(IsMangaFilter));
        OnPropertyChanged(nameof(IsBooksFilter));
        Rebuild();
    }

    public bool IsAllFilter => TypeFilter == HistoryTypeFilter.All;

    public bool IsComicsFilter => TypeFilter == HistoryTypeFilter.Comics;

    public bool IsMangaFilter => TypeFilter == HistoryTypeFilter.Manga;

    public bool IsBooksFilter => TypeFilter == HistoryTypeFilter.Books;

    /// <summary>Nothing in History at all (before any search/filter) - "Nothing read yet".</summary>
    public bool IsEmpty => IsLoaded && _rows.Count == 0;

    /// <summary>History has rows but the search/chips hide them all - "No history matches".</summary>
    public bool IsFilteredEmpty => IsLoaded && _rows.Count > 0 && Items.Count == 0;

    public bool HasRows => _rows.Count > 0;

    /// <summary>Reloads from the database if anything was recorded since the last load (or on first show).</summary>
    public void Refresh()
    {
        if (_stale)
        {
            _ = RefreshAsync();
        }
    }

    public async Task RefreshAsync()
    {
        int generation = ++_loadGeneration;
        IReadOnlyList<ReadingHistoryRow> rows;
        try
        {
            rows = await _runInBackground(() =>
            {
                using var context = _contextFactory();
                return ReadingHistoryResolver.Resolve(context);
            });
        }
        catch (Exception ex)
        {
            if (generation == _loadGeneration)
            {
                RaiseFailure("Couldn't load reading history", ex);
            }

            return;
        }

        if (generation != _loadGeneration)
        {
            return; // a newer load started while this one ran
        }

        _rows = rows;
        _stale = false;
        IsLoaded = true;
        Rebuild();
    }

    private void Rebuild()
    {
        Items.Clear();
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(_nowUtc(), DateTimeKind.Utc), _timeZone).Date;
        DateTime? currentDay = null;
        foreach (var row in _rows.Where(Matches))
        {
            var item = new HistoryRowItem(row, _timeZone);
            if (item.LocalDate != currentDay)
            {
                currentDay = item.LocalDate;
                Items.Add(new HistoryDayHeader(HistoryDayLabel.For(item.LocalDate, today)));
            }

            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(HasRows));
        ClearAllCommand.NotifyCanExecuteChanged();
    }

    private bool Matches(ReadingHistoryRow row)
    {
        bool typeOk = TypeFilter switch
        {
            HistoryTypeFilter.Comics => row.ContentKind == ReadingHistoryContentKind.Comic,
            HistoryTypeFilter.Manga => row.ContentKind == ReadingHistoryContentKind.Manga,
            HistoryTypeFilter.Books => row.ContentKind == ReadingHistoryContentKind.Book,
            _ => true,
        };
        if (!typeOk)
        {
            return false;
        }

        string query = SearchText.Trim();
        return query.Length == 0
            || row.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (row.ItemLabel?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    [RelayCommand]
    private void SetTypeFilter(HistoryTypeFilter filter) => TypeFilter = filter;

    /// <summary>Row click: series or book detail. A greyed row has nowhere to go.</summary>
    [RelayCommand]
    private void OpenRow(HistoryRowItem? item)
    {
        if (item?.Row is not { IsInLibrary: true } row)
        {
            return;
        }

        if (row.DetailSeriesId is int seriesId)
        {
            _goDetailForSeries(seriesId);
        }
        else if (row.DetailBookId is int bookId)
        {
            _goBookDetailForBook(bookId);
        }
    }

    /// <summary>▶ / Open: resume, read next, or open a PDF - same reader entry points as Home's Continue Reading.</summary>
    [RelayCommand]
    private void PlayRow(HistoryRowItem? item)
    {
        if (item?.Row is not { Action: not ReadingHistoryAction.None, TargetId: int targetId } row)
        {
            return;
        }

        if (row.ContentKind == ReadingHistoryContentKind.Book)
        {
            _goReaderForBook(targetId, row.BookFormat ?? BookFormat.Epub);
        }
        else
        {
            _goReaderForIssue(targetId);
        }
    }

    /// <summary>Hides the row's whole series from History (never from stats). The list reloads via the
    /// recorder's event, which the constructor posts past this click's routing.</summary>
    [RelayCommand]
    private void RemoveRow(HistoryRowItem? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            _recorder.HideFromHistory(item.Row.GroupKey);
        }
        catch (Exception ex)
        {
            RaiseFailure("Couldn't remove from reading history", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasRows))]
    private async Task ClearAll()
    {
        bool confirmed = await _dialogs.ConfirmAsync(
            "This hides every entry from History. Your stats, streaks and goals are kept.",
            title: "Clear reading history?",
            confirmLabel: "Clear",
            isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        try
        {
            _recorder.HideFromHistory(null);
        }
        catch (Exception ex)
        {
            RaiseFailure("Couldn't clear reading history", ex);
        }
    }

    public IReadOnlyList<ContextMenuEntry>? BuildContextMenu(object? target)
    {
        if (target is not HistoryRowItem item)
        {
            return null;
        }

        var row = item.Row;
        var entries = new List<ContextMenuEntry>();
        switch (row.Action)
        {
            case ReadingHistoryAction.Resume:
                entries.Add(ContextMenuEntry.Item(item.PlayTooltip ?? "Resume", PlayRowCommand, item, Symbol.Play));
                break;
            case ReadingHistoryAction.ReadNext:
                entries.Add(ContextMenuEntry.Item(item.PlayTooltip ?? "Read next", PlayRowCommand, item, Symbol.Next));
                break;
            case ReadingHistoryAction.Open:
                entries.Add(ContextMenuEntry.Item("Open", PlayRowCommand, item, Symbol.Open));
                break;
        }

        if (row.IsInLibrary)
        {
            entries.Add(ContextMenuEntry.Item(row.DetailBookId is not null ? "Open book" : "Open series", OpenRowCommand, item, Symbol.Info));
        }

        if (entries.Count > 0)
        {
            entries.Add(ContextMenuEntry.Separator);
        }

        entries.Add(ContextMenuEntry.Item("Remove from history", RemoveRowCommand, item, Symbol.Delete, isDanger: true));
        return entries;
    }

    private void RaiseFailure(string title, Exception ex) =>
        _activity?.RaiseAlert(new ActivityAlert
        {
            Severity = ActivityAlertSeverity.Warning,
            Title = title,
            Detail = ex.Message,
        });
}
