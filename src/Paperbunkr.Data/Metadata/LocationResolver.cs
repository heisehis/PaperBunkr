using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Maintains the derived <see cref="Location"/> / <see cref="LocationAppearance"/> index over the
/// free-text <see cref="Issue.Locations"/> ComicInfo field (docs/superpowers/specs/2026-09-23-metron-
/// api-utilization-design.md) - exact mirror of <see cref="CharacterResolver"/>. Metron has no
/// location data at all (confirmed - no such REST resource), so any <see cref="ComicMetadataExternalId"/>
/// rows attached to entities this resolver creates will only ever come from ComicVine.
/// </summary>
public static class LocationResolver
{
    public static IReadOnlyList<string> ParseNames(string? locationsText) => CharacterResolver.ParseNames(locationsText);

    public static Location GetOrCreate(PaperbunkrDbContext context, string name)
    {
        string trimmed = name.Trim();
        var existing = context.Locations.FirstOrDefault(l => l.Name.ToLower() == trimmed.ToLower());
        if (existing is not null)
        {
            return existing;
        }

        var location = new Location { Name = trimmed };
        context.Locations.Add(location);
        context.SaveChanges();
        return location;
    }

    /// <summary>Re-derives one issue's <see cref="LocationAppearance"/> rows from its current <see cref="Issue.Locations"/> text, then prunes any <see cref="Location"/> left with no appearances.</summary>
    public static void SyncFromIssue(PaperbunkrDbContext context, int issueId)
    {
        var issue = context.Issues.FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            return;
        }

        var wanted = ParseNames(issue.Locations);
        var existing = context.LocationAppearances.Include(a => a.Location).Where(a => a.IssueId == issueId).ToList();

        foreach (var stale in existing.Where(a => !wanted.Contains(a.Location!.Name, StringComparer.OrdinalIgnoreCase)))
        {
            context.LocationAppearances.Remove(stale);
        }

        foreach (var name in wanted.Where(n => !existing.Any(a => string.Equals(a.Location!.Name, n, StringComparison.OrdinalIgnoreCase))))
        {
            var location = GetOrCreate(context, name);
            context.LocationAppearances.Add(new LocationAppearance { LocationId = location.Id, IssueId = issueId });
        }

        context.SaveChanges();
        PruneOrphans(context);
    }

    /// <summary>Backfills the whole library - idempotent, safe to re-run.</summary>
    public static void RebuildAll(PaperbunkrDbContext context)
    {
        var issueIds = context.Issues
            .Where(i => i.Locations != null && i.Locations != "")
            .Select(i => i.Id)
            .ToList();

        foreach (int id in issueIds)
        {
            SyncFromIssue(context, id);
        }
    }

    private static void PruneOrphans(PaperbunkrDbContext context)
    {
        var orphans = context.Locations.Where(l => !l.Appearances.Any()).ToList();
        if (orphans.Count > 0)
        {
            context.Locations.RemoveRange(orphans);
            context.SaveChanges();
        }
    }

    public static IReadOnlyList<Location> GetLocationsForSeries(PaperbunkrDbContext context, int seriesId) =>
        context.LocationAppearances
            .Include(a => a.Location)
            .Where(a => a.Issue != null && a.Issue.SeriesId == seriesId)
            .Select(a => a.Location!)
            .Distinct()
            .OrderBy(l => l.Name)
            .ToList();

    public static IReadOnlyList<Series> GetSeriesForLocation(PaperbunkrDbContext context, int locationId) =>
        context.LocationAppearances
            .Include(a => a.Issue).ThenInclude(i => i!.Series)
            .Where(a => a.LocationId == locationId && a.Issue != null)
            .Select(a => a.Issue!.Series!)
            .Distinct()
            .OrderBy(s => s.Name)
            .ToList();
}
