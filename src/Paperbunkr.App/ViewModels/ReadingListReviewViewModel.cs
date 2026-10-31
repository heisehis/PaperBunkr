using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Matching;

namespace Paperbunkr.App.ViewModels;

public sealed partial class ReviewListOption : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    [ObservableProperty] private bool _selected;
}

public sealed record ReviewCandidate(LibraryIssue Issue, string Identity, string Reason)
{
    public string Label => $"{Issue.SeriesName} #{Issue.Number}";
    public string Details => $"Year: {Issue.Year?.ToString() ?? "unknown"} · Volume: {Issue.Volume ?? "unknown"} · Format: {Issue.Format ?? "unknown"}"
        + (Issue.FileIsMissing ? " · File missing" : "");
}

public sealed partial class ReviewRow : ObservableObject
{
    public ReviewEntry? Entry { get; init; }
    public ResolvedEntry? Imported { get; init; }
    public int ListIndex { get; init; }
    public int EntryIndex { get; init; }
    public string ListName { get; init; } = "";
    public ComicReference Reference => Entry?.Match.Reference ?? Imported!.Reference;
    public MatchResult Match => Entry?.Match ?? Imported!.Match;
    public string Label => Reference.Display;
    public string Details => $"Requested volume: {Reference.Volume ?? "unknown"} · Format: {Reference.Format ?? "unknown"}"
        + $" · ComicVine: {Reference.ComicVineIssueId ?? "none"} · Metron: {Reference.MetronIssueId ?? "none"}";
    public bool IsDuplicate => Imported?.Outcome == EntryOutcome.Duplicate;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private ReviewCandidate? _choice;
    public string Status => Choice is { } c ? $"Chosen: {c.Label}" : IsDuplicate ? "Duplicate source entry — excluded"
        : Imported?.IssueId is not null ? "Matched" : Match.NeedsChoice ? "Needs a choice" : "Missing";
}

public sealed record ReviewHistoryRow(ReadingListReviewBatch Batch, string Details);

/// <summary>One retained workspace for staged imports and link reviews; no write happens until Apply.</summary>
public sealed partial class ReadingListReviewViewModel : ViewModelBase
{
    private readonly Func<PaperbunkrDbContext> _context;
    private readonly IDialogService? _dialogs;
    private readonly Action _back;
    private readonly Action _changed;
    private IReadOnlyList<ImportReviewList>? _manifest;
    private int? _parentFolder;
    private int[] _listIds = [];
    private int? _singleItem;
    private string? _lastBatch;
    private int _candidateRequest;
    private readonly object _candidateGate = new();
    private int _skippedCount;
    private int _failedCount;
    private int? _reportedCount;
    private IReadOnlyList<string> _importFailures = Array.Empty<string>();
    public ReadingListReviewViewModel(IDialogService? dialogs, Action back, Action changed, Func<PaperbunkrDbContext>? context = null)
    { _dialogs = dialogs; _back = back; _changed = changed; _context = context ?? PaperbunkrDb.CreateContext; }

    public ObservableCollection<ReviewRow> Rows { get; } = new();
    public ObservableCollection<ReviewCandidate> Candidates { get; } = new();
    public ObservableCollection<ReviewListOption> Lists { get; } = new();
    public ObservableCollection<ReviewHistoryRow> History { get; } = new();
    public bool NoCandidates => Candidates.Count == 0;
    [ObservableProperty] private string _title = "Review reading lists";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isSelector;
    [ObservableProperty] private bool _isHistory;
    [ObservableProperty] private bool _isImport;
    [ObservableProperty] private bool _keepUnresolved = true;
    [ObservableProperty] private bool _compact;
    [ObservableProperty] private bool _showDetail;
    [ObservableProperty] private bool _hasImmediateUndo;
    [ObservableProperty] private ReviewRow? _selectedRow;
    public bool HasDraft => IsImport && _manifest is not null || Rows.Any(r => r.Choice is not null);
    public bool ShowReviewBody => !IsHistory && !IsSelector;
    partial void OnIsHistoryChanged(bool value) => OnPropertyChanged(nameof(ShowReviewBody));
    partial void OnIsSelectorChanged(bool value) => OnPropertyChanged(nameof(ShowReviewBody));

