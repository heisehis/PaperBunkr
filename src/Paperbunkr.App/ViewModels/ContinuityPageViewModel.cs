using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.ContinuityScreen;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One continuity's page (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Continuity page"): the hero (stats,
/// GCD chip, Continue, Add series, Manage), Needs attention, Series as runs or a custom-ordered wall, Events in order, and Compare &amp;
/// merge. The member list loads synchronously so every edit shows at once (ported from the old screen's Continuities part, behaviour
/// unchanged); the overview - runs, stats, events, attention, Continue - is worked out off the UI thread and applied when it arrives.
/// Writes go through <see cref="ContinuityResolver"/>, the same calls the Related tab uses.
/// </summary>
public partial class ContinuityPageViewModel : ViewModelBase
{
    /// <summary>Automatic / Custom per continuity, for the session (the design's F2; nothing is stored). Per page, and the app has one.</summary>
    private readonly Dictionary<int, bool> _customOrder = new();

    private readonly ContinuityScreenNavigation _nav;
    private int _overviewGeneration;
    private int? _compareContinuityId;

    public ContinuityPageViewModel(ContinuityScreenNavigation nav) => _nav = nav;

    public int? ContinuityId { get; private set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPublisher))]
    private string _publisher = string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool HasPublisher => !string.IsNullOrWhiteSpace(Publisher);

    /// <summary>More/less on the hero's description (clamped to 3 lines).</summary>
    [ObservableProperty]
    private bool _isDescriptionExpanded;

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    /// <summary>The member series in their stored order (<c>ContinuityMembership.SortOrder</c>), each card carrying its note.</summary>
    public ObservableCollection<SeriesCardSample> Members { get; } = new();

    public bool HasNoMembers => ContinuityId is not null && Members.Count == 0;

    public string SeriesHeader => $"Series · {Members.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)}";

    // --- Load ---

    public void Load(int continuityId)
    {
        bool switched = ContinuityId != continuityId;
        ContinuityId = continuityId;

        using var context = PaperbunkrDb.CreateContext();
        var continuity = context.Continuities.AsNoTracking().FirstOrDefault(c => c.Id == continuityId);
        if (continuity is null)
        {
            return;
        }

        Name = continuity.Name;
        Description = continuity.Description ?? string.Empty;
        Publisher = continuity.Publisher ?? string.Empty;

        RefreshMembers(context, continuityId);
        MemberSelection.Clear();
        SeriesSelection.Clear();
        RaiseSelectionState();

        if (switched)
        {
            IsDescriptionExpanded = false;
            IsAddingSeries = false;
            SeriesSearchQuery = string.Empty;
            SeriesSearchResults.Clear();
            IsCustomOrder = _customOrder.GetValueOrDefault(continuityId);
            _deleteConfirm?.Cancel();          // an armed delete never carries over to another continuity
            ClearOverview();
        }

        LoadOverlaps(context, continuityId);
        _ = LoadOverviewAsync();
    }

    private void RefreshMembers(PaperbunkrDbContext context, int continuityId)
    {
        // Membership in SortOrder order, then the series re-queried with their issues so the posters show real counts, covers and
        // progress rings. Each card carries its membership note.
        var memberships = ContinuityResolver.GetMemberships(context, continuityId);
        var seriesIds = memberships.Select(m => m.SeriesId).ToList();
        var withIssues = context.Series.Include(s => s.Issues).Where(s => seriesIds.Contains(s.Id)).ToDictionary(s => s.Id);

        Members.Clear();
        foreach (var membership in memberships)
        {
            if (withIssues.TryGetValue(membership.SeriesId, out var series))
            {
                var card = SeriesCardSample.FromSeries(series);
                card.MembershipNote = membership.Note;
                Members.Add(card);
            }
        }

        OnPropertyChanged(nameof(HasNoMembers));
        OnPropertyChanged(nameof(SeriesHeader));
        RebuildPosters();
    }

    /// <summary>Reloads after an edit here: the page, its overview, and the sidebar's counts.</summary>
    private void Reload()
    {
        if (ContinuityId is int id)
        {
            Load(id);
            _nav.SidebarChanged();
        }
    }

    // --- Overview (off the UI thread) ---

    [ObservableProperty]
    private string _statsLine = string.Empty;

    [ObservableProperty]
    private bool _showGcdChip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContinue), nameof(ContinueLabel))]
    private ContinueTarget? _continue;

    public bool HasContinue => Continue is not null;

    public string ContinueLabel => Continue is { } c ? $"Continue · {c.Label}" : string.Empty;

    [ObservableProperty]
    private bool _isOverviewLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverviewError))]
    private string? _overviewError;

    public bool HasOverviewError => OverviewError is not null;

    public ObservableCollection<string> CollageKeys { get; } = new();

    public ObservableCollection<ContinuityRunRow> Runs { get; } = new();

    public ObservableCollection<ContinuityPosterItem> StandalonePosters { get; } = new();

    public ObservableCollection<ContinuityPosterItem> CustomPosters { get; } = new();

    public ObservableCollection<OverviewEventRow> EventsInOrder { get; } = new();

    public ObservableCollection<OverviewConnection> Connections { get; } = new();

    public ObservableCollection<OutsideSeriesInfo> OutsideSeries { get; } = new();

    public bool HasRuns => Runs.Count > 0;

    public bool HasStandalone => StandalonePosters.Count > 0;

    public bool HasEvents => EventsInOrder.Count > 0;

    public string EventsHeader => $"Events in order · {EventsInOrder.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttention))]
    private AttentionItem? _duplicatesAttention;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttention))]
    private AttentionItem? _connectionsAttention;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttention))]
    private AttentionItem? _outsideAttention;

    public bool HasAttention => DuplicatesAttention is not null || ConnectionsAttention is not null || OutsideAttention is not null;

    private ContinuityOverview? _overview;

    /// <summary>The most recent overview load - tests await it.</summary>
    internal Task OverviewTask { get; private set; } = Task.CompletedTask;

    private Task LoadOverviewAsync()
    {
        if (ContinuityId is not int id)
        {
            return Task.CompletedTask;
        }

        int generation = ++_overviewGeneration;
        IsOverviewLoading = true;
        OverviewError = null;
        return OverviewTask = LoadOverviewCoreAsync(id, generation);
    }

    private async Task LoadOverviewCoreAsync(int id, int generation)
    {
        ContinuityOverview? overview;
        try
        {
            overview = (ContinuityOverview?)await _nav.Runner(() =>
            {
                using var context = PaperbunkrDb.CreateContext();
                using var gcd = _nav.OpenGcd();
                return ContinuityOverviewLoader.Load(context, id, gcd);
            });
        }
        catch (Exception ex)
        {
            if (generation == _overviewGeneration)
            {
                IsOverviewLoading = false;
                OverviewError = $"Couldn't load this continuity's overview: {ex.Message}";
                _nav.Activity.RaiseAlert(new ActivityAlert
                {
                    Severity = ActivityAlertSeverity.Warning,
                    Title = "Continuity overview failed to load",
                    Detail = ex.Message,
                });
            }

            return;
        }

        if (generation != _overviewGeneration || ContinuityId != id)
        {
            return;
        }

        IsOverviewLoading = false;
        ApplyOverview(overview);
    }

    [RelayCommand]
    private Task RetryOverview() => LoadOverviewAsync();

    private void ClearOverview()
    {
        _overviewGeneration++;
        ApplyOverview(null);
    }

    private void ApplyOverview(ContinuityOverview? overview)
    {
        _overview = overview;
        StatsLine = overview?.StatsLine ?? string.Empty;
        ShowGcdChip = overview?.ShowGcdChip ?? false;
        Continue = overview?.Continue;

        CollageKeys.Clear();
        foreach (var key in overview?.CollageKeys ?? Array.Empty<string>())
        {
            CollageKeys.Add(key);
        }

        EventsInOrder.Clear();
        foreach (var row in overview?.Events ?? Array.Empty<OverviewEventRow>())
        {
            EventsInOrder.Add(row);
        }

        Connections.Clear();
        foreach (var c in overview?.Connections ?? Array.Empty<OverviewConnection>())
        {
            Connections.Add(c);
        }

        OutsideSeries.Clear();
        foreach (var s in overview?.OutsideSeries ?? Array.Empty<OutsideSeriesInfo>())
        {
            OutsideSeries.Add(s);
        }

        var attention = overview?.Attention ?? Array.Empty<AttentionItem>();
        DuplicatesAttention = attention.FirstOrDefault(a => a.Kind == AttentionKind.Duplicates);
        ConnectionsAttention = attention.FirstOrDefault(a => a.Kind == AttentionKind.Connections);
        OutsideAttention = attention.FirstOrDefault(a => a.Kind == AttentionKind.OutsideSeries);

        OnPropertyChanged(nameof(HasEvents));
        OnPropertyChanged(nameof(EventsHeader));
        RebuildPosters();
    }

    /// <summary>Runs and Standalone from the overview (until it arrives, every member is Standalone), and the Custom wall.</summary>
    private void RebuildPosters()
    {
        var cards = Members.ToDictionary(c => c.SeriesId);
        var stats = _overview?.SeriesStats;
        string YearsOf(int seriesId) => stats?.GetValueOrDefault(seriesId)?.Years ?? string.Empty;
        ContinuityPosterItem Poster(SeriesCardSample card, bool arrow = false) =>
            new() { Card = card, Name = card.Name, Years = YearsOf(card.SeriesId), ArrowBefore = arrow };

        Runs.Clear();
        StandalonePosters.Clear();
        if (_overview is { } overview)
        {
            foreach (var run in overview.Runs)
            {
                var posters = run.Items
                    .Where(i => i.SeriesId is null || cards.ContainsKey(i.SeriesId.Value))
                    .Select(i => i.SeriesId is int sid
                        ? Poster(cards[sid], i.ArrowBefore)
                        : new ContinuityPosterItem { Name = i.Name, Years = i.Years, IsPlaceholder = true, Url = i.Url, ArrowBefore = i.ArrowBefore })
                    .ToList();
                if (posters.Count >= 2)
                {
                    Runs.Add(new ContinuityRunRow { Label = run.Label, Posters = posters });
                }
            }

            var placed = Runs.SelectMany(r => r.Posters).Select(p => p.Card?.SeriesId).OfType<int>().ToHashSet();
            foreach (int id in overview.Standalone.Where(cards.ContainsKey).Concat(cards.Keys.Where(k => !overview.Standalone.Contains(k))))
            {
                if (placed.Add(id))
                {
                    StandalonePosters.Add(Poster(cards[id]));
                }
            }
        }
        else
        {
            foreach (var card in Members)
            {
                StandalonePosters.Add(Poster(card));
            }
        }

        CustomPosters.Clear();
        foreach (var card in Members)
        {
            CustomPosters.Add(Poster(card));
        }

        OnPropertyChanged(nameof(HasRuns));
        OnPropertyChanged(nameof(HasStandalone));
    }

    // --- Hero actions ---

    [RelayCommand]
    private void ContinueReading()
    {
        if (Continue is not { } target)
        {
            return;
        }

        if (target.EventId is int eventId)
        {
            _nav.GoToReaderInEvent(target.IssueId, eventId);
        }
        else
        {
            _nav.GoToReader(target.IssueId);
        }
    }

    [RelayCommand]
    private void DeleteContinuity()
    {
        if (ContinuityId is int id)
        {
            _nav.DeleteContinuity(id);
        }
    }

    /// <summary>Manage → Delete continuity: the same two-click confirm as the sidebar row (the menu item's label arms, then confirms).</summary>
    public TwoStepConfirm DeleteConfirm => _deleteConfirm ??= new TwoStepConfirm(() => DeleteContinuityCommand.Execute(null),
        idleLabel: "Delete continuity", armedLabel: "Confirm delete continuity?");

    private TwoStepConfirm? _deleteConfirm;

    /// <summary>
    /// "Create reading list ▸" (docs/superpowers/specs/2026-09-28-reading-lists-build-from-events-design.md §2): <c>"story"</c> reads the
    /// continuity map left to right (events in chronology order, issues between them by date); <c>"publication"</c> is every member-series
    /// issue by release date. Either way the list stays linked to this continuity so it can be rebuilt.
    /// </summary>
    [RelayCommand]
    private void CreateReadingList(string? order)
    {
        if (ContinuityId is not int continuityId)
        {
            return;
        }

        var kind = order == "publication" ? ContinuityOrderKind.PublicationOrder : ContinuityOrderKind.StoryOrder;
        int listId;
        string name;
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            var entries = ContinuityListOrders.Compute(context, continuityId, kind);
            var list = ContinuityReadingListBuilder.CreateFromOrder(context, continuityId, entries, kind);
            listId = list.Id;
            name = list.Name;
        }
        catch (InvalidOperationException ex)
        {
            _nav.Notify("Couldn't create the reading list", ex.Message);
            return;
        }

        _nav.Notify("Reading list created", kind == ContinuityOrderKind.StoryOrder
            ? $"\"{name}\" — in story order, as the map reads."
            : $"\"{name}\" — {Members.Count} series in publication order.");
        _nav.GoToReadingList(listId);
    }

    // --- Needs attention ---

    [RelayCommand]
    private void OpenDuplicates() => _nav.OpenSuggestions(null);

    /// <summary>Accept a connection from the flyout: creates exactly the suggested relation. Deferred - the row is in the list this rebuilds.</summary>
    [RelayCommand]
    private void AcceptConnection(OverviewConnection? connection)
    {
        if (connection is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            using (var context = PaperbunkrDb.CreateContext())
            {
                EventRelationResolver.TryCreate(context, connection.SourceEventId, connection.TargetEventId, connection.Type);
            }

            _ = LoadOverviewAsync();
        });
    }

    /// <summary>"Not connected": never suggested or inferred again. Deferred like <see cref="AcceptConnection"/>.</summary>
    [RelayCommand]
    private void DismissConnection(OverviewConnection? connection)
    {
        if (connection is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            using (var context = PaperbunkrDb.CreateContext())
            {
                EventConnectorSweep.Dismiss(context, connection.SourceEventId, connection.TargetEventId);
            }

            _ = LoadOverviewAsync();
        });
    }

    /// <summary>Adds a series the continuity's events pull in. Deferred - the row is in the flyout list this rebuilds.</summary>
    [RelayCommand]
    private void AddOutsideSeries(OutsideSeriesInfo? series)
    {
        if (series is null || ContinuityId is not int continuityId)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            using (var context = PaperbunkrDb.CreateContext())
            {
                ContinuityResolver.AddSeriesToContinuity(context, series.SeriesId, continuityId);
            }

            Reload();
        });
    }

    // --- Events in order ---

    /// <summary>A row opens its event. Deferred: opening swaps the whole page the row lives in.</summary>
    [RelayCommand]
    private void OpenEvent(OverviewEventRow? row)
    {
        if (row is not null)
        {
            int id = row.EventId;
            Dispatcher.UIThread.Post(() => _nav.OpenEvent(id));
        }
    }

    /// <summary>The row's Map button: the event on its Map tab. Deferred like <see cref="OpenEvent"/>.</summary>
    [RelayCommand]
    private void OpenEventMap(OverviewEventRow? row)
    {
        if (row is not null)
        {
            int id = row.EventId;
            Dispatcher.UIThread.Post(() => _nav.OpenEventMap(id));
        }
    }

    // --- Series order: Automatic (runs) or Custom (the stored order, dragged) ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutomaticOrder), nameof(OrderLabel))]
    private bool _isCustomOrder;

    public bool IsAutomaticOrder => !IsCustomOrder;

    public string OrderLabel => IsCustomOrder ? "Order: Custom" : "Order: Automatic";

    partial void OnIsCustomOrderChanged(bool value)
    {
        if (ContinuityId is int id)
        {
            _customOrder[id] = value;
        }
    }

    [RelayCommand]
    private void UseAutomaticOrder() => IsCustomOrder = false;

    [RelayCommand]
    private void UseCustomOrder() => IsCustomOrder = true;

    /// <summary>Drag-and-drop in the Custom wall: puts <paramref name="card"/> at <paramref name="targetIndex"/> and saves the order.</summary>
    public void MoveSeriesTo(SeriesCardSample card, int targetIndex)
    {
        if (ContinuityId is not int continuityId)
        {
            return;
        }

        var ordered = Members.Select(c => c.SeriesId).ToList();
        int from = ordered.IndexOf(card.SeriesId);
        if (from < 0)
        {
            return;
        }

        targetIndex = Math.Clamp(targetIndex, 0, ordered.Count - 1);
        if (from == targetIndex)
        {
            return;
        }

        ordered.RemoveAt(from);
        ordered.Insert(targetIndex, card.SeriesId);
        using (var context = PaperbunkrDb.CreateContext())
        {
            ContinuityResolver.SetMembershipOrder(context, continuityId, ordered);
        }

        Dispatcher.UIThread.Post(Reload);        // the drop target / focused poster is in the wall this rebuilds
    }

    /// <summary>Keyboard equivalent of dragging one slot earlier (Ctrl+←).</summary>
    [RelayCommand]
    private void MoveSeriesEarlier(SeriesCardSample? card)
    {
        if (card is not null)
        {
            MoveSeriesTo(card, Members.IndexOf(card) - 1);
        }
    }

    /// <summary>Keyboard equivalent of dragging one slot later (Ctrl+→).</summary>
    [RelayCommand]
    private void MoveSeriesLater(SeriesCardSample? card)
    {
        if (card is not null)
        {
            MoveSeriesTo(card, Members.IndexOf(card) + 1);
        }
    }

    // --- Posters: open, note, remove ---

    [RelayCommand]
    private void OpenSeries(SeriesCardSample? card)
    {
        if (card is not null)
        {
            _nav.GoToSeriesDetail(card.SeriesId);
        }
    }

    [RelayCommand]
    private void BeginEditNote(SeriesCardSample? card)
    {
        if (card is null)
        {
            return;
        }

        foreach (var other in Members)
        {
            other.IsEditingNote = other == card;
        }
    }

    /// <summary>Persists the edited note (blank clears it) and closes the editor.</summary>
    [RelayCommand]
    private void SetNote(SeriesCardSample? card)
    {
        if (card is null || ContinuityId is not int continuityId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            ContinuityResolver.SetMembershipNote(context, continuityId, card.SeriesId, card.MembershipNote);
        }

        card.MembershipNote = string.IsNullOrWhiteSpace(card.MembershipNote) ? null : card.MembershipNote.Trim();
        card.IsEditingNote = false;
    }

    /// <summary>Remove one series. Deferred - the clicked button is on the poster being removed.</summary>
    [RelayCommand]
    private void RemoveSeries(SeriesCardSample? card)
    {
        if (card is null || ContinuityId is not int continuityId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            ContinuityResolver.RemoveSeriesFromContinuity(context, card.SeriesId, continuityId);
        }

        Dispatcher.UIThread.Post(Reload);
    }

    // --- Add series (search and multi-select, a panel under the hero) ---

    public ObservableCollection<SeriesSearchResult> SeriesSearchResults { get; } = new();

    [ObservableProperty]
    private bool _isAddingSeries;

    [ObservableProperty]
    private string _seriesSearchQuery = string.Empty;

    [RelayCommand]
    private void ToggleAddSeries()
    {
        IsAddingSeries = !IsAddingSeries;
        SeriesSearchQuery = string.Empty;
        SeriesSearchResults.Clear();
    }

    partial void OnSeriesSearchQueryChanged(string value) => SearchSeries();

    [RelayCommand]
    private void SearchSeries()
    {
        SeriesSearchResults.Clear();
        string query = SeriesSearchQuery.Trim();
        if (query.Length == 0)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var matches = context.Series.AsNoTracking()
            .AsEnumerable()
            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();

        // A nudge, not a block: a series already in other continuities says so under its name (smart features §7.1).
        var alsoIn = ContinuityCharacterSuggestionResolver.OtherContinuityLines(context, matches.Select(s => s.Id).ToList(), ContinuityId);
        foreach (var series in matches)
        {
            SeriesSearchResults.Add(new SeriesSearchResult
            {
                SeriesId = series.Id,
                Name = series.Name,
                OtherContinuities = alsoIn.GetValueOrDefault(series.Id),
            });
        }
    }

    [RelayCommand]
    private void AddSeries(SeriesSearchResult? result)
    {
        if (result is null || ContinuityId is not int continuityId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            ContinuityResolver.AddSeriesToContinuity(context, result.SeriesId, continuityId);
        }

        IsAddingSeries = false;
        SeriesSearchQuery = string.Empty;
        SeriesSearchResults.Clear();
        Dispatcher.UIThread.Post(Reload);
    }

    // --- Bulk selection (docs/superpowers/specs/2026-08-28-bulk-selection-lists-continuities-events-design.md) ---

    public TileSelectionController<SeriesSearchResult> SeriesSelection { get; } = new();

    public TileSelectionController<SeriesCardSample> MemberSelection { get; } = new();

    public bool AnySeriesSelected => SeriesSelection.Count > 0;

    public bool AnyMembersSelected => MemberSelection.Count > 0;

    public string SeriesSelectionSummary => $"{SeriesSelection.Count} selected";

    public string MemberSelectionSummary => $"{MemberSelection.Count} selected";

    private void RaiseSelectionState()
    {
        OnPropertyChanged(nameof(AnySeriesSelected));
        OnPropertyChanged(nameof(AnyMembersSelected));
        OnPropertyChanged(nameof(SeriesSelectionSummary));
        OnPropertyChanged(nameof(MemberSelectionSummary));
    }

    [RelayCommand]
    private void ToggleSeriesSelection(SeriesSearchResult? r)
    {
        if (r is null) return;
        SeriesSelection.Toggle(SeriesSearchResults, r, isShiftHeld: false);
        RaiseSelectionState();
    }

    [RelayCommand]
    private void ToggleMemberSelection(SeriesCardSample? card)
    {
        if (card is null) return;
        MemberSelection.Toggle(Members, card, isShiftHeld: false);
        RaiseSelectionState();
    }

    [RelayCommand]
    private void ClearSeriesSelection()
    {
        SeriesSelection.Clear(SeriesSearchResults);
        RaiseSelectionState();
    }

    [RelayCommand]
    private void ClearMemberSelection()
    {
        MemberSelection.Clear(Members);
        RaiseSelectionState();
    }

    [RelayCommand]
    private void AddSelectedSeries()
    {
        if (ContinuityId is not int continuityId || SeriesSelection.Count == 0)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var r in SeriesSearchResults.Where(x => SeriesSelection.SelectedIds.Contains(x.Id)))
            {
                ContinuityResolver.AddSeriesToContinuity(context, r.SeriesId, continuityId);
            }
        }

        SeriesSelection.Clear();
        RaiseSelectionState();
        Reload();
    }

    [RelayCommand]
    private void RemoveSelectedSeries()
    {
        if (ContinuityId is not int continuityId || MemberSelection.Count == 0)
        {
            return;
        }

        var ids = MemberSelection.SelectedIds.ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (int seriesId in ids)
            {
                ContinuityResolver.RemoveSeriesFromContinuity(context, seriesId, continuityId);
            }
        }

        MemberSelection.Clear();
        RaiseSelectionState();
        Reload();
    }

    // --- Compare & merge (an overlay: this continuity | shared | the other) ---

    /// <summary>Other continuities sharing at least one series - the overlay's picker.</summary>
    public ObservableCollection<ContinuityOverlapCard> OverlappingContinuities { get; } = new();

    public ObservableCollection<SeriesCardSample> SharedSeries { get; } = new();

    public ObservableCollection<SeriesCardSample> OnlyHereSeries { get; } = new();

    public ObservableCollection<SeriesCardSample> OnlyThereSeries { get; } = new();

    [ObservableProperty]
    private bool _isComparing;

    [ObservableProperty]
    private string _compareName = string.Empty;

    [ObservableProperty]
    private TwoStepConfirm? _mergeConfirm;

    public bool HasNoOverlappingContinuities => OverlappingContinuities.Count == 0;

    public bool HasComparison => _compareContinuityId is not null;

    private void LoadOverlaps(PaperbunkrDbContext context, int continuityId)
    {
        ResetComparison();
        OverlappingContinuities.Clear();
        foreach (var (other, shared) in ContinuityResolver.GetOverlappingContinuities(context, continuityId))
        {
            OverlappingContinuities.Add(new ContinuityOverlapCard { ContinuityId = other.Id, Name = other.Name, SharedSeriesCount = shared });
        }

        OnPropertyChanged(nameof(HasNoOverlappingContinuities));
    }

    private void ResetComparison()
    {
        _compareContinuityId = null;
        CompareName = string.Empty;
        MergeConfirm = null;
        SharedSeries.Clear();
        OnlyHereSeries.Clear();
        OnlyThereSeries.Clear();
        OnPropertyChanged(nameof(HasComparison));
    }

    [RelayCommand]
    private void ToggleCompare()
    {
        IsComparing = !IsComparing;
        if (!IsComparing)
        {
            ResetComparison();
        }
        else if (!HasComparison && OverlappingContinuities.Count > 0)
        {
            CompareWith(OverlappingContinuities[0]);
        }
    }

    [RelayCommand]
    private void CompareWith(ContinuityOverlapCard? card)
    {
        if (card is null || ContinuityId is not int activeId)
        {
            return;
        }

        _compareContinuityId = card.ContinuityId;
        CompareName = card.Name;
        int targetId = card.ContinuityId;
        string targetName = card.Name;
        MergeConfirm = new TwoStepConfirm(() => MergeInto(targetId, targetName), idleLabel: $"Merge into {card.Name}…", armedLabel: "Confirm merge?");

        using var context = PaperbunkrDb.CreateContext();
        var here = ContinuityResolver.GetSeriesInContinuity(context, activeId).Select(s => s.Id).ToHashSet();
        var there = ContinuityResolver.GetSeriesInContinuity(context, targetId).Select(s => s.Id).ToHashSet();
        var all = context.Series.Include(s => s.Issues).Where(s => here.Contains(s.Id) || there.Contains(s.Id)).ToList()
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        SharedSeries.Clear();
        OnlyHereSeries.Clear();
        OnlyThereSeries.Clear();
        foreach (var series in all)
        {
            var target = here.Contains(series.Id) && there.Contains(series.Id) ? SharedSeries : here.Contains(series.Id) ? OnlyHereSeries : OnlyThereSeries;
            target.Add(SeriesCardSample.FromSeries(series));
        }

        OnPropertyChanged(nameof(HasComparison));
    }

    /// <summary>"Merge into …": folds this continuity's series into the target, deletes this one and opens the target.</summary>
    private void MergeInto(int targetId, string targetName)
    {
        if (ContinuityId is not int sourceId || targetId == sourceId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            ContinuityResolver.Merge(context, sourceId, targetId);
        }

        // Deferred: the confirm button lives in the overlay this closes.
        Dispatcher.UIThread.Post(() =>
        {
            IsComparing = false;
            _nav.SidebarChanged();
            _nav.OpenContinuity(targetId);
            _nav.Notify("Continuities merged", $"Series folded into \"{targetName}\".");
        });
    }
}
