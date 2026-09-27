using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Maintains the derived <see cref="Creator"/> / <see cref="CreatorCredit"/> index over <see cref="Issue"/>'s
/// seven per-role free-text credit fields (docs/superpowers/specs/2026-09-23-metron-api-utilization-
/// design.md). Not a direct mirror of <see cref="CharacterResolver"/>: <see cref="Character"/>/
/// <see cref="Team"/>/<see cref="Location"/> each parse one combined field, but a creator's roles are
/// split across <see cref="Issue.Writer"/>, <see cref="Issue.Penciller"/>, <see cref="Issue.Inker"/>,
/// <see cref="Issue.Colorist"/>, <see cref="Issue.Letterer"/>, <see cref="Issue.CoverArtist"/>,
/// <see cref="Issue.Editor"/>, and <see cref="Issue.Translator"/> - one creator can hold several of
/// these on the same issue, so each (creator, role) pair is its own <see cref="CreatorCredit"/> row.
/// </summary>
public static class CreatorResolver
{
    /// <summary>Every per-issue credit field this resolver reads, paired with the <see cref="CreatorCredit.Role"/> string it writes.</summary>
    private static readonly IReadOnlyList<(string Role, Func<Issue, string?> Field)> RoleFields = new (string, Func<Issue, string?>)[]
    {
        ("Writer", i => i.Writer),
        ("Penciller", i => i.Penciller),
        ("Inker", i => i.Inker),
        ("Colorist", i => i.Colorist),
        ("Letterer", i => i.Letterer),
        ("CoverArtist", i => i.CoverArtist),
        ("Editor", i => i.Editor),
        ("Translator", i => i.Translator),
    };

    public static IReadOnlyList<string> ParseNames(string? creditText) => CharacterResolver.ParseNames(creditText);

    public static Creator GetOrCreate(PaperbunkrDbContext context, string name)
    {
        string trimmed = name.Trim();
        var existing = context.Creators.FirstOrDefault(c => c.Name.ToLower() == trimmed.ToLower());
        if (existing is not null)
        {
            return existing;
        }

        var creator = new Creator { Name = trimmed };
        context.Creators.Add(creator);
        context.SaveChanges();
        return creator;
    }

    /// <summary>
    /// Re-derives one issue's <see cref="CreatorCredit"/> rows from its current per-role credit text,
    /// then prunes any <see cref="Creator"/> left with no credits. One name appearing under two role
    /// fields on the same issue (e.g. writer AND artist) produces two separate rows, not a merge.
    /// </summary>
    public static void SyncFromIssue(PaperbunkrDbContext context, int issueId)
    {
        var issue = context.Issues.FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            return;
        }

        var wanted = RoleFields
            .SelectMany(rf => ParseNames(rf.Field(issue)).Select(name => (Role: rf.Role, Name: name)))
            .ToList();

        var existing = context.CreatorCredits.Include(c => c.Creator).Where(c => c.IssueId == issueId).ToList();

        foreach (var stale in existing.Where(c => !wanted.Any(w =>
                     string.Equals(w.Name, c.Creator!.Name, StringComparison.OrdinalIgnoreCase) && w.Role == c.Role)))
        {
            context.CreatorCredits.Remove(stale);
        }

        foreach (var w in wanted.Where(w => !existing.Any(c =>
                     string.Equals(c.Creator!.Name, w.Name, StringComparison.OrdinalIgnoreCase) && c.Role == w.Role)))
        {
            var creator = GetOrCreate(context, w.Name);
            context.CreatorCredits.Add(new CreatorCredit { CreatorId = creator.Id, IssueId = issueId, Role = w.Role });
        }

        context.SaveChanges();
        PruneOrphans(context);
    }

    /// <summary>Backfills the whole library - idempotent, safe to re-run.</summary>
    public static void RebuildAll(PaperbunkrDbContext context)
    {
        // RoleFields' delegates can't be translated by EF's query provider, so the "any credit field
        // is non-empty" filter is written out plainly here rather than via RoleFields.Any(...).
        var issueIds = context.Issues
            .Where(i =>
                (i.Writer != null && i.Writer != "") ||
                (i.Penciller != null && i.Penciller != "") ||
                (i.Inker != null && i.Inker != "") ||
                (i.Colorist != null && i.Colorist != "") ||
                (i.Letterer != null && i.Letterer != "") ||
                (i.CoverArtist != null && i.CoverArtist != "") ||
                (i.Editor != null && i.Editor != "") ||
                (i.Translator != null && i.Translator != ""))
            .Select(i => i.Id)
            .ToList();

        foreach (int id in issueIds)
        {
            SyncFromIssue(context, id);
        }
    }

    private static void PruneOrphans(PaperbunkrDbContext context)
    {
        var orphans = context.Creators.Where(c => !c.Credits.Any()).ToList();
        if (orphans.Count > 0)
        {
            context.Creators.RemoveRange(orphans);
            context.SaveChanges();
        }
    }

    public static IReadOnlyList<Creator> GetCreatorsForSeries(PaperbunkrDbContext context, int seriesId) =>
        context.CreatorCredits
            .Include(c => c.Creator)
            .Where(c => c.Issue != null && c.Issue.SeriesId == seriesId)
            .Select(c => c.Creator!)
            .Distinct()
            .OrderBy(c => c.Name)
            .ToList();

    public static IReadOnlyList<Series> GetSeriesForCreator(PaperbunkrDbContext context, int creatorId) =>
        context.CreatorCredits
            .Include(c => c.Issue).ThenInclude(i => i!.Series)
            .Where(c => c.CreatorId == creatorId && c.Issue != null)
            .Select(c => c.Issue!.Series!)
            .Distinct()
            .OrderBy(s => s.Name)
            .ToList();
}
