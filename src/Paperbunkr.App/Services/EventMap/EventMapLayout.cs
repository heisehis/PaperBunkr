using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>
/// The Event Map's pure layout engine (docs/superpowers/specs/2026-09-25-event-map-design.md §1 "Layout"). Positions
/// are fully determined by the data - reading order, the chosen spine and the filter - so there is no auto-layout: a
/// row's column and track are computed in one pass, and everything the view needs afterwards (card rectangles, edges,
/// the visible range, keyboard navigation, inspector links, the dimming set) is answered by the returned
/// <see cref="EventMapLayoutResult"/>.
/// </summary>
public static class EventMapLayout
{
    /// <summary>Lanes cycle through this many skin colours (spec §3 "Colors").</summary>
    public const int LaneColorCount = 6;

    /// <summary><see cref="EventMapTrack.ColorIndex"/>/<see cref="EventMapEdge.ColorIndex"/> for the trunk's accent colour.</summary>
    public const int TrunkColor = -1;

    /// <summary><see cref="EventMapEdge.ColorIndex"/> for a relay chain edge that hops lanes (drawn in the muted text colour).</summary>
    public const int MutedColor = -2;

    public static EventMapLayoutResult Compute(EventMapSource source, SpineChoice spine, EventMapFilter filter, EventMapDensity density)
    {
        var metrics = EventMapDensityMetrics.For(density);
        var ordered = source.Rows.OrderBy(r => r.Position).ThenBy(r => r.MembershipId).ToList();
        int? spineId = spine.SeriesId;

        // Trunk membership is decided on the unfiltered rows' roles. Core is never a signal: it is the default role.
        bool IsTrunkRow(EventMapRow r) =>
            spineId is int s && (r.SeriesId == s || r.Role is EventMembershipRole.Prologue or EventMembershipRole.Epilogue);

        IEnumerable<EventMapRow> kept = ordered;
        if (filter == EventMapFilter.HideOptional)
        {
            kept = kept.Where(r => r.Role != EventMembershipRole.Optional);
        }
        else if (filter == EventMapFilter.SpineOnly && spineId is not null)
        {
            kept = kept.Where(IsTrunkRow);
        }

        var rows = kept.ToList();
        bool spineMode = spineId is not null && rows.Any(IsTrunkRow);
        string spineName = spineId is int sid
            ? source.Rows.FirstOrDefault(r => r.SeriesId == sid)?.SeriesName ?? string.Empty
            : string.Empty;

        return spineMode
            ? ComputeSpine(rows, rows.Select(IsTrunkRow).ToArray(), spine, spineName, filter, density, metrics)
            : ComputeRelay(rows, spine, filter, density, metrics);
    }

    private static EventMapLayoutResult ComputeSpine(
        List<EventMapRow> rows, bool[] isTrunk, SpineChoice spine, string spineName,
        EventMapFilter filter, EventMapDensity density, EventMapDensityMetrics metrics)
    {
        int spineId = spine.SeriesId!.Value;

        // Lanes: one per series with rows left after trunk promotion, ordered by that series' first position
        // (promoted rows included - "that series' first Position", spec §1).
        var firstIndex = new Dictionary<int, int>();
        for (int i = 0; i < rows.Count; i++)
        {
            firstIndex.TryAdd(rows[i].SeriesId, i);
        }

        var laneSeries = rows.Where((_, i) => !isTrunk[i]).Select(r => r.SeriesId).Distinct()
            .OrderBy(id => firstIndex[id]).ToList();
        var laneTrack = new Dictionary<int, int>();
        for (int k = 0; k < laneSeries.Count; k++)
        {
            laneTrack[laneSeries[k]] = k + 1;
        }

        // Columns: compact packing. A trunk row takes the next free column and opens a segment one column later;
        // a lane row takes the later of the segment start and its own lane's next free column.
        var cells = new List<EventMapCell>(rows.Count);
        var segments = new List<EventMapSegment> { new(0, null, 0) };
        var laneNext = new Dictionary<int, int>();
        int maxColumn = -1;
        int segmentStart = 0;
        int segment = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            int track;
            int column;
            if (isTrunk[i])
            {
                track = 0;
                column = maxColumn + 1;
                segment++;
                segmentStart = column + 1;
                segments.Add(new EventMapSegment(segment, i, segmentStart));
            }
            else
            {
                track = laneTrack[rows[i].SeriesId];
                column = Math.Max(segmentStart, laneNext.GetValueOrDefault(track));
                laneNext[track] = column + 1;
            }

            maxColumn = Math.Max(maxColumn, column);
            cells.Add(new EventMapCell(i, rows[i], track, column, segment, isTrunk[i]));
        }

