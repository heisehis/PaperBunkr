using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The persistent Needs Review queue (docs/onboarding.md §14 step 5,
/// docs/superpowers/specs/2026-08-06-migration-ux-design.md §C). Three live sections - Content
/// Type and Missing Files are derived queries (nothing to persist; an item drops off once the
/// underlying data changes), Series Conflicts is the one section backed by a stored
/// <see cref="SeriesConflict"/> row, since "is this the same series?" isn't a field predicate the
/// other two sections' live-query approach can express.
/// </summary>
public partial class NeedsReviewViewModel : ViewModelBase
{
    private readonly Action<int> _onOpenSeriesDetail;
    private readonly IFilePickerService _filePicker;

    public NeedsReviewViewModel(Action<int> onOpenSeriesDetail, IFilePickerService filePicker)
    {
        _onOpenSeriesDetail = onOpenSeriesDetail;
        _filePicker = filePicker;
        ContentTypeItems = new ObservableCollection<SeriesReviewItem>();
        MissingFileItems = new ObservableCollection<MissingFileRowViewModel>();
        SeriesConflicts = new ObservableCollection<SeriesConflictRowViewModel>();
        Refresh();
    }

    public ObservableCollection<SeriesReviewItem> ContentTypeItems { get; }

    public ObservableCollection<MissingFileRowViewModel> MissingFileItems { get; }

    public ObservableCollection<SeriesConflictRowViewModel> SeriesConflicts { get; }

    public bool HasContentTypeItems => ContentTypeItems.Count > 0;

    public bool HasMissingFileItems => MissingFileItems.Count > 0;

    public bool HasSeriesConflictItems => SeriesConflicts.Count > 0;

    public bool HasPendingItems => HasContentTypeItems || HasMissingFileItems || HasSeriesConflictItems;

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(HasContentTypeItems));
        OnPropertyChanged(nameof(HasMissingFileItems));
        OnPropertyChanged(nameof(HasSeriesConflictItems));
        OnPropertyChanged(nameof(HasPendingItems));
    }

    [RelayCommand]
    private void OpenSeriesDetail(SeriesReviewItem? item)
    {
        if (item is not null)
        {
            _onOpenSeriesDetail(item.SeriesId);
        }
    }

    [RelayCommand]
    private void KeepAllSeparate() => SeriesConflictRowViewModel.ApplyBulkAction(SeriesConflicts, ConflictBulkAction.KeepAllSeparate);

    [RelayCommand]
    private void MergeAllAboveNinety() => SeriesConflictRowViewModel.ApplyBulkAction(SeriesConflicts, ConflictBulkAction.MergeAboveNinetyPercent);

    public void Refresh()
    {
        using var context = PaperbunkrDb.CreateContext();

        RefreshContentTypeItems(context);
        RefreshMissingFileItems(context);
        RefreshSeriesConflicts(context);
        NotifyCountsChanged();
    }

    private void RefreshContentTypeItems(PaperbunkrDbContext context)
    {
        // Reuses the existing SmartList field/query machinery (same mechanism the "Missing Files"
        // system smart list uses for IsMissing) rather than a bespoke Series query.
        var transient = new SmartList
        {
            Conditions = new List<SmartListCondition>
            {
                new() { Field = SmartListField.ContentType, Operator = SmartListOperator.Is, Value = ContentType.Unknown.ToString() },
            },
        };

        var seriesNeedingReview = SmartListQueryBuilder.Build(context, transient)
            .Select(i => i.Series)
            .Where(s => s is not null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .OrderBy(s => s.Name)
            .ToList();

        ContentTypeItems.Clear();
        foreach (var series in seriesNeedingReview)
        {
            ContentTypeItems.Add(new SeriesReviewItem { SeriesId = series.Id, SeriesName = series.Name });
        }
    }

    private void RefreshMissingFileItems(PaperbunkrDbContext context)
    {
        var missingFilesList = context.SmartLists.Include(s => s.Conditions)
            .FirstOrDefault(s => s.IsSystem && s.Name == "Missing Files");

        MissingFileItems.Clear();
        if (missingFilesList is null)
        {
            return;
        }

        // MissingAcknowledged is a review-queue concept, not a library-wide filter - the system
        // smart list itself is untouched, so browsing it directly still shows every missing file.
        var issues = SmartListQueryBuilder.Build(context, missingFilesList)
            .Where(i => !i.MissingAcknowledged)
            .OrderBy(i => i.Series?.Name);

        foreach (var issue in issues)
        {
            int issueId = issue.Id;
            MissingFileItems.Add(new MissingFileRowViewModel(
                issueId,
                $"{issue.Series?.Name ?? "Unknown"} #{issue.Number}",
                onRelink: RelinkMissingFile,
                onRemove: _ => RemoveMissingFile(issueId),
                onDismiss: _ => DismissMissingFile(issueId)));
        }
    }

    private async Task RelinkMissingFile(MissingFileRowViewModel row)
    {
        string? path = await _filePicker.PickOpenFileAsync("Locate the file", "cbz", "Comic Archive");
        if (path is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(row.IssueId);
        if (issue is not null)
        {
            issue.FilePath = path;
            issue.FileIsMissing = false;
            issue.IsPlaceholder = false;
            context.SaveChanges();
        }

        Refresh();
    }

    private void RemoveMissingFile(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(issueId);
        if (issue is not null)
        {
            context.Issues.Remove(issue);
            context.SaveChanges();
        }

        Refresh();
    }

    private void DismissMissingFile(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(issueId);
        if (issue is not null)
        {
            issue.MissingAcknowledged = true;
            context.SaveChanges();
        }

        Refresh();
    }

    private void RefreshSeriesConflicts(PaperbunkrDbContext outerContext)
    {
        SeriesConflicts.Clear();

        var pending = outerContext.SeriesConflicts
            .Where(c => c.Status == SeriesConflictStatus.Pending)
            .OrderByDescending(c => c.DetectedAt)
            .ToList();

        foreach (var conflict in pending)
        {
            int conflictId = conflict.Id;
            SeriesConflicts.Add(new SeriesConflictRowViewModel(
                conflict.IncomingName,
                conflict.MatchedName,
                conflict.Similarity,
                onMerge: _ => ResolveConflict(conflictId, merge: true),
                onKeepSeparate: _ => ResolveConflict(conflictId, merge: false)));
        }
    }

    private void ResolveConflict(int conflictId, bool merge)
    {
        using var context = PaperbunkrDb.CreateContext();
        var conflict = context.SeriesConflicts
            .Include(c => c.ExistingSeries).ThenInclude(s => s!.Issues)
            .Include(c => c.SeriesA).ThenInclude(s => s!.Issues)
            .Include(c => c.SeriesB).ThenInclude(s => s!.Issues)
            .FirstOrDefault(c => c.Id == conflictId);
        if (conflict is null)
        {
            return;
        }

        if (!merge)
        {
            conflict.Status = SeriesConflictStatus.KeptSeparate;
            context.SaveChanges();
            return;
        }

        // Against-existing conflict: fold the newly-created SeriesA into the pre-existing series.
        // Intra-import conflict: fold SeriesB into SeriesA (arbitrary but consistent choice).
        Series? target = conflict.ExistingSeries ?? conflict.SeriesA;
        Series? source = conflict.ExistingSeries is not null ? conflict.SeriesA : conflict.SeriesB;

        if (target is not null && source is not null && target.Id != source.Id)
        {
            MergeSeriesInto(context, source, target);
        }

        conflict.Status = SeriesConflictStatus.Merged;
        context.SaveChanges();
    }

    /// <summary>
    /// Moves every issue from <paramref name="source"/> into <paramref name="target"/> - deduped
    /// by (Number, Volume), the same rule <see cref="Paperbunkr.Data.CeMigration.CeLibraryMigrator"/>'s
    /// idempotent commit path uses - and removes the now-empty source series.
    /// </summary>
    private static void MergeSeriesInto(PaperbunkrDbContext context, Series source, Series target)
    {
        var existingKeys = new HashSet<(string?, int?)>(target.Issues.Select(i => (i.Number, i.Volume)));

        foreach (var issue in source.Issues.ToList())
        {
            if (existingKeys.Add((issue.Number, issue.Volume)))
            {
                issue.SeriesId = target.Id;
                issue.Series = target;
            }
            else
            {
                context.Issues.Remove(issue);
            }
        }

        context.Series.Remove(source);
    }
}
