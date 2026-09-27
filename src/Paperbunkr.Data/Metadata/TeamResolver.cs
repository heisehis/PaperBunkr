using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Maintains the derived <see cref="Team"/> / <see cref="TeamAppearance"/> index over the free-text
/// <see cref="Issue.Teams"/> ComicInfo field (docs/superpowers/specs/2026-09-23-metron-api-utilization-
/// design.md) - exact mirror of <see cref="CharacterResolver"/>. The string field stays the editable
/// source of truth - <see cref="SyncFromIssue"/> re-derives an issue's appearance rows after any edit
/// or scrape, and <see cref="RebuildAll"/> backfills the whole library once.
/// </summary>
public static class TeamResolver
{
    public static IReadOnlyList<string> ParseNames(string? teamsText) => CharacterResolver.ParseNames(teamsText);

    public static Team GetOrCreate(PaperbunkrDbContext context, string name)
    {
        string trimmed = name.Trim();
        var existing = context.Teams.FirstOrDefault(t => t.Name.ToLower() == trimmed.ToLower());
        if (existing is not null)
        {
            return existing;
        }

        var team = new Team { Name = trimmed };
        context.Teams.Add(team);
        context.SaveChanges();
        return team;
    }

    /// <summary>Re-derives one issue's <see cref="TeamAppearance"/> rows from its current <see cref="Issue.Teams"/> text, then prunes any <see cref="Team"/> left with no appearances.</summary>
    public static void SyncFromIssue(PaperbunkrDbContext context, int issueId)
    {
        var issue = context.Issues.FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            return;
        }

        var wanted = ParseNames(issue.Teams);
        var existing = context.TeamAppearances.Include(a => a.Team).Where(a => a.IssueId == issueId).ToList();

        foreach (var stale in existing.Where(a => !wanted.Contains(a.Team!.Name, StringComparer.OrdinalIgnoreCase)))
        {
            context.TeamAppearances.Remove(stale);
        }

        foreach (var name in wanted.Where(n => !existing.Any(a => string.Equals(a.Team!.Name, n, StringComparison.OrdinalIgnoreCase))))
        {
            var team = GetOrCreate(context, name);
            context.TeamAppearances.Add(new TeamAppearance { TeamId = team.Id, IssueId = issueId });
        }

        context.SaveChanges();
        PruneOrphans(context);
    }

    /// <summary>Backfills the whole library - idempotent, safe to re-run.</summary>
    public static void RebuildAll(PaperbunkrDbContext context)
    {
        var issueIds = context.Issues
            .Where(i => i.Teams != null && i.Teams != "")
            .Select(i => i.Id)
            .ToList();

        foreach (int id in issueIds)
        {
            SyncFromIssue(context, id);
        }
    }

    private static void PruneOrphans(PaperbunkrDbContext context)
    {
        var orphans = context.Teams.Where(t => !t.Appearances.Any()).ToList();
        if (orphans.Count > 0)
        {
            context.Teams.RemoveRange(orphans);
            context.SaveChanges();
        }
    }

    public static IReadOnlyList<Team> GetTeamsForSeries(PaperbunkrDbContext context, int seriesId) =>
        context.TeamAppearances
            .Include(a => a.Team)
            .Where(a => a.Issue != null && a.Issue.SeriesId == seriesId)
            .Select(a => a.Team!)
            .Distinct()
            .OrderBy(t => t.Name)
            .ToList();

    public static IReadOnlyList<Series> GetSeriesForTeam(PaperbunkrDbContext context, int teamId) =>
        context.TeamAppearances
            .Include(a => a.Issue).ThenInclude(i => i!.Series)
            .Where(a => a.TeamId == teamId && a.Issue != null)
            .Select(a => a.Issue!.Series!)
            .Distinct()
            .OrderBy(s => s.Name)
            .ToList();
}