        var tracks = new List<EventMapTrack> { new(0, spineId, spineName, IsTrunk: true, TrunkColor, cells.Count(c => c.IsTrunk)) };
        for (int k = 0; k < laneSeries.Count; k++)
        {
            int seriesId = laneSeries[k];
            var laneCells = cells.Where(c => c.Track == k + 1).ToList();
            tracks.Add(new EventMapTrack(k + 1, seriesId, laneCells[0].Row.SeriesName, IsTrunk: false, k % LaneColorCount, laneCells.Count));
        }

        var byTrack = GroupByTrack(cells, tracks.Count);
        var edges = new List<EventMapEdge>();

        foreach (var track in tracks)
        {
            var list = byTrack[track.Index];
            for (int j = 1; j < list.Count; j++)
            {
                edges.Add(MakeEdge(EventMapEdgeKind.Sequence, cells[list[j - 1]], cells[list[j]], track.ColorIndex));
            }
        }

        foreach (var seg in segments.Where(s => s.TrunkCell is not null))
        {
            var trunk = cells[seg.TrunkCell!.Value];
            foreach (var track in tracks.Where(t => !t.IsTrunk))
            {
                int first = byTrack[track.Index].FirstOrDefault(ci => cells[ci].Segment == seg.Index, -1);
                if (first >= 0)
                {
                    edges.Add(MakeEdge(EventMapEdgeKind.TieIn, trunk, cells[first], TrunkColor));
                }
            }
        }

        foreach (var promoted in cells.Where(c => c.IsTrunk && c.Row.SeriesId != spineId))
        {
            var next = cells.Skip(promoted.Index + 1).FirstOrDefault(c => !c.IsTrunk && c.Row.SeriesId == promoted.Row.SeriesId);
            if (next is not null)
            {
                edges.Add(MakeEdge(EventMapEdgeKind.Continuity, promoted, next, tracks[next.Track].ColorIndex));
            }
        }

