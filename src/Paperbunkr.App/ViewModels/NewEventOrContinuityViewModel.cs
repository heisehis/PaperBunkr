using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The create/edit dialog for the Events &amp; Continuity screen
/// (docs/superpowers/specs/2026-08-28-events-continuity-screen-redesign-design.md and
/// docs/superpowers/specs/2026-08-28-continuity-editing-design.md). Handles both "New event" /
/// "New continuity" and, via <see cref="LoadForEdit"/>, "Edit details" for an existing event or
/// continuity - name, description, and (continuity only) publisher. Class name is historical; it
/// is no longer new-only.
/// </summary>
public partial class NewEventOrContinuityViewModel : ViewModelBase
{
    public enum Kind { Event, Continuity }
    public enum SetupStage { Basics, Provider, Review }

    private readonly Action<Kind, int> _onSaved;
    private readonly Action _onCancel;
    private bool _guidedCreate;

    public ObservableCollection<EventTypeSelection> EventTypeSelections { get; } = new(Enum.GetValues<StoryEventType>().Select(t => new EventTypeSelection(t)));
    public string[] PresetOptions => IsContinuity
        ? new[] { "Blank", "Publisher Universe", "Alternate Universe" }
        : new[] { "Blank", "Crossover Event", "Limited Series Event" };
    public string[] ProviderOptions { get; } = new[] { "Metron", "ComicVine" };

    [ObservableProperty] private string _selectedPreset = "Blank";
    [ObservableProperty] private SetupStage _stage = SetupStage.Basics;
    [ObservableProperty] private string _designation = string.Empty;
    public bool IsBasics => Stage == SetupStage.Basics;
    public bool IsProviderStage => Stage == SetupStage.Provider;
    public bool IsReviewStage => Stage == SetupStage.Review;
    public bool IsAlternateUniversePreset => IsContinuity && (SelectedPreset == "Alternate Universe" || IsEdit);
    public bool HasSelectedProvider => _selectedArc is not null;
    public ObservableCollection<Paperbunkr.Data.ReadingLists.Sources.ArcSearchResult> ArcResults { get; } = new();
    public ObservableCollection<MetronUniverseInfo> UniverseResults { get; } = new();
    public bool HasUniverseResults => UniverseResults.Count > 0;
    public ObservableCollection<ProviderIssueMatch> ProviderIssueMatches { get; } = new();
    public ObservableCollection<RelatedEventSelection> RelatedEvents { get; } = new();
    public ObservableCollection<ProviderSeriesMatch> ProviderSeriesMatches { get; } = new();
    public string ProviderSeriesSummary => $"{ProviderSeriesMatches.Count(m => m.IsSelected)} selected · {ProviderSeriesMatches.Count(m => m.MatchLabel == "Title suggestion · confirm manually")} title suggestions · {ProviderSeriesMatches.Count(m => m.MatchLabel == "Not in library")} unavailable";
    public string[] RelatedEventRelationTypes { get; } = new[] { "Related", "Prequel", "Sequel", "Continuation", "Crossover", "SameUniverse" };
    [ObservableProperty] private string _selectedRelatedEventRelationType = "Related";
    public string ProviderIssueSummary => $"{ProviderIssueMatches.Count(m => m.IssueId.HasValue)} exact library matches · {ProviderIssueMatches.Count(m => !m.IssueId.HasValue)} unavailable or ambiguous";
    [ObservableProperty] private string _providerQuery = string.Empty;
    [ObservableProperty] private string _providerSource = "Metron";
    [ObservableProperty] private string _providerStatus = string.Empty;
    [ObservableProperty] private string _metronUniverseId = string.Empty;
    [ObservableProperty] private string _metronUniverseQuery = string.Empty;
    partial void OnProviderSourceChanged(string value)
    {
        _selectedArc = null; _selectedArcId = null; _selectedArcDescription = null; _selectedArcCover = null; _selectedComicVineArcId = null;
        ProviderIssueMatches.Clear();
        OnPropertyChanged(nameof(HasSelectedProvider)); OnPropertyChanged(nameof(SelectedProviderName)); OnPropertyChanged(nameof(ProviderIssueSummary));
    }
    partial void OnMetronUniverseIdChanged(string value)
    {
        _selectedUniverse = null; ProviderSeriesMatches.Clear();
        OnPropertyChanged(nameof(HasSelectedUniverse)); OnPropertyChanged(nameof(SelectedUniverseSummary));
    }
    private MetronUniverseInfo? _selectedUniverse;
    public string SelectedUniverseSummary => _selectedUniverse is null ? string.Empty : $"{_selectedUniverse.Name} · Metron universe {_selectedUniverse.Id}";
    public bool HasSelectedUniverse => _selectedUniverse is not null;
    private Paperbunkr.Data.ReadingLists.Sources.ArcSearchResult? _selectedArc;
    private string? _selectedArcId;
    private string? _selectedArcDescription;
    private string? _selectedArcCover;
    private string? _selectedComicVineArcId;
    public string SelectedProviderName => _selectedArc?.Name ?? string.Empty;

