using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>Row builders and the worked samples shared by the Event Map pure tests.</summary>
internal static class EventMapFixtures
{
    public const int Crisis = 1;
    public const int SeriesA = 2;
    public const int SeriesB = 3;

    private static int _nextMembershipId = 1000;

    public static EventMapRow Row(int seriesId, string number, int position,
        EventMembershipRole role = EventMembershipRole.Core,
        EventMapReadState read = EventMapReadState.Unread,
        int? membershipId = null,
        string? seriesName = null)
    {
        string name = seriesName ?? seriesId switch
        {
            Crisis => "Crisis",
            SeriesA => "Adventure",
            SeriesB => "Brave",
            _ => $"Series {seriesId}",
        };

        int id = membershipId ?? System.Threading.Interlocked.Increment(ref _nextMembershipId);
        return new EventMapRow(id, IssueId: id + 50_000, position, role, seriesId, name, number, 2020, 1, FileIsMissing: false, read);
    }

    public static EventMapSource Source(string eventName, params EventMapRow[] rows) => new(1, eventName, null, rows);

    public static EventMapSource Source(string eventName, int? savedSpine, params EventMapRow[] rows) => new(1, eventName, savedSpine, rows);

    /// <summary>
    /// The 10-row spine sample (stands in for the design session's screen-1 mockup, which isn't in the repo).
    /// Reading order and the compact columns it must produce:
    /// <code>
    /// idx row        lane    column  why
    ///  0  A #0       A       0       segment 0, lane A free at 0
    ///  1  Crisis #1  trunk   1       maxColumn(0)+1; segment 1 starts at 2
    ///  2  A #1       A       2       max(2, A next 1)
    ///  3  B #1       B       2       max(2, B next 0)
    ///  4  A #2       A       3       max(2, A next 3)
    ///  5  Crisis #2  trunk   4       maxColumn(3)+1; segment 2 starts at 5
    ///  6  B #2       B       5       max(5, B next 3)
    ///  7  A #3       A       5       max(5, A next 4)
    ///  8  B #3       B       6       max(5, B next 6)
    ///  9  A #4       A       6       max(5, A next 6)   → 7 columns
    /// </code>
    /// </summary>
    public static EventMapRow[] SpineSampleRows() => new[]
    {
        Row(SeriesA, "0", 1),
        Row(Crisis, "1", 2),
        Row(SeriesA, "1", 3),
        Row(SeriesB, "1", 4),
        Row(SeriesA, "2", 5),
        Row(Crisis, "2", 6),
        Row(SeriesB, "2", 7),
        Row(SeriesA, "3", 8),
        Row(SeriesB, "3", 9),
        Row(SeriesA, "4", 10),
    };

    public const int X = 11;
    public const int Y = 12;
    public const int Z = 13;

    /// <summary>The 12-row relay sample (stands in for the relay-screen mockup): X1 X2 Y1 X3 Z1 Y2 Z2 X4 Y3 Z3 X5 Y4.</summary>
    public static EventMapRow[] RelaySampleRows()
    {
        var order = new[] { (X, "1"), (X, "2"), (Y, "1"), (X, "3"), (Z, "1"), (Y, "2"), (Z, "2"), (X, "4"), (Y, "3"), (Z, "3"), (X, "5"), (Y, "4") };
        return order.Select((o, i) => Row(o.Item1, o.Item2, i + 1)).ToArray();
    }

    public static EventMapLayoutResult Layout(EventMapSource source, EventMapFilter filter = EventMapFilter.All, EventMapDensity density = EventMapDensity.Standard) =>
        EventMapLayout.Compute(source, SpineResolver.Resolve(source), filter, density);
}