        return new EventMapLayoutResult(true, spine, filter, density, metrics, cells, tracks, edges, segments, maxColumn + 1);
    }

    private static EventMapLayoutResult ComputeRelay(
        List<EventMapRow> rows, SpineChoice spine, EventMapFilter filter, EventMapDensity density, EventMapDensityMetrics metrics)
    {
        var laneSeries = rows.Select(r => r.SeriesId).Distinct().ToList();     // first appearance = first Position
        var laneTrack = laneSeries.Select((id, k) => (id, k)).ToDictionary(x => x.id, x => x.k);

        // Strict columns: every row gets its own column, so the chain always moves right.
        var cells = rows.Select((row, i) => new EventMapCell(i, row, laneTrack[row.SeriesId], i, 0, IsTrunk: false)).ToList();
        var tracks = laneSeries.Select((id, k) =>
        {
            var laneCells = cells.Where(c => c.Track == k).ToList();
            return new EventMapTrack(k, id, laneCells[0].Row.SeriesName, IsTrunk: false, k % LaneColorCount, laneCells.Count);
        }).ToList();

        var edges = new List<EventMapEdge>();
        for (int i = 1; i < cells.Count; i++)
        {
            var from = cells[i - 1];
            var to = cells[i];
            edges.Add(MakeEdge(EventMapEdgeKind.Chain, from, to, from.Track == to.Track ? tracks[from.Track].ColorIndex : MutedColor));
        }

        return new EventMapLayoutResult(false, spine, filter, density, metrics, cells, tracks, edges,
            new List<EventMapSegment> { new(0, null, 0) }, cells.Count);
    }

    /// <summary>
    /// Block-relay mode for the continuity map (docs/superpowers/specs/2026-09-27-continuity-map-design.md §3): the rows keep the order
    /// <see cref="ContinuityMapBuilder"/> gave them, one lane per <see cref="ContinuityMapSource.Lanes"/> entry. An event block is a relay,
    /// one card per column in reading order, and its chain follows that order across lanes. Between-events and year blocks are packed by
    /// date: issues from the same month share a column across lanes (a lane with several that month takes several columns), so a
    /// stretch of publication history reads as a timeline instead of one card at a time. There only consecutive issues of the same lane
    /// are joined (publication order isn't a reading path). Nothing crosses a block boundary.
    /// </summary>
    public static EventMapLayoutResult ComputeBlocks(ContinuityMapSource source, EventMapDensity density)
    {
        var metrics = EventMapDensityMetrics.For(density);
        var laneTrack = source.Lanes.Select((l, k) => (l.SeriesId, k)).ToDictionary(x => x.SeriesId, x => x.k);
        var columns = new int[source.Rows.Count];
        var columnLabels = new List<string?>();
        var blockColumns = new (int First, int Last)[source.Blocks.Count];
        foreach (var block in source.Blocks)
        {
            int first = columnLabels.Count;
            if (block.Kind == EventMapBlockKind.Event)
            {
                for (int r = block.FirstRow; r <= block.LastRow; r++)
                {
                    columns[r] = columnLabels.Count;
                    columnLabels.Add((r - block.FirstRow + 1).ToString(CultureInfo.CurrentCulture));
                }
            }
            else
            {
                // Rows arrive sorted by date (ContinuityMapBuilder.SortLoose), so each month is one consecutive run.
                int r = block.FirstRow;
                while (r <= block.LastRow)
                {
                    int? date = ContinuityMapBuilder.SortKey(source.Rows[r]);
                    int runStart = columnLabels.Count;
                    var perLane = new Dictionary<int, int>();
                    for (; r <= block.LastRow && ContinuityMapBuilder.SortKey(source.Rows[r]) == date; r++)
                    {
                        int lane = laneTrack[source.Rows[r].SeriesId];
                        int n = perLane.GetValueOrDefault(lane);
                        perLane[lane] = n + 1;
                        columns[r] = runStart + n;
                    }

                    int width = perLane.Values.Max();
                    columnLabels.Add(DateLabel(date));
                    for (int k = 1; k < width; k++)
                    {
                        columnLabels.Add(null);
                    }
                }
            }

            blockColumns[block.Index] = (first, columnLabels.Count - 1);
        }

        var cells = source.Rows
            .Select((row, i) => new EventMapCell(i, row, laneTrack[row.SeriesId], columns[i], row.BlockIndex, IsTrunk: false))
            .ToList();
        var tracks = source.Lanes.Select((l, k) => new EventMapTrack(
            k, l.SeriesId, l.Name, IsTrunk: false, k % LaneColorCount, cells.Count(c => c.Track == k), l.Outside)).ToList();

        var edges = new List<EventMapEdge>();
        foreach (var block in source.Blocks)
        {
            var inBlock = cells.Skip(block.FirstRow).Take(block.LastRow - block.FirstRow + 1).ToList();
            if (block.Kind == EventMapBlockKind.Event)
            {
                for (int i = 1; i < inBlock.Count; i++)
                {
                    var from = inBlock[i - 1];
                    var to = inBlock[i];
                    edges.Add(MakeEdge(EventMapEdgeKind.Chain, from, to, from.Track == to.Track ? tracks[from.Track].ColorIndex : MutedColor));
                }
            }
            else
            {
                foreach (var lane in inBlock.GroupBy(c => c.Track))
                {
                    var list = lane.ToList();
                    for (int i = 1; i < list.Count; i++)
                    {
                        edges.Add(MakeEdge(EventMapEdgeKind.Sequence, list[i - 1], list[i], tracks[lane.Key].ColorIndex));
                    }
                }
            }
        }

        return new EventMapLayoutResult(false, SpineChoice.Relay, EventMapFilter.All, density, metrics, cells, tracks, edges,
            new List<EventMapSegment> { new(0, null, 0) }, columnLabels.Count, source.Blocks, blockColumns, columnLabels);
    }

    /// <summary>"Nov 2003" for a <c>year*100+month</c> key, "2003" when the month is unknown, "Undated" for none.</summary>
    internal static string DateLabel(int? dateKey)
    {
        if (dateKey is not int key)
        {
            return "Undated";
        }

        int year = key / 100;
        int month = key % 100;
        return month is >= 1 and <= 12
            ? $"{CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month)} {year.ToString(CultureInfo.InvariantCulture)}"
            : year.ToString(CultureInfo.InvariantCulture);
    }

    private static EventMapEdge MakeEdge(EventMapEdgeKind kind, EventMapCell from, EventMapCell to, int color) =>
        new(kind, from.Index, to.Index, Math.Min(from.Column, to.Column), Math.Max(from.Column, to.Column), color);

    internal static List<int>[] GroupByTrack(IReadOnlyList<EventMapCell> cells, int trackCount)
    {
        var byTrack = new List<int>[trackCount];
        for (int t = 0; t < trackCount; t++)
        {
            byTrack[t] = new List<int>();
        }

        foreach (var cell in cells)
        {
            byTrack[cell.Track].Add(cell.Index);
        }

        return byTrack;
    }
}

