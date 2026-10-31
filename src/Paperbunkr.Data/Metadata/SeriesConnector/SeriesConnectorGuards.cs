using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>The rules every automatic relation and suggestion obeys (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.5).</summary>
public static class SeriesConnectorGuards
{
    public static (int Low, int High) Pair(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>A pair the connector must leave alone: the same series, a pair you related yourself, or a dismissed pair.</summary>
    public static bool IsBlocked(SeriesConnectorSnapshot snapshot, int a, int b)
    {
        if (a == b)
        {
            return true;
        }

        var pair = Pair(a, b);
        return snapshot.UserPairs.Contains(pair) || snapshot.DismissedPairs.Contains(pair);
    }

    /// <summary>Same publisher, or one side's publisher isn't known (an unknown publisher doesn't block, two different known ones do).</summary>
    public static bool SamePublisher(ConnectorSeries a, ConnectorSeries b) =>
        a.PublisherKey is null || b.PublisherKey is null || string.Equals(a.PublisherKey, b.PublisherKey, StringComparison.Ordinal);

    /// <summary>Comics (including Unknown) never link to manga-family series through inference.</summary>
    public static bool SameContentFamily(ConnectorSeries a, ConnectorSeries b) => IsMangaFamily(a.ContentType) == IsMangaFamily(b.ContentType);

    private static bool IsMangaFamily(ContentType type) => type is ContentType.Manga or ContentType.Manhua or ContentType.Manhwa;

    /// <summary>Years shared by two runs (0 when they don't overlap or a year is unknown).</summary>
    public static int OverlapYears(ConnectorSeries a, ConnectorSeries b)
    {
        if (a.StartYear is not int aStart || b.StartYear is not int bStart)
        {
            return 0;
        }

        int aEnd = a.EndYear ?? aStart, bEnd = b.EndYear ?? bStart;
        return Math.Max(0, Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart) + 1);
    }
}