    public async Task<bool> ConfirmExitAsync()
    {
        if (IsBusy) { Status = "Wait for the current review operation to finish before exiting."; return false; }
        return !HasDraft || _dialogs is not null && await _dialogs.ConfirmAsync("Discard unfinished reading-list review choices and exit?",
            "Unfinished review", "Discard and exit", "Keep reviewing");
    }
    public string Summary => $"{Rows.Count} entries · {Rows.Count(r => r.Choice is not null || r.Imported?.IssueId is not null)} resolved · "
        + $"{Rows.Count(r => !r.IsDuplicate && r.Choice is null && r.Imported?.IssueId is null && r.Match.NeedsChoice)} need a choice · "
        + $"{Rows.Count(r => !r.IsDuplicate && r.Choice is null && r.Imported?.IssueId is null && !r.Match.NeedsChoice)} missing · "
        + $"{Rows.Count(r => r.IsDuplicate)} duplicates"
        + (IsImport && !KeepUnresolved ? $" · {Rows.Count(r => !r.IsDuplicate && r.Choice is null && r.Imported?.IssueId is null)} excluded" : "")
        + (IsImport ? $" · {_skippedCount} skipped · {_failedCount} failed files" + (_reportedCount is int count ? $" · source reports {count}" : "") : "");
    partial void OnKeepUnresolvedChanged(bool value) => OnPropertyChanged(nameof(Summary));
    partial void OnSelectedRowChanged(ReviewRow? value)
    {
        ShowDetail = value is not null;
        SearchQuery = "";
        _ = LoadCandidatesAsync(value);
    }
    private async Task LoadCandidatesAsync(ReviewRow? row)
    {
        int request = Interlocked.Increment(ref _candidateRequest);
        try
        {
            var candidates = await Task.Run(() =>
            {
                if (row is null) return new List<ReviewCandidate>();
                using var db = _context();
                return row.Match.Candidates.Select(c => new ReviewCandidate(c.Issue, ReadingListReviewService.CandidateIdentity(db, c.Issue), c.Summary)).ToList();
            });
            lock (_candidateGate)
            {
                if (SelectedRow != row || request != _candidateRequest) return;
                Candidates.Clear(); foreach (var c in candidates) Candidates.Add(c);
                OnPropertyChanged(nameof(NoCandidates));
            }
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    public async Task<bool> CanStartAsync()
    {
        if (IsBusy) return false;
        if (!HasDraft) return true;
        bool discard = _dialogs is not null && await _dialogs.ConfirmAsync("There are unfinished review choices. Discard them and start this operation?",
            "Unfinished review", "Discard", "Resume");
        if (discard) Reset();
        return discard;
    }
    public async Task StartReviewAsync(int[] listIds, int? itemId = null)
    {
        Reset(); _listIds = listIds; _singleItem = itemId; Title = "Resolve reading-list entries";
        IsSelector = listIds.Length == 0;
        if (IsSelector)
        {
            var options = await Task.Run(() => { using var db = _context(); return db.ReadingLists.OrderBy(l => l.Name).Select(l => new ReviewListOption { Id = l.Id, Name = l.Name }).ToList(); });
            foreach (var option in options) Lists.Add(option);
        }
        else await LoadRowsAsync();
    }
    [RelayCommand] private async Task ReviewSelected()
    {
        _listIds = Lists.Where(l => l.Selected).Select(l => l.Id).ToArray();
        if (_listIds.Length == 0) { Status = "Choose at least one list."; return; }
        IsSelector = false; await LoadRowsAsync();
    }
    private async Task LoadRowsAsync()
    {
        await RunAsync(async () =>
        {
            var entries = await Task.Run(() => { using var db = _context(); return ReadingListReviewService.Load(db, _listIds, _singleItem); });
            SetRows(entries.Select(e => new ReviewRow { Entry = e, ListName = e.ListName }));
            Status = entries.Count == 0 ? "No unresolved entries in these lists." : "Choose comics, then Apply. Unchosen entries stay unresolved.";
        });
    }
    public async Task StartImportAsync(PreparedImport input, int? parentFolder)
    {
        Reset(); IsImport = true; Title = "Preview reading-list import"; _parentFolder = parentFolder; _listIds = []; _singleItem = null;
        _skippedCount = input.Lists.Sum(l => l.Skipped.Count); _failedCount = input.Failures.Count;
        _reportedCount = input.Lists.All(l => l.Reported.HasValue) ? input.Lists.Sum(l => l.Reported!.Value) : null;
        _importFailures = input.Failures;
        await RunAsync(async () =>
        {
            _manifest = await Task.Run(() => { using var db = _context(); return ReadingListImportReview.Resolve(db, input); });
            SetImportRows();
            Status = ImportNotes();
        });
    }
    private void SetImportRows() => SetRows(_manifest!.SelectMany((l, li) => l.Resolution.Entries.Select((e, ei) => new ReviewRow
    { Imported = e, ListName = l.Input.Name, ListIndex = li, EntryIndex = ei })));
    private string ImportNotes() => string.Join(Environment.NewLine, _importFailures.Concat(_manifest!.SelectMany(l =>
        l.Input.Skipped.Select(s => $"{l.Input.Name}: {s}").Concat(l.Input.Reported is int reported && reported != l.Input.References.Count + l.Input.Skipped.Count
            ? new[] { $"{l.Input.Name}: source reports {reported} entries; {l.Input.References.Count} parsed and {l.Input.Skipped.Count} skipped." } : Array.Empty<string>()))));
    private void SetRows(IEnumerable<ReviewRow> rows)
    {
        SelectedRow = null; Rows.Clear();
        foreach (var row in rows)
        {
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));
            Rows.Add(row);
        }
        SelectedRow = Rows.FirstOrDefault(); OnPropertyChanged(nameof(Summary));
    }
    [RelayCommand] private async Task Search()
    {
        var row = SelectedRow; string query = SearchQuery.Trim();
        if (row is null || query.Length == 0) { await LoadCandidatesAsync(row); return; }
        int request = Interlocked.Increment(ref _candidateRequest);
        await RunAsync(async () =>
        {
            var found = await Task.Run(() =>
            {
                using var db = _context();
                return LibraryIndex.Load(db).All.Where(i => $"{i.SeriesName} #{i.Number} {i.Year}".Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(100).Select(i => new ReviewCandidate(i, ReadingListReviewService.CandidateIdentity(db, i), "Library search result — manual choice")).ToList();
            });
            lock (_candidateGate)
            {
                if (SelectedRow != row || request != _candidateRequest) return;
                Candidates.Clear(); foreach (var c in found) Candidates.Add(c);
                OnPropertyChanged(nameof(NoCandidates));
            }
            Status = $"{found.Count} results (up to 100).";
        });
    }
    [RelayCommand] private void Choose(ReviewCandidate? candidate)
    {
        if (SelectedRow is not { IsDuplicate: false } row || candidate is null || IsBusy) return;
        row.Choice = candidate; OnPropertyChanged(nameof(HasDraft));
    }
    [RelayCommand] private void ClearChoice()
    { if (SelectedRow is { } row) row.Choice = null; OnPropertyChanged(nameof(HasDraft)); }
    [RelayCommand] private void Step(string? direction)
    {
        Dispatcher.UIThread.Post(() => { int n = Rows.IndexOf(SelectedRow!); SelectedRow = Rows.ElementAtOrDefault(Math.Clamp(n + (direction == "previous" ? -1 : 1), 0, Math.Max(0, Rows.Count - 1))); });
    }
    [RelayCommand] private void ShowQueue() => ShowDetail = false;
    [RelayCommand] private void Back() => Dispatcher.UIThread.Post(_back);
    [RelayCommand] private async Task Discard()
    {
        if (IsBusy) return;
        if (!HasDraft || _dialogs is not null && await _dialogs.ConfirmAsync("Discard this unfinished review?", "Discard review", "Discard"))
        { await Task.Yield(); Reset(); _back(); }
    }
    [RelayCommand] private async Task Apply()
    {
        if (IsBusy || IsSelector || IsHistory) return;
        await RunAsync(async () =>
        {
            var choices = Rows.Where(r => r.Choice is not null).ToList();
            if (IsImport && _manifest is not null)
            {
                var manifest = _manifest; var imports = choices.Where(r => r.Imported is not null)
                    .Select(r => new ImportReviewChoice(r.ListIndex, r.EntryIndex, r.Choice!.Issue, r.Choice.Identity)).ToList();
                var links = choices.Where(r => r.Entry is not null).Select(r => new ReviewChoice(r.Entry!, r.Choice!.Issue, r.Choice.Identity)).ToList();
                var result = await Task.Run(() =>
                {
                    using var db = _context(); using var transaction = db.Database.BeginTransaction();
                    var ids = ReadingListImportReview.Commit(db, manifest, imports, KeepUnresolved, _parentFolder);
                    var batch = links.Count == 0 ? null : ReadingListReviewService.Apply(db, links, string.Join(", ", choices.Where(r => r.Entry is not null).Select(r => r.ListName).Distinct()));
                    transaction.Commit(); return (Count: ids.Count, BatchId: batch?.Id);
                });
                if (result.BatchId is { } id) { _lastBatch = id; HasImmediateUndo = true; }
                _listIds = links.Select(c => c.Entry.ListId).Distinct().ToArray();
                _manifest = null; IsImport = false; SetRows([]); Status = $"Imported {result.Count} reading lists."
                    + (links.Count > 0 ? $" Updated {links.Count} existing entries; those link changes can be undone." : "");
            }
            else
            {
                var batch = await Task.Run(() =>
                {
                    using var db = _context();
                    return ReadingListReviewService.Apply(db, choices.Select(r => new ReviewChoice(r.Entry!, r.Choice!.Issue, r.Choice.Identity)).ToList(),
                        string.Join(", ", choices.Select(r => r.ListName).Distinct()));
                });
                _lastBatch = batch.Id; HasImmediateUndo = true;
                await LoadRowsAsync(); Status = $"Applied {choices.Count} link changes. Undo is available for 30 days.";
            }
            _changed(); OnPropertyChanged(nameof(HasDraft));
        });
    }
    [RelayCommand] private async Task ApplyElsewhere()
    {
        if (SelectedRow is not { Choice: { } choice } selected) return;
        await RunAsync(async () =>
        {
            var found = await Task.Run(() => { using var db = _context(); return ReadingListReviewService.EquivalentEntries(db, selected.Reference, selected.Entry?.ListId); });
            var entries = found.Where(e => Rows.FirstOrDefault(r => r.Entry?.ItemId == e.ItemId)?.Choice is not { } staged
                || staged.Issue.Id == choice.Issue.Id).ToList();
            if (entries.Count == 0) { Status = "No equivalent unresolved entries in other lists."; return; }
            if (_dialogs is null || await _dialogs.ShowAsync(new ConfirmDialogRequest("Stage this choice for these equivalent entries? Apply will save them together.",
                "Apply choice elsewhere", entries.Select(e => $"{e.ListName}: {e.Label}").ToList(), "Stage choices", "Cancel")) != 0) return;
            foreach (var e in entries)
            {
                var row = Rows.FirstOrDefault(r => r.Entry?.ItemId == e.ItemId);
                if (row is null) { row = new ReviewRow { Entry = e, ListName = e.ListName }; row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary)); Rows.Add(row); }
                row.Choice = choice;
            }
            OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(HasDraft));
        });
    }
    [RelayCommand] private async Task Recheck()
    {
        if (IsBusy) return;
        await RunAsync(async () =>
        {
            var choices = Rows.Where(r => r.Choice is not null).ToList();
            if (IsImport && _manifest is not null)
            {
                var input = new PreparedImport(_manifest.Select(l => l.Input).ToList(), _importFailures);
                _manifest = await Task.Run(() => { using var db = _context(); return ReadingListImportReview.Resolve(db, input); });
                SetImportRows();
            }
            else await LoadRowsAsync();
            var refreshedChoices = await Task.Run(() =>
            {
                using var db = _context(); var library = LibraryIndex.Load(db);
                return choices.Select(old => (Old: old,
                    Entry: old.Entry is { } e ? ReadingListReviewService.Load(db, new[] { e.ListId }, e.ItemId).FirstOrDefault() : null,
                    Candidate: library.Find(old.Choice!.Issue.Id) is { } comic
                        ? new ReviewCandidate(comic, ReadingListReviewService.CandidateIdentity(db, comic), old.Choice.Reason) : old.Choice)).ToList();
            });
            foreach (var (old, entry, candidate) in refreshedChoices)
            {
                var row = Rows.FirstOrDefault(r => old.Entry is not null ? r.Entry?.ItemId == old.Entry.ItemId : r.ListIndex == old.ListIndex && r.EntryIndex == old.EntryIndex);
                if (row is null && entry is not null)
                {
                    row = new ReviewRow { Entry = entry, ListName = entry.ListName };
                    row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary)); Rows.Add(row);
                }
                // Deleted entries keep their stale choice visible, so Apply explains the conflict instead of silently dropping work.
                if (row is null && old.Entry is not null) { row = old; Rows.Add(row); }
                if (row is not null) row.Choice = candidate;
            }
            OnPropertyChanged(nameof(Summary));
            Status = "Rechecked. Review the refreshed choices before applying." + (IsImport ? Environment.NewLine + ImportNotes() : "");
        });
    }
    public async Task ShowHistoryAsync()
    {
        IsSelector = false; IsHistory = true;
        await RunAsync(async () =>
        {
            var batches = await Task.Run(() => { using var db = _context(); return ReadingListReviewService.History(db); });
            History.Clear(); foreach (var b in batches)
            {
                var changes = JsonSerializer.Deserialize<List<ReviewChange>>(b.Payload)!;
                History.Add(new(b, $"{b.CreatedUtc.ToLocalTime():g} · {b.Label} · {changes.Count} links · {changes.Count(c => c.Undone)} undone"));
            }
            Status = "Review/link history for this library, retained for 30 days.";
        });
    }
    [RelayCommand] private void CloseHistory() => IsHistory = false;
    [RelayCommand] private Task OpenHistory() => ShowHistoryAsync();
    [RelayCommand] private Task UndoLast() => _lastBatch is null ? Task.CompletedTask : UndoCore(_lastBatch);
    [RelayCommand] private Task Undo(ReviewHistoryRow row) => UndoCore(row.Batch.Id);
    private async Task UndoCore(string id)
    {
        await RunAsync(async () =>
        {
            var preview = await Task.Run(() => { using var db = _context(); return ReadingListReviewService.PreviewUndo(db, id); });
            int eligible = preview.Rows.Count(r => r.Conflict is null);
            if (_dialogs is null || await _dialogs.ShowAsync(new ConfirmDialogRequest($"Restore {eligible} entries? Later notes, roles and order are preserved.", "Undo review batch",
                preview.Rows.Select(r => $"{r.Change.Before.ListName}: {r.Change.Before.Label} — {r.Conflict ?? "will restore"}").ToList(), "Restore eligible entries", "Cancel")) != 0) return;
            int restored = await Task.Run(() => { using var db = _context(); return ReadingListReviewService.Undo(db, preview); });
            bool hasDraft = HasDraft;
            if (IsHistory) await ShowHistoryAsync();
            else if (!hasDraft) await LoadRowsAsync();
            Status = $"Restored {restored} entries; {preview.Rows.Count - restored} left unchanged."
                + (hasDraft ? " Your staged choices are retained; recheck before applying." : "");
            _changed();
        });
    }
    private async Task RunAsync(Func<Task> action)
    {
        bool previous = IsBusy; IsBusy = true;
        try { await action(); } catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = previous; }
    }
    private void Reset()
    {
        SelectedRow = null; Rows.Clear(); Candidates.Clear(); Lists.Clear(); _manifest = null;
        _skippedCount = _failedCount = 0; _reportedCount = null;
        _importFailures = Array.Empty<string>();
        IsImport = IsSelector = IsHistory = false; KeepUnresolved = true; Status = ""; SearchQuery = "";
        OnPropertyChanged(nameof(HasDraft)); OnPropertyChanged(nameof(Summary));
    }
}
