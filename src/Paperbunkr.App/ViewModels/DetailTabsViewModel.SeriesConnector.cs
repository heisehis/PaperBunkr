using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.SeriesConnector;

namespace Paperbunkr.App.ViewModels;

/// <summary>A suggestion card's payload: the other series and what the suggestion would save if accepted.</summary>
public sealed record RelationSuggestionPayload(int OtherSeriesId, InferredSeriesRelation Suggestion);

/// <summary>An "Other events" card's payload: the event to open.</summary>
public sealed record EventCardPayload(int StoryEventId);

/// <summary>
/// The Related tab's series-connector parts (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §6): one row per story
/// event with a main series (main series first, then the tie-ins with their issue ranges), an "Other events" rail for big events with no
/// main series, the suggestion cards at the end of the Related rail, and the "Find relations" button. Replaces the old flat Same Event rail.
/// </summary>
public partial class DetailTabsViewModel
{
    /// <summary>How many event rows show before "Show all".</summary>
    public const int VisibleEventRowCap = 3;

    public ObservableCollection<EventRelationRow> EventRows { get; } = new();

    /// <summary>What the view draws: the first <see cref="VisibleEventRowCap"/> rows, or all of them once "Show all" is clicked.</summary>
    public ObservableCollection<EventRelationRow> VisibleEventRows { get; } = new();

