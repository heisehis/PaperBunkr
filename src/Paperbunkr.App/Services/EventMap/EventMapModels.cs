using System.Collections.Generic;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.EventMap;

// Plain data for the Event Map (docs/superpowers/specs/2026-09-25-event-map-design.md §1). Everything the map shows
// is computed from these by the pure EventMapLayout, so layout, navigation and edge geometry are unit-testable
// without Avalonia or a database.

public enum EventMapReadState
{
    Unread,
    InProgress,
    Read,
}

/// <summary>
/// One event membership as the map needs it. <see cref="Number"/> is the effective issue number ("" when unknown). <see cref="SortDate"/>
/// (<c>year*100+month</c>) orders the continuity map's loose issues: GCD's on-sale date for matched issues, else the cover date.
/// </summary>
public sealed record EventMapRow(
    int MembershipId,
    int IssueId,
    int Position,
    EventMembershipRole Role,
    int SeriesId,
    string SeriesName,
    string Number,
    int? Year,
    int? Month,
    bool FileIsMissing,
    EventMapReadState ReadState,
    string? CoverKey = null,
    string? Summary = null,
    int? OwnerEventId = null,
    string? OwnerEventName = null,
    IReadOnlyList<EventMapEventRef>? AlsoIn = null,
    bool OutsideContinuity = false,
    int BlockIndex = 0,
    int? SortDate = null);

/// <summary>An event named on a continuity-map card ("Event", "Also in") or in the Events picker.</summary>
public sealed record EventMapEventRef(int Id, string Name);

/// <summary>What a continuity-map block holds (docs/superpowers/specs/2026-09-27-continuity-map-design.md §3).</summary>
public enum EventMapBlockKind
{
    /// <summary>One story event's issues, in its own order.</summary>
    Event,

    /// <summary>Issues in no event, placed chronologically before the next event.</summary>
    Between,

    /// <summary>Publication order (a continuity with no events): one calendar year, or "Undated".</summary>
    Year,
}

/// <summary>A run of consecutive rows/columns on a continuity map, labelled on the ruler's band row.</summary>
public sealed record EventMapBlock(int Index, EventMapBlockKind Kind, string Label, int? EventId, int FirstRow, int LastRow);

/// <summary>
/// A relation drawn between two event bands on the ruler (§4). <see cref="IsOrdered"/> connectors run earlier → later with an arrow;
/// Crossovers are unordered and drawn as a bracket. <see cref="IsYours"/> draws solid, otherwise dashed (inferred or Wikidata).
/// </summary>
public sealed record EventMapConnector(int FromBlock, int ToBlock, string Label, bool IsOrdered, bool IsYours, string Tooltip);

/// <summary>
/// A continuity-map lane: a series, flagged when it isn't a member of the continuity. <see cref="ContinuesAs"/> names the lane right below
/// when that series continues this one (a GCD series bond or your own Continuation relation; docs/superpowers/specs/2026-09-27-gcd-data-design.md §4).
/// </summary>
public sealed record ContinuityLane(int SeriesId, string Name, bool Outside, string? ContinuesAs = null);

/// <summary>Filters the continuity map is built with (§3/§4).</summary>
public sealed record ContinuityMapOptions(IReadOnlySet<int> HiddenEventIds, bool HideOptional, bool EventsOnly)
{
    public static ContinuityMapOptions Default { get; } = new(new HashSet<int>(), false, false);
}

/// <summary>A built continuity map: ordered rows split into blocks, lanes, connectors, and the counts for the status line.</summary>
public sealed record ContinuityMapSource(
    int ContinuityId,
    string Name,
    bool IsPublicationOrder,
    IReadOnlyList<EventMapRow> Rows,
    IReadOnlyList<EventMapBlock> Blocks,
    IReadOnlyList<ContinuityLane> Lanes,
    IReadOnlyList<EventMapConnector> Connectors,
    IReadOnlyList<EventMapEventRef> Events,
    int EventIssueCount,
    int LooseIssueCount,
    int PendingDuplicatePairs);

/// <summary>What <see cref="EventMapLoader.Load"/> returns: the event, its saved spine choice and its rows in <c>(Position, MembershipId)</c> order.</summary>
public sealed record EventMapSource(int StoryEventId, string EventName, int? SpineSeriesId, IReadOnlyList<EventMapRow> Rows);

public enum SpineSource
{
    /// <summary>No spine: the map uses the relay layout.</summary>
    None,

