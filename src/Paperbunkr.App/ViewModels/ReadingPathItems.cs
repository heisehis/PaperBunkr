using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>Where an item sits relative to "Up next" on the journey path: the spine is accent above it, muted below.</summary>
public enum PathSpineState
{
    Before,
    Current,
    After,
}

/// <summary>
/// One entry of a reading list's journey path (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §4): a chapter header, a
/// track row, the expanded "Up next" card, or the empty-list card. The path is one flat, virtualized list; each item draws its own
/// spine segment, so the spine needs no overlay.
/// </summary>
public abstract partial class PathItem : ObservableObject
{
    public PathSpineState Spine { get; init; }

    public bool SpineLit => Spine == PathSpineState.Before;
}

/// <summary>A sub-arc header (a consecutive group-label run). Click toggles collapse.</summary>
public sealed partial class ChapterHeaderItem : PathItem
{
    public string Label { get; init; } = string.Empty;

    public int Count { get; init; }

    public int ReadCount { get; init; }

    /// <summary>Holds the Up next issue.</summary>
    public bool IsCurrent { get; init; }

    /// <summary>The <c>ReadingListItem</c> ids in this run - what a chapter rename relabels.</summary>
    public IReadOnlyList<int> ItemIds { get; init; } = Array.Empty<int>();

    public bool IsComplete => Count > 0 && ReadCount >= Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caption))]
    private bool _isCollapsed;

    /// <summary>"▾ Main event · 12 · 7 read".</summary>
    public string Caption => $"{(IsCollapsed ? "▸" : "▾")} {Label} · {Count} · {(IsComplete ? "all read" : $"{ReadCount} read")}";

    /// <summary>Inline rename while editing.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = string.Empty;
}

/// <summary>An issue on the path, wrapping the existing row state. Reading mode uses <see cref="PathRowItem"/> (a light template: no
/// hidden edit controls per row, which made scrolling heavy); Edit mode uses <see cref="PathEditRowItem"/>.</summary>
public abstract class PathIssueItem : PathItem
{
    public required ReadingListItemRowViewModel Row { get; init; }

    /// <summary>Filled dot = read, hollow = unread, dashed = missing.</summary>
    public bool DotRead => Row.IsRead;

    public bool DotMissing => Row.IsMissing;

    public bool HasNote => !string.IsNullOrWhiteSpace(Row.Notes);

    public string YearText => Row.Item.Issue?.EffectiveYear() is int y && y > 0 ? y.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
}

/// <summary>An issue row in reading mode.</summary>
public sealed class PathRowItem : PathIssueItem
{
}

/// <summary>An issue row in Edit mode: handle, checkbox, role picker, suggestion, note and remove.</summary>
public sealed class PathEditRowItem : PathIssueItem
{
}

/// <summary>The next issue to read, expanded into a card on the path.</summary>
public sealed class UpNextItem : PathItem
{
    public required ReadingListItemRowViewModel Row { get; init; }

    /// <summary>"Up next · 13 of 31".</summary>
    public string Caption { get; init; } = string.Empty;

    public string? Summary => Row.Item.Issue?.Summary;

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public bool HasNote => !string.IsNullOrWhiteSpace(Row.Notes);
}

/// <summary>An empty list: "Add issues to get started".</summary>
public sealed class EmptyListItem : PathItem
{
}

/// <summary>One tile of Covers mode (uniform, virtualized wrap). The first tile of a chapter carries the chapter name.</summary>
public sealed class CoverTileItem
{
    public required ReadingListItemRowViewModel Row { get; init; }

    public string? ChapterCaption { get; init; }

    /// <summary>"13 ▶ next", "11 ✓", "14 missing".</summary>
    public string Caption { get; init; } = string.Empty;

    public bool IsNextUp => Row.IsNextUp;

    public double CoverOpacity => Row.IsRead ? 0.45 : 1;
}

