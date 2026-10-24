namespace Paperbunkr.Data.Metadata;

/// <summary>
/// For an add-to-continuity picker: which other continuities a series is already in. (This used to share a file with the shared-character
/// suggestions, dropped 2026-10-12: character names carry no universe, so overlap in them says "same franchise", not "same continuity"
/// - docs/superpowers/specs/2026-10-12-continuity-sources-design.md §3.)
/// </summary>
public static class ContinuityMembershipLines
{
    /// <summary>For each of <paramref name="seriesIds"/> that is in other continuities (not <paramref name="excludingContinuityId"/>), the line to show under it - "Also in: X, Y".</summary>
    public static Dictionary<int, string> OtherContinuityLines(PaperbunkrDbContext context, IReadOnlyCollection<int> seriesIds, int? excludingContinuityId)
    {
        if (seriesIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        return context.ContinuityMemberships
            .Where(m => seriesIds.Contains(m.SeriesId) && m.ContinuityId != excludingContinuityId)
            .Select(m => new { m.SeriesId, m.Continuity.Name })
            .ToList()
            .GroupBy(m => m.SeriesId)
            .ToDictionary(
                g => g.Key,
                g => "Also in: " + string.Join(", ", g.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));
    }
}
