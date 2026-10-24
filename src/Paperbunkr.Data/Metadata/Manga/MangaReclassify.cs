using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>
/// Manga to Comic safety (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md D8, acceptance K). When a series stops being manga-family, every chapter that has a
/// snapshot gets its pre-inheritance values back, so deliberate per-issue history is never lost to series-level inheritance. Runs inside the same save that changes the type
/// (see <see cref="PaperbunkrDbContext"/>), so the type change and the restore land together or not at all. Comic to Manga does nothing here: nothing is fanned out until the user saves Edit Series.
/// </summary>
public static class MangaReclassify
{
    /// <summary>Series in the change tracker whose content type is leaving the manga family in the save about to run.</summary>
    public static List<int> FindLeavingManga(PaperbunkrDbContext context)
    {
        var ids = new List<int>();
        foreach (var entry in context.ChangeTracker.Entries<Series>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var type = entry.Property(s => s.ContentType);
            if (type.IsModified && MangaInheritedFields.IsMangaFamily(type.OriginalValue) && !MangaInheritedFields.IsMangaFamily(type.CurrentValue))
            {
                ids.Add(entry.Entity.Id);
            }
        }

        return ids;
    }

    /// <summary>Restores the chapters of <paramref name="seriesIds"/> from their snapshots and returns the chapter ids whose credits need their creator index re-derived. Staged on <paramref name="context"/>; the caller's save persists it.</summary>
    public static List<int> StageRestore(PaperbunkrDbContext context, IReadOnlyCollection<int> seriesIds)
    {
        var creditIssueIds = new List<int>();
        if (seriesIds.Count == 0)
        {
            return creditIssueIds;
        }

        var issues = context.Issues.Include(i => i.Tags)
            .Where(i => seriesIds.Contains(i.SeriesId))
            .ToList();
        var snapshots = context.IssueMetadataSnapshots
            .Where(s => issues.Select(i => i.Id).Contains(s.IssueId))
            .ToDictionary(s => s.IssueId, s => s.FieldsJson);

        foreach (var issue in issues)
        {
            if (!snapshots.TryGetValue(issue.Id, out string? json))
            {
                continue;
            }

            string before = Fingerprint(issue);
            MangaIssueSnapshots.Restore(issue, json);
            if (before != Fingerprint(issue))
            {
                creditIssueIds.Add(issue.Id);
                issue.EmbeddedDirty = true;
            }
        }

        return creditIssueIds;
    }

    private static string Fingerprint(Issue issue) =>
        string.Join("¦", MangaInheritedFields.All.Select(f => MangaInheritedFields.ComparableIssueValue(issue, f)));
}
