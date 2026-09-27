using System;
using System.Collections.Generic;
using Paperbunkr.App.Services.AdDetection;

namespace Paperbunkr.App.Services.Compare;

/// <summary>The page of edition B that pairs with a page of edition A, and how sure that is.</summary>
/// <param name="PageB">0-based page of B to show beside A's page.</param>
/// <param name="Distance">The dHash distance (0-64) of the match, or null when nothing was close enough and <paramref name="PageB"/> is just the default pairing.</param>
public readonly record struct PageMatch(int PageB, int? Distance)
{
    public bool IsMatched => Distance is not null;
}

/// <summary>
/// Pairs pages of two editions of one book (docs/superpowers/specs/2026-09-26-comic-reader-compare-design.md): editions differ in page count (an extra cover, an ad, a credits page), so page N of one is not page N of the other.
/// A page of A is matched to the page of B with the closest dHash (<see cref="PageHasher"/>) inside a window around where it would be by number plus a user offset.
/// </summary>
public static class PageAligner
{
    /// <summary>How many pages either side of the expected one are searched.</summary>
    public const int Window = 8;

    /// <summary>The largest dHash distance (of 64 bits) still counted as the same page. Re-scans and re-compressions of one page differ by a few bits; different pages by 20 or more.</summary>
    public const int MaxDistance = 12;

    /// <summary>The expected page of B for a page of A: the same number plus <paramref name="offset"/>, kept inside B.</summary>
    public static int Expected(int pageA, int offset, int pageCountB) => Math.Clamp(pageA + offset, 0, Math.Max(0, pageCountB - 1));

    /// <summary>
    /// Finds the best partner for the page of A whose hash is <paramref name="hashA"/>. <paramref name="hashB"/> gives B's hash of a page, or null when it is not known (yet); only known pages are considered.
    /// A tie goes to the page nearest the expected one. Nothing within <see cref="MaxDistance"/>: the expected page, marked unmatched.
    /// </summary>
    public static PageMatch FindMatch(long hashA, int pageA, int offset, int pageCountB, Func<int, long?> hashB, int window = Window, int maxDistance = MaxDistance)
    {
        int expected = Expected(pageA, offset, pageCountB);
        int best = -1;
        int bestDistance = int.MaxValue;
        int from = Math.Max(0, expected - window);
        int to = Math.Min(pageCountB - 1, expected + window);
        for (int page = from; page <= to; page++)
        {
            if (hashB(page) is not { } other)
            {
                continue;
            }

            int distance = PageHasher.Distance(hashA, other);
            if (distance < bestDistance || (distance == bestDistance && Math.Abs(page - expected) < Math.Abs(best - expected)))
            {
                best = page;
                bestDistance = distance;
            }
        }

        return best >= 0 && bestDistance <= maxDistance ? new PageMatch(best, bestDistance) : new PageMatch(expected, null);
    }
}

/// <summary>Which edition wins a row of the facts strip.</summary>
public enum FactsWinner
{
    None,
    A,
    B,
}

/// <summary>What the facts strip says about one edition.</summary>
public sealed record EditionFacts(string Format, long FileSizeBytes, int PageCount, int PageWidth, int PageHeight)
{
    public long PageArea => (long)PageWidth * PageHeight;

    public long BytesPerPage => PageCount > 0 ? FileSizeBytes / PageCount : 0;

    public string SizeLabel => FormatBytes(FileSizeBytes);

    public string ResolutionLabel => PageWidth > 0 && PageHeight > 0 ? $"{PageWidth} × {PageHeight}" : "unknown";

    public string BytesPerPageLabel => PageCount > 0 ? FormatBytes(BytesPerPage) + " / page" : "";

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };
}

/// <summary>The badges of the facts strip: which edition has the higher resolution, the larger file, more pages.</summary>
public static class EditionComparison
{
    /// <summary>One edition must have at least this much more (5%) to win a row, so a rounding difference is not a badge.</summary>
    public const double Margin = 1.05;

    public static FactsWinner HigherResolution(EditionFacts a, EditionFacts b) => Winner(a.PageArea, b.PageArea);

    public static FactsWinner LargerFile(EditionFacts a, EditionFacts b) => Winner(a.FileSizeBytes, b.FileSizeBytes);

    public static FactsWinner MorePages(EditionFacts a, EditionFacts b) => a.PageCount == b.PageCount ? FactsWinner.None : a.PageCount > b.PageCount ? FactsWinner.A : FactsWinner.B;

    private static FactsWinner Winner(long a, long b)
    {
        if (a <= 0 || b <= 0)
        {
            return FactsWinner.None;
        }

        if (a >= b * Margin)
        {
            return FactsWinner.A;
        }

        return b >= a * Margin ? FactsWinner.B : FactsWinner.None;
    }
}

/// <summary>A lazily filled, thread-safe store of one edition's page hashes (memory only, never persisted).</summary>
public sealed class PageHashCache
{
    private readonly Func<int, long?> _compute;
    private readonly Dictionary<int, long?> _hashes = new();
    private readonly object _lock = new();

    public PageHashCache(int pageCount, Func<int, long?> compute)
    {
        PageCount = pageCount;
        _compute = compute;
    }

    public int PageCount { get; }

    /// <summary>The hash if it has been computed already, else null (a page that could not be hashed is also null).</summary>
    public long? TryGet(int page)
    {
        lock (_lock)
        {
            return _hashes.TryGetValue(page, out var hash) ? hash : null;
        }
    }

    public bool IsKnown(int page)
    {
        lock (_lock)
        {
            return _hashes.ContainsKey(page);
        }
    }

    /// <summary>Computes the hash of <paramref name="page"/> if it is not known (call off the UI thread) and returns it.</summary>
    public long? Get(int page)
    {
        if (page < 0 || page >= PageCount)
        {
            return null;
        }

        lock (_lock)
        {
            if (_hashes.TryGetValue(page, out var known))
            {
                return known;
            }
        }

        long? hash = _compute(page);
        lock (_lock)
        {
            _hashes[page] = hash;
        }

        return hash;
    }
}