    /// <summary>The user picked the spine (<c>StoryEvent.SpineSeriesId</c>).</summary>
    User,

    /// <summary>A member series whose name matches the event's.</summary>
    Auto,
}

/// <summary>The resolved spine. <see cref="AutoMatchSeriesId"/> is the name match regardless of any saved choice, so the picker can label it.</summary>
public sealed record SpineChoice(int? SeriesId, SpineSource Source, int? AutoMatchSeriesId)
{
    public static SpineChoice Relay { get; } = new(null, SpineSource.None, null);

    public bool HasSpine => SeriesId is not null;
}

public enum EventMapFilter
{
    All,

    /// <summary>Trunk rows only. Only offered in spine mode; treated as <see cref="All"/> in relay mode.</summary>
    SpineOnly,

    /// <summary>Drops rows whose role is <see cref="EventMembershipRole.Optional"/>.</summary>
    HideOptional,

    /// <summary>Continuity map only: hides the between-events blocks (issues in no event).</summary>
    EventsOnly,
}

public enum EventMapDensity
{
    Compact,
    Standard,
    Covers,
}

/// <summary>Grid metrics for one density stop (spec §1 table). A card sits centred in its column × track cell.</summary>
public sealed record EventMapDensityMetrics(double SlotWidth, double TrackHeight, double CardWidth, double CardHeight)
{
    public static EventMapDensityMetrics Compact { get; } = new(118, 44, 100, 30);

    public static EventMapDensityMetrics Standard { get; } = new(170, 84, 150, 64);

    public static EventMapDensityMetrics Covers { get; } = new(150, 220, 124, 196);

    public static EventMapDensityMetrics For(EventMapDensity density) => density switch
    {
        EventMapDensity.Compact => Compact,
        EventMapDensity.Covers => Covers,
        _ => Standard,
    };
}

public enum EventMapEdgeKind
{
    /// <summary>Consecutive rows within one lane, or consecutive trunk rows.</summary>
    Sequence,

    /// <summary>A segment's trunk row to the first row of each lane in that segment.</summary>
    TieIn,

    /// <summary>A Prologue/Epilogue promoted onto the trunk to the next row of its own series (drawn faint).</summary>
    Continuity,

    /// <summary>Relay mode: consecutive rows in reading order, hopping lanes where needed.</summary>
    Chain,
}

/// <summary>
/// One laid-out card. <see cref="Index"/> is its place in reading order among the rows that survived the filter.
/// <see cref="Segment"/> is 0 for rows before the first trunk row (and for every row in relay mode); segment k ≥ 1 is
/// anchored by the k-th trunk row.
/// </summary>
public sealed record EventMapCell(int Index, EventMapRow Row, int Track, int Column, int Segment, bool IsTrunk);

/// <summary>One horizontal lane. The trunk is track 0 in spine mode and has <see cref="ColorIndex"/> -1 (the accent colour).</summary>
public sealed record EventMapTrack(int Index, int SeriesId, string SeriesName, bool IsTrunk, int ColorIndex, int Count, bool IsOutside = false);

/// <summary>An edge between two cells (by <see cref="EventMapCell.Index"/>), with the column span its path covers.</summary>
public sealed record EventMapEdge(EventMapEdgeKind Kind, int From, int To, int MinColumn, int MaxColumn, int ColorIndex);

/// <summary>A spine-mode segment: the trunk cell that anchors it (null for segment 0) and the first column lane rows may use.</summary>
public sealed record EventMapSegment(int Index, int? TrunkCell, int StartColumn);

/// <summary>The inspector's connections for one card (spec §1 "Inspector links"). Values are cell indices.</summary>
public sealed record EventMapLinks(int? Follows, int? LeadsTo, int? TiesInto, int? PreviousInSeries, IReadOnlyList<int> SegmentOrder);

/// <summary>Columns and tracks to realize for a viewport, overscan included. <see cref="IsEmpty"/> when there is nothing to show.</summary>
public readonly record struct EventMapVisibleRange(int FirstColumn, int LastColumn, int FirstTrack, int LastTrack)
{
    public static EventMapVisibleRange Empty { get; } = new(0, -1, 0, -1);

    public bool IsEmpty => LastColumn < FirstColumn || LastTrack < FirstTrack;

    public bool Contains(int column, int track) =>
        column >= FirstColumn && column <= LastColumn && track >= FirstTrack && track <= LastTrack;
}
