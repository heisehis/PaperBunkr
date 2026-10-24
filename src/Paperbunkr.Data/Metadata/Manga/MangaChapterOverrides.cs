using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>
/// Keeps a chapter's override rows honest after the chapter is edited directly (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md D13, risk in §9).
/// A chapter value that differs from a non-empty series value is a deliberate exception and gets an override row, so the next series save does not overwrite it. A value that
/// matches the series (or a series that has no value) has none. <see cref="Reset"/> is the "Reset to series" action.
/// </summary>
public static class MangaChapterOverrides
{
    /// <summary>
    /// Call after the editor has written the chapter and before it saves. <paramref name="issue"/> needs <see cref="Issue.Tags"/> and <see cref="Issue.FieldOverrides"/> loaded.
    /// Does nothing for a Comic, an unconfirmed series or a remote series. Returns the keys whose override state changed.
    /// </summary>
    public static IReadOnlyList<string> SyncAfterEdit(PaperbunkrDbContext context, Issue issue) =>
        SyncAfterEdit(issue, MangaSeriesInheritance.Load(context, issue.SeriesId));

    /// <summary>Same, for a caller that edits many chapters and has already loaded each series' inheritance once (null = not a confirmed manga series).</summary>
    public static IReadOnlyList<string> SyncAfterEdit(Issue issue, MangaSeriesInheritance? inheritance)
    {
        if (inheritance is null)
        {
            return Array.Empty<string>();
        }

        var changed = new List<string>();
        foreach (var field in MangaInheritedFields.All)
        {
            string seriesValue = MangaInheritedFields.ComparableSeriesValue(inheritance.Values, field);
            string chapterValue = MangaInheritedFields.ComparableIssueValue(issue, field);
            bool shouldOverride = seriesValue.Length > 0 && chapterValue != seriesValue;
            var existing = issue.FieldOverrides.FirstOrDefault(o => o.FieldKey == field.Key);

            if (shouldOverride && existing is null)
            {
                issue.FieldOverrides.Add(new IssueFieldOverride { IssueId = issue.Id, FieldKey = field.Key });
                changed.Add(field.Key);
            }
            else if (!shouldOverride && existing is not null)
            {
                issue.FieldOverrides.Remove(existing);
                changed.Add(field.Key);
            }
        }

        return changed;
    }

    /// <summary>
    /// "Reset to series": removes the override and writes the series value onto this chapter only. The caller saves; credit fields also need
    /// <see cref="CreatorResolver.SyncFromIssue"/> afterwards. Returns false when the series has no value for the field (nothing to reset to).
    /// </summary>
    public static bool Reset(PaperbunkrDbContext context, Issue issue, string key)
    {
        var field = MangaInheritedFields.Find(key);
        var inheritance = MangaSeriesInheritance.Load(context, issue.SeriesId);
        if (field is null || inheritance is null)
        {
            return false;
        }

        var existing = issue.FieldOverrides.FirstOrDefault(o => o.FieldKey == key);
        if (existing is not null)
        {
            issue.FieldOverrides.Remove(existing);
        }

        if (MangaInheritedFields.ComparableSeriesValue(inheritance.Values, field).Length == 0)
        {
            return existing is not null;
        }

        MangaInheritedFields.WriteToIssue(issue, field, inheritance.Values);
        if (inheritance.SyncEnabled)
        {
            issue.EmbeddedDirty = true;
        }

        return true;
    }

    /// <summary>Whether the chapter is currently an exception for <paramref name="key"/>.</summary>
    public static bool IsOverridden(Issue issue, string key) => issue.FieldOverrides.Any(o => o.FieldKey == key);
}