    public sealed class EventTypeSelection : ObservableObject
    {
        public EventTypeSelection(StoryEventType type) => Type = type;
        public StoryEventType Type { get; }
        public string Label => Type switch
        {
            StoryEventType.LimitedSeries => "Limited Series",
            StoryEventType.StoryArc => "Story Arc",
            StoryEventType.OneShotOrSpecial => "One-shot / Special",
            _ => Type.ToString(),
        };
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    public sealed class ProviderIssueMatch : ObservableObject
    {
        public int Position { get; init; }
        public string Label { get; init; } = string.Empty;
        public int? IssueId { get; init; }
        public bool CanAdd => IssueId.HasValue;
        public string? Problem { get; init; }
        public EventMembershipRole? SuggestedRole { get; init; }
        public string? SuggestedRoleLabel => SuggestedRole?.ToString();
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    public sealed class RelatedEventSelection : ObservableObject
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    public sealed class ProviderSeriesMatch : ObservableObject
    {
        public int MetronSeriesId { get; init; }
        public int? LocalSeriesId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string MatchLabel { get; init; } = string.Empty;
        public bool CanAdd { get; init; }
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    public NewEventOrContinuityViewModel(Action<Kind, int> onSaved, Action onCancel)
    {
        _onSaved = onSaved;
        _onCancel = onCancel;
    }

    /// <summary>Puts the dialog into create mode for a brand-new event or continuity.</summary>
    public void Reset(Kind kind)
    {
        IsEdit = false;
        _editId = 0;
        CurrentKind = kind;
        Name = kind == Kind.Event ? "New Story Event" : "New Continuity";
        Publisher = string.Empty;
        Description = string.Empty;
        StartDate = EndDate = string.Empty;
        EventTags = string.Empty;
        EventCoverPath = null;
        Designation = string.Empty;
        _selectedUniverse = null;
        MetronUniverseId = string.Empty;
        MetronUniverseQuery = string.Empty;
        UniverseResults.Clear();
        OnPropertyChanged(nameof(HasUniverseResults));
        SelectedPreset = "Blank";
        Stage = SetupStage.Basics;
        _guidedCreate = false;
        _editId = 0;
        foreach (var choice in EventTypeSelections) choice.IsSelected = false;
        _selectedArc = null; _selectedArcId = null; _selectedUniverse = null;
        ProviderIssueMatches.Clear(); ProviderSeriesMatches.Clear();
        LoadPublisherSuggestions();
    }

    /// <summary>The library's publishers for the continuity dialog's publisher box (docs/superpowers/specs/2026-09-28-continuity-screen-
    /// redesign-design.md, R9): every publisher named on an issue, a series or another continuity, alphabetically.</summary>
    public IReadOnlyList<string> PublisherSuggestions { get; private set; } = Array.Empty<string>();

    public bool HasPublisher => !string.IsNullOrWhiteSpace(Publisher);

    private void LoadPublisherSuggestions()
    {
        if (CurrentKind != Kind.Continuity)
        {
            return;
        }

        try
        {
            using var context = PaperbunkrDb.CreateContext();
            PublisherSuggestions = context.Issues.Select(i => i.Publisher)
                .Concat(context.Series.Select(s => s.Publisher))
                .Concat(context.Continuities.Select(c => c.Publisher))
                .Where(p => p != null && p != "")
                .Distinct()
                .AsEnumerable()
                .Select(p => p!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            PublisherSuggestions = Array.Empty<string>();
        }

        OnPropertyChanged(nameof(PublisherSuggestions));
    }

    /// <summary>Puts the dialog into edit mode, pre-filled from an existing entity.</summary>
    public void LoadForEdit(Kind kind, int id)
    {
        Stage = SetupStage.Basics;
        _guidedCreate = false;
        using var context = PaperbunkrDb.CreateContext();

        if (kind == Kind.Event)
        {
            var storyEvent = context.StoryEvents.FirstOrDefault(e => e.Id == id);
            if (storyEvent is null)
            {
                return;
            }

            Name = storyEvent.Name;
            Description = storyEvent.Description ?? string.Empty;
            Publisher = string.Empty;
            StartDate = storyEvent.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            EndDate = storyEvent.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            EventTags = string.Join(Environment.NewLine, context.StoryEventTags.Where(t => t.StoryEventId == id).Select(t => t.Value));
            EventCoverPath = storyEvent.CoverSource == StoryEventCoverSource.Manual ? StoryEventCoverPaths.GetPath(id) : null;
            foreach (var choice in EventTypeSelections)
                choice.IsSelected = context.StoryEventTypeAssignments.Any(t => t.StoryEventId == id && t.Type == choice.Type);
        }
        else
        {
            var continuity = context.Continuities.FirstOrDefault(c => c.Id == id);
            if (continuity is null)
            {
                return;
            }

            Name = continuity.Name;
            Description = continuity.Description ?? string.Empty;
            Publisher = continuity.Publisher ?? string.Empty;
            Designation = continuity.Designation ?? string.Empty;
        }

        CurrentKind = kind;
        _editId = id;
        IsEdit = true;
        LoadPublisherSuggestions();
    }

    private int _editId;

    /// <summary>Reopens the optional setup flow for a previously created entity without changing its existing edit workflow.</summary>
    public void LoadGuidedSetup(Kind kind, int id)
    {
        LoadForEdit(kind, id);
        _guidedCreate = true;
        Stage = SetupStage.Provider;
        ArcResults.Clear();
        ProviderIssueMatches.Clear();
        ProviderSeriesMatches.Clear();
        _selectedArc = null;
        _selectedArcId = null;
        _selectedArcDescription = null; _selectedArcCover = null; _selectedComicVineArcId = null;
        _selectedUniverse = null;
        ProviderSeriesMatches.Clear(); ProviderIssueMatches.Clear();
        MetronUniverseId = string.Empty;
        MetronUniverseQuery = Name;
        UniverseResults.Clear();
        OnPropertyChanged(nameof(HasUniverseResults));
        ProviderStatus = string.Empty;
        OnPropertyChanged(nameof(HasSelectedUniverse)); OnPropertyChanged(nameof(SelectedUniverseSummary));
        LoadRelatedEvents(id);
    }

    [ObservableProperty]
    private bool _isEdit;

    partial void OnIsEditChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SaveButtonLabel));
        OnPropertyChanged(nameof(IsAlternateUniversePreset));
    }

