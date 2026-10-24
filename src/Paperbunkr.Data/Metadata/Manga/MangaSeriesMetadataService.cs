using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>
/// Loads and saves a manga-family series' canonical metadata and passes it down to its chapters (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md
/// D2/D8/D9/D10). The series value is the authority. Saving writes it into every chapter that has no override for that field, in one transaction, so the issue
/// columns that Smart Lists, search, sort and file export read stay correct. Never touches chapter number, title, volume, translator, scan group, language, summary,
/// file information or reading state. Own database context per call, so it is safe off the UI thread.
/// </summary>
public sealed class MangaSeriesMetadataService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    /// <param name="contextFactory">A new context per call (production passes <c>PaperbunkrDb.CreateContext</c> from the App project).</param>
    public MangaSeriesMetadataService(Func<PaperbunkrDbContext> contextFactory) => _contextFactory = contextFactory;

    /// <summary>The series' edit buffer, or null when it does not exist. Once confirmed (<see cref="Series.CanonicalReviewVersion"/> above 0) the values are the series' own; before that they are proposed from the chapters and flagged where the chapters disagree.</summary>
    public MangaSeriesEdit? Load(int seriesId)
    {
        using var context = _contextFactory();
        var series = context.Series
            .Include(s => s.Credits)
            .Include(s => s.SeriesTags)
            .Include(s => s.Titles)
            .AsSplitQuery()
            .FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return null;
        }

        var edit = new MangaSeriesEdit
        {
            SeriesId = seriesId,
            Name = series.Name,
            Summary = series.Summary,
            Status = series.Status,
            ReadingMode = series.ReadingMode,
            Titles = series.Titles.Select(t => new MangaTitleEdit(t.Type, t.Value)).ToList(),
            CanonicalReviewVersion = series.CanonicalReviewVersion,
            LockedFields = MangaFieldLocks.Parse(series.LockedFields),
            IsRemote = series.RemoteSourceId is not null,
            EmbeddedSyncEnabled = series.EmbeddedSyncEnabled,
        };

        if (series.CanonicalReviewVersion > 0)
        {
            edit.Values = MangaInheritedFields.ReadFromSeries(series);
        }
        else
        {
            var issues = context.Issues.Include(i => i.Tags).Where(i => i.SeriesId == seriesId).ToList();
            var (values, candidates) = SeriesCanonicalSeeder.Seed(series, issues);
            edit.Values = values;
            edit.ReviewCandidates = candidates;
        }

        edit.Baseline = edit.Values.Clone();
        edit.BaselineName = edit.Name;
        edit.BaselineSummary = edit.Summary;
        edit.BaselineStatus = edit.Status;
        edit.BaselineReadingMode = edit.ReadingMode;
        edit.BaselineTitlesKey = MangaSeriesEdit.TitlesKey(edit.Titles);
        edit.BaselineEmbeddedSyncEnabled = edit.EmbeddedSyncEnabled;
        return edit;
    }

    /// <summary>The fields on which some chapters already hold a different non-blank value, so the editor can ask Keep or Replace before saving.</summary>
    public IReadOnlyList<MangaFieldException> PreviewExceptions(MangaSeriesEdit edit)
    {
        using var context = _contextFactory();
        var issues = LoadIssues(context, edit.SeriesId);
        var plan = BuildPlan(edit, issues);
        return plan.Exceptions
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => new MangaFieldException(kv.Key, MangaInheritedFields.Find(kv.Key)?.Label ?? kv.Key, kv.Value.Count))
            .ToList();
    }

    /// <summary>
    /// Saves <paramref name="edit"/>. Returns <see cref="MangaSaveResult.Nothing"/> when nothing changed. Everything happens in one transaction; on any failure
    /// nothing is written. The caller enqueues file write-back for the returned issue ids (they are also marked <see cref="Issue.EmbeddedDirty"/> here).
    /// </summary>
    public MangaSaveResult Save(MangaSeriesEdit edit, MangaExceptionChoice choice = MangaExceptionChoice.KeepAsExceptions)
    {
        using var context = _contextFactory();
        var series = context.Series
            .Include(s => s.Credits)
            .Include(s => s.SeriesTags)
            .Include(s => s.Titles)
            .AsSplitQuery()
            .FirstOrDefault(s => s.Id == edit.SeriesId);
        if (series is null || series.RemoteSourceId is not null)
        {
            return MangaSaveResult.Nothing;
        }

        var issues = LoadIssues(context, edit.SeriesId);
        var plan = BuildPlan(edit, issues);

        string newName = string.IsNullOrWhiteSpace(edit.Name) ? series.Name : edit.Name.Trim();
        string? newSummary = MangaInheritedFields.NullIfBlank(edit.Summary);
        bool nameChanged = !string.Equals(newName, edit.BaselineName, StringComparison.Ordinal);
        bool summaryChanged = !string.Equals(newSummary, MangaInheritedFields.NullIfBlank(edit.BaselineSummary), StringComparison.Ordinal);
        bool statusChanged = edit.Status != edit.BaselineStatus;
        bool readingModeChanged = edit.ReadingMode != edit.BaselineReadingMode;
        bool titlesChanged = MangaSeriesEdit.TitlesKey(edit.Titles) != edit.BaselineTitlesKey;
        bool syncSwitchChanged = edit.EmbeddedSyncEnabled != edit.BaselineEmbeddedSyncEnabled;
        bool firstConfirmation = series.CanonicalReviewVersion == 0;
        var lockedKeys = MangaFieldLocks.Parse(series.LockedFields);
        bool unlockChanged = edit.UnlockedKeys.Any(lockedKeys.Contains);

        if (plan.ChangedKeys.Count == 0 && !nameChanged && !summaryChanged && !statusChanged && !readingModeChanged && !titlesChanged && !syncSwitchChanged && !firstConfirmation && !unlockChanged)
        {
            return MangaSaveResult.Nothing;
        }

        using var transaction = context.Database.BeginTransaction();

        var changedIssueIds = new HashSet<int>();
        var creditChangedIssueIds = new HashSet<int>();
        lockedKeys.ExceptWith(edit.UnlockedKeys);

        // The first confirmation stores every field on the series (the values were only proposed until now); later saves store what changed. Only fields the user
        // actually edited become locked against providers.
        MangaInheritedFields.WriteToSeries(series, edit.Values, firstConfirmation ? MangaInheritedFields.All.Select(f => f.Key) : plan.ChangedKeys);
        // A value a provider supplied (an accepted candidate) is recorded as provider-sourced and is not locked, so a later refresh may update it again.
        var provider = edit.AppliedFromProvider;
        if (provider is null)
        {
            foreach (string key in plan.EditedKeys)
            {
                lockedKeys.Add(key);
            }
        }

        if (nameChanged)
        {
            series.Name = newName;
        }

        if (summaryChanged)
        {
            RecordProposal(context, series.Id, MetadataProposalField.Summary, series.Summary, newSummary, provider);
            series.Summary = newSummary;
            if (provider is null)
            {
                lockedKeys.Add(MangaFieldLocks.SummaryKey);
            }
        }

        if (statusChanged)
        {
            RecordProposal(context, series.Id, MetadataProposalField.Status, series.Status.ToString(), edit.Status.ToString(), provider);
            series.Status = edit.Status;
            if (provider is null)
            {
                lockedKeys.Add(MangaFieldLocks.StatusKey);
            }
        }

        // A file records only right-to-left or not, so only a change to that part owes the chapters a write; any of the seven modes can still be chosen.
        bool rightToLeftFlagChanged = false;
        if (readingModeChanged)
        {
            rightToLeftFlagChanged = IsRightToLeft(series.ReadingMode) != IsRightToLeft(edit.ReadingMode);
            RecordProposal(context, series.Id, MetadataProposalField.ReadingMode, series.ReadingMode.ToString(), edit.ReadingMode.ToString(), provider);
            series.ReadingMode = edit.ReadingMode;
        }

        foreach (string key in plan.EditedKeys)
        {
            var proposalField = key switch
            {
                MangaInheritedFields.Publisher => (MetadataProposalField?)MetadataProposalField.Publisher,
                MangaInheritedFields.Imprint => MetadataProposalField.Imprint,
                MangaInheritedFields.AgeRating => MetadataProposalField.AgeRating,
                _ => null,
            };
            if (proposalField is not null && edit.Baseline is not null)
            {
                RecordProposal(context, series.Id, proposalField.Value, edit.Baseline.Text.GetValueOrDefault(key), MangaInheritedFields.NullIfBlank(edit.Values.Text.GetValueOrDefault(key)), provider);
            }
        }

        if (titlesChanged)
        {
            ReplaceTitles(series, edit.Titles);
        }

        series.EmbeddedSyncEnabled = edit.EmbeddedSyncEnabled;
        series.LockedFields = MangaFieldLocks.Join(lockedKeys);
        series.CanonicalReviewVersion = 1;

        // Fan-out. Snapshot a chapter once, before its first write.
        var snapshotted = new HashSet<int>(context.IssueMetadataSnapshots.Where(s => s.Issue!.SeriesId == edit.SeriesId).Select(s => s.IssueId));
        foreach (var issue in issues)
        {
            bool written = false;
            foreach (string key in plan.ChangedKeys)
            {
                var field = MangaInheritedFields.Find(key)!;
                if (issue.FieldOverrides.Any(o => o.FieldKey == key))
                {
                    continue;
                }

                bool isException = plan.Exceptions[key].Contains(issue.Id);
                if (isException && choice == MangaExceptionChoice.KeepAsExceptions)
                {
                    issue.FieldOverrides.Add(new IssueFieldOverride { IssueId = issue.Id, FieldKey = key });
                    continue;
                }

                if (MangaInheritedFields.ComparableIssueValue(issue, field) == MangaInheritedFields.ComparableSeriesValue(edit.Values, field))
                {
                    continue;
                }

                EnsureSnapshot(context, issue, snapshotted);
                MangaInheritedFields.WriteToIssue(issue, field, edit.Values);
                written = true;
                if (field.Kind == MangaFieldKind.Credit)
                {
                    creditChangedIssueIds.Add(issue.Id);
                }
            }

            // Series name and summary reach the exported file too (ComicInfo Series; Summary fallback for a chapter with none).
            bool exportOnly = nameChanged || (rightToLeftFlagChanged) || (summaryChanged && string.IsNullOrWhiteSpace(issue.Summary));
            if (written || exportOnly)
            {
                changedIssueIds.Add(issue.Id);
                if (series.EmbeddedSyncEnabled)
                {
                    issue.EmbeddedDirty = true;
                }
            }
        }

        context.SaveChanges();

        foreach (int issueId in creditChangedIssueIds)
        {
            CreatorResolver.SyncFromIssue(context, issueId);
        }

        transaction.Commit();
        return new MangaSaveResult(true, changedIssueIds.ToList(), plan.ChangedKeys.ToList());
    }

    private static bool IsRightToLeft(ReadingMode mode) => mode is ReadingMode.RightToLeft or ReadingMode.HorizontalContinuousRightToLeft;

    private static List<Issue> LoadIssues(PaperbunkrDbContext context, int seriesId) =>
        context.Issues.Include(i => i.Tags).Include(i => i.FieldOverrides).Where(i => i.SeriesId == seriesId).AsSplitQuery().ToList();

    /// <param name="ChangedKeys">Fields to fan out to chapters.</param>
    /// <param name="EditedKeys">Fields whose value the user changed from what was loaded: these get locked and recorded as manual proposals.</param>
    private sealed record Plan(List<string> ChangedKeys, List<string> EditedKeys, Dictionary<string, HashSet<int>> Exceptions);

    /// <summary>
    /// Which fields the edit changed, and per field which chapters would be exceptions. A field counts as changed when its value differs from the loaded baseline, or on the first
    /// confirmation (version 0), when every field is being confirmed. A chapter is an exception when it holds a non-blank value that differs from the new series value
    /// and also from the value the editor was opened with (so a chapter that merely followed that value is not).
    /// </summary>
    private static Plan BuildPlan(MangaSeriesEdit edit, IReadOnlyList<Issue> issues)
    {
        var changed = new List<string>();
        var edited = new List<string>();
        var exceptions = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        bool first = edit.CanonicalReviewVersion == 0;

        foreach (var field in MangaInheritedFields.All)
        {
            string newValue = MangaInheritedFields.ComparableSeriesValue(edit.Values, field);
            string oldValue = edit.Baseline is null ? string.Empty : MangaInheritedFields.ComparableSeriesValue(edit.Baseline, field);
            bool fieldChanged = first || newValue != oldValue;
            exceptions[field.Key] = new HashSet<int>();
            if (newValue != oldValue)
            {
                edited.Add(field.Key);
            }

            if (!fieldChanged)
            {
                continue;
            }

            changed.Add(field.Key);
            foreach (var issue in issues)
            {
                if (issue.FieldOverrides.Any(o => o.FieldKey == field.Key))
                {
                    continue;
                }

                string current = MangaInheritedFields.ComparableIssueValue(issue, field);
                bool differs = current.Length > 0 && current != newValue;
                // A chapter holding the value the editor was opened with simply follows the series, even on the first confirmation; only one that held something else is an exception.
                bool followedOld = current == oldValue;
                if (differs && !followedOld)
                {
                    exceptions[field.Key].Add(issue.Id);
                }
            }
        }

        // A first confirmation that changes nothing and finds no differing chapter still has nothing to fan out; the caller handles that through ChangedKeys being filtered below.
        if (first)
        {
            changed = changed.Where(key =>
            {
                var field = MangaInheritedFields.Find(key)!;
                string newValue = MangaInheritedFields.ComparableSeriesValue(edit.Values, field);
                return issues.Any(i => !i.FieldOverrides.Any(o => o.FieldKey == key) && MangaInheritedFields.ComparableIssueValue(i, field) != newValue)
                    || edit.Baseline is null
                    || newValue != MangaInheritedFields.ComparableSeriesValue(edit.Baseline, field);
            }).ToList();
        }

        return new Plan(changed, edited, exceptions);
    }

    private static void EnsureSnapshot(PaperbunkrDbContext context, Issue issue, HashSet<int> snapshotted)
    {
        if (snapshotted.Add(issue.Id))
        {
            context.IssueMetadataSnapshots.Add(new IssueMetadataSnapshot { IssueId = issue.Id, FieldsJson = MangaIssueSnapshots.Serialize(issue), TakenUtc = DateTime.UtcNow });
        }
    }

    private static void RecordProposal(PaperbunkrDbContext context, int seriesId, MetadataProposalField field, string? current, string? proposed, ExternalMetadataProvider? provider) =>
        context.MetadataProposals.Add(new MetadataProposal
        {
            SeriesId = seriesId,
            Field = field,
            CurrentValue = current,
            ProposedValue = proposed,
            Source = provider is null ? MetadataProposalSource.Manual : MetadataProposalSource.MetadataProvider,
            ProviderKey = provider,
            Confidence = 1.0m,
            Status = MetadataProposalStatus.Accepted,
            ResolvedAt = DateTime.UtcNow,
        });

    private static void ReplaceTitles(Series series, IReadOnlyList<MangaTitleEdit> wanted)
    {
        foreach (var existing in series.Titles.Where(t => !wanted.Any(w => w.Type == t.Type && string.Equals(w.Value, t.Value, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            series.Titles.Remove(existing);
        }

        foreach (var want in wanted.Where(w => !string.IsNullOrWhiteSpace(w.Value)))
        {
            if (!series.Titles.Any(t => t.Type == want.Type && string.Equals(t.Value, want.Value, StringComparison.OrdinalIgnoreCase)))
            {
                series.Titles.Add(new SeriesTitle { SeriesId = series.Id, Type = want.Type, Value = want.Value.Trim() });
            }
        }
    }
}
