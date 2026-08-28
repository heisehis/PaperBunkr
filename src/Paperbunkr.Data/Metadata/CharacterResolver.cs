using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Maintains the derived <see cref="Character"/> / <see cref="CharacterAppearance"/> index over the
/// free-text <see cref="Issue.Characters"/> ComicInfo field (docs/superpowers/specs/2026-08-27-
/// metadata-model-phase4g-age-progression-design.md). The string field stays the editable source of
/// truth - <see cref="SyncFromIssue"/> re-derives an issue's appearance rows after any edit, and
/// <see cref="RebuildAll"/> backfills the whole library once.
/// </summary>
public static class CharacterResolver
{
    private static readonly char[] Separators = { ',', ';' };

    /// <summary>Splits a CE <c>Characters</c> string ("Batman, Robin; Nightwing") into deduplicated trimmed names.</summary>
    public static IReadOnlyList<string> ParseNames(string? charactersText) =>
        (charactersText ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static Character GetOrCreate(PaperbunkrDbContext context, string name)
    {
        string trimmed = name.Trim();
        var existing = context.Characters.FirstOrDefault(c => c.Name.ToLower() == trimmed.ToLower());
        if (existing is not null)
        {
            return existing;
        }

        var character = new Character { Name = trimmed };
        context.Characters.Add(character);
        context.SaveChanges();
        return character;
    }

    /// <summary>Re-derives one issue's <see cref="CharacterAppearance"/> rows from its current <see cref="Issue.Characters"/> text, then prunes any <see cref="Character"/> left with no appearances.</summary>
    public static void SyncFromIssue(PaperbunkrDbContext context, int issueId)
    {
        var issue = context.Issues.FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            return;
        }

        var wanted = ParseNames(issue.Characters);
        var existing = context.CharacterAppearances.Include(a => a.Character).Where(a => a.IssueId == issueId).ToList();

        foreach (var stale in existing.Where(a => !wanted.Contains(a.Character!.Name, StringComparer.OrdinalIgnoreCase)))
        {
            context.CharacterAppearances.Remove(stale);
        }

        foreach (var name in wanted.Where(n => !existing.Any(a => string.Equals(a.Character!.Name, n, StringComparison.OrdinalIgnoreCase))))
        {
            var character = GetOrCreate(context, name);
            context.CharacterAppearances.Add(new CharacterAppearance { CharacterId = character.Id, IssueId = issueId });
        }

        context.SaveChanges();
        PruneOrphans(context);
    }

    /// <summary>Backfills the whole library - idempotent, safe to re-run.</summary>
    public static void RebuildAll(PaperbunkrDbContext context)
    {
        var issueIds = context.Issues
            .Where(i => i.Characters != null && i.Characters != "")
            .Select(i => i.Id)
            .ToList();

        foreach (int id in issueIds)
        {
            SyncFromIssue(context, id);
        }
    }

    private static void PruneOrphans(PaperbunkrDbContext context)
    {
        var orphans = context.Characters.Where(c => !c.Appearances.Any()).ToList();
        if (orphans.Count > 0)
        {
            context.Characters.RemoveRange(orphans);
            context.SaveChanges();
        }
    }

    public static IReadOnlyList<Character> GetCharactersForSeries(PaperbunkrDbContext context, int seriesId) =>
        context.CharacterAppearances
            .Include(a => a.Character)
            .Where(a => a.Issue != null && a.Issue.SeriesId == seriesId)
            .Select(a => a.Character!)
            .Distinct()
            .OrderBy(c => c.Name)
            .ToList();

    public static IReadOnlyList<Series> GetSeriesForCharacter(PaperbunkrDbContext context, int characterId) =>
        context.CharacterAppearances
            .Include(a => a.Issue).ThenInclude(i => i!.Series)
            .Where(a => a.CharacterId == characterId && a.Issue != null)
            .Select(a => a.Issue!.Series!)
            .Distinct()
            .OrderBy(s => s.Name)
            .ToList();

    /// <summary>
    /// Series ids (excluding the input set) whose issues feature at least one character that also
    /// appears somewhere in <paramref name="seriesIds"/>. Deliberately a single one-hop expansion,
    /// not a transitive closure - a ubiquitous character ("Spider-Man") would otherwise pull in
    /// most of a publisher. Used by <see cref="SeriesFamilyResolver.GetFamily"/> in character-aware
    /// mode.
    /// </summary>
    public static IReadOnlyList<int> GetSeriesIdsSharingCharacterWith(PaperbunkrDbContext context, IReadOnlyCollection<int> seriesIds)
    {
        var characterIds = context.CharacterAppearances
            .Where(a => a.Issue != null && seriesIds.Contains(a.Issue.SeriesId))
            .Select(a => a.CharacterId)
            .Distinct()
            .ToList();

        if (characterIds.Count == 0)
        {
            return Array.Empty<int>();
        }

        return context.CharacterAppearances
            .Where(a => characterIds.Contains(a.CharacterId) && a.Issue != null && !seriesIds.Contains(a.Issue.SeriesId))
            .Select(a => a.Issue!.SeriesId)
            .Distinct()
            .ToList();
    }
}
