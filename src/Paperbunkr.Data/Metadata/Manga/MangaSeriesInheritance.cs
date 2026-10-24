using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>
/// A confirmed manga series' canonical values, ready to be written onto a chapter (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §5). Used where a chapter
/// is created or re-read from its file, so old embedded ComicInfo can never override what the user confirmed on the series. Load returns null for a series that is not
/// manga-family, is not confirmed yet, or is a mirror of another library, and the caller then does nothing, so Comics are never touched.
/// </summary>
public sealed class MangaSeriesInheritance
{
    private MangaSeriesInheritance(int seriesId, MangaFieldValues values, bool syncEnabled)
    {
        SeriesId = seriesId;
        Values = values;
        SyncEnabled = syncEnabled;
    }

    public int SeriesId { get; }

    public MangaFieldValues Values { get; }

    public bool SyncEnabled { get; }

    public static MangaSeriesInheritance? Load(PaperbunkrDbContext context, int seriesId)
    {
        var series = context.Series.AsNoTracking()
            .Include(s => s.Credits)
            .Include(s => s.SeriesTags)
            .AsSplitQuery()
            .FirstOrDefault(s => s.Id == seriesId);
        if (series is null || series.CanonicalReviewVersion == 0 || series.RemoteSourceId is not null || !MangaInheritedFields.IsMangaFamily(series.ContentType))
        {
            return null;
        }

        return new MangaSeriesInheritance(seriesId, MangaInheritedFields.ReadFromSeries(series), series.EmbeddedSyncEnabled);
    }

    /// <summary>
    /// Writes the series value onto <paramref name="issue"/> for every inheritable field that is not overridden and where the series actually has a value (a blank series value
    /// never blanks a chapter). <see cref="Issue.Tags"/> must be loaded for an existing chapter. Returns the keys that changed; marks the chapter
    /// <see cref="Issue.EmbeddedDirty"/> when anything did and sync is on. The caller saves.
    /// </summary>
    public IReadOnlyList<string> Apply(Issue issue, ISet<string>? overriddenKeys = null)
    {
        var changed = new List<string>();
        foreach (var field in MangaInheritedFields.All)
        {
            if (overriddenKeys is not null && overriddenKeys.Contains(field.Key))
            {
                continue;
            }

            string seriesValue = MangaInheritedFields.ComparableSeriesValue(Values, field);
            if (seriesValue.Length == 0 || seriesValue == MangaInheritedFields.ComparableIssueValue(issue, field))
            {
                continue;
            }

            MangaInheritedFields.WriteToIssue(issue, field, Values);
            changed.Add(field.Key);
        }

        if (changed.Count > 0 && SyncEnabled)
        {
            issue.EmbeddedDirty = true;
        }

        return changed;
    }

    /// <summary>The override keys for the given chapters, so a batch can apply inheritance without a query per chapter.</summary>
    public static Dictionary<int, HashSet<string>> LoadOverrides(PaperbunkrDbContext context, IEnumerable<int> issueIds)
    {
        var ids = issueIds.ToList();
        return context.IssueFieldOverrides.AsNoTracking()
            .Where(o => ids.Contains(o.IssueId))
            .AsEnumerable()
            .GroupBy(o => o.IssueId)
            .ToDictionary(g => g.Key, g => g.Select(o => o.FieldKey).ToHashSet(StringComparer.Ordinal));
    }
}
