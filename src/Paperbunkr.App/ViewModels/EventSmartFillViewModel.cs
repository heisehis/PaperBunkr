using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Avalonia.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.EventEnrichment;

namespace Paperbunkr.App.ViewModels;

public partial class EventFillSelection : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    [ObservableProperty] private bool _isSelected;
    public Action<int>? Compare { get; init; }
    [RelayCommand] private void CompareEvent() => Compare?.Invoke(Id);
}

public partial class EventEvidenceRow : ObservableObject
{
    public required EventEvidence Evidence { get; init; }
    public string Field => EventMetadataStore.FieldLabel(Evidence.Field);
    public bool HasSource => Evidence.SourceUrl is not null;
    public string Source => $"{Evidence.Provider} · {Evidence.Reason}" + (Evidence.Field is EventMetadataField.StartDate or EventMetadataField.EndDate
        ? " · " + JsonSerializer.Deserialize<EventPublicationDate>(Evidence.Value)?.Basis : "");
    public string Value => EventMetadataStore.Display(Evidence.Field, Evidence.Value);
    public string Current { get; init; } = "";
    public string Protection { get; init; } = "";
    [ObservableProperty] private Bitmap? _coverPreview;
    public Action<EventEvidenceRow>? Selected { get; init; }
    [ObservableProperty] private bool _isSelected;
    partial void OnIsSelectedChanged(bool value) { if (value) Selected?.Invoke(this); }
    [RelayCommand] private void OpenSource()
    {
        if (Uri.TryCreate(Evidence.SourceUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}

public partial class EventFillHistoryRow : ObservableObject
{
    public Guid Operation { get; init; }
    public string Label { get; init; } = "";
    public string Details { get; init; } = "";
    public required Func<Guid, Task> Undo { get; init; }
    [RelayCommand] private Task UndoChangeAsync() => Undo(Operation);
}

public partial class EventSmartFillViewModel : ViewModelBase
{
    private readonly Action _changed;
    private readonly EventSmartFillRunner _runner;
    private readonly IActivityService _activity;
    private CancellationTokenSource? _work;
    private int? _eventId;
    private EventEnrichmentProposal? _proposal;
    private Guid? _lastOperation;
    private int _generation;
    public Func<EventMetadataSnapshot, EventMetadataSnapshot>? DraftSnapshot { get; init; }
    public Func<EventEnrichmentProposal, EventEvidence[], CancellationToken, Task<int>>? ApplyDraft { get; init; }
    public bool IsDraft => ApplyDraft is not null;
    public bool ShowSelectEvents => !IsBatch && !IsDraft;
    public Action<int>? ContinueSetup { get; set; }
    [RelayCommand] private void LinkProvider()
    {
        if (_eventId is not int id || IsDraft) return;
        Close();
        Dispatcher.UIThread.Post(() => ContinueSetup?.Invoke(id));
    }
    public EventSmartFillViewModel(Action? changed = null, EventSmartFillRunner? runner = null, IActivityService? activity = null)
    { _changed = changed ?? (() => { }); _runner = runner ?? new(); _activity = activity ?? new ActivityService(); }

    public ObservableCollection<EventFillSelection> Events { get; } = new();
    public ObservableCollection<EventEvidenceRow> Candidates { get; } = new();
    public ObservableCollection<EventFillHistoryRow> History { get; } = new();
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSelectEvents))] private bool _isBatch;
    [ObservableProperty] private string _title = "Event Smart fill";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _sources = "";

    public void Open(int eventId, bool fillImmediately = false)
    {
        _work?.Cancel(); _generation++; _eventId = eventId; IsBatch = false; IsBusy = false;
        using var db = _runner.ContextFactory();
        Title = "Smart fill · " + db.StoryEvents.Where(e => e.Id == eventId).Select(e => e.Name).FirstOrDefault();
        ClearCandidates(); Sources = ""; Status = "Existing values are preserved. Select alternatives only when you want a replacement.";
        IsOpen = true; LoadHistory();
        if (fillImmediately) FillSingleCommand.Execute(null); else FetchCommand.Execute(null);
    }

    [RelayCommand]
    public void OpenBatch()
    {
        _work?.Cancel(); _generation++; _eventId = null; IsBatch = true; IsBusy = false; Title = "Smart fill selected events";
        Events.Clear(); ClearCandidates(); History.Clear(); Sources = "";
        using var db = _runner.ContextFactory();
        foreach (var row in db.StoryEvents.OrderBy(e => e.Name).Select(e => new { e.Id, e.Name }))
            Events.Add(new() { Id = row.Id, Name = row.Name, Compare = id => Open(id) });
        Status = "Select events, then fill supported blanks. Existing values and deliberately cleared fields stay protected.";
        IsOpen = true;
    }

    [RelayCommand] private void CancelWork() => _work?.Cancel();
    [RelayCommand] private void Close()
    {
        _work?.Cancel(); _generation++;
        int generation = _generation;
        Dispatcher.UIThread.Post(() => { if (generation == _generation) IsOpen = false; });
    }

