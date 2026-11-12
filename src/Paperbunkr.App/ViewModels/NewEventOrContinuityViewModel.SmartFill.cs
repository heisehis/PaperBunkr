using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.EventEnrichment;

namespace Paperbunkr.App.ViewModels;

public partial class NewEventOrContinuityViewModel
{
    private EventSmartFillViewModel? _draftSources;
    private readonly IActivityService? _smartFillActivity;
    private readonly Dictionary<EventMetadataField, (EventEvidence Evidence, EventFieldSnapshot Before)> _draftEvidence = new();
    private EventPublicationDate? _loadedStart, _loadedEnd;
    private EventMetadataSnapshot? _loadedMetadata;
    private int _draftGeneration;
    private string? _draftIdentity;
    private bool _draftCoverCleared;
    private string? _loadedCoverPath;
    private bool _customCoverChanged;
    partial void OnEventCoverPathChanged(string? value) { if (value is not null) _draftCoverCleared = false; }
    public EventSmartFillRunner SmartFillRunner { get; set; } = new();
    public EventSmartFillViewModel DraftSources => _draftSources ??= new(runner: SmartFillRunner, activity: _smartFillActivity)
    {
        DraftSnapshot = DraftSnapshot,
        ApplyDraft = ApplyDraftAsync,
    };

    [ObservableProperty] private bool _startDateUncertain;
    [ObservableProperty] private bool _endDateUncertain;
    [ObservableProperty] private bool _endDateProvisional;
    public bool CanFillEvent => CurrentKind == Kind.Event && IsEdit && IsBasics && _editId > 0;

    [RelayCommand] private void SmartFillDraft() { if (CanFillEvent) DraftSources.Open(_editId, true); }
    [RelayCommand] private void CompareDraftSources() { if (CanFillEvent) DraftSources.Open(_editId); }

    private void ResetSmartFillDraft()
    {
        _draftGeneration++; _draftEvidence.Clear(); _loadedMetadata = null; _loadedStart = _loadedEnd = null;
        _draftCoverCleared = false;
        _loadedCoverPath = null; _customCoverChanged = false;
        _draftIdentity = null;
        StartDateUncertain = EndDateUncertain = EndDateProvisional = false;
        if (_draftSources is not null) _draftSources.CloseCommand.Execute(null);
    }

    private void LoadSmartFillDates(StoryEvent e)
    {
        _loadedStart = EventPublicationDate.Effective(e, false); _loadedEnd = EventPublicationDate.Effective(e, true);
        StartDateUncertain = _loadedStart?.Uncertain ?? false; EndDateUncertain = _loadedEnd?.Uncertain ?? false;
        EndDateProvisional = _loadedEnd?.Provisional ?? false;
        using var db = PaperbunkrDb.CreateContext();
        var row = EventMetadataStore.Query(db).Single(x => x.Id == e.Id);
        _loadedMetadata = EventMetadataStore.Capture(db, row);
        OnPropertyChanged(nameof(CanFillEvent));
    }

    private EventMetadataSnapshot DraftSnapshot(EventMetadataSnapshot snapshot)
    {
        var fields = snapshot.Fields.ToDictionary(x => x.Key, x => x.Value);
        foreach (var field in Enum.GetValues<EventMetadataField>())
        {
            string? value = DraftValue(field, snapshot.Fields[field].Value);
            bool cleared = value is null && ((_loadedMetadata?.Fields[field].Value is not null) || snapshot.Fields[field].Value is not null);
            fields[field] = snapshot.Fields[field] with { Value = value, Protected = snapshot.Fields[field].Protected || cleared };
        }
        return snapshot with { Fields = fields };
    }

