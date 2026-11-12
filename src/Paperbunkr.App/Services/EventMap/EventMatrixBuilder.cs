using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>A phase in a matrix; the last column is the series total.</summary>
public sealed record EventMatrixColumn(string Label, string Tooltip, int Key, bool IsTotal = false);
public sealed record EventMatrixCell(IReadOnlyList<int> Indices, int Read, int InProgress, int Missing = 0)
{
    public int Count => Indices.Count;
}
public sealed record EventMatrixRow(int SeriesId, string Name, int ColorIndex, IReadOnlyList<EventMatrixCell> Cells);
public sealed record EventMatrix(IReadOnlyList<EventMatrixColumn> Columns, IReadOnlyList<EventMatrixRow> Rows)
{
    public static EventMatrix Empty { get; } = new(Array.Empty<EventMatrixColumn>(), Array.Empty<EventMatrixRow>());
}

/// <summary>Builds an unfolded series-by-phase view of the map's filtered reading order.</summary>
public static class EventMatrixBuilder
{
    public static EventMatrix Build(EventMapLayoutResult layout, IReadOnlySet<int>? hiddenSeries = null,
        Func<int, EventMapReadState>? readState = null)
    {
        var cells = layout.Cells.Where(c => hiddenSeries is null || !hiddenSeries.Contains(c.Row.SeriesId)).ToList();
        if (cells.Count == 0) return EventMatrix.Empty;
        var columns = new List<EventMatrixColumn>();
        Func<EventMapCell, int> key;
        if (layout.IsBlockMode)
        {
            key = c => c.Row.BlockIndex;
            foreach (var block in layout.Blocks.Where(b => cells.Any(c => c.Row.BlockIndex == b.Index)))
                columns.Add(new EventMatrixColumn(block.Label, block.Label, block.Index));
        }
        else if (layout.IsSpineMode)
        {
            key = c => c.Segment;
            foreach (var segment in layout.Segments.Where(s => cells.Any(c => c.Segment == s.Index)))
            {
                var anchor = segment.TrunkCell is int i ? layout.Cells[i].Row : null;
                string label = anchor is null ? "Before" : anchor.Role switch
                {
                    EventMembershipRole.Prologue => "Prologue",
                    EventMembershipRole.Epilogue => "Epilogue",
                    _ => string.IsNullOrEmpty(anchor.Number) ? anchor.SeriesName : $"#{anchor.Number}",
                };
                string title = anchor is null ? label : $"{anchor.SeriesName} #{anchor.Number}";
                columns.Add(new EventMatrixColumn(label, title, segment.Index));
            }
        }
        else
        {
            key = c => (Math.Max(1, c.Row.Position) - 1) / 8;
            foreach (int chunk in cells.Select(key).Distinct().OrderBy(i => i))
                columns.Add(new EventMatrixColumn($"{chunk * 8 + 1}–{chunk * 8 + 8}", $"Positions {chunk * 8 + 1}–{chunk * 8 + 8}", chunk));
        }

        columns.Add(new EventMatrixColumn("Total", "Total", -1, true));
        var rows = cells.GroupBy(c => c.Row.SeriesId)
            .OrderBy(g => layout.IsSpineMode && g.Key == layout.Spine.SeriesId ? -1 : g.Min(c => c.Track))
            .ThenBy(g => g.Min(c => c.Index))
            .Select(group =>
            {
                var first = group.First();
                var matrixCells = columns.Select(column =>
                {
                    var all = (column.IsTotal ? group : group.Where(c => key(c) == column.Key)).ToList();
                    // Ghosts (M4) are only "missing": never in the cell's issues or its read counts.
                    var inColumn = all.Where(c => !c.Row.IsGhost).ToList();
                    EventMapReadState State(EventMapCell c) => readState?.Invoke(c.Index) ?? c.Row.ReadState;
                    return new EventMatrixCell(inColumn.Select(c => c.Index).ToArray(),
                        inColumn.Count(c => State(c) == EventMapReadState.Read),
                        inColumn.Count(c => State(c) == EventMapReadState.InProgress),
                        all.Count(c => c.Row.IsGhost && c.Row.Ghost!.Kind == Paperbunkr.Data.Metadata.EventGhostKind.Missing));
                }).ToArray();
                return new EventMatrixRow(group.Key, first.Row.SeriesName,
                    first.ColorIndexOverride ?? layout.Tracks[first.Track].ColorIndex, matrixCells);
            }).ToArray();
        return new EventMatrix(columns, rows);
    }
}