    [RelayCommand]
    private async Task FetchAsync()
    {
        if (_eventId is not int id) return;
        var generation = _generation; var cancellation = StartWork();
        try
        {
            var proposal = await _runner.FetchAsync(id, cancellation.Token);
            if (generation != _generation) return;
            if (DraftSnapshot is not null) proposal = proposal with { Snapshot = DraftSnapshot(proposal.Snapshot) };
            _proposal = proposal; PopulateCandidates(proposal);
            Sources = string.Join(Environment.NewLine, proposal.Sources.Select(s => $"{s.Provider}: {s.Status} · {s.Detail}"));
            Status = Candidates.Count == 0 ? "No supported alternatives were found. You can still edit the event manually." : "Choose at most one value per field. Applying a selected value can replace existing data or refill a protected blank.";
        }
        catch (OperationCanceledException) { if (generation == _generation) Status = "Cancelled."; }
        catch (Exception) { if (generation == _generation) Status = "Couldn't fetch event metadata. Check connections and retry."; }
        finally { if (generation == _generation) IsBusy = false; }
    }

    [RelayCommand]
    private async Task FillSingleAsync()
    {
        if (_eventId is not int id) return;
        await FillEventsAsync([id]);
    }

    [RelayCommand]
    private Task FillSelectedAsync() => FillEventsAsync(Events.Where(e => e.IsSelected).Select(e => e.Id).ToArray());