    /// <summary>Events with no main series that span four or more series - one card each, opening the event.</summary>
    public ObservableCollection<PosterRailItem> OtherEventsRail { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenEventRows))]
    private bool _showAllEvents;

    partial void OnShowAllEventsChanged(bool value) => RebuildVisibleEventRows();

    public bool HasEventRows => EventRows.Count > 0;

    public bool HasHiddenEventRows => !ShowAllEvents && EventRows.Count > VisibleEventRowCap;

    public string ShowAllEventsLabel => $"Show all {EventRows.Count} events";

    public bool HasOtherEvents => OtherEventsRail.Count > 0;

    /// <summary>Opens a story event's page; set by the shell (the screens that host this view model don't otherwise reach the Continuity screen).</summary>
    public Action<int>? OpenStoryEvent { get; set; }

    /// <summary>The last suggestions load, so tests can wait for it.</summary>
    internal Task SuggestionsLoaded { get; private set; } = Task.CompletedTask;

    private int _suggestionsGeneration;

    [RelayCommand]
    private void ShowAllEventRows() => ShowAllEvents = true;

    [RelayCommand]
    private void OpenEvent(object? payload)
    {
        int? id = payload switch
        {
            int eventId => eventId,
            EventCardPayload card => card.StoryEventId,
            EventRelationRow row => row.StoryEventId,
            _ => null,
        };

        if (id is int storyEventId)
        {
            OpenStoryEvent?.Invoke(storyEventId);
        }
    }

    /// <summary>The last event-rows load, so tests can wait for it.</summary>
    internal Task EventRowsLoaded { get; private set; } = Task.CompletedTask;

    private int _eventRowsGeneration;

    /// <summary>
    /// Builds the event rows on a thread-pool thread and shows them when done (same shape as More Like This): a series in many events, or a
    /// tie-in to a 40-series event, decodes dozens of covers, which stalled the page open on the UI thread (2026-10-29).
    /// </summary>
    private void RefreshEventRows(int seriesId)
    {
        int generation = ++_eventRowsGeneration;
        EventRows.Clear();
        OtherEventsRail.Clear();
        ShowAllEvents = false;
        RebuildVisibleEventRows();
        NotifyEventRowsChanged();

        var contextFactory = _contextFactory;
        EventRowsLoaded = LoadAsync();

        async Task LoadAsync()
        {
            (List<EventRelationRow> Rows, List<PosterRailItem> Other) built;
            try
            {
                built = await Task.Run(() => BuildEventRows(contextFactory, seriesId)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogMilestone($"Detail: event rows failed ({ex.GetType().Name}: {ex.Message}).");
                return;
            }

            if (generation != _eventRowsGeneration || _seriesId != seriesId)
            {
                return;
            }

            foreach (var row in built.Rows)
            {
                EventRows.Add(row);
            }

            foreach (var card in built.Other)
            {
                OtherEventsRail.Add(card);
            }

            RebuildVisibleEventRows();
            NotifyEventRowsChanged();
        }
    }

    private void NotifyEventRowsChanged()
    {
        OnPropertyChanged(nameof(HasEventRows));
        OnPropertyChanged(nameof(HasOtherEvents));
        OnPropertyChanged(nameof(ShowAllEventsLabel));
        OnPropertyChanged(nameof(HasAnyRelatedRail));
    }

    private static (List<EventRelationRow> Rows, List<PosterRailItem> Other) BuildEventRows(Func<PaperbunkrDbContext> contextFactory, int seriesId)
    {
        using var context = contextFactory();
        var other = new List<PosterRailItem>();
        var rows = new List<(EventRelationRow Row, DateTime? Start)>();

        var eventIds = context.EventMemberships.AsNoTracking()
            .Where(m => m.Issue!.SeriesId == seriesId)
            .Select(m => m.StoryEventId)
            .Distinct()
            .ToList();
        if (eventIds.Count == 0)
        {
            return (new(), other);
        }

        var members = context.EventMemberships.AsNoTracking()
            .Where(m => eventIds.Contains(m.StoryEventId))
            .Select(m => new { m.StoryEventId, m.Position, m.Issue!.SeriesId, SeriesName = m.Issue.Series!.Name })
            .ToList()
            .GroupBy(m => m.StoryEventId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Position).Select(m => (m.SeriesId, m.SeriesName)).ToList());
        var keys = eventIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
        var tieIns = context.RelationEvidence.AsNoTracking()
            .Where(e => e.Provider == RelationEvidenceProvider.Inferred && e.ProviderSourceId != null && keys.Contains(e.ProviderSourceId)
                && e.MediaRelation!.RelationType == RelationType.TieIn)
            .Select(e => new { e.ProviderSourceId, e.ProviderRelationType, e.MediaRelation!.SourceSeriesId, e.MediaRelation.TargetSeriesId })
            .ToList();
        var memberSeriesIds = members.Values.SelectMany(v => v).Select(m => m.SeriesId).Distinct().ToList();
        var names = context.Series.AsNoTracking()
            .Where(s => memberSeriesIds.Contains(s.Id))
            .ToDictionary(s => s.Id, s => s.Name);

        // One decode per series, however many events it shows up in.
        var covers = new Dictionary<int, Avalonia.Media.Imaging.Bitmap?>();
        Avalonia.Media.Imaging.Bitmap? CoverOf(int id) => covers.TryGetValue(id, out var c) ? c : covers[id] = RailCoverFor(context, id);

        PosterRailItem Card(int id, string name, string? subLabel) => new()
        {
            Id = id,
            Name = name,
            SubLabel = subLabel,
            CoverBrush = SeriesCardSample.CoverBrushFor(name),
            CoverImage = CoverOf(id),
            Payload = id,
        };

        foreach (var storyEvent in context.StoryEvents.AsNoTracking().Where(e => eventIds.Contains(e.Id)).ToList())
        {
            var rowsInOrder = members.GetValueOrDefault(storyEvent.Id) ?? new();
            var seriesInEvent = rowsInOrder.Select(r => r.SeriesId).Distinct().ToList();
            if (seriesInEvent.Count < 2)
            {
                continue;
            }

            int? main = EventSpineResolver.Resolve(storyEvent.Name, storyEvent.SpineSeriesId, rowsInOrder);
            if (main is not int mainId)
            {
                if (seriesInEvent.Count > EventTieInInference.MaxCrossoverSeries)
                {
                    other.Add(new PosterRailItem
                    {
                        Id = storyEvent.Id,
                        Name = storyEvent.Name,
                        SubLabel = $"{seriesInEvent.Count} series",
                        CoverBrush = SeriesCardSample.CoverBrushFor(storyEvent.Name),
                        CoverImage = CoverOf(seriesInEvent[0]),
                        Payload = new EventCardPayload(storyEvent.Id),
                    });
                }

                continue;
            }

            var row = new EventRelationRow(storyEvent.Id, storyEvent.Name, $"{seriesInEvent.Count} series · open event");
            if (mainId != seriesId && names.TryGetValue(mainId, out var mainName))
            {
                row.Items.Add(Card(mainId, mainName, "Main event"));
            }

            string key = storyEvent.Id.ToString(CultureInfo.InvariantCulture);
            var order = seriesInEvent.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            foreach (var tieIn in tieIns
                .Where(t => t.ProviderSourceId == key && t.TargetSeriesId == mainId && t.SourceSeriesId is int s && s != seriesId && names.ContainsKey(s))
                .OrderBy(t => order.GetValueOrDefault(t.SourceSeriesId!.Value, int.MaxValue)))
            {
                int tieInId = tieIn.SourceSeriesId!.Value;
                row.Items.Add(Card(tieInId, names[tieInId], IssueRangeFrom(tieIn.ProviderRelationType)));
            }

            if (row.Items.Count > 0)
            {
                rows.Add((row, storyEvent.StartDate));
            }
        }

        // Newest event first; undated ones last.
        var ordered = rows.OrderBy(r => r.Start is null).ThenByDescending(r => r.Start).ThenBy(r => r.Row.Title, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Row).ToList();
        return (ordered, other);
    }

    /// <summary>"Tie-in · #33–34" → "#33–34": the row already says it's the event's tie-ins.</summary>
    private static string? IssueRangeFrom(string? reason)
    {
        const string Prefix = "Tie-in · ";
        return reason is not null && reason.StartsWith(Prefix, StringComparison.Ordinal) ? reason[Prefix.Length..] : reason;
    }

    private void RebuildVisibleEventRows()
    {
        VisibleEventRows.Clear();
        foreach (var row in ShowAllEvents ? EventRows : EventRows.Take(VisibleEventRowCap))
        {
            VisibleEventRows.Add(row);
        }

        OnPropertyChanged(nameof(HasHiddenEventRows));
    }

    // ----- Suggestions (§6.3) -----

    /// <summary>Appends this series' suggestions to the end of the Related rail, from a thread-pool thread (same shape as More Like This).</summary>
    private void RefreshSuggestions(int seriesId)
    {
        int generation = ++_suggestionsGeneration;
        var contextFactory = _contextFactory;
        SuggestionsLoaded = LoadAsync();

        async Task LoadAsync()
        {
            List<PosterRailItem> items;
            try
            {
                items = await Task.Run(() => BuildSuggestions(contextFactory, seriesId)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // A missing suggestion is never worth failing the page over.
                DiagnosticsService.LogMilestone($"Detail: related-series suggestions failed ({ex.GetType().Name}: {ex.Message}).");
                return;
            }

            if (generation != _suggestionsGeneration || _seriesId != seriesId)
            {
                return;
            }

            foreach (var item in items)
            {
                RelatedRail.Add(item);
            }
        }
    }

    private static List<PosterRailItem> BuildSuggestions(Func<PaperbunkrDbContext> contextFactory, int seriesId)
    {
        using var context = contextFactory();
        if (!SeriesConnectorRunner.IsEnabled(context))
        {
            return new();
        }

        var items = new List<PosterRailItem>();
        foreach (var suggestion in SeriesRelationSuggestions.For(context, seriesId))
        {
            int other = suggestion.SourceSeriesId == seriesId ? suggestion.TargetSeriesId : suggestion.SourceSeriesId;
            if (context.Series.AsNoTracking().FirstOrDefault(s => s.Id == other) is not { } series)
            {
                continue;
            }

            items.Add(new PosterRailItem
            {
                Id = other,
                Name = series.Name,
                SubLabel = suggestion.Reason,
                CoverBrush = SeriesCardSample.CoverBrushFor(series.Name),
                CoverImage = RailCoverFor(context, other),
                IsSuggestion = true,
                Payload = new RelationSuggestionPayload(other, suggestion),
            });
        }

        return items;
    }

    /// <summary>Saves a suggestion as your own relation. Deferred a tick: the button is inside the card the refresh replaces.</summary>
    [RelayCommand]
    private void AcceptSuggestion(object? payload)
    {
        if (payload is not RelationSuggestionPayload suggestion || _seriesId is not int currentSeriesId)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            using var context = _contextFactory();
            var s = suggestion.Suggestion;
            MediaRelationResolver.TryCreate(context, s.SourceSeriesId, s.TargetSeriesId, s.Type);
            if (_seriesId == currentSeriesId)
            {
                RefreshRelated(context, currentSeriesId);
            }
        });
    }

    private void DismissSuggestion(RelationSuggestionPayload suggestion)
    {
        if (_seriesId is not int currentSeriesId)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            using var context = _contextFactory();
            MediaRelationResolver.DismissSeriesPair(context, currentSeriesId, suggestion.OtherSeriesId);
            if (_seriesId == currentSeriesId)
            {
                RefreshRelated(context, currentSeriesId);
            }
        });
    }

    // ----- Find relations (§5.2) -----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindRelationsCommand))]
    private bool _isFindingRelations;

    private bool CanFindRelations() => !IsFindingRelations;

    /// <summary>The Related tab's "Find relations": the whole connector, Metron included, as an Activity Center job; then the tab reloads.</summary>
    [RelayCommand(CanExecute = nameof(CanFindRelations))]
    private async Task FindRelations()
    {
        if (_seriesId is not int currentSeriesId)
        {
            return;
        }

        IsFindingRelations = true;
        using var job = ActivityCenter?.StartJob(ActivityJobKind.SyncMetadata, "Finding related series", cancellable: true,
            trigger: ActivityTrigger.Manual, toastPolicy: ActivityToastPolicy.FailuresOnly);
        try
        {
            job?.Begin();
            var contextFactory = _contextFactory;
            string summary = await Task.Run(() => SeriesConnectorRunner.RunFullAsync(contextFactory, job, job?.CancellationToken ?? CancellationToken.None)).ConfigureAwait(true);
            job?.Succeed(summary);
        }
        catch (OperationCanceledException)
        {
            job?.Fail("Cancelled.");
        }
        catch (Exception ex)
        {
            job?.Fail($"Couldn't find related series: {ex.Message}", ex: ex);
        }
        finally
        {
            IsFindingRelations = false;
        }

        if (_seriesId == currentSeriesId)
        {
            using var context = _contextFactory();
            RefreshRelated(context, currentSeriesId);
            RefreshEventRows(currentSeriesId);
        }
    }
}
