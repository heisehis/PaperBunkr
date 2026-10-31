namespace Paperbunkr.Data.Books;

/// <summary>
/// Which numbers are missing inside a book series (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §6).
/// Only holes <b>between</b> owned numbers are reported (own 1, 2 and 4: "#3"); the total of a series is not known, so the tail is never guessed.
/// A series with any unnumbered book gets no gaps at all (we cannot tell where that book sits), fractional numbers (2.5) neither fill nor create a gap,
/// and an absurd spread (a stray index of 9999) is treated as bad data rather than 9,000 ghost cards.
/// </summary>
public static class BookSeriesGaps
{
    public const int MaxSpread = 200;

    public static IReadOnlyList<int> Find(IEnumerable<float?> seriesIndices)
    {
        var all = seriesIndices.ToList();
        if (all.Count == 0 || all.Any(i => i is null))
        {
            return Array.Empty<int>();
        }

        var wholes = all
            .Select(i => i!.Value)
            .Where(i => i >= 0f && MathF.Floor(i) == i)
            .Select(i => (int)i)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
        if (wholes.Count < 2)
        {
            return Array.Empty<int>();
        }

        int min = wholes[0];
        int max = wholes[^1];
        if (max - min > MaxSpread)
        {
            return Array.Empty<int>();
        }

        var owned = wholes.ToHashSet();
        return Enumerable.Range(min, max - min + 1).Where(n => !owned.Contains(n)).ToList();
    }
}
