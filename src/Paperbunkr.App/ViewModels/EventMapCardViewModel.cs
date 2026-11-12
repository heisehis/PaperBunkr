using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One card on the Event Map (docs/superpowers/specs/2026-09-25-event-map-design.md §3). Rebuilt with every layout -
/// they're cheap - but updated in place for selection, dimming and read-state changes, so a realized
/// <c>EventMapCard</c> never has to be re-templated for those.
/// </summary>
public sealed partial class EventMapCardViewModel : ObservableObject
{
    private static readonly string[] SmallWords = { "a", "an", "the", "of", "on", "and", "in", "to", "for" };

    public EventMapCardViewModel(EventMapCell cell, EventMapTrack track)
    {
        Index = cell.Index;
        var row = cell.Row;
        IssueId = row.IssueId;
        MembershipId = row.MembershipId;
        SeriesId = row.SeriesId;
        SeriesName = row.SeriesName;
        Number = row.Number;
        Title = string.IsNullOrEmpty(row.Number) ? row.SeriesName : $"{row.SeriesName} #{row.Number}";
        Badge = string.IsNullOrEmpty(row.Number) ? Initials(row.SeriesName) : $"{Initials(row.SeriesName)} #{row.Number}";
        CoverDate = FormatCoverDate(row.Year, row.Month);
        CoverKey = row.CoverKey;
        Summary = row.Summary;
        RoleLabel = EventMembershipRoleOption.FormatLabel(row.Role);
        IsTrunk = cell.IsTrunk;
        IsMissing = row.FileIsMissing;
        ColorIndex = cell.ColorIndexOverride ?? track.ColorIndex;
        Column = cell.Column;
        Track = cell.Track;
        OwnerEventId = row.OwnerEventId;
        OwnerEventName = row.OwnerEventName;
        CbroOverlapNotes = row.OwnerEventCbroNotes ?? Array.Empty<string>();
        AlsoIn = row.AlsoIn ?? Array.Empty<EventMapEventRef>();
        IsOutsideContinuity = row.OutsideContinuity;
        Ghost = row.Ghost;
        _readState = row.ReadState;
    }

    /// <summary>An issue from the event's provider list that isn't a member (continuity map M4); null for a real member.</summary>
    public Paperbunkr.Data.Metadata.EventGhost? Ghost { get; }

    public bool IsGhost => Ghost is not null;

    /// <summary>A real member: it can be read, marked read and opened in its series.</summary>
    public bool IsMember => Ghost is null;

    /// <summary>The library owns it, but it isn't in this event ("In your library").</summary>
    public bool IsOwnedGhost => Ghost?.Kind == Paperbunkr.Data.Metadata.EventGhostKind.OwnedNotMember;

    /// <summary>The library has no copy ("Not in your library").</summary>
    public bool IsMissingGhost => Ghost?.Kind == Paperbunkr.Data.Metadata.EventGhostKind.Missing;

    public string GhostLabel => IsOwnedGhost ? "In your library" : "Not in your library";

    /// <summary>"From Metron · checked 3 days ago".</summary>
    public string GhostSourceLabel => Ghost is { } g ? $"From {g.Provider} · checked {Ago(g.FetchedAt)}" : string.Empty;

    /// <summary>The issue's page on the provider, for "Open on Metron/ComicVine".</summary>
    public string? GhostProviderUrl => Ghost is not { } g || g.ProviderIssueId is not { Length: > 0 } id ? null
        : g.Provider == Paperbunkr.Data.Metadata.EventArcSnapshots.Metron ? $"https://metron.cloud/issue/{Uri.EscapeDataString(id)}/"
        : $"https://comicvine.gamespot.com/issue/4000-{Uri.EscapeDataString(id)}/";

    public string GhostProviderLabel => Ghost is { } g ? $"Open on {g.Provider}" : string.Empty;

    /// <summary>The main character's or team's image (M5 §2); null when it has no real image.</summary>
    [ObservableProperty]
    private string? _facePath;

    /// <summary>Set after Add to Wanted, so the inspector shows "Wanted" instead of the button.</summary>
    [ObservableProperty]
    private bool _isWanted;

    private static string Ago(DateTime utc)
    {
        int days = (int)Math.Floor((DateTime.UtcNow - utc).TotalDays);
        return days <= 0 ? "today" : days == 1 ? "yesterday" : $"{days} days ago";
    }

    /// <summary>Continuity map: the event whose block this card sits in; null for an issue in no event (or on an event map).</summary>
    public int? OwnerEventId { get; }