    private string? DraftValue(EventMetadataField field, string? fallback = null) => field switch
    {
        EventMetadataField.Name => Blank(Name), EventMetadataField.Description => Blank(Description),
        EventMetadataField.StartDate => DateJson(false), EventMetadataField.EndDate => DateJson(true),
        EventMetadataField.Types => EventTypeSelections.Any(t => t.IsSelected) ? JsonSerializer.Serialize(EventTypeSelections.Where(t => t.IsSelected).Select(t => t.Type).Order().ToArray()) : null,
        EventMetadataField.Tags => Blank(EventTags) is null ? null : JsonSerializer.Serialize(EventTags.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries).Select(t => new EventTagValue(t.Trim(), "Event", IssueTagWeight.Unset)).OrderBy(t => t.Value).ToArray()),
        EventMetadataField.Cover => _draftCoverCleared ? null : EventCoverPath is not null ? JsonSerializer.Serialize(new EventCoverValue(null, StoryEventCoverSource.Manual))
            : _draftEvidence.TryGetValue(field, out var candidate) ? candidate.Evidence.Value : fallback,
        _ => fallback,
    };
    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private EventPublicationDate? DraftDate(bool end)
    {
        var parsed = EventPublicationDate.Parse(end ? EndDate : StartDate);
        if (parsed is null) return null;
        var loaded = end ? _loadedEnd : _loadedStart;
        return (loaded?.Entry == parsed.Entry ? loaded : parsed) with
        { Uncertain = end ? EndDateUncertain : StartDateUncertain, Provisional = end ? EndDateProvisional : loaded?.Entry == parsed.Entry && loaded.Provisional };
    }
    private string? DateJson(bool end) => DraftDate(end) is { } date ? JsonSerializer.Serialize(date) : null;

    private async Task<int> ApplyDraftAsync(EventEnrichmentProposal proposal, EventEvidence[] selected, CancellationToken ct)
    {
        int generation = _draftGeneration, count = 0;
        foreach (var candidate in selected)
        {
            if (candidate.Field == EventMetadataField.Cover)
            {
                var cover = JsonSerializer.Deserialize<EventCoverValue>(candidate.Value);
                if (cover?.Url is null || !await SmartFillRunner.PrepareCover(_editId, cover.Url, ct)) continue;
            }
            ct.ThrowIfCancellationRequested();
            if (generation != _draftGeneration) return count;
            var before = proposal.Snapshot.Fields[candidate.Field];
            if (DraftValue(candidate.Field, before.Value) != before.Value) continue;
            SetDraft(candidate.Field, candidate.Value);
            _draftIdentity ??= proposal.Snapshot.Identity;
            if (!_draftEvidence.ContainsKey(candidate.Field)) _draftEvidence[candidate.Field] = (candidate, before);
            else _draftEvidence[candidate.Field] = (candidate, _draftEvidence[candidate.Field].Before);
            count++;
        }
        return count;
    }

    private void SetDraft(EventMetadataField field, string? value)
    {
        switch (field)
        {
            case EventMetadataField.Name: Name = value ?? ""; break;
            case EventMetadataField.Description: Description = value ?? ""; break;
            case EventMetadataField.StartDate:
            case EventMetadataField.EndDate:
                var date = value is null ? null : JsonSerializer.Deserialize<EventPublicationDate>(value);
                if (field == EventMetadataField.StartDate) { _loadedStart = date; StartDate = date?.Entry ?? ""; StartDateUncertain = date?.Uncertain ?? false; }
                else { _loadedEnd = date; EndDate = date?.Entry ?? ""; EndDateUncertain = date?.Uncertain ?? false; EndDateProvisional = date?.Provisional ?? false; }
                break;
            case EventMetadataField.Types:
                var types = value is null ? [] : JsonSerializer.Deserialize<StoryEventType[]>(value)!;
                foreach (var choice in EventTypeSelections) choice.IsSelected = types.Contains(choice.Type);
                break;
            case EventMetadataField.Tags:
                EventTags = value is null ? "" : string.Join(Environment.NewLine, JsonSerializer.Deserialize<EventTagValue[]>(value)!.Select(t => t.Value)); break;
            case EventMetadataField.Cover: EventCoverPath = null; _draftCoverCleared = value is null; break;
        }
    }

    private void SaveSmartFillDates(PaperbunkrDbContext db, StoryEvent e)
    {
        EventMetadataStore.SetDate(db, e, false, DraftDate(false));
        EventMetadataStore.SetDate(db, e, true, DraftDate(true));
    }

    private void RecheckSmartFillDraft(PaperbunkrDbContext db, StoryEvent e)
    {
        var current = EventMetadataStore.Capture(db, e);
        foreach (var (field, draft) in _draftEvidence.ToArray())
        {
            if (current.Identity == _draftIdentity && current.Fields[field].Revision == draft.Before.Revision) continue;
            SetDraft(field, current.Fields[field].Value); _draftEvidence.Remove(field);
            ProviderStatus = "Some fetched metadata was skipped because the event changed; its current saved values were preserved.";
        }
    }

    private void SaveSmartFillDraftHistory(PaperbunkrDbContext db, StoryEvent e)
    {
        Guid operation = Guid.NewGuid();
        foreach (var (field, draft) in _draftEvidence)
        {
            string? saved = EventMetadataStore.Value(db, e, field);
            if (saved is null)
            {
                var cleared = EventMetadataStore.State(e, field);
                cleared.ProtectedBlank = true; cleared.Revision = Guid.NewGuid().ToString("N"); cleared.EvidenceJson = null;
                continue;
            }
            if (saved != draft.Evidence.Value) continue;
            var state = EventMetadataStore.State(e, field);
            state.EvidenceJson = JsonSerializer.Serialize(draft.Evidence);
            db.EventMetadataChanges.Add(new() { StoryEvent = e, OperationId = operation, Field = field,
                BeforeValue = draft.Before.Value, BeforeProtected = draft.Before.Protected, AfterValue = draft.Evidence.Value,
                AppliedRevision = state.Revision, EvidenceJson = state.EvidenceJson });
        }
        db.SaveChanges(); _draftEvidence.Clear();
    }
}
