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
using Paperbunkr.Data.Metadata;
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
        MetadataProposalItems = new ObservableCollection<MetadataProposalRowViewModel>();
        Refresh();
    }

    public ObservableCollection<SeriesReviewItem> ContentTypeItems { get; }

    public ObservableCollection<MissingFileRowViewModel> MissingFileItems { get; }

    public ObservableCollection<SeriesConflictRowViewModel> SeriesConflicts { get; }

    public ObservableCollection<MetadataProposalRowViewModel> MetadataProposalItems { get; }

    public bool HasContentTypeItems => ContentTypeItems.Count > 0;

    public bool HasMissingFileItems => MissingFileItems.Count > 0;

    public bool HasSeriesConflictItems => SeriesConflicts.Count > 0;

    public bool HasMetadataProposalItems => MetadataProposalItems.Count > 0;

    public bool HasPendingItems => HasContentTypeItems || HasMissingFileItems || HasSeriesConflictItems || HasMetadataProposalItems;

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(HasContentTypeItems));
        OnPropertyChanged(nameof(HasMissingFileItems));
        OnPropertyChanged(nameof(HasSeriesConflictItems));
        OnPropertyChanged(nameof(HasMetadataProposalItems));
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
        RefreshMetadataProposalItems(context);
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
                $"{issue.Series?.Name ?? "Unknown"} #{issue.EffectiveNumber()}",
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
            LibraryDeletionHelper.RemoveIssue(context, issue);
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
            .Include(c => c.ExistingSeries).ThenInclude(s => s!.Issues).ThenInclude(i => i.MetadataProposals)
            .Include(c => c.SeriesA).ThenInclude(s => s!.Issues).ThenInclude(i => i.MetadataProposals)
            .Include(c => c.SeriesB).ThenInclude(s => s!.Issues).ThenInclude(i => i.MetadataProposals)
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
    /// Lists <see cref="MetadataProposalStatus.Pending"/> AND <see cref="MetadataProposalStatus.Accepted"/>
    /// rows - unlike the other three sections, an Accepted proposal isn't "resolved" the way a
    /// merged/kept-separate conflict is; it's "applied but still auditable/correctable" under the
    /// default Automatic policy (docs/superpowers/specs/2026-08-17-metadata-model-phase2a-metadata-
    /// proposals-design.md). Rejected/Ignored rows drop off, same as a resolved conflict does.
    /// </summary>
    private void RefreshMetadataProposalItems(PaperbunkrDbContext context)
    {
        MetadataProposalItems.Clear();

        var proposals = context.MetadataProposals
            .Include(p => p.Issue).ThenInclude(i => i!.Series)
            .Include(p => p.Issue).ThenInclude(i => i!.MetadataProposals)
            .Include(p => p.Series)
            .Where(p => p.Status == MetadataProposalStatus.Pending || p.Status == MetadataProposalStatus.Accepted)
            .OrderByDescending(p => p.CreatedAt)
            .ToList();

        foreach (var proposal in proposals)
        {
            int proposalId = proposal.Id;
            // Series-scoped rows (Summary/Status/Genre, docs/superpowers/specs/2026-08-23-apply-
            // from-provider-design.md) have no issue to name - just the series itself.
            string label = proposal.SeriesId is not null
                ? proposal.Series?.Name ?? "Unknown"
                : $"{proposal.Issue?.Series?.Name ?? "Unknown"} #{proposal.Issue?.EffectiveNumber() ?? "?"}";
            MetadataProposalItems.Add(new MetadataProposalRowViewModel(
                label,
                proposal.Field.ToString(),
                proposal.CurrentValue,
                proposal.ProposedValue,
                proposal.Source.ToString(),
                isAlreadyAccepted: proposal.Status == MetadataProposalStatus.Accepted,
                onAccept: _ => ResolveProposal(proposalId, accept: true),
                onReject: _ => ResolveProposal(proposalId, accept: false)));
        }
    }

    /// <summary>
    /// Deliberately doesn't call <see cref="Refresh"/> - same as <see cref="ResolveConflict"/>, the
    /// row's own <c>IsResolved</c>/<c>ResolutionLabel</c> (driven by its Accept/Reject command)
    /// gives immediate in-place feedback without re-querying and rebuilding every section.
    /// </summary>
    private void ResolveProposal(int proposalId, bool accept)
    {
        using var context = PaperbunkrDb.CreateContext();
        var proposal = context.MetadataProposals.Find(proposalId);
        if (proposal is null)
        {
            return;
        }

        proposal.Status = accept ? MetadataProposalStatus.Accepted : MetadataProposalStatus.Rejected;
        proposal.ResolvedAt = DateTime.UtcNow;
        context.SaveChanges();

        // Series is write-time, not read-time like every other field (docs/superpowers/specs/
        // 2026-08-17-metadata-model-phase2b-series-reassignment-design.md) - accepting it must
        // actually move the issue, not just flip Status. Reject needs no extra step: the issue
        // simply stays on whatever series it's already on.
        if (accept && proposal.Field == MetadataProposalField.Series)
        {
            SeriesReassignmentResolver.Apply(context, proposal);
        }

        // Series-scoped Summary/Status/Genre proposals (docs/superpowers/specs/2026-08-23-apply-
        // from-provider-design.md) arrive already Accepted and write straight to the Series field -
        // unlike Issue-scoped proposals (never written to the raw field, only surfaced through an
        // Effective* resolver), Reject here needs a real revert step, not just a status flip.
        if (!accept && proposal.SeriesId is not null)
        {
            RevertSeriesField(context, proposal);
        }
    }

    /// <summary>Writes <see cref="MetadataProposal.CurrentValue"/> (the pre-proposal snapshot) back into the Series field it came from, undoing <c>MetadataLinkResolver</c>'s auto-accept write.</summary>
    private static void RevertSeriesField(PaperbunkrDbContext context, MetadataProposal proposal)
    {
        var series = context.Series.Find(proposal.SeriesId);
        if (series is null)
        {
            return;
        }

        switch (proposal.Field)
        {
            case MetadataProposalField.Summary:
                series.Summary = proposal.CurrentValue;
                break;
            case MetadataProposalField.Genre:
                series.Genre = proposal.CurrentValue;
                break;
            case MetadataProposalField.Status:
                series.Status = string.IsNullOrEmpty(proposal.CurrentValue)
                    ? SeriesStatus.Unknown
                    : Enum.Parse<SeriesStatus>(proposal.CurrentValue);
                break;
        }

        context.SaveChanges();
    }

    /// <summary>
    /// Moves every issue from <paramref name="source"/> into <paramref name="target"/> - deduped
    /// by (Number, Volume), the same rule <see cref="Paperbunkr.Data.CeMigration.CeLibraryMigrator"/>'s
    /// idempotent commit path uses - and removes the now-empty source series.
    /// </summary>
    private static void MergeSeriesInto(PaperbunkrDbContext context, Series source, Series target)
    {
        var existingKeys = new HashSet<(string?, string?)>(target.Issues.Select(i => (i.EffectiveNumber(), i.EffectiveVolume())));

        foreach (var issue in source.Issues.ToList())
        {
            if (existingKeys.Add((issue.EffectiveNumber(), issue.EffectiveVolume())))
            {
                issue.SeriesId = target.Id;
                issue.Series = target;
            }
            else
            {
                context.Issues.Remove(issue);
                CoverImageCache.Invalidate(issue.Id);
            }
        }

        context.Series.Remove(source);
    }
}