/// <summary>The output of <see cref="EventMapLayout.Compute"/>, plus every query the map's view and view model ask of it.</summary>
public sealed class EventMapLayoutResult
{
    /// <summary>Corner radius for orthogonal edge paths.</summary>
    public const double EdgeCornerRadius = 7;

    private readonly List<int>[] _byTrack;
    private readonly List<int>[] _byColumn;
    private readonly IReadOnlyList<(int First, int Last)>? _blockColumns;
    private readonly IReadOnlyList<string?>? _columnLabels;

    internal EventMapLayoutResult(
        bool isSpineMode, SpineChoice spine, EventMapFilter filter, EventMapDensity density, EventMapDensityMetrics metrics,
        IReadOnlyList<EventMapCell> cells, IReadOnlyList<EventMapTrack> tracks, IReadOnlyList<EventMapEdge> edges,
        IReadOnlyList<EventMapSegment> segments, int columnCount, IReadOnlyList<EventMapBlock>? blocks = null,
        IReadOnlyList<(int First, int Last)>? blockColumns = null, IReadOnlyList<string?>? columnLabels = null)
    {
        Blocks = blocks ?? Array.Empty<EventMapBlock>();
        _blockColumns = blockColumns;
        _columnLabels = columnLabels;
        IsSpineMode = isSpineMode;
        Spine = spine;
        Filter = filter;
        Density = density;
        Metrics = metrics;
        Cells = cells;
        Tracks = tracks;
        Edges = edges;
        Segments = segments;
        ColumnCount = columnCount;

        _byTrack = EventMapLayout.GroupByTrack(cells, tracks.Count);
        _byColumn = new List<int>[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            _byColumn[c] = new List<int>();
        }

        foreach (var cell in cells)
        {
            _byColumn[cell.Column].Add(cell.Index);
        }
    }

    public static EventMapLayoutResult Empty { get; } = new(false, SpineChoice.Relay, EventMapFilter.All, EventMapDensity.Standard,
        EventMapDensityMetrics.Standard, Array.Empty<EventMapCell>(), Array.Empty<EventMapTrack>(), Array.Empty<EventMapEdge>(),
        new[] { new EventMapSegment(0, null, 0) }, 0);

    public bool IsSpineMode { get; }

    /// <summary>Continuity-map blocks (event, between-events, year); empty on an event map.</summary>
    public IReadOnlyList<EventMapBlock> Blocks { get; }

    public bool IsBlockMode => Blocks.Count > 0;

    /// <summary>Column span of a block: its own columns, which differ from its rows once a between-events block is packed by date.</summary>
    public (int First, int Last) BlockColumns(EventMapBlock block) =>
        _blockColumns is { } spans && block.Index < spans.Count ? spans[block.Index] : (block.FirstRow, block.LastRow);

    /// <summary>
    /// The ruler's label for a column: the month in a packed between-events/year block (only on the first column of each month), the
    /// position in an event block, else the plain column number.
    /// </summary>
    public string? ColumnLabel(int column) =>
        _columnLabels is { } labels
            ? column >= 0 && column < labels.Count ? labels[column] : null
            : (column + 1).ToString(CultureInfo.CurrentCulture);

    public SpineChoice Spine { get; }

    public EventMapFilter Filter { get; }

    public EventMapDensity Density { get; }

    public EventMapDensityMetrics Metrics { get; }

    public IReadOnlyList<EventMapCell> Cells { get; }

    public IReadOnlyList<EventMapTrack> Tracks { get; }

    public IReadOnlyList<EventMapEdge> Edges { get; }

    public IReadOnlyList<EventMapSegment> Segments { get; }

    public int ColumnCount { get; }

    public int TrackCount => Tracks.Count;

    public bool IsEmpty => Cells.Count == 0;

    public Size TotalSize => new(ColumnCount * Metrics.SlotWidth, TrackCount * Metrics.TrackHeight);

    /// <summary>Cell indices on one track, in reading order.</summary>
    public IReadOnlyList<int> CellsOnTrack(int track) => track >= 0 && track < _byTrack.Length ? _byTrack[track] : Array.Empty<int>();