    public string? OwnerEventName { get; }

    public IReadOnlyList<string> CbroOverlapNotes { get; }

    public bool HasCbroOverlapNotes => CbroOverlapNotes.Count > 0;

    /// <summary>Continuity map: other events that also contain this issue (it appears once, in the first).</summary>
    public IReadOnlyList<EventMapEventRef> AlsoIn { get; }

    public bool IsOutsideContinuity { get; }

    public int Index { get; }

    public int IssueId { get; }

    public int MembershipId { get; }

    public int SeriesId { get; }

    public string SeriesName { get; }

    public string Number { get; }

    /// <summary>"Adventure #2".</summary>
    public string Title { get; }

    /// <summary>Compact-stop label, e.g. "AB #2".</summary>
    public string Badge { get; }

    /// <summary>"Apr 1985", "1985" or empty.</summary>
    public string CoverDate { get; }

    public bool HasCoverDate => CoverDate.Length > 0;

    public string? CoverKey { get; }

    public string? Summary { get; }

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public string RoleLabel { get; }

    public bool IsTrunk { get; }

    public bool IsMissing { get; }

    /// <summary>The lane colour (<see cref="EventMapLayout.TrunkColor"/> for the trunk).</summary>
    public int ColorIndex { get; }

    public int Column { get; }

    public int Track { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRead), nameof(IsInProgress), nameof(IsUnread), nameof(ReadStateLabel), nameof(AutomationName), nameof(ToolTipText), nameof(MarkReadLabel))]
    private EventMapReadState _readState;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isDimmed;

    public bool IsRead => ReadState == EventMapReadState.Read;

    public bool IsInProgress => ReadState == EventMapReadState.InProgress;

    public bool IsUnread => ReadState == EventMapReadState.Unread;

    public string ReadStateLabel => ReadState switch
    {
        EventMapReadState.Read => "Read",
        EventMapReadState.InProgress => "In progress",
        _ => "Unread",
    };

    /// <summary>Inspector toggle label: an in-progress issue offers "Mark read", like an unread one.</summary>
    public string MarkReadLabel => IsRead ? "Mark unread" : "Mark read";

    /// <summary>"Adventure #2, Tie In, unread" - read state is never conveyed by colour alone (spec §3).</summary>
    public string AutomationName => IsGhost
        ? $"{Title}, {GhostLabel.ToLowerInvariant()}"
        : $"{Title}, {RoleLabel}, {ReadStateLabel.ToLowerInvariant()}{(IsMissing ? ", file missing" : string.Empty)}";

    public string ToolTipText => $"{Title}\n{RoleLabel} · {ReadStateLabel}{(HasCoverDate ? " · " + CoverDate : string.Empty)}{(IsMissing ? "\nFile missing" : string.Empty)}";

    internal static string Initials(string seriesName)
    {
        var words = seriesName
            .Split(new[] { ' ', '-', ':', '/', '(', ')', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .ToList();
        if (words.Count == 0)
        {
            return "?";
        }

        if (words.Count == 1)
        {
            return words[0].Length <= 3 ? words[0].ToUpperInvariant() : words[0][..2].ToUpperInvariant();
        }

        var significant = words.Where(w => !SmallWords.Contains(w, StringComparer.OrdinalIgnoreCase)).ToList();
        if (significant.Count == 0)
        {
            significant = words;
        }

        return string.Concat(significant.Take(3).Select(w => char.ToUpperInvariant(w[0])));
    }

    internal static string FormatCoverDate(int? year, int? month)
    {
        if (year is not int y)
        {
            return string.Empty;
        }

        return month is >= 1 and <= 12
            ? $"{CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month.Value)} {y}"
            : y.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>A lane header: colour bar, name (trimmed with a tooltip) and issue count (spec §3 "Lane headers").</summary>
public sealed record EventMapLaneHeader(string Label, string FullName, int Count, int ColorIndex, bool IsTrunk, double Height, bool IsOutside = false,
    string? ContinuesAs = null, string? RelationLabel = null)
{
    public string CountLabel => (Count == 1 ? "1 issue" : $"{Count} issues")
                                + (IsOutside ? " · outside this continuity" : string.Empty)
                                + (ContinuesAs is null ? string.Empty : " · continues as ↓");
}

/// <summary>An inspector link ("Follows", "Ties into", a Segment-order entry). Clicking selects <see cref="Index"/> on the map.</summary>
public sealed record EventMapLinkViewModel(string Relation, string Title, int Index, bool IsCurrent = false, string? RoleLabel = null, int? EventId = null);
