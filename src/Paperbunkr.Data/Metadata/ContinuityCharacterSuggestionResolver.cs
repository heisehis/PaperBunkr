using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// A series that is not in a continuity but shares several characters with one that is.
/// </summary>
/// <param name="SharedCharacters">Up to three of the shared character names, most widely shared first.</param>
/// <param name="WithSeriesName">The member series it shares the most of those characters with.</param>
public sealed record ContinuityCharacterSuggestion(
    int ContinuityId, string ContinuityName, int SeriesId, string SeriesName, IReadOnlyList<string> SharedCharacters, int SharedCount, string WithSeriesName)
{
    /// <summary>"Shares Spider-Man, Venom, Carnage with Amazing Spider-Man" (+ "and 2 more").</summary>
    public string Reason
    {
        get
        {
            string names = string.Join(", ", SharedCharacters);
            string more = SharedCount > SharedCharacters.Count ? $" and {SharedCount - SharedCharacters.Count} more" : string.Empty;
            return $"Shares {names}{more} with {WithSeriesName}";
        }
    }

    public string DismissalKey => $"{ContinuityId}:{SeriesId}";
}

/// <summary>
/// Suggests series for existing continuities from the characters they share with member series, and tells an add-to-continuity
/// picker which other continuities a series is already in (docs/superpowers/specs/2026-10-06-smart-features-design.md §7.1). Local
/// only: it reads <see cref="CharacterAppearance"/> rows the library already has. "Propose, don't assert" - nothing is added until the
/// user accepts a suggestion.
/// </summary>
public static class ContinuityCharacterSuggestionResolver
{
    /// <summary>One or two shared characters is a cameo, not a shared continuity.</summary>
    public const int MinSharedCharacters = 3;

    public const int PerContinuityLimit = 5;

    public const int TotalLimit = 40;

    public static IReadOnlyList<ContinuityCharacterSuggestion> GetSuggestions(PaperbunkrDbContext context)
    {
        var memberships = context.ContinuityMemberships.Select(m => new { m.ContinuityId, m.SeriesId }).ToList();
        if (memberships.Count == 0)
        {
            return [];
        }

        var appearances = context.CharacterAppearances
            .Where(a => a.Issue != null)
            .Select(a => new { a.CharacterId, a.Issue!.SeriesId })
            .Distinct()
            .ToList();
        if (appearances.Count == 0)
        {
            return [];
        }

        var dismissed = HealthDismissals.KeysFor(context, HealthDismissals.ContinuitySuggestion);
        var continuityNames = context.Continuities.Select(c => new { c.Id, c.Name }).ToDictionary(c => c.Id, c => c.Name);
        var seriesNames = context.Series.Select(s => new { s.Id, s.Name }).ToDictionary(s => s.Id, s => s.Name);
        var characterNames = context.Characters.Select(c => new { c.Id, c.Name }).ToDictionary(c => c.Id, c => c.Name);

        var charactersBySeries = appearances.GroupBy(a => a.SeriesId).ToDictionary(g => g.Key, g => g.Select(a => a.CharacterId).ToHashSet());

        var result = new List<ContinuityCharacterSuggestion>();
        foreach (var continuity in memberships.GroupBy(m => m.ContinuityId))
        {
            if (!continuityNames.TryGetValue(continuity.Key, out var continuityName))
            {
                continue;
            }

            var members = continuity.Select(m => m.SeriesId).ToHashSet();

            // Every character of the continuity, with the member series it appears in.
            var memberSeriesByCharacter = new Dictionary<int, List<int>>();
            foreach (int member in members)
            {
                if (!charactersBySeries.TryGetValue(member, out var characters))
                {
                    continue;
                }

                foreach (int character in characters)
                {
                    if (!memberSeriesByCharacter.TryGetValue(character, out var list))
                    {
                        memberSeriesByCharacter[character] = list = [];
                    }

                    list.Add(member);
                }
            }

            if (memberSeriesByCharacter.Count < MinSharedCharacters)
            {
                continue;
            }

            var forContinuity = new List<ContinuityCharacterSuggestion>();
            foreach (var (seriesId, characters) in charactersBySeries)
            {
                if (members.Contains(seriesId) || !seriesNames.TryGetValue(seriesId, out var seriesName) || dismissed.Contains($"{continuity.Key}:{seriesId}"))
                {
                    continue;
                }

                var shared = characters.Where(memberSeriesByCharacter.ContainsKey).ToList();
                if (shared.Count < MinSharedCharacters)
                {
                    continue;
                }

                // The member series that carries the most of the shared characters names the suggestion.
                int with = shared.SelectMany(c => memberSeriesByCharacter[c]).GroupBy(id => id).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
                var names = shared
                    .OrderByDescending(c => memberSeriesByCharacter[c].Count)
                    .ThenBy(c => characterNames.GetValueOrDefault(c, string.Empty), StringComparer.OrdinalIgnoreCase)
                    .Select(c => characterNames.GetValueOrDefault(c, string.Empty))
                    .Where(n => n.Length > 0)
                    .Take(3)
                    .ToList();

                forContinuity.Add(new ContinuityCharacterSuggestion(
                    continuity.Key, continuityName, seriesId, seriesName, names, shared.Count, seriesNames.GetValueOrDefault(with, "a member series")));
            }

            result.AddRange(forContinuity
                .OrderByDescending(s => s.SharedCount)
                .ThenBy(s => s.SeriesName, StringComparer.OrdinalIgnoreCase)
                .Take(PerContinuityLimit));
        }

        return result
            .OrderByDescending(s => s.SharedCount)
            .ThenBy(s => s.ContinuityName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.SeriesName, StringComparer.OrdinalIgnoreCase)
            .Take(TotalLimit)
            .ToList();
    }

    /// <summary>
    /// For an add-to-continuity picker: for each of <paramref name="seriesIds"/> that is in other continuities (not
    /// <paramref name="excludingContinuityId"/>), the line to show under it - "Also in: X, Y".
    /// </summary>
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