    public int? IndexOfIssue(int issueId)
    {
        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i].Row.IssueId == issueId)
            {
                return i;
            }
        }

        return null;
    }

    // --- Geometry ---

    public Rect CardRect(int index)
    {
        var cell = Cells[index];
        var m = Metrics;
        return new Rect(
            (cell.Column * m.SlotWidth) + ((m.SlotWidth - m.CardWidth) / 2),
            (cell.Track * m.TrackHeight) + ((m.TrackHeight - m.CardHeight) / 2),
            m.CardWidth,
            m.CardHeight);
    }

    /// <summary>
    /// The corner points of an edge's path, before rounding (spec §1 "Edge geometry"). Tie-in and continuity: trunk
    /// bottom-centre, straight down the trunk's column, along the target lane's top gutter, down into the target's
    /// top-centre. Chain hop: source right-centre, the gutter between the two columns, vertical, target left-centre.
    /// Sequence and same-lane chain: a straight line from right-centre to left-centre.
    /// </summary>
    public IReadOnlyList<Point> EdgePoints(EventMapEdge edge)
    {
        var a = CardRect(edge.From);
        var b = CardRect(edge.To);
        var from = Cells[edge.From];
        var to = Cells[edge.To];

        switch (edge.Kind)
        {
            case EventMapEdgeKind.TieIn:
            case EventMapEdgeKind.Continuity:
                double gutterY = (to.Track * Metrics.TrackHeight) + ((Metrics.TrackHeight - Metrics.CardHeight) / 4);
                return new[]
                {
                    new Point(a.Center.X, a.Bottom),
                    new Point(a.Center.X, gutterY),
                    new Point(b.Center.X, gutterY),
                    new Point(b.Center.X, b.Top),
                };

            case EventMapEdgeKind.Chain when from.Track != to.Track:
                double midX = (a.Right + b.Left) / 2;
                return new[]
                {
                    new Point(a.Right, a.Center.Y),
                    new Point(midX, a.Center.Y),
                    new Point(midX, b.Center.Y),
                    new Point(b.Left, b.Center.Y),
                };

            default:
                return new[] { new Point(a.Right, a.Center.Y), new Point(b.Left, b.Center.Y) };
        }
    }

    /// <summary>Columns and tracks meeting <paramref name="viewport"/> (surface coordinates), with one of overscan on each side, clamped to the grid.</summary>
    public EventMapVisibleRange VisibleRange(Rect viewport)
    {
        if (IsEmpty || viewport.Width <= 0 || viewport.Height <= 0)
        {
            return EventMapVisibleRange.Empty;
        }

        (int firstColumn, int lastColumn) = Span(viewport.X, viewport.Width, Metrics.SlotWidth, ColumnCount);
        (int firstTrack, int lastTrack) = Span(viewport.Y, viewport.Height, Metrics.TrackHeight, TrackCount);
        return new EventMapVisibleRange(firstColumn, lastColumn, firstTrack, lastTrack);

        static (int First, int Last) Span(double start, double length, double size, int count)
        {
            int first = (int)Math.Floor(start / size) - 1;              // first visible, minus one of overscan
            int last = (int)Math.Ceiling((start + length) / size);      // last visible is Ceiling - 1; plus one of overscan
            return (Math.Clamp(first, 0, count - 1), Math.Clamp(last, 0, count - 1));
        }
    }

    /// <summary>Cell indices inside <paramref name="range"/>, in column order.</summary>
    public IEnumerable<int> CellsIn(EventMapVisibleRange range)
    {
        if (range.IsEmpty)
        {
            yield break;
        }

        for (int c = range.FirstColumn; c <= range.LastColumn && c < _byColumn.Length; c++)
        {
            foreach (int index in _byColumn[c])
            {
                int track = Cells[index].Track;
                if (track >= range.FirstTrack && track <= range.LastTrack)
                {
                    yield return index;
                }
            }
        }
    }

    // --- Navigation (spec §1 "Navigation"; §4 keyboard) ---

    public int? Next(int index) => index + 1 < Cells.Count ? index + 1 : null;

    public int? Prev(int index) => index > 0 && index - 1 < Cells.Count ? index - 1 : null;

    public int? First() => Cells.Count > 0 ? 0 : null;

    public int? Last() => Cells.Count > 0 ? Cells.Count - 1 : null;

    /// <summary>The card on the neighbouring track (<paramref name="direction"/> −1 up, +1 down) with the closest column; the earlier column wins a tie.</summary>
    public int? NearestInLane(int index, int direction)
    {
        int track = Cells[index].Track + Math.Sign(direction);
        if (track < 0 || track >= TrackCount)
        {
            return null;
        }

        int column = Cells[index].Column;
        int? best = null;
        foreach (int candidate in _byTrack[track])
        {
            if (best is null)
            {
                best = candidate;
                continue;
            }

            int d = Math.Abs(Cells[candidate].Column - column);
            int bestD = Math.Abs(Cells[best.Value].Column - column);
            if (d < bestD || (d == bestD && Cells[candidate].Column < Cells[best.Value].Column))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Left (<paramref name="direction"/> -1) and Right (+1): the nearest card in the same lane in that direction, so the key moves along the lane. A lane with nothing further that way falls back to the nearest
    /// column beyond it (the closest lane within that column), so the key never just stops while the map goes on.
    /// </summary>
    public int? NearestAlongLane(int index, int direction)
    {
        var cell = Cells[index];
        int sign = Math.Sign(direction);
        int? best = null;
        foreach (int candidate in _byTrack[cell.Track])
        {
            int distance = (Cells[candidate].Column - cell.Column) * sign;
            if (distance > 0 && (best is null || distance < (Cells[best.Value].Column - cell.Column) * sign))
            {
                best = candidate;
            }
        }

        if (best is not null)
        {
            return best;
        }

        for (int column = cell.Column + sign; column >= 0 && column < _byColumn.Length; column += sign)
        {
            int? closest = null;
            foreach (int candidate in _byColumn[column])
            {
                if (closest is null || Math.Abs(Cells[candidate].Track - cell.Track) < Math.Abs(Cells[closest.Value].Track - cell.Track))
                {
                    closest = candidate;
                }
            }

            if (closest is not null)
            {
                return closest;
            }
        }

        return null;
    }

    /// <summary>First unread (not fully read) trunk card in spine mode, or first unread card in relay mode; falls back to the first card.</summary>
    public int? FirstUnread()
    {
        foreach (var cell in Cells)
        {
            if (cell.Row.ReadState != EventMapReadState.Read && (!IsSpineMode || cell.IsTrunk))
            {
                return cell.Index;
            }
        }

        return First();
    }

    // --- Inspector links and the related (undimmed) set ---

    public EventMapLinks LinksFor(int index)
    {
        var cell = Cells[index];
        if (!IsSpineMode)
        {
            int? previousInSeries = null;
            for (int i = index - 1; i >= 0; i--)
            {
                if (Cells[i].Row.SeriesId == cell.Row.SeriesId)
                {
                    previousInSeries = i;
                    break;
                }
            }

            return new EventMapLinks(Prev(index), Next(index), null, previousInSeries, Array.Empty<int>());
        }

        var lane = _byTrack[cell.Track];
        int at = lane.IndexOf(index);
        int? follows = at > 0 ? lane[at - 1] : null;
        int? leadsTo = at + 1 < lane.Count ? lane[at + 1] : null;
        int? tiesInto = !cell.IsTrunk ? Segments[cell.Segment].TrunkCell : null;
        var segmentOrder = Cells.Where(c => c.Segment == cell.Segment).Select(c => c.Index).ToList();
        return new EventMapLinks(follows, leadsTo, tiesInto, null, segmentOrder);
    }

    /// <summary>
    /// Cards that stay undimmed while <paramref name="index"/> is selected. Spine mode: its whole lane, its segment's
    /// trunk card, and that trunk card's trunk neighbours. Relay mode: its chain neighbours and its whole lane.
    /// </summary>
    public HashSet<int> RelatedSet(int index)
    {
        var cell = Cells[index];
        var set = new HashSet<int>(_byTrack[cell.Track]) { index };
        if (IsSpineMode)
        {
            if (Segments[cell.Segment].TrunkCell is int trunk)
            {
                set.Add(trunk);
                var trunkLane = _byTrack[0];
                int at = trunkLane.IndexOf(trunk);
                if (at > 0) set.Add(trunkLane[at - 1]);
                if (at + 1 < trunkLane.Count) set.Add(trunkLane[at + 1]);
            }
        }
        else
        {
            if (Prev(index) is int p) set.Add(p);
            if (Next(index) is int n) set.Add(n);
        }

        return set;
    }
}
