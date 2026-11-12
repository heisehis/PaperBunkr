using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>"Colour by" (M5 §3).</summary>
public enum EventMapColourBy { Lane, Writer, Artist, ReadState }

/// <summary>One entry in the Colour by legend: a creator with their colour, or the "others" bucket.</summary>
public sealed record MapLegendItem(string Label, int ColorIndex, int? CreatorId, bool IsOthers, bool IsActive = false);

/// <summary>A bracket drawn under a lane's cards: one contiguous stretch of columns (M5 §3-§4).</summary>
public sealed record MapBracketSpan(int Track, int FirstColumn, int LastColumn, MapBracketKind Kind, string? Label, string Tooltip, int ColorIndex, int? CreatorId);

/// <summary>
/// Continuity map M5 (docs/superpowers/specs/2026-11-11-continuity-map-structure-links-design.md): Featuring (dim or hide everything
/// without a character or team), character faces on cards, Colour by writer / artist / read state with a legend, creator-run brackets,
/// and story-arc brackets. All from the Series structure indexes, loaded with the map.
/// </summary>
public sealed partial class EventMapViewModel
{
    /// <summary>Bar colours for <see cref="BarColors"/> that aren't lane-palette indices.</summary>
    public const int BarFaint = -1000, BarRead = -1001, BarInProgress = -1002, BarUnread = -1003;

    private const int LegendSize = 6;
    private static readonly string[] ColourByLabels = { "Lane", "Writer", "Artist", "Read state" };