/// <summary>
/// Builds the path from a list's rows (pure, so it's testable). Chapters come from <see cref="ReadingListGrouping.Runs"/>; an unlabelled
/// list has none. The Up next row is replaced by an <see cref="UpNextItem"/> (not while editing). A collapsed chapter keeps its header
/// and drops its rows. Rule for the first open (decision Q8): fully read chapters start collapsed - see <see cref="DefaultCollapsed"/>.
/// </summary>
public static class ReadingPathBuilder
{
    public static IReadOnlyList<PathItem> Build(IReadOnlyList<ReadingListItemRowViewModel> rows, IReadOnlySet<string> collapsed, bool editing)
    {
        if (rows.Count == 0)
        {
            return new PathItem[] { new EmptyListItem() };
        }

        int nextIndex = Enumerable.Range(0, rows.Count).FirstOrDefault(i => rows[i].IsNextUp, -1);
        var items = new List<PathItem>();
        int index = 0;
        foreach (var (label, run) in ReadingListGrouping.Runs(rows, r => r.Item.GroupLabel))
        {
            int start = index;
            int end = index + run.Count - 1;
            index += run.Count;
            bool holdsNext = nextIndex >= start && nextIndex <= end;
            bool isCollapsed = !editing && label.Length > 0 && !holdsNext && collapsed.Contains(label);

            if (label.Length > 0)
            {
                items.Add(new ChapterHeaderItem
                {
                    Label = label,
                    Count = run.Count,
                    ReadCount = run.Count(r => r.IsRead),
                    IsCurrent = holdsNext,
                    ItemIds = run.Select(r => r.Item.Id).ToList(),
                    IsCollapsed = isCollapsed,
                    Spine = SpineFor(start, nextIndex),
                });
            }

            if (isCollapsed)
            {
                continue;
            }

            for (int k = 0; k < run.Count; k++)
            {
                int position = start + k;
                var row = run[k];
                if (!editing && position == nextIndex)
                {
                    items.Add(new UpNextItem { Row = row, Caption = $"Up next · {position + 1} of {rows.Count}", Spine = PathSpineState.Current });
                }
                else if (editing)
                {
                    items.Add(new PathEditRowItem { Row = row, Spine = SpineFor(position, nextIndex) });
                }
                else
                {
                    items.Add(new PathRowItem { Row = row, Spine = SpineFor(position, nextIndex) });
                }
            }
        }

        return items;
    }

    private static PathSpineState SpineFor(int position, int nextIndex) =>
        nextIndex < 0 ? PathSpineState.Before
        : position < nextIndex ? PathSpineState.Before
        : position == nextIndex ? PathSpineState.Current
        : PathSpineState.After;

    /// <summary>Labels of the chapters that start collapsed: every labelled run whose issues are all read.</summary>
    public static HashSet<string> DefaultCollapsed(IReadOnlyList<ReadingListItemRowViewModel> rows) =>
        ReadingListGrouping.Runs(rows, r => r.Item.GroupLabel)
            .Where(run => run.Label.Length > 0 && run.Items.All(r => r.IsRead))
            .Select(run => run.Label)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Covers mode: one tile per row; the first tile of each labelled run carries the chapter name.</summary>
    public static IReadOnlyList<CoverTileItem> BuildCovers(IReadOnlyList<ReadingListItemRowViewModel> rows)
    {
        var tiles = new List<CoverTileItem>();
        foreach (var (label, run) in ReadingListGrouping.Runs(rows, r => r.Item.GroupLabel))
        {
            for (int k = 0; k < run.Count; k++)
            {
                var row = run[k];
                string state = row.IsNextUp ? "▶ next" : row.IsRead ? "✓" : row.IsMissing ? "missing" : string.Empty;
                tiles.Add(new CoverTileItem
                {
                    Row = row,
                    ChapterCaption = k == 0 && label.Length > 0 ? label : null,
                    Caption = $"{row.Position} {state}".TrimEnd(),
                });
            }
        }

        return tiles;
    }
}