    [ObservableProperty]
    private Kind _currentKind;

    partial void OnCurrentKindChanged(Kind value)
    {
        OnPropertyChanged(nameof(IsContinuity));
        OnPropertyChanged(nameof(IsAlternateUniversePreset));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(PresetOptions));
    }

    partial void OnStageChanged(SetupStage value)
    {
        OnPropertyChanged(nameof(IsBasics)); OnPropertyChanged(nameof(IsProviderStage)); OnPropertyChanged(nameof(IsReviewStage));
    }

    partial void OnSelectedPresetChanged(string value)
    {
        OnPropertyChanged(nameof(IsAlternateUniversePreset));
        if (CurrentKind == Kind.Event)
        {
            foreach (var choice in EventTypeSelections) choice.IsSelected = value switch
            {
                "Crossover Event" => choice.Type == StoryEventType.Crossover,
                "Limited Series Event" => choice.Type == StoryEventType.LimitedSeries,
                _ => false,
            };
        }
    }

    public bool IsContinuity => CurrentKind == Kind.Continuity;

    [ObservableProperty] private string _startDate = string.Empty;
    partial void OnStartDateChanged(string value) { OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(DateEntryError)); }
    [ObservableProperty] private string _endDate = string.Empty;
    partial void OnEndDateChanged(string value) { OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(DateEntryError)); }
    [ObservableProperty] private string _eventTags = string.Empty;
    [ObservableProperty] private string? _eventCoverPath;

    [RelayCommand]
    private async Task ChooseEventCoverAsync()
    {
        string? path = await new FilePickerService().PickImageFileAsync("Choose Event Cover");
        if (path is null) return;
        EventCoverPath = path;
    }

    [RelayCommand]
    private void ClearEventCover() => EventCoverPath = null;

    public string Title => (IsEdit, CurrentKind) switch
    {
        (true, Kind.Event) => "EDIT EVENT",
        (true, Kind.Continuity) => "EDIT CONTINUITY",
        (false, Kind.Event) => "NEW EVENT",
        _ => "NEW CONTINUITY",
    };

    public string SaveButtonLabel => Stage switch { SetupStage.Basics when !IsEdit => "Save and continue", SetupStage.Provider => "Review", SetupStage.Review => "Apply setup", _ => "Save" };

    [ObservableProperty]
    private string _name = string.Empty;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(CanCreate));

    [ObservableProperty]
    private string _publisher = string.Empty;

    partial void OnPublisherChanged(string value) => OnPropertyChanged(nameof(HasPublisher));

    [ObservableProperty]
    private string _description = string.Empty;

    public string? DateEntryError =>
        (!string.IsNullOrWhiteSpace(StartDate) && ParseDate(StartDate) is null)
            || (!string.IsNullOrWhiteSpace(EndDate) && ParseDate(EndDate) is null)
            ? "Enter dates as YYYY-MM-DD, or leave them blank."
            : ParseDate(StartDate) is DateTime start && ParseDate(EndDate) is DateTime end && end < start
                ? "The end date must be on or after the start date."
                : null;

    public bool CanCreate => !string.IsNullOrWhiteSpace(Name) && (CurrentKind == Kind.Continuity || DateEntryError is null);

    [RelayCommand]
    private void Create()
    {
        if (Stage == SetupStage.Provider)
        {
            Stage = SetupStage.Review;
            return;
        }
        if (Stage == SetupStage.Review)
        {
            ApplyGuidedSetup();
            return;
        }
        if (!CanCreate)
        {
            return;
        }

        string name = Name.Trim();
        string? description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        string? publisher = string.IsNullOrWhiteSpace(Publisher) ? null : Publisher.Trim();
        var now = DateTime.UtcNow;

        using var context = PaperbunkrDb.CreateContext();

        if (CurrentKind == Kind.Event)
        {
            DateTime? startDate = ParseDate(StartDate);
            DateTime? endDate = ParseDate(EndDate);
            if ((!string.IsNullOrWhiteSpace(StartDate) && startDate is null) || (!string.IsNullOrWhiteSpace(EndDate) && endDate is null)
                || (startDate is not null && endDate is not null && endDate < startDate)) return;
            if (IsEdit)
            {
                var storyEvent = context.StoryEvents.FirstOrDefault(e => e.Id == _editId);
                if (storyEvent is null)
                {
                    return;
                }

                if (!string.Equals(storyEvent.Name, name, StringComparison.Ordinal))
                {
                    // A name you chose is protected: the Story Event resolver never merges a User event without asking
                    // (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md, Q9).
                    storyEvent.Origin = StoryEventOrigin.User;
                }

                storyEvent.Name = name;
                storyEvent.Description = description;
                storyEvent.StartDate = startDate;
                storyEvent.EndDate = endDate;
                SaveEventTags(context, storyEvent.Id);
                SaveEventTypes(context, storyEvent.Id);
                SaveEventCover(storyEvent);
                storyEvent.UpdatedAt = now;
                context.SaveChanges();
                if (_guidedCreate) Stage = SetupStage.Provider;
                else _onSaved(Kind.Event, storyEvent.Id);
            }
            else
            {
                var storyEvent = new StoryEvent { Name = name, Description = description, StartDate = startDate, EndDate = endDate, CreatedAt = now, UpdatedAt = now };
                context.StoryEvents.Add(storyEvent);
                context.SaveChanges();
                SaveEventTags(context, storyEvent.Id);
                SaveEventTypes(context, storyEvent.Id);
                SaveEventCover(storyEvent);
                context.SaveChanges();
                _editId = storyEvent.Id;
                _guidedCreate = true;
                LoadRelatedEvents(storyEvent.Id);
                Stage = SetupStage.Provider;
            }
        }
        else
        {
            if (IsEdit)
            {
                var continuity = context.Continuities.FirstOrDefault(c => c.Id == _editId);
                if (continuity is null)
                {
                    return;
                }

                continuity.Name = name;
                continuity.Description = description;
                continuity.Publisher = publisher;
                continuity.Designation = string.IsNullOrWhiteSpace(Designation) ? null : Designation.Trim();
                continuity.UpdatedAt = now;
                context.SaveChanges();
                if (_guidedCreate) Stage = SetupStage.Provider;
                else _onSaved(Kind.Continuity, continuity.Id);
            }
            else
            {
                // The same case-insensitive reuse the Related tab's continuity box has: a name that already exists opens that one
                // (filling in what it lacks) instead of making a second "Earth-616".
                var continuity = ContinuityResolver.GetOrCreate(context, name);
                continuity.Publisher ??= publisher;
                continuity.Description ??= description;
                continuity.Designation ??= string.IsNullOrWhiteSpace(Designation) ? null : Designation.Trim();
                continuity.UpdatedAt = now;
                context.SaveChanges();
                _editId = continuity.Id;
                _guidedCreate = true;
                Stage = SetupStage.Provider;
            }
        }
    }

    private void SaveEventTypes(PaperbunkrDbContext context, int eventId)
    {
        var selected = EventTypeSelections.Where(c => c.IsSelected).Select(c => c.Type).ToHashSet();
        var current = context.StoryEventTypeAssignments.Where(t => t.StoryEventId == eventId).ToList();
        context.StoryEventTypeAssignments.RemoveRange(current.Where(t => !selected.Contains(t.Type)));
        foreach (var type in selected.Where(t => current.All(row => row.Type != t)))
            context.StoryEventTypeAssignments.Add(new StoryEventTypeAssignment { StoryEventId = eventId, Type = type });
    }

    [RelayCommand]
    private void SkipSetup() => FinishGuidedSetup();

    [RelayCommand]
    private void BackToBasics() => Stage = SetupStage.Basics;

    [RelayCommand]
    private async Task SearchProviderAsync()
    {
        if (CurrentKind != Kind.Event || string.IsNullOrWhiteSpace(ProviderQuery))
        {
            ProviderStatus = IsContinuity ? "Select a Metron universe from the continuity's existing Metron browser after saving." : "Enter an Event title to search.";
            return;
        }
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            var provider = ComicProviderFactory.Parse(ProviderSource);
            var sources = ProviderArcIdentitySource.CreateAvailable(context);
            if (!sources.TryGetValue(provider, out var source))
            {
                ProviderStatus = ComicProviderFactory.MissingCredentialsMessage(provider);
                return;
            }
            ProviderStatus = "Searching…";
            _selectedArc = null; _selectedArcId = null; _selectedArcDescription = null; _selectedArcCover = null; _selectedComicVineArcId = null;
            ProviderIssueMatches.Clear(); OnPropertyChanged(nameof(HasSelectedProvider)); OnPropertyChanged(nameof(ProviderIssueSummary));
            var results = await source.SearchArcsAsync(ProviderQuery.Trim(), CancellationToken.None);
            ArcResults.Clear();
            foreach (var result in results) ArcResults.Add(result);
            ProviderStatus = results.Count == 0 ? "No provider Events found." : $"{results.Count} provider results. Select one to preview.";
        }
        catch (Exception ex)
        {
            ProviderStatus = $"Provider search failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SearchMetronUniversesAsync()
    {
        string query = string.IsNullOrWhiteSpace(MetronUniverseQuery) ? Name.Trim() : MetronUniverseQuery.Trim();
        if (query.Length == 0)
        {
            ProviderStatus = "Enter a continuity name or universe designation to search Metron.";
            return;
        }
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            var provider = ComicProviderFactory.Create(context, ComicProvider.Metron);
            if (provider is not IMetronUniverses metron)
            {
                ProviderStatus = ComicProviderFactory.MissingCredentialsMessage(ComicProvider.Metron);
                return;
            }
            ProviderStatus = "Searching Metron universes…";
            var results = await metron.SearchUniversesAsync(query, CancellationToken.None);
            UniverseResults.Clear();
            foreach (var result in results) UniverseResults.Add(result);
            OnPropertyChanged(nameof(HasUniverseResults));
            ProviderStatus = results.Count == 0 ? "No Metron universes matched. Try the designation or a shorter name."
                : $"{results.Count} Metron universes found. Choose the one that represents this continuity.";
        }
        catch (Exception ex) { ProviderStatus = $"Metron universe search failed: {ex.Message}"; }
    }

    [RelayCommand]
    private Task SelectMetronUniverseAsync(MetronUniverseInfo? universe) => universe is null
        ? Task.CompletedTask
        : LoadMetronUniverseAsync(universe.Id);

    [RelayCommand]
    private async Task SearchMetronUniverseAsync()
    {
        if (!int.TryParse(MetronUniverseId, out int id) || id <= 0)
        {
            ProviderStatus = "Enter a Metron universe ID to preview it.";
            return;
        }
        await LoadMetronUniverseAsync(id);
    }

    private async Task LoadMetronUniverseAsync(int id)
    {
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            var metronProvider = ComicProviderFactory.Create(context, ComicProvider.Metron);
            if (metronProvider is not IMetronUniverses metron)
            {
                ProviderStatus = ComicProviderFactory.MissingCredentialsMessage(ComicProvider.Metron);
                return;
            }
            ProviderStatus = "Looking up Metron universe…";
            _selectedUniverse = await metron.GetUniverseAsync(id, CancellationToken.None);
            OnPropertyChanged(nameof(HasSelectedUniverse)); OnPropertyChanged(nameof(SelectedUniverseSummary));
            ProviderSeriesMatches.Clear();
            if (_selectedUniverse is not null && metronProvider is MetronClient metronClient)
            {
                var catalog = await metronClient.GetUniverseSeriesAsync(id, CancellationToken.None);
                var providerLinks = context.ComicMetadataExternalIds.AsNoTracking()
                    .Where(link => link.EntityKind == ComicMetadataEntityKind.Series && link.Provider == ComicProvider.Metron)
                    .Select(link => new { link.EntityId, link.ExternalId }).ToList();
                var localById = providerLinks.Where(link => int.TryParse(link.ExternalId, out _))
                    .GroupBy(link => int.Parse(link.ExternalId, CultureInfo.InvariantCulture))
                    .ToDictionary(group => group.Key, group => group.Select(link => link.EntityId).Distinct().ToList());
                var currentMembers = context.ContinuityMemberships.Where(m => m.ContinuityId == _editId).Select(m => m.SeriesId).ToHashSet();
                var localNames = context.Series.AsNoTracking().Select(s => new { s.Id, s.Name }).ToList();
                foreach (var item in catalog)
                {
                    int? localId = null;
                    bool exact = localById.TryGetValue(item.Id, out var exactIds) && exactIds.Count == 1;
                    bool collision = localById.TryGetValue(item.Id, out exactIds) && exactIds.Count > 1;
                    string matchLabel;
                    if (exact)
                    {
                        localId = exactIds![0];
                        matchLabel = "Exact Metron series ID";
                    }
                    else if (collision) matchLabel = "Metron ID collision in library";
                    else
                    {
                        var titleMatches = localNames.Where(local => string.Equals(local.Name, item.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (titleMatches.Count == 1) { localId = titleMatches[0].Id; matchLabel = "Title suggestion · confirm manually"; }
                        else matchLabel = titleMatches.Count > 1 ? "Several title matches · choose manually" : "Not in library";
                    }
                    bool canAdd = localId.HasValue && !currentMembers.Contains(localId.Value);
                    ProviderSeriesMatches.Add(new ProviderSeriesMatch
                    {
                        MetronSeriesId = item.Id, LocalSeriesId = localId, Name = item.Name,
                        MatchLabel = localId.HasValue && currentMembers.Contains(localId.Value) ? "Already in this continuity" : matchLabel,
                        CanAdd = canAdd, IsSelected = exact && canAdd,
                    });
                }
            }
            ProviderStatus = _selectedUniverse is null ? "Metron did not return that universe." : "Universe loaded. Exact series IDs are selected; title suggestions are unchecked.";
        }
        catch (Exception ex) { ProviderStatus = $"Metron lookup failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SelectProviderArcAsync(Paperbunkr.Data.ReadingLists.Sources.ArcSearchResult? result)
    {
        if (result is null) return;
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            var provider = ComicProviderFactory.Parse(ProviderSource);
            var sources = ProviderArcIdentitySource.CreateAvailable(context);
            if (!sources.TryGetValue(provider, out var source)) { ProviderStatus = ComicProviderFactory.MissingCredentialsMessage(provider); return; }
            var listSource = Paperbunkr.Data.ReadingLists.Sources.ReadingListSourceRegistry.Get(context, ProviderSource);
            var overview = listSource is null ? null : await listSource.GetArcOverviewAsync(result.Id, CancellationToken.None);
            _selectedArc = result; _selectedArcId = result.Id;
            _selectedArcDescription = overview?.Description ?? result.Deck;
            _selectedArcCover = overview?.CoverImageUrl;
            _selectedComicVineArcId = overview?.ComicVineId;
            ProviderIssueMatches.Clear();
            var entries = await listSource!.GetArcEntriesAsync(result.Id, CancellationToken.None);
            var suppliedIds = entries.Issues.SelectMany(issue => GetProviderIds(issue)).Distinct().ToList();
            var localIds = suppliedIds.Count == 0
                ? new List<ComicMetadataExternalId>()
                : context.ComicMetadataExternalIds.AsNoTracking()
                    .Where(id => id.EntityKind == ComicMetadataEntityKind.Issue && suppliedIds.Select(x => x.ExternalId).Contains(id.ExternalId))
                    .ToList();
            var chosenIds = new HashSet<int>();
            int position = 0;
            foreach (var arcIssue in entries.Issues)
            {
                var ids = GetProviderIds(arcIssue).ToHashSet();
                var matches = localIds.Where(local => ids.Contains((local.Provider, local.ExternalId)))
                    .Select(local => local.EntityId).Distinct().ToList();
                int? issueId = matches.Count == 1 ? matches[0] : null;
                string? problem = matches.Count > 1 ? "Conflicting local provider identity records" : null;
                if (issueId is int matchedId && !chosenIds.Add(matchedId))
                { issueId = null; problem = "Another source entry already resolves to this Issue"; }
                EventMembershipRole? suggested = null;
                if (issueId is int suggestionId)
                {
                    var localIssue = context.Issues.AsNoTracking().FirstOrDefault(i => i.Id == suggestionId);
                    if (localIssue?.Format is string format)
                        suggested = FormatSignalCatalog.Resolve(format).SuggestedRole;
                }
                string label = $"{position + 1}. {arcIssue.Series} #{arcIssue.Number}";
                if (arcIssue.Year > 0) label += $" · {arcIssue.Year}";
                ProviderIssueMatches.Add(new ProviderIssueMatch { Position = position++, Label = label, IssueId = issueId, Problem = problem,
                    SuggestedRole = suggested, IsSelected = issueId.HasValue });
            }
            OnPropertyChanged(nameof(ProviderIssueSummary));
            OnPropertyChanged(nameof(HasSelectedProvider));
            OnPropertyChanged(nameof(SelectedProviderName));
            OnPropertyChanged(nameof(ProviderIssueSummary));
            ProviderStatus = $"Preview: {result.Name} · {entries.Issues.Count} source issues. Apply only adds selected exact-ID matches and fills blank Event fields.";
        }
        catch (Exception ex) { ProviderStatus = $"Could not load provider preview: {ex.Message}"; }
    }

    private void ApplyGuidedSetup()
    {
        if (!_guidedCreate || _editId <= 0) { FinishGuidedSetup(); return; }
        using var context = PaperbunkrDb.CreateContext();
        var now = DateTime.UtcNow;
        if (CurrentKind == Kind.Event)
        {
            var row = context.StoryEvents.FirstOrDefault(e => e.Id == _editId);
            if (row is not null && _selectedArc is not null && _selectedArcId is not null)
            {
                bool metron = string.Equals(ProviderSource, "Metron", StringComparison.OrdinalIgnoreCase);
                bool identityCollision = metron
                    ? context.StoryEvents.Any(e => e.Id != row.Id && e.MetronArcId == _selectedArcId)
                    : context.StoryEvents.Any(e => e.Id != row.Id && e.ComicVineArcId == _selectedArcId);
                bool providerConflict = metron
                    ? (!string.IsNullOrWhiteSpace(row.MetronArcId) && row.MetronArcId != _selectedArcId)
                      || (!string.IsNullOrWhiteSpace(row.ComicVineArcId) && !string.IsNullOrWhiteSpace(_selectedComicVineArcId) && row.ComicVineArcId != _selectedComicVineArcId)
                    : (!string.IsNullOrWhiteSpace(row.ComicVineArcId) && row.ComicVineArcId != _selectedArcId);
                if (identityCollision || providerConflict)
                {
                    ProviderStatus = identityCollision
                        ? "This provider identity is already linked to another Event. Review that Event instead of creating a duplicate link."
                        : "The provider identity conflicts with the Event's saved identity. No changes were applied.";
                    return;
                }
                if (metron)
                {
                    row.MetronArcId ??= _selectedArcId;
                    row.ComicVineArcId ??= _selectedComicVineArcId;
                }
                else row.ComicVineArcId ??= _selectedArcId;
                row.Description ??= _selectedArcDescription;
                if (row.CoverSource == StoryEventCoverSource.None && !string.IsNullOrWhiteSpace(_selectedArcCover))
                { row.CoverImageUrl = _selectedArcCover; row.CoverSource = metron ? StoryEventCoverSource.Metron : StoryEventCoverSource.ComicVine; }
                row.UpdatedAt = now;
                using var transaction = context.Database.BeginTransaction();
                var existingIssueIds = context.EventMemberships.Where(m => m.StoryEventId == row.Id).Select(m => m.IssueId).ToHashSet();
                int nextPosition = context.EventMemberships.Where(m => m.StoryEventId == row.Id).Select(m => (int?)m.Position).Max() is int maxPosition ? maxPosition + 1 : 0;
                var selectedMatches = ProviderIssueMatches.Where(m => m.IsSelected && m.IssueId.HasValue && !existingIssueIds.Contains(m.IssueId.Value))
                    .OrderBy(m => m.Position).ToList();
                foreach (var match in selectedMatches)
                {
                    if (match.IssueId is not int issueId || !context.Issues.Any(i => i.Id == issueId)) continue;
                    context.EventMemberships.Add(new EventMembership
                    {
                        StoryEventId = row.Id, IssueId = issueId, Position = nextPosition++, Role = EventMembershipRole.Core,
                        SuggestedRole = match.SuggestedRole, SuggestedReason = match.SuggestedRole is null ? null : "Suggested from the library's existing format role detection.",
                    });
                }
                if (Enum.TryParse<RelationType>(SelectedRelatedEventRelationType, out var relationType))
                {
                    foreach (var related in RelatedEvents.Where(e => e.IsSelected))
                    {
                        bool exists = context.EventRelations.Any(r => r.SourceEventId == row.Id && r.TargetEventId == related.Id && r.RelationType == relationType);
                        if (!exists) context.EventRelations.Add(new EventRelation { SourceEventId = row.Id, TargetEventId = related.Id, RelationType = relationType, CreatedAt = now });
                    }
                }
                context.SaveChanges();
                transaction.Commit();
            }
        }
        else
        {
            var row = context.Continuities.FirstOrDefault(c => c.Id == _editId);
            if (row is not null)
            {
                if (_selectedUniverse is not null)
                {
                    if (context.Continuities.Any(c => c.Id != row.Id && c.MetronId == _selectedUniverse.Id.ToString()))
                    {
                        ProviderStatus = "That Metron universe is already linked to another continuity. No changes were applied.";
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(row.MetronId) && row.MetronId != _selectedUniverse.Id.ToString())
                    {
                        ProviderStatus = "The Metron universe conflicts with this continuity's saved identity. No changes were applied.";
                        return;
                    }
                    row.MetronId ??= _selectedUniverse.Id.ToString();
                    row.Designation ??= _selectedUniverse.Designation;
                    row.Publisher ??= _selectedUniverse.Publisher;
                }
                row.Designation ??= string.IsNullOrWhiteSpace(Designation) ? null : Designation.Trim();
                row.UpdatedAt = now;
                using var transaction = context.Database.BeginTransaction();
                foreach (var seriesId in ProviderSeriesMatches.Where(m => m.IsSelected && m.CanAdd).Select(m => m.LocalSeriesId!.Value).Distinct())
                    ContinuityResolver.AddSeriesToContinuity(context, seriesId, row.Id);
                context.SaveChanges();
                transaction.Commit();
            }
        }
        FinishGuidedSetup();
    }

    private void FinishGuidedSetup() => _onSaved(CurrentKind, _editId);

    private void LoadRelatedEvents(int excludeId)
    {
        RelatedEvents.Clear();
        if (CurrentKind != Kind.Event) return;
        using var context = PaperbunkrDb.CreateContext();
        foreach (var item in context.StoryEvents.AsNoTracking().Where(e => e.Id != excludeId).OrderBy(e => e.Name).Select(e => new { e.Id, e.Name }).ToList())
            RelatedEvents.Add(new RelatedEventSelection { Id = item.Id, Name = item.Name });
    }

    private static IEnumerable<(ComicProvider Provider, string ExternalId)> GetProviderIds(Paperbunkr.Data.ReadingLists.Sources.ArcIssue issue)
    {
        if (!string.IsNullOrWhiteSpace(issue.ComicVineIssueId)) yield return (ComicProvider.ComicVine, issue.ComicVineIssueId);
        if (!string.IsNullOrWhiteSpace(issue.MetronIssueId)) yield return (ComicProvider.Metron, issue.MetronIssueId);
    }

    private static DateTime? ParseDate(string text) => DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;

    private void SaveEventTags(PaperbunkrDbContext context, int eventId)
    {
        var tags = EventTags.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = context.StoryEventTags.Where(t => t.StoryEventId == eventId).ToList();
        context.StoryEventTags.RemoveRange(existing.Where(t => !tags.Contains(t.Value, StringComparer.OrdinalIgnoreCase)));
        foreach (var tag in tags.Where(t => existing.All(e => !string.Equals(e.Value, t, StringComparison.OrdinalIgnoreCase))))
            context.StoryEventTags.Add(new StoryEventTag { StoryEventId = eventId, Value = tag, Category = "Event", Weight = IssueTagWeight.Unset });
    }

    private void SaveEventCover(StoryEvent storyEvent)
    {
        if (EventCoverPath is null)
        {
            if (storyEvent.CoverSource == StoryEventCoverSource.Manual) StoryEventCoverCache.Invalidate(storyEvent.Id);
            if (storyEvent.CoverSource == StoryEventCoverSource.Manual) { storyEvent.CoverImageUrl = null; storyEvent.CoverSource = StoryEventCoverSource.None; }
            return;
        }
        if (!StoryEventCoverCache.TrySetCustomCover(storyEvent.Id, EventCoverPath)) return;
        storyEvent.CoverImageUrl = null;
        storyEvent.CoverSource = StoryEventCoverSource.Manual;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_guidedCreate && _editId > 0)
            _onSaved(CurrentKind, _editId);
        else
            _onCancel();
    }
}