    private (ComicMetadataEntityKind Kind, int Id)? _pendingFeaturing;
    private bool _showArcsEvent, _showArcsContinuity = true;
    private bool _facesHooked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFeaturing), nameof(FeaturingText), nameof(FeaturingStatus))]
    private MapEntity? _featuring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideOthersLabel))]
    private bool _featuringHideOthers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColourByText), nameof(HasLegend))]
    private EventMapColourBy _colourBy = EventMapColourBy.Lane;

    /// <summary>The creator a legend click (or a run bracket click) singled out: everything else dims.</summary>
    [ObservableProperty]
    private int? _highlightedCreator;

    /// <summary>For the canvas: the cards to keep at full opacity while featuring or highlighting (null = no such dimming).</summary>
    [ObservableProperty]
    private IReadOnlySet<int>? _featureSet;

    /// <summary>For the canvas: per-card bar colour when Colour by isn't Lane (null = lane colours).</summary>
    [ObservableProperty]
    private IReadOnlyList<int>? _barColors;

    [ObservableProperty]
    private IReadOnlyList<MapBracketSpan> _brackets = Array.Empty<MapBracketSpan>();

    public ObservableCollection<MapLegendItem> Legend { get; } = new();

    public IReadOnlyList<string> FeaturingOptions => Structure.Entities.Select(e => e.Label).ToList();

    public IReadOnlyList<string> ColourByOptions => ColourByLabels;

    private EventMapStructure Structure => _continuityData?.Structure ?? _source?.Structure ?? EventMapStructure.Empty;

    public bool HasFeaturingChoices => Structure.Entities.Count > 0;

    /// <summary>The character index (Series structure D1) hasn't finished building: Featuring is empty for now.</summary>
    public bool IsIndexBuilding => !Structure.IndexReady;

    public bool IsFeaturing => Featuring is not null;

    public string HideOthersLabel => FeaturingHideOthers ? "Show others" : "Hide others";

    public bool HasLegend => Legend.Count > 0;

    /// <summary>Arc brackets: on by default on continuity maps, off on event maps (§4); per scope, for the session.</summary>
    public bool ShowArcs
    {
        get => IsContinuityScope ? _showArcsContinuity : _showArcsEvent;
        set
        {
            if (IsContinuityScope) _showArcsContinuity = value; else _showArcsEvent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ArcsLabel));
            RefreshStructureView();
        }
    }

    public string ArcsLabel => ShowArcs ? "Hide arcs" : "Show arcs";

    /// <summary>"featuring Wolverine · 12 issues" for the status line.</summary>
    public string FeaturingStatus => Featuring is { } f ? $"featuring {f.Name} · {f.Count} issue{(f.Count == 1 ? "" : "s")}" : string.Empty;

    /// <summary>The Featuring picker's text ("Wolverine · 12"); clearing it turns Featuring off.</summary>
    public string FeaturingText
    {
        get => Featuring?.Label ?? string.Empty;
        set
        {
            var match = string.IsNullOrWhiteSpace(value) ? null : Structure.Entities.FirstOrDefault(e => e.Label == value);
            if (match != Featuring && (match is not null || string.IsNullOrWhiteSpace(value)))
            {
                Featuring = match;
            }
        }
    }

    public string ColourByText
    {
        get => ColourByLabels[(int)ColourBy];
        set
        {
            int index = Array.IndexOf(ColourByLabels, value);
            if (index >= 0) ColourBy = (EventMapColourBy)index;
        }
    }

    /// <summary>Test seam: re-reads face images by name after <see cref="EntityChipImages.Changed"/>.</summary>
    internal Func<IReadOnlyList<string>, Dictionary<string, string>> FaceRefreshRunner { get; set; } = names =>
    {
        using var context = PaperbunkrDb.CreateContext();
        return EventMapStructureLoader.Faces(context, names);
    };

    /// <summary>Opens the map with Featuring set (entity pages and the Overview's Key characters, §5); applied once the map has loaded.</summary>
    public void SetFeaturing(ComicMetadataEntityKind kind, int id)
    {
        var match = Structure.Entities.FirstOrDefault(e => e.Kind == kind && e.Id == id);
        if (match is null)
        {
            _pendingFeaturing = (kind, id);
            return;
        }

        _pendingFeaturing = null;
        Featuring = match;
    }

    [RelayCommand]
    private void ClearFeaturing() => Featuring = null;

    [RelayCommand]
    private void ToggleFeaturingHideOthers() => FeaturingHideOthers = !FeaturingHideOthers;

    [RelayCommand]
    private void ToggleArcs() => ShowArcs = !ShowArcs;

    /// <summary>A run bracket click: highlight that creator (again to clear).</summary>
    public void ToggleCreatorHighlight(int creatorId) => HighlightedCreator = HighlightedCreator == creatorId ? null : creatorId;

    [RelayCommand]
    private void HighlightCreator(MapLegendItem? item) =>
        HighlightedCreator = item is { CreatorId: int id } && HighlightedCreator != id ? id : null;

    partial void OnFeaturingChanged(MapEntity? value)
    {
        HighlightedCreator = null;
        if (FeaturingHideOthers) Relayout(SelectedCard?.IssueId); else RefreshStructureView();
        UpdateStatusText();
    }

    partial void OnFeaturingHideOthersChanged(bool value)
    {
        if (Featuring is not null) Relayout(SelectedCard?.IssueId);
    }

    partial void OnColourByChanged(EventMapColourBy value)
    {
        HighlightedCreator = null;
        RefreshStructureView();
    }

    partial void OnHighlightedCreatorChanged(int? value) => RefreshStructureView();

    /// <summary>Issue ids featuring the current entity (null = not featuring).</summary>
    private HashSet<int>? FeaturedIssues()
    {
        if (Featuring is not { } f)
        {
            return null;
        }

        return Structure.Issues.Where(p => f.Kind == ComicMetadataEntityKind.Character ? p.Value.Characters.Contains(f.Id) : p.Value.Teams.Contains(f.Id))
            .Select(p => p.Key).ToHashSet();
    }

    /// <summary>"Hide others": drops issues without the featured entity before layout (ghosts go too, they have no characters).</summary>
    private IReadOnlyList<EventMapRow> FilterFeatured(IReadOnlyList<EventMapRow> rows)
    {
        if (!FeaturingHideOthers || FeaturedIssues() is not { } keep)
        {
            return rows;
        }

        return rows.Where(r => keep.Contains(r.IssueId)).ToList();
    }

    /// <summary>Recomputes everything M5 draws from the current layout: faces, the feature set, bar colours, the legend and brackets.</summary>
    private void RefreshStructureView()
    {
        if (_pendingFeaturing is { } pending && Structure.Entities.FirstOrDefault(e => e.Kind == pending.Kind && e.Id == pending.Id) is { } entity)
        {
            _pendingFeaturing = null;
            Featuring = entity;          // re-enters through OnFeaturingChanged
            return;
        }

        HookFaces();
        var structure = Structure;
        foreach (var card in Cards)
        {
            card.FacePath = structure.Issues.TryGetValue(card.IssueId, out var s) ? s.FacePath : null;
        }

        // Featuring dims everything without the entity; a highlighted creator dims everything they didn't make.
        HashSet<int>? keep = null;
        if (FeaturedIssues() is { } featured)
        {
            keep = Cards.Where(c => featured.Contains(c.IssueId)).Select(c => c.Index).ToHashSet();
        }

        if (HighlightedCreator is int creator)
        {
            var byCreator = Cards.Where(c => CreatorOf(c, structure)?.Id == creator).Select(c => c.Index);
            keep = keep is null ? byCreator.ToHashSet() : keep.Intersect(byCreator).ToHashSet();
        }

        FeatureSet = keep;

        // Colour by: the six creators with the most cards get the lane palette, everyone else (and uncredited issues) the faint bar.
        Legend.Clear();
        if (ColourBy is EventMapColourBy.Writer or EventMapColourBy.Artist)
        {
            var top = Cards.Where(c => c.IsMember).Select(c => CreatorOf(c, structure)).OfType<MapCreator>()
                .GroupBy(c => c.Id).OrderByDescending(g => g.Count()).ThenBy(g => g.First().Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(LegendSize).Select((g, i) => (g.First(), Color: i, Count: g.Count())).ToList();
            var colorOf = top.ToDictionary(t => t.Item1.Id, t => t.Color);
            BarColors = Cards.Select(c => CreatorOf(c, structure) is { } who && colorOf.TryGetValue(who.Id, out int color) ? color : BarFaint).ToList();
            foreach (var (who, color, count) in top)
            {
                Legend.Add(new MapLegendItem($"{who.Name} {count}", color, who.Id, false, HighlightedCreator == who.Id));
            }

            if (Cards.Any(c => c.IsMember && (CreatorOf(c, structure) is not { } who2 || !colorOf.ContainsKey(who2.Id))))
            {
                Legend.Add(new MapLegendItem("others", EventMapLayout.MutedColor, null, true));
            }
        }
        else if (ColourBy == EventMapColourBy.ReadState)
        {
            BarColors = Cards.Select(c => c.IsRead ? BarRead : c.IsInProgress ? BarInProgress : BarUnread).ToList();
        }
        else
        {
            BarColors = null;
        }

        OnPropertyChanged(nameof(HasLegend));
        Brackets = ComputeBrackets(structure);
        OnPropertyChanged(nameof(FeaturingOptions));
        OnPropertyChanged(nameof(HasFeaturingChoices));
        OnPropertyChanged(nameof(IsIndexBuilding));
        OnPropertyChanged(nameof(ShowArcs));
        OnPropertyChanged(nameof(ArcsLabel));
    }

    private MapCreator? CreatorOf(EventMapCardViewModel card, EventMapStructure structure) =>
        structure.Issues.TryGetValue(card.IssueId, out var s) ? ColourBy == EventMapColourBy.Artist ? s.Artist : s.Writer : null;

    /// <summary>
    /// Run brackets (Writer / Artist colouring only, in the creator's legend colour) and arc brackets (when Arcs is on), each split into
    /// contiguous column stretches on its lane; only the first stretch carries the label.
    /// </summary>
    private IReadOnlyList<MapBracketSpan> ComputeBrackets(EventMapStructure structure)
    {
        var spans = new List<MapBracketSpan>();
        bool runs = ColourBy is EventMapColourBy.Writer or EventMapColourBy.Artist;
        string role = ColourBy == EventMapColourBy.Artist ? "Artist" : "Writer";
        var legendColor = Legend.Where(l => l.CreatorId is not null).ToDictionary(l => l.CreatorId!.Value, l => l.ColorIndex);
        var indexByIssue = Cards.Where(c => c.IsMember).GroupBy(c => c.IssueId).ToDictionary(g => g.Key, g => g.First());
        foreach (var bracket in structure.Brackets)
        {
            if (bracket.Kind == MapBracketKind.Run && (!runs || bracket.Role != role)) continue;
            if (bracket.Kind == MapBracketKind.Arc && !ShowArcs) continue;
            var cards = bracket.IssueIds.Select(id => indexByIssue.GetValueOrDefault(id)).OfType<EventMapCardViewModel>().ToList();
            foreach (var lane in cards.GroupBy(c => c.Track))
            {
                var columns = lane.Select(c => c.Column).Distinct().OrderBy(c => c).ToList();
                if (columns.Count == 0) continue;
                int start = columns[0], previous = columns[0];
                bool first = true;
                int color = bracket.Kind == MapBracketKind.Run && bracket.CreatorId is int id && legendColor.TryGetValue(id, out int c) ? c : BarFaint;
                void Close()
                {
                    spans.Add(new MapBracketSpan(lane.Key, start, previous, bracket.Kind, first ? bracket.Label : null, bracket.Tooltip, color, bracket.CreatorId));
                    first = false;
                }

                foreach (int column in columns.Skip(1))
                {
                    if (column != previous + 1)
                    {
                        Close();
                        start = column;
                    }

                    previous = column;
                }

                Close();
            }
        }

        return spans;
    }

    /// <summary>Faces that download after the map loaded are picked up when <see cref="EntityChipImages.Changed"/> fires (no fetching here).</summary>
    private void HookFaces()
    {
        if (_facesHooked)
        {
            return;
        }

        _facesHooked = true;
        EntityChipImages.Changed += () => Dispatcher.UIThread.Post(async () =>
        {
            var names = Structure.Issues.Values.Select(s => s.FaceName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0) return;
            var paths = await Task.Run(() => FaceRefreshRunner(names));
            foreach (var card in Cards)
            {
                if (Structure.Issues.TryGetValue(card.IssueId, out var s) && s.FaceName is { } name && paths.TryGetValue(name, out var path))
                {
                    card.FacePath = path;
                }
            }
        });
    }
}