    private async Task FillEventsAsync(int[] ids)
    {
        if (ids.Length == 0) { Status = "Select at least one event."; return; }
        var generation = _generation; var cancellation = StartWork();
        Guid operation = Guid.NewGuid(); int filled = 0, failures = 0, compared = 0, done = 0;
        using var job = _activity.StartJob(ActivityJobKind.SyncMetadata, "Smart fill event metadata");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, job.CancellationToken);
        try
        {
            foreach (int id in ids)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (generation == _generation) Status = $"Fetching event {++done} of {ids.Length}…";
                job.Report(done, ids.Length, "Filling supported blanks");
                try
                {
                    var proposal = await _runner.FetchAsync(id, linked.Token);
                    if (DraftSnapshot is not null) proposal = proposal with { Snapshot = DraftSnapshot(proposal.Snapshot) };
                    var result = ApplyDraft is null ? await _runner.ApplyAsync(proposal, null, operation, linked.Token)
                        : new EventApplyResult(operation, await ApplyDraft(proposal, proposal.Automatic.ToArray(), linked.Token), []);
                    filled += result.Changed;
                    failures += result.Skipped.Count;
                    compared += proposal.Evidence.Select(e => e.Field).Distinct().Count(f => proposal.Snapshot.Fields[f].Value is null
                        && !proposal.Snapshot.Fields[f].Protected && proposal.Automatic.All(a => a.Field != f));
                    failures += proposal.Sources.Count(s => s.Status == "Failed");
                    if (generation == _generation && ids.Length == 1)
                    {
                        _proposal = proposal with { Snapshot = proposal.Snapshot with { Identity = result.Identity ?? proposal.Snapshot.Identity } }; PopulateCandidates(_proposal);
                        Sources = string.Join(Environment.NewLine, proposal.Sources.Select(s => $"{s.Provider}: {s.Status} · {s.Detail}").Concat(result.Skipped));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { failures++; }
            }
            if (filled == 0 && failures > 0) job.Fail($"No fields filled; {failures} checks failed.");
            else job.Succeed($"Filled {filled} fields; {failures} skipped/failed checks");
            if (generation == _generation) Status = $"Filled {filled} fields{(IsDraft ? " in the draft; Save commits them" : "")} · {compared} need comparison · {failures} skipped/failed checks. Existing fields were preserved.";
        }
        catch (OperationCanceledException)
        { if (generation == _generation) Status = $"Cancelled. {filled} saved fields remain available to undo."; }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false; _lastOperation = ApplyDraft is null ? operation : null;
                _changed(); LoadHistory();
                if (_proposal is not null && _eventId.HasValue) RefreshCurrentCandidates();
            }
        }
    }

    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        if (_proposal is null || IsBusy) return;
        var selected = Candidates.Where(c => c.IsSelected).Select(c => c.Evidence).ToArray();
        if (selected.Length == 0) { Status = "Select the values you want to apply."; return; }
        var generation = _generation; var work = StartWork();
        try
        {
            var result = ApplyDraft is null ? await _runner.ApplyAsync(_proposal, selected, Guid.NewGuid(), work.Token)
                : new EventApplyResult(Guid.NewGuid(), await ApplyDraft(_proposal, selected, work.Token), []);
            if (generation != _generation) return;
            _lastOperation = ApplyDraft is null ? result.OperationId : null;
            if (result.Identity is not null) _proposal = _proposal with { Snapshot = _proposal.Snapshot with { Identity = result.Identity } };
            Status = $"Applied {result.Changed} fields" + (ApplyDraft is null ? ". " : " to the draft; Save commits them. ") + string.Join(" ", result.Skipped);
            _changed(); LoadHistory(); RefreshCurrentCandidates();
        }
        catch (OperationCanceledException) { Status = "Cancelled before applying this event."; }
        catch (Exception) { Status = "Couldn't apply changes. Fetch again; existing values were preserved."; }
        finally { if (generation == _generation) IsBusy = false; }
    }

    [RelayCommand] private Task UndoLastAsync() => _lastOperation is Guid operation ? UndoAsync(operation) : Task.CompletedTask;
    private async Task UndoAsync(Guid operation)
    {
        if (IsBusy) return; IsBusy = true;
        try
        {
            var result = await _runner.UndoAsync(operation, _eventId);
            Status = $"Undid {result.Changed} fields. " + string.Join(" ", result.Skipped);
            Dispatcher.UIThread.Post(() => { _changed(); LoadHistory(); RefreshCurrentCandidates(); });
        }
        catch (Exception) { Status = "Couldn't undo changes. Fetch the current event and try again."; }
        finally { IsBusy = false; }
    }

    private CancellationTokenSource StartWork()
    {
        _work?.Cancel(); _work = new(); _runner.BeginOperation(); IsBusy = true; return _work;
    }
    private void RefreshCurrentCandidates()
    {
        if (_proposal is null || _eventId is not int id) return;
        if (DraftSnapshot is not null)
        {
            _proposal = _proposal with { Snapshot = DraftSnapshot(_proposal.Snapshot) }; PopulateCandidates(_proposal); return;
        }
        using var db = _runner.ContextFactory(); var e = EventMetadataStore.Query(db).SingleOrDefault(e => e.Id == id);
        if (e is null) return;
        if (EventMetadataStore.Identity(e) != _proposal.Snapshot.Identity)
        { _proposal = null; ClearCandidates(); Status = "Event identity or members changed. Fetch again before using alternatives."; return; }
        _proposal = _proposal with { Snapshot = EventMetadataStore.Capture(db, e) };
        PopulateCandidates(_proposal);
    }
    private void PopulateCandidates(EventEnrichmentProposal proposal)
    {
        // Calls follow awaited work; Post also protects commands that can complete synchronously in tests.
        ClearCandidates();
        foreach (var evidence in proposal.Evidence.DistinctBy(e => (e.Field, e.Value, e.Provider)))
        {
            var current = proposal.Snapshot.Fields[evidence.Field];
            Candidates.Add(new()
            {
                Evidence = evidence, Current = "Current: " + EventMetadataStore.Display(evidence.Field, current.Value),
                Protection = current.Protected ? "Intentionally empty · select to fill again" : evidence.Automatic ? "Supported evidence" : "Review suggested value",
                Selected = row => { foreach (var other in Candidates.Where(r => r != row && r.Field == row.Field)) other.IsSelected = false; },
            });
        }
        _ = LoadCoverPreviewsAsync(_generation, _work?.Token ?? CancellationToken.None);
    }
    private async Task LoadCoverPreviewsAsync(int generation, CancellationToken ct)
    {
        foreach (var row in Candidates.Where(c => c.Evidence.Field == EventMetadataField.Cover).ToArray())
        {
            try
            {
                var cover = JsonSerializer.Deserialize<EventCoverValue>(row.Evidence.Value);
                if (cover?.Url is null || !await _runner.PrepareCover(_eventId ?? 0, cover.Url, ct)) continue;
                if (generation != _generation || !Candidates.Contains(row)) return;
                row.CoverPreview = StoryEventCoverCache.GetCandidate(cover.Url);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { /* A preview failure does not select or save the cover. */ }
        }
    }
    private void ClearCandidates()
    {
        var previews = Candidates.Select(c => c.CoverPreview).OfType<Bitmap>().ToArray();
        Candidates.Clear();
        Dispatcher.UIThread.Post(() => { foreach (var preview in previews) preview.Dispose(); });
    }
    public void ReleaseClosedPreviews() { if (!IsOpen) ClearCandidates(); }
    private void LoadHistory()
    {
        if (ApplyDraft is not null) { History.Clear(); return; }
        using var db = _runner.ContextFactory();
        var rows = db.EventMetadataChanges.AsNoTracking().Where(c => _eventId == null ? c.OperationId == _lastOperation : c.StoryEventId == _eventId)
            .OrderByDescending(c => c.Id).Take(100).ToList();
        History.Clear();
        foreach (var group in rows.GroupBy(c => c.OperationId))
            History.Add(new() { Operation = group.Key, Label = $"{group.Max(c => c.CreatedAt):g} · {group.Count(c => !c.Undone)} fields remaining",
                Details = string.Join(Environment.NewLine, group.Select(c => $"{EventMetadataStore.FieldLabel(c.Field)}: {EventMetadataStore.Display(c.Field, c.BeforeValue)} → {EventMetadataStore.Display(c.Field, c.AfterValue)}" + (c.Undone ? " · undone" : c.MergeInvalidated ? " · merged; undo unavailable" : ""))), Undo = UndoAsync });
    }
}
