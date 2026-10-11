using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
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
/// The persistent review queues (docs/onboarding.md §14 step 5,
/// docs/superpowers/specs/2026-08-06-migration-ux-design.md §C): Content Type, Duplicate Files, Series
/// Conflicts, Metadata Proposals and Advertisement Pages. Hosted by Preferences → Library → Library Health
/// (docs/superpowers/specs/2026-09-25-needs-review-into-library-health-design.md) - it used to live in the CE
/// migration overlay's "Needs Review" tab. Content Type and Duplicate Files are derived queries (nothing to
/// persist; an item drops off once the underlying data changes); Series Conflicts, Metadata Proposals and Ad
/// Pages are backed by stored rows, since "is this the same series?" isn't a field predicate the live-query
/// approach can express.
/// </summary>
public partial class NeedsReviewViewModel : ViewModelBase
{
    private readonly Action<int> _onOpenSeriesDetail;

    public NeedsReviewViewModel(Action<int> onOpenSeriesDetail, bool loadOnConstruction = true)
    {
        _onOpenSeriesDetail = onOpenSeriesDetail;
        ContentTypeItems = new ObservableCollection<SeriesReviewItem>();
        AutoClassifiedItems = new ObservableCollection<SeriesReviewItem>();
        AcceptAllHighConfidenceConfirm = new TwoStepConfirm(AcceptAllHighConfidence, "Accept all high confidence", "Confirm accept all?");
        SeriesConflicts = new ObservableCollection<SeriesConflictRowViewModel>();
        PendingProposalGroups = new ObservableCollection<ProposalGroupViewModel>();
        AppliedProposalGroups = new ObservableCollection<ProposalGroupViewModel>();
        DuplicateGroupItems = new ObservableCollection<DuplicateGroupRowViewModel>();
        AdPageGroupItems = new ObservableCollection<AdPageGroupRowViewModel>();

        // List-wide actions for the two stored queues that only had per-row / per-group buttons. Both change data in bulk
        // (Accept writes tags / moves issues between series, Reject is remembered for good), so they use the app's inline
        // two-step confirm (first click arms for 3 s, second commits) instead of a modal.
        AcceptAllProposalsConfirm = new TwoStepConfirm(() => AcceptProposals(MetadataProposalStatus.Pending, null), "Accept All", "Confirm accept all?");
        RejectAllProposalsConfirm = new TwoStepConfirm(() => RejectProposals(MetadataProposalStatus.Pending, null), "Reject All", "Confirm reject all?");
        AcceptAllAdPagesConfirm = new TwoStepConfirm(() => ResolveAllAdPages(accept: true), "Accept All", "Confirm accept all?");
        RejectAllAdPagesConfirm = new TwoStepConfirm(() => ResolveAllAdPages(accept: false), "Reject All", "Confirm reject all?");
        RejectAllAppliedConfirm = new TwoStepConfirm(() => RejectProposals(MetadataProposalStatus.Accepted, null), "Reject All Applied", "Confirm reject all?");

        // Production passes false: nothing is loaded until someone asks. It is re-run when it actually matters -
        // migration completion, the live folder-watch handler and GoPreferences() (which hosts the queues and their
        // pending badges), all through RefreshAsync so the database work stays off the UI thread.
        if (loadOnConstruction)
        {
            Refresh();
        }
    }

    /// <summary>The five queues, so an action can refresh only the one it changed (docs/superpowers/specs/2026-09-26-library-health-subtabs-design.md).</summary>
    public enum Queue
    {
        ContentType,
        SeriesConflicts,
        Proposals,
        Duplicates,
        AdPages,
    }

    private int _refreshTicket;

    /// <summary>True while a <see cref="RefreshAsync"/> is computing. The previous result stays on screen meanwhile.</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>False until the first refresh has finished - counts show "…" until then instead of a misleading 0.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreCountsLoading), nameof(PendingCountLabel), nameof(DuplicateCountLabel), nameof(ConflictCountLabel),
        nameof(ContentTypeCountLabel), nameof(AdPageCountLabel), nameof(ProposalSummaryLabel))]
    private bool _hasLoaded;

    public bool AreCountsLoading => !HasLoaded;

    /// <summary>A count as text, or "…" until the first refresh has finished (a 0 shown before that would be a lie).</summary>
    private string CountLabel(int count) => HasLoaded ? count.ToString("N0") : "…";

    public string PendingCountLabel => CountLabel(PendingCount);

    public string DuplicateCountLabel => CountLabel(DuplicateGroupItems.Count);

    public string ConflictCountLabel => CountLabel(SeriesConflicts.Count);

    public string ContentTypeCountLabel => CountLabel(ContentTypeItems.Count);

    public string AdPageCountLabel => CountLabel(AdPageGroupItems.Sum(g => g.Pages.Count));

    /// <summary>Accept every pending Metadata Proposal at once. Applied ones are untouched.</summary>
    public TwoStepConfirm AcceptAllProposalsConfirm { get; }

    /// <summary>Reject every pending Metadata Proposal at once. Applied ones are untouched - they have their own bulk actions below.</summary>
    public TwoStepConfirm RejectAllProposalsConfirm { get; }

    /// <summary>
    /// Discard everything the Automatic policy applied on its own (all groups): each proposal becomes Rejected, and a
    /// series-scoped one also writes its field back to the value it had before. Destructive, so two-step.
    /// </summary>
    public TwoStepConfirm RejectAllAppliedConfirm { get; }

    /// <summary>Accept every pending ad-page proposal across all groups.</summary>
    public TwoStepConfirm AcceptAllAdPagesConfirm { get; }

    /// <summary>Reject every pending ad-page proposal across all groups (each is remembered so it is never proposed again).</summary>
    public TwoStepConfirm RejectAllAdPagesConfirm { get; }

    public ObservableCollection<SeriesReviewItem> ContentTypeItems { get; }

    /// <summary>Series the tracker pipeline classified on its own in the last 30 days, each with an Undo (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md).</summary>
    public ObservableCollection<SeriesReviewItem> AutoClassifiedItems { get; }

    public bool HasAutoClassifiedItems => AutoClassifiedItems.Count > 0;

    public string AutoClassifiedCountLabel => AutoClassifiedItems.Count == 1 ? "1 series" : $"{AutoClassifiedItems.Count:N0} series";

    [ObservableProperty]
    private bool _isAutoClassifiedOpen;

    [RelayCommand]
    private void ToggleAutoClassifiedOpen() => IsAutoClassifiedOpen = !IsAutoClassifiedOpen;

    /// <summary>"38 not looked up (Western evidence) · 112 with no tracker match", or empty. Counts what the queue is not asking about so the list is not a silent subset.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContentTypeSummary))]
    private string _contentTypeSummary = string.Empty;

    public bool HasContentTypeSummary => !string.IsNullOrEmpty(ContentTypeSummary);

    /// <summary>How many rows "Accept all high confidence" would take.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHighConfidenceItems))]
    private int _highConfidenceCount;

    public bool HasHighConfidenceItems => HighConfidenceCount > 0;

    /// <summary>Accept every high-confidence suggestion at once (two-step: it changes many series). Conflicting and doubtful rows are left for a person.</summary>
    public TwoStepConfirm AcceptAllHighConfidenceConfirm { get; }

    public ObservableCollection<SeriesConflictRowViewModel> SeriesConflicts { get; }

    /// <summary>
    /// Proposals still waiting on a human (<see cref="MetadataProposalStatus.Pending"/>), bucketed by field and source - the
    /// only proposals that count toward <see cref="HasPendingItems"/>/<see cref="PendingCount"/>. A bucket only loads its
    /// rows when it is expanded.
    /// </summary>
    public ObservableCollection<ProposalGroupViewModel> PendingProposalGroups { get; }

    /// <summary>Total pending proposals across all groups.</summary>
    public int PendingProposalCount => PendingProposalGroups.Sum(g => g.Count);

    /// <summary>
    /// Proposals the default Automatic policy already applied (<see cref="MetadataProposalStatus.Accepted"/>) that nobody
    /// has reviewed yet (<see cref="MetadataProposal.ReviewedAt"/> is null), bucketed by field and source. On a big
    /// library that is thousands of rows, so the list is a few buckets with a count and bulk Accept / Reject each, and a
    /// bucket only builds its rows when it is expanded.
    /// </summary>
    public ObservableCollection<ProposalGroupViewModel> AppliedProposalGroups { get; }

    /// <summary>Total unreviewed applied proposals across all groups.</summary>
    public int AppliedProposalCount => AppliedProposalGroups.Sum(g => g.Count);

    public string AppliedProposalCountLabel => AppliedProposalCount == 1 ? "1 proposal" : $"{AppliedProposalCount:N0} proposals";

    public ObservableCollection<DuplicateGroupRowViewModel> DuplicateGroupItems { get; }

    /// <summary>Pages the ad-detection scan thinks are advertisements, grouped by the ad they matched (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md §5). Nothing is applied until accepted.</summary>
    public ObservableCollection<AdPageGroupRowViewModel> AdPageGroupItems { get; }

    public bool HasContentTypeItems => ContentTypeItems.Count > 0;

    public bool HasSeriesConflictItems => SeriesConflicts.Count > 0;

    public bool HasPendingProposalItems => PendingProposalGroups.Count > 0;

    /// <summary>True when there is anything to show in the Metadata Proposals section - pending or applied.</summary>
    public bool HasAnyProposalItems => HasPendingProposalItems || HasAppliedProposalItems;

    /// <summary>"0 pending · 1,814 applied" for the section header, or "…" until the first refresh has finished.</summary>
    public string ProposalSummaryLabel => HasLoaded ? $"{PendingProposalCount:N0} pending · {AppliedProposalCount:N0} applied" : "…";

    /// <summary>"1 proposal" / "1,204 proposals".</summary>
    public string PendingProposalCountLabel => PendingProposalCount == 1 ? "1 proposal" : $"{PendingProposalCount:N0} proposals";

    public bool HasAppliedProposalItems => AppliedProposalGroups.Count > 0;

    public bool HasDuplicateFileItems => DuplicateGroupItems.Count > 0;

    public bool HasAdPageItems => AdPageGroupItems.Count > 0;

    /// <summary>
    /// Missing Files is deliberately not part of this (it has its own count in Library Health's stat tiles), and
    /// neither are Applied proposals (audit history). Everything else here is a queue that needs a decision.
    /// </summary>
    public bool HasPendingItems => PendingCount > 0;

    /// <summary>
    /// How many things are waiting on a decision, for Library Health's "Needs review · N" chip: series with an unknown
    /// content type, series conflicts, pending proposals, duplicate groups (one per group, not per file) and ad pages
    /// (one per page).
    /// </summary>
    public int PendingCount =>
        ContentTypeItems.Count
        + SeriesConflicts.Count
        + PendingProposalCount
        + DuplicateGroupItems.Count
        + AdPageGroupItems.Sum(g => g.Pages.Count);

    [ObservableProperty]
    private bool _isAppliedProposalsExpanded;

    [RelayCommand]
    private void ToggleAppliedProposalsExpanded() => IsAppliedProposalsExpanded = !IsAppliedProposalsExpanded;

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(HasContentTypeItems));
        OnPropertyChanged(nameof(HasSeriesConflictItems));
        OnPropertyChanged(nameof(HasPendingProposalItems));
        OnPropertyChanged(nameof(HasThresholdAccept));
        OnPropertyChanged(nameof(PendingProposalCount));
        OnPropertyChanged(nameof(PendingProposalCountLabel));
        OnPropertyChanged(nameof(HasAppliedProposalItems));
        OnPropertyChanged(nameof(AppliedProposalCount));
        OnPropertyChanged(nameof(AppliedProposalCountLabel));
        OnPropertyChanged(nameof(HasDuplicateFileItems));
        OnPropertyChanged(nameof(HasAdPageItems));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPendingItems));
        OnPropertyChanged(nameof(PendingCountLabel));
        OnPropertyChanged(nameof(DuplicateCountLabel));
        OnPropertyChanged(nameof(ConflictCountLabel));
        OnPropertyChanged(nameof(ContentTypeCountLabel));
        OnPropertyChanged(nameof(HasAutoClassifiedItems));
        OnPropertyChanged(nameof(AutoClassifiedCountLabel));
        OnPropertyChanged(nameof(AdPageCountLabel));
        OnPropertyChanged(nameof(HasAnyProposalItems));
        OnPropertyChanged(nameof(ProposalSummaryLabel));
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

    /// <summary>Resolves every currently-visible duplicate group using its own pre-computed default (largest, present file) - "use current state" semantics, same as <see cref="MergeAllAboveNinety"/> for conflicts.</summary>
    [RelayCommand]
    private void KeepLargestInAllGroups()
    {
        foreach (var group in DuplicateGroupItems.ToList())
        {
            ApplyResolveDuplicateGroup(group);
        }

        Refresh(Queue.Duplicates);
    }

    /// <summary>
    /// Hides every currently-listed duplicate group without touching any file (sets <c>DuplicateAcknowledged</c> on every
    /// member) - the bulk form of a group's own Dismiss. A group reappears if a new file later joins it.
    /// </summary>
    [RelayCommand]
    private void DismissAllDuplicateGroups()
    {
        var issueIds = DuplicateGroupItems.SelectMany(g => g.IssueIds).ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var issue in context.Issues.Where(i => issueIds.Contains(i.Id)))
            {
                issue.DuplicateAcknowledged = true;
            }

            context.SaveChanges();
        }

        Refresh(Queue.Duplicates);
    }

    /// <summary>What the last "Merge entries that share one file" did, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuplicateStatusMessage))]
    private string? _duplicateStatusMessage;

    public bool HasDuplicateStatusMessage => !string.IsNullOrEmpty(DuplicateStatusMessage);

    /// <summary>Folds library entries that point at the very same file into one, touching no files. Two entries for one file make every
    /// duplicate resolution risky (removing one used to send the shared file to the Recycle Bin), so this is the first thing to run.</summary>
    [RelayCommand]
    private void MergeSamePathEntries()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            DuplicateStatusMessage = SamePathEntryMerger.Merge(context).ToString();
        }

        Refresh(Queue.Duplicates);
    }

    /// <summary>Reloads every queue synchronously. Tests and small callers use this; the UI uses <see cref="RefreshAsync"/>.</summary>
    public void Refresh() => RefreshCore(null);

    /// <summary>Reloads just the queue an action changed (the others cannot have moved), synchronously.</summary>
    public void Refresh(Queue queue) => RefreshCore(queue);

    private void RefreshCore(Queue? only)
    {
        Interlocked.Increment(ref _refreshTicket); // any refresh still in flight is now stale
        List<Action> applies;
        using (var context = PaperbunkrDb.CreateContext())
        {
            applies = Load(context, only);
        }

        Apply(applies);

        // A refresh that was still in flight is stale now and will not clear the flag itself (it only does so while it is the
        // newest), so this one has to.
        IsRefreshing = false;
    }

    /// <summary>
    /// Reloads every queue with the database work on a background thread and the collection swap back on the UI thread. The
    /// previous result stays on screen while it runs (<see cref="IsRefreshing"/>), and a result superseded by a newer refresh
    /// is dropped. A failure keeps the previous result. This is what opening Preferences, a folder-watch event and a
    /// finished migration call.
    /// </summary>
    public async Task RefreshAsync()
    {
        // The collection swap must run on the UI thread, and the code after the await only does if this started there (a
        // folder-watch callback may not have). Hop over first instead of assuming.
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RefreshAsync);
            return;
        }

        int ticket = Interlocked.Increment(ref _refreshTicket);
        IsRefreshing = true;
        try
        {
            var applies = await Task.Run(() =>
            {
                using var context = PaperbunkrDb.CreateContext();
                return Load(context, null);
            });

            if (ticket == Volatile.Read(ref _refreshTicket))
            {
                Apply(applies);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Needs Review refresh failed, keeping the previous result: {ex}");
        }
        finally
        {
            if (ticket == Volatile.Read(ref _refreshTicket))
            {
                IsRefreshing = false;
            }
        }
    }

    /// <summary>
    /// The database half of a refresh: reads what the requested queues need and returns, per queue, the step that swaps its
    /// collection. Nothing here touches a bound collection, so it is safe on any thread.
    /// </summary>
    private List<Action> Load(PaperbunkrDbContext context, Queue? only)
    {
        var applies = new List<Action>();
        if (only is null or Queue.ContentType)
        {
            applies.Add(LoadContentTypeItems(context));
        }

        if (only is null or Queue.SeriesConflicts)
        {
            applies.Add(LoadSeriesConflicts(context));
        }

        if (only is null or Queue.Proposals)
        {
            applies.Add(LoadMetadataProposalGroups(context));
        }

        if (only is null or Queue.Duplicates)
        {
            applies.Add(LoadDuplicateFileItems(context));
        }

        if (only is null or Queue.AdPages)
        {
            applies.Add(LoadAdPageItems(context));
        }

        return applies;
    }

    private void Apply(List<Action> applies)
    {
        foreach (var apply in applies)
        {
            apply();
        }

        HasLoaded = true;
        NotifyCountsChanged();
    }

    /// <summary>
    /// Pending <see cref="AdPageProposal"/>s from the ad-detection scan, one group per matched ad (docs/superpowers/
    /// specs/2026-09-21-comic-reader-page-intelligence-design.md §5). Like Metadata Proposals it is a stored queue;
    /// accepting writes a normal Advertisement page tag, rejecting remembers the page so it is never proposed again.
    /// </summary>
    private Action LoadAdPageItems(PaperbunkrDbContext context)
    {
        var groupRows = new List<(AdPageGroupRowViewModel Row, string? SourcePath, int SourcePage)>();

        var proposals = context.AdPageProposals
            .Include(p => p.Issue).ThenInclude(i => i!.Series)
            .Include(p => p.MatchedAdHash)
            .Where(p => p.Status == AdPageProposalStatus.Pending)
            .OrderBy(p => p.MatchedAdHashId).ThenBy(p => p.Issue!.Series!.Name).ThenBy(p => p.IssueId).ThenBy(p => p.PageNumber)
            .ToList();

        foreach (var group in proposals.GroupBy(p => p.MatchedAdHashId))
        {
            var ad = group.First().MatchedAdHash;
            string? sourcePath = null;
            string sourceLabel = "Ad from a removed issue";
            if (ad?.SourceIssueId is int sourceIssueId && ad.SourcePageNumber is int sourcePage)
            {
                var source = context.Issues.Include(i => i.Series).FirstOrDefault(i => i.Id == sourceIssueId);
                if (source is not null)
                {
                    sourcePath = source.FilePath;
                    sourceLabel = $"{source.Series?.Name ?? "Unknown"} #{source.EffectiveNumber() ?? "?"} · page {sourcePage + 1}";
                }
            }

            var pages = group.Select(p => new AdPageProposalRowViewModel(
                p.Id,
                p.IssueId,
                p.PageNumber,
                $"{p.Issue?.Series?.Name ?? "Unknown"} #{p.Issue?.EffectiveNumber() ?? "?"} · page {p.PageNumber + 1}",
                p.Distance,
                onAccept: r => ResolveAdPage(r.ProposalId, accept: true),
                onReject: r => ResolveAdPage(r.ProposalId, accept: false))).ToList();

            var groupRow = new AdPageGroupRowViewModel(
                group.Key,
                sourceLabel,
                pages,
                onAcceptAll: g => ResolveAdPages(g.ProposalIds, accept: true),
                onRejectAll: g => ResolveAdPages(g.ProposalIds, accept: false));
            groupRows.Add((groupRow, sourcePath, ad?.SourcePageNumber ?? 0));
        }

        return () =>
        {
            AdPageGroupItems.Clear();
            foreach (var (row, sourcePath, sourcePage) in groupRows)
            {
                AdPageGroupItems.Add(row);
                LoadAdThumbnail(row, sourcePath, sourcePage);
            }
        };
    }

    /// <summary>Decodes the ad's source page off the UI thread and hands the bitmap back to the row. Best-effort: no source file, no thumbnail.</summary>
    private static void LoadAdThumbnail(AdPageGroupRowViewModel row, string? sourcePath, int sourcePage)
    {
        if (string.IsNullOrEmpty(sourcePath))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            var bitmap = Paperbunkr.App.Services.AdDetection.AdPageThumbnailLoader.Load(sourcePath, sourcePage);
            if (bitmap is not null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => row.Thumbnail = bitmap);
            }
        });
    }

    private void ResolveAdPage(int proposalId, bool accept) => ResolveAdPages(new[] { proposalId }, accept);

    private void ResolveAllAdPages(bool accept) => ResolveAdPages(AdPageGroupItems.SelectMany(g => g.ProposalIds).ToList(), accept);

    /// <summary>
    /// Accept/Reject for one page or a whole group. The list refresh is deferred one dispatcher tick: this runs from a
    /// Button inside a row of <see cref="AdPageGroupItems"/> (or a page inside one), and clearing that collection
    /// synchronously would detach the clicking control mid-route (CLAUDE.md "routed event" gotcha). <see cref="Refresh"/>
    /// makes its own fresh context, so nothing from this method's context is captured.
    /// </summary>
    private void ResolveAdPages(IReadOnlyList<int> proposalIds, bool accept)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            if (accept)
            {
                AdPageProposalResolver.AcceptAll(context, proposalIds);
            }
            else
            {
                AdPageProposalResolver.RejectAll(context, proposalIds);
            }
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() => Refresh(Queue.AdPages));
    }

    private Action LoadContentTypeItems(PaperbunkrDbContext context)
    {
        // The smart-list field this used to evaluate is just Series.ContentType (SmartListCatalog: i.Series?.ContentType), but
        // going through SmartListQueryBuilder.Build loaded every issue with Series, MetadataProposals and Tags first (430-1,060 ms
        // on a 3,650-issue library). Same set, direct: series of unknown content type that have at least one issue - now minus the ones a
        // person already decided (locked) or waved off (skipped), plus any series with a tracker-suggested type waiting to be confirmed.
        var rows = context.Series.AsNoTracking()
            .Where(s => !s.ContentTypeLocked && s.RemoteSourceId == null && s.ContentTypeCheck != ContentTypeCheck.Skipped && s.Issues.Any()
                && (s.ContentType == ContentType.Unknown || s.ContentTypeSuggestion != null))
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.ContentType,
                s.ContentTypeSource,
                s.ContentTypeSuggestion,
                s.ContentTypeConfidence,
                s.ContentTypeEvidence,
                s.ContentTypeCheck,
                s.GcdSeriesId,
                s.CoverIssueId,
            })
            .ToList();

        // The per-series issue facts come from three flat queries joined here: correlated subqueries (First/Distinct inside the projection) need the SQL APPLY
        // operation, which SQLite does not support.
        var rowIds = rows.Select(r => r.Id).ToList();
        var firstIssueIds = context.Issues.AsNoTracking()
            .Where(i => rowIds.Contains(i.SeriesId))
            .GroupBy(i => i.SeriesId)
            .Select(g => new { SeriesId = g.Key, First = g.Min(i => i.Id) })
            .ToDictionary(x => x.SeriesId, x => x.First);
        var publishersBySeries = context.Issues.AsNoTracking()
            .Where(i => rowIds.Contains(i.SeriesId))
            .Select(i => new { i.SeriesId, i.Publisher })
            .Distinct()
            .ToList()
            .GroupBy(x => x.SeriesId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Publisher).ToList());
        var scrapedSeries = context.Issues.AsNoTracking()
            .Where(i => rowIds.Contains(i.SeriesId) && i.MetadataSource != null)
            .Select(i => i.SeriesId)
            .Distinct()
            .ToHashSet();

        int skipped = 0, noMatch = 0;
        var items = new List<SeriesReviewItem>();
        foreach (var row in rows)
        {
            var evidence = ContentTypeEvidence.Deserialize(row.ContentTypeEvidence);
            string? skipReason = row.ContentTypeSuggestion is null
                ? ContentTypeClassificationService.SkipReason(row.GcdSeriesId, row.ContentTypeSource, row.ContentType,
                    publishersBySeries.GetValueOrDefault(row.Id) ?? new List<string?>(), scrapedSeries.Contains(row.Id))
                : null;
            if (skipReason is not null)
            {
                skipped++;
            }

            if (row.ContentTypeCheck == ContentTypeCheck.NoMatch && row.ContentTypeSuggestion is null)
            {
                noMatch++;
            }

            items.Add(BuildContentTypeItem(row.Id, row.Name, row.CoverIssueId ?? firstIssueIds.GetValueOrDefault(row.Id), row.ContentType, row.ContentTypeSuggestion,
                row.ContentTypeConfidence, evidence, StatusFor(row.ContentTypeSuggestion, row.ContentTypeCheck, skipReason), autoClassified: false));
        }

        items = items
            .OrderByDescending(i => i.HasSuggestion)
            .ThenBy(i => i.IsConflict)
            .ThenByDescending(i => i.Confidence ?? 0)
            .ThenBy(i => i.SeriesName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Recently auto-classified: applied on its own in the last 30 days and not yet confirmed, undone or locked by a person.
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var autoRows = context.Series.AsNoTracking()
            .Where(s => s.ContentTypeSource == ContentTypeSource.Provider && !s.ContentTypeLocked && s.PreviousContentType != null
                && s.ContentTypeAutoAppliedUtc != null && s.ContentTypeAutoAppliedUtc >= cutoff)
            .OrderByDescending(s => s.ContentTypeAutoAppliedUtc)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.ContentType,
                s.PreviousContentType,
                s.ContentTypeConfidence,
                s.ContentTypeEvidence,
                s.CoverIssueId,
            })
            .ToList();
        var autoIds = autoRows.Select(r => r.Id).ToList();
        var autoFirstIssueIds = context.Issues.AsNoTracking()
            .Where(i => autoIds.Contains(i.SeriesId))
            .GroupBy(i => i.SeriesId)
            .Select(g => new { SeriesId = g.Key, First = g.Min(i => i.Id) })
            .ToDictionary(x => x.SeriesId, x => x.First);
        var autoItems = autoRows
            .Select(r => BuildContentTypeItem(r.Id, r.Name, r.CoverIssueId ?? autoFirstIssueIds.GetValueOrDefault(r.Id), r.PreviousContentType ?? ContentType.Unknown, r.ContentType,
                r.ContentTypeConfidence, ContentTypeEvidence.Deserialize(r.ContentTypeEvidence), string.Empty, autoClassified: true))
            .ToList();

        var summaryParts = new List<string>();
        if (skipped > 0)
        {
            summaryParts.Add($"{skipped:N0} not looked up (Western evidence)");
        }

        if (noMatch > 0)
        {
            summaryParts.Add($"{noMatch:N0} with no tracker match");
        }

        string summary = string.Join(" · ", summaryParts);
        int highConfidence = items.Count(i => i.IsHighConfidence);

        return () =>
        {
            ContentTypeItems.Clear();
            foreach (var item in items)
            {
                ContentTypeItems.Add(item);
            }

            AutoClassifiedItems.Clear();
            foreach (var item in autoItems)
            {
                AutoClassifiedItems.Add(item);
            }

            ContentTypeSummary = summary;
            HighConfidenceCount = highConfidence;
        };
    }

    private static string StatusFor(ContentType? suggestion, ContentTypeCheck check, string? skipReason) =>
        suggestion is not null ? string.Empty
        : skipReason is not null ? $"Not looked up: {skipReason.ToLowerInvariant()}"
        : check == ContentTypeCheck.NoMatch ? "No match on the tracker sites"
        : "Not looked up yet";

    /// <summary>
    /// Turns a series row plus its stored evidence into a queue row. <paramref name="current"/> is the type shown in the "Now" column; for a
    /// recently auto-classified row <paramref name="suggestion"/> is the type that was applied and <paramref name="current"/> is what Undo restores.
    /// </summary>
    private static SeriesReviewItem BuildContentTypeItem(int id, string name, int? coverIssueId, ContentType current, ContentType? suggestion,
        double? confidence, IReadOnlyList<ContentTypeEvidenceItem> evidence, string status, bool autoClassified)
    {
        var typed = evidence.Where(e => e.Type is not null).ToList();
        bool conflict = typed.Select(e => e.Type).Distinct().Count() > 1;
        bool high = !autoClassified && suggestion is not null && !conflict
            && typed.Any(e => e.Type == suggestion && !e.QueueOnly && e.Score >= TitleMatchScorer.AutoThreshold);

        var chips = evidence.Select(e => new ContentTypeEvidenceChip(
            $"{e.Provider}: {(string.IsNullOrWhiteSpace(e.Raw) ? e.Type?.ToString() ?? "no type" : e.Raw)}",
            $"Matched \"{e.MatchedTitle}\" ({e.Score:P0}){(e.Type is null ? ", which says nothing about its type" : $" - reads as {e.Type}")}{(e.QueueOnly ? " (a weak signal, never applied on its own)" : string.Empty)}"))
            .ToList();

        return new SeriesReviewItem
        {
            SeriesId = id,
            SeriesName = name,
            CoverIssueId = coverIssueId,
            CurrentLabel = current.ToString(),
            Suggestion = suggestion,
            Confidence = suggestion is null ? null : confidence,
            IsConflict = conflict,
            IsHighConfidence = high,
            StatusLabel = status,
            Chips = chips,
            IsAutoClassified = autoClassified,
        };
    }

    // ----- Content Type row actions (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md). Each runs from a Button inside a row of
    // ContentTypeItems / AutoClassifiedItems, so the list refresh is deferred one dispatcher tick (CLAUDE.md "routed event" gotcha). -----

    /// <summary>Writes one series' content-type change in a fresh context, then the caller refreshes the queue after the click has finished routing.</summary>
    private static void EditContentType(int seriesId, Action<Series> edit)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Find(seriesId);
        if (series is not null)
        {
            edit(series);
            context.SaveChanges();
        }
    }

    private void RefreshContentTypeSoon() => Avalonia.Threading.Dispatcher.UIThread.Post(() => Refresh(Queue.ContentType));

    /// <summary>Accept: apply the suggested type and lock it.</summary>
    [RelayCommand]
    private void AcceptContentType(SeriesReviewItem? item)
    {
        if (item is null)
        {
            return;
        }

        EditContentType(item.SeriesId, s => SeriesContentTypeEditor.AcceptSuggestion(s));
        RefreshContentTypeSoon();
    }

    /// <summary>Keep: lock the type the series has now.</summary>
    [RelayCommand]
    private void KeepContentType(SeriesReviewItem? item)
    {
        if (item is null)
        {
            return;
        }

        EditContentType(item.SeriesId, SeriesContentTypeEditor.Keep);
        RefreshContentTypeSoon();
    }

    /// <summary>"Not a comic: skip": never looked up again.</summary>
    [RelayCommand]
    private void SkipContentType(SeriesReviewItem? item)
    {
        if (item is null)
        {
            return;
        }

        EditContentType(item.SeriesId, s => SeriesContentTypeEditor.Skip(s, DateTime.UtcNow));
        RefreshContentTypeSoon();
    }

    /// <summary>Change: set a type of the person's own choosing and lock it.</summary>
    private void ChangeContentType(SeriesReviewItem? item, ContentType type)
    {
        if (item is null)
        {
            return;
        }

        EditContentType(item.SeriesId, s => SeriesContentTypeEditor.SetManual(s, type));
        RefreshContentTypeSoon();
    }

    [RelayCommand] private void ChangeContentTypeToComic(SeriesReviewItem? item) => ChangeContentType(item, ContentType.Comic);

    [RelayCommand] private void ChangeContentTypeToManga(SeriesReviewItem? item) => ChangeContentType(item, ContentType.Manga);

    [RelayCommand] private void ChangeContentTypeToManhwa(SeriesReviewItem? item) => ChangeContentType(item, ContentType.Manhwa);

    [RelayCommand] private void ChangeContentTypeToManhua(SeriesReviewItem? item) => ChangeContentType(item, ContentType.Manhua);

    /// <summary>Undo an automatic classification: the previous type and reading mode come back and the series is locked so the same guess is not re-applied.</summary>
    [RelayCommand]
    private void UndoAutoClassified(SeriesReviewItem? item)
    {
        if (item is null)
        {
            return;
        }

        EditContentType(item.SeriesId, s => SeriesContentTypeEditor.Undo(s));
        RefreshContentTypeSoon();
    }

    private void AcceptAllHighConfidence()
    {
        var ids = ContentTypeItems.Where(i => i.IsHighConfidence).Select(i => i.SeriesId).ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var series in context.Series.Where(s => ids.Contains(s.Id)))
            {
                SeriesContentTypeEditor.AcceptSuggestion(series);
            }

            context.SaveChanges();
        }

        RefreshContentTypeSoon();
    }

    /// <summary>
    /// Builds duplicate clusters over every non-placeholder issue, then keeps only clusters that
    /// still have at least one un-acknowledged member - a cluster where every current member was
    /// previously dismissed stays hidden, but if a new file later joins that same cluster it
    /// reappears showing every member (acknowledged or not) so the user has full context again,
    /// rather than a confusing partial view.
    /// <para>
    /// The clustering (<c>BuildDuplicateGroups</c>, a linear hash pass, 11-65 ms) is unchanged. What was slow was loading
    /// every issue as a tracked entity with <c>Include(Series)</c> (460-1,270 ms on a 3,650-issue library), so this loads a
    /// no-tracking projection of just the fields the grouping and the rows read (54-190 ms) plus one <c>Id -&gt; Name</c>
    /// lookup for the label. Proposals were never included here, so the effective values are the raw ones, as before.
    /// </para>
    /// </summary>
    private Action LoadDuplicateFileItems(PaperbunkrDbContext context)
    {
        var issues = context.Issues.AsNoTracking()
            .Where(i => !i.IsPlaceholder)
            .Select(i => new Issue
            {
                Id = i.Id,
                SeriesId = i.SeriesId,
                Format = i.Format,
                Count = i.Count,
                Number = i.Number,
                Volume = i.Volume,
                Year = i.Year,
                LanguageISO = i.LanguageISO,
                Month = i.Month,
                Day = i.Day,
                FilePath = i.FilePath,
                FileSize = i.FileSize,
                FileIsMissing = i.FileIsMissing,
                AddedTime = i.AddedTime,
                DuplicateAcknowledged = i.DuplicateAcknowledged,
            })
            .ToList();
        var seriesNames = context.Series.AsNoTracking().Select(s => new { s.Id, s.Name }).ToDictionary(s => s.Id, s => s.Name);

        var rows = SmartListQueryBuilder.BuildDuplicateGroups(issues)
            .Where(g => g.Any(i => !i.DuplicateAcknowledged))
            .Select(members =>
            {
                var first = members[0];
                string label = $"{seriesNames.GetValueOrDefault(first.SeriesId) ?? "Unknown"} #{first.EffectiveNumber()} · {members.Count} copies";
                return new DuplicateGroupRowViewModel(label, members, onResolve: ResolveDuplicateGroup, onDismiss: DismissDuplicateGroup, onResolveKeepFiles: ResolveDuplicateGroupKeepFiles, onCompare: CompareDuplicateGroup);
            })
            .ToList();

        return () =>
        {
            DuplicateGroupItems.Clear();
            foreach (var row in rows)
            {
                DuplicateGroupItems.Add(row);
            }
        };
    }

    private void ResolveDuplicateGroup(DuplicateGroupRowViewModel group)
    {
        ApplyResolveDuplicateGroup(group);
        Refresh(Queue.Duplicates);
    }

    /// <summary>Deletes every non-kept candidate via <see cref="LibraryDeletionHelper"/> (Recycle Bin, cross-reference cleanup) without refreshing - shared by the single-group Resolve action and the bulk "Keep Largest in All Groups" action, which refreshes once after applying every group.</summary>
    private void ResolveDuplicateGroupKeepFiles(DuplicateGroupRowViewModel group)
    {
        ApplyResolveDuplicateGroup(group, deleteFile: false);
        Refresh(Queue.Duplicates);
    }

    private void ApplyResolveDuplicateGroup(DuplicateGroupRowViewModel group, bool deleteFile = true) =>
        DuplicateGroupResolver.RemoveIssues(group.NonKeptIssueIds, deleteFile);

    private void DismissDuplicateGroup(DuplicateGroupRowViewModel group)
    {
        DuplicateGroupResolver.Acknowledge(group.IssueIds);
        Refresh(Queue.Duplicates);
    }

    /// <summary>Raised with the copy marked to keep and the others when a group's Compare button is pressed; the shell opens the Compare screen (docs/superpowers/specs/2026-09-26-comic-reader-compare-design.md #11).</summary>
    public event Action<int, IReadOnlyList<int>>? CompareRequested;

    private void CompareDuplicateGroup(DuplicateGroupRowViewModel group)
    {
        int keep = group.Candidates.FirstOrDefault(c => c.IsKeep)?.IssueId ?? group.IssueIds[0];
        var others = group.IssueIds.Where(id => id != keep).ToList();
        if (others.Count > 0)
        {
            CompareRequested?.Invoke(keep, others);
        }
    }

    private Action LoadSeriesConflicts(PaperbunkrDbContext outerContext)
    {
        var pending = outerContext.SeriesConflicts.AsNoTracking()
            .Where(c => c.Status == SeriesConflictStatus.Pending)
            .OrderByDescending(c => c.DetectedAt)
            .ToList();

        var rows = pending.Select(conflict =>
        {
            int conflictId = conflict.Id;
            return new SeriesConflictRowViewModel(
                conflict.IncomingName,
                conflict.MatchedName,
                conflict.Similarity,
                onMerge: _ => ResolveConflict(conflictId, merge: true),
                onKeepSeparate: _ => ResolveConflict(conflictId, merge: false));
        }).ToList();

        return () =>
        {
            SeriesConflicts.Clear();
            foreach (var row in rows)
            {
                SeriesConflicts.Add(row);
            }
        };
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
            SeriesMergeHelper.MergeInto(context, source, target);
        }

        conflict.Status = SeriesConflictStatus.Merged;
        context.SaveChanges();
    }

    /// <summary>
    /// Buckets the proposals into <see cref="PendingProposalGroups"/> (<see cref="MetadataProposalStatus.Pending"/>) and
    /// <see cref="AppliedProposalGroups"/> (unreviewed <see cref="MetadataProposalStatus.Accepted"/> ones), each with a
    /// single <c>GROUP BY</c> and no rows loaded until a group is expanded. Unlike the other sections, an Accepted proposal
    /// isn't "resolved" the way a merged/kept-separate conflict is; it's "applied but still auditable/correctable" under the
    /// default Automatic policy (docs/superpowers/specs/2026-08-17-metadata-model-phase2a-metadata-proposals-design.md)
    /// until someone reviews it (<see cref="MetadataProposal.ReviewedAt"/>). Rejected/Ignored rows drop off, same as a
    /// resolved conflict does.
    /// </summary>
    private Action LoadMetadataProposalGroups(PaperbunkrDbContext context)
    {
        var pending = LoadProposalGroups(context, MetadataProposalStatus.Pending);
        var applied = LoadProposalGroups(context, MetadataProposalStatus.Accepted);
        var settings = context.GetOrCreateAppSettings();
        var policy = settings.MetadataResolutionPolicy;
        decimal minConfidence = settings.AutoApplyMinConfidence;

        return () =>
        {
            ApplyAutoApplySettings(policy, minConfidence);

            PendingProposalGroups.Clear();
            foreach (var group in pending)
            {
                PendingProposalGroups.Add(group);
            }

            AppliedProposalGroups.Clear();
            foreach (var group in applied)
            {
                AppliedProposalGroups.Add(group);
            }
        };
    }

    private List<ProposalGroupViewModel> LoadProposalGroups(PaperbunkrDbContext context, MetadataProposalStatus status)
    {
        var groups = ProposalsIn(context, status, null)
            .GroupBy(p => new { p.Field, p.Source, p.ProviderKey })
            .Select(g => new { g.Key.Field, g.Key.Source, g.Key.ProviderKey, Count = g.Count() })
            .ToList();

        return groups
            .OrderByDescending(g => g.Count).ThenBy(g => g.Field).ThenBy(g => g.Source)
            .Select(g => new ProposalGroupViewModel(
                status,
                g.Field,
                g.Source,
                g.ProviderKey,
                g.Count,
                loadRows: LoadProposalGroupRows,
                onAcceptAll: group => AcceptProposals(group.Status, group),
                onRejectAll: group => RejectProposals(group.Status, group)))
            .ToList();
    }

    /// <summary>
    /// The proposals of one kind, optionally narrowed to one group's field + source (+ provider). "Pending" means status
    /// Pending; anything else means the unreviewed applied ones (Accepted with no <see cref="MetadataProposal.ReviewedAt"/>).
    /// </summary>
    private static IQueryable<MetadataProposal> ProposalsIn(PaperbunkrDbContext context, MetadataProposalStatus status, ProposalGroupViewModel? group)
    {
        IQueryable<MetadataProposal> query = status == MetadataProposalStatus.Pending
            ? context.MetadataProposals.Where(p => p.Status == MetadataProposalStatus.Pending)
            : context.MetadataProposals.Where(p => p.Status == MetadataProposalStatus.Accepted && p.ReviewedAt == null);
        if (group is not null)
        {
            var field = group.Field;
            var source = group.Source;
            var provider = group.Provider;
            query = query.Where(p => p.Field == field && p.Source == source && p.ProviderKey == provider);
        }

        return query;
    }

    /// <summary>Builds the rows of a group being expanded: its latest <see cref="ProposalGroupViewModel.RowLimit"/> proposals.</summary>
    private IReadOnlyList<MetadataProposalRowViewModel> LoadProposalGroupRows(ProposalGroupViewModel group)
    {
        using var context = PaperbunkrDb.CreateContext();
        var proposals = ProposalsIn(context, group.Status, group)
            .Include(p => p.Issue).ThenInclude(i => i!.Series)
            .Include(p => p.Series)
            .OrderByDescending(p => p.CreatedAt)
            .Take(ProposalGroupViewModel.RowLimit)
            .ToList();
        bool isApplied = group.Status != MetadataProposalStatus.Pending;
        return proposals.Select(p => CreateProposalRow(p, isApplied, group)).ToList();
    }

    private MetadataProposalRowViewModel CreateProposalRow(MetadataProposal proposal, bool isApplied, ProposalGroupViewModel? group)
    {
        int proposalId = proposal.Id;
        // Series-scoped rows (Summary/Status/Genre, docs/superpowers/specs/2026-08-23-apply-
        // from-provider-design.md) have no issue to name - just the series itself.
        string label = proposal.SeriesId is not null
            ? proposal.Series?.Name ?? "Unknown"
            : $"{proposal.Issue?.Series?.Name ?? "Unknown"} #{proposal.Issue?.EffectiveNumber() ?? "?"}";
        return new MetadataProposalRowViewModel(
            label,
            proposal.Field.ToString(),
            proposal.CurrentValue,
            proposal.ProposedValue,
            proposal.Source.ToString(),
            isAlreadyAccepted: isApplied,
            onAccept: _ =>
            {
                ResolveProposal(proposalId, accept: true);
                group?.NotifyRowResolved();
                NotifyProposalCountsChanged();
            },
            onReject: _ =>
            {
                ResolveProposal(proposalId, accept: false);
                group?.NotifyRowResolved();
                NotifyProposalCountsChanged();
            });
    }

    private void NotifyProposalCountsChanged()
    {
        OnPropertyChanged(nameof(HasPendingProposalItems));
        OnPropertyChanged(nameof(HasThresholdAccept));
        OnPropertyChanged(nameof(PendingProposalCount));
        OnPropertyChanged(nameof(PendingProposalCountLabel));
        OnPropertyChanged(nameof(HasAppliedProposalItems));
        OnPropertyChanged(nameof(AppliedProposalCount));
        OnPropertyChanged(nameof(AppliedProposalCountLabel));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPendingItems));
        OnPropertyChanged(nameof(PendingCountLabel));
        OnPropertyChanged(nameof(HasAnyProposalItems));
        OnPropertyChanged(nameof(ProposalSummaryLabel));
    }

    /// <summary>"Accept All" on the Applied section: keep every unreviewed applied proposal and take them all out of the list. One <c>UPDATE</c>, no rows loaded.</summary>
    [RelayCommand]
    private void AcceptAllApplied() => AcceptProposals(MetadataProposalStatus.Accepted, null);

    /// <summary>
    /// Accept every proposal of a kind (or one group's). Applied ones are already in effect, so accepting them only marks them
    /// reviewed - one <c>UPDATE</c>. Pending ones become Accepted and reviewed; a Series-field proposal also moves the issue,
    /// so those go through <see cref="ResolveProposal"/> one by one on a fresh context each (a series emptied by one move can't
    /// leave a stale entity behind for the next) and the rest are one <c>UPDATE</c>.
    /// </summary>
    private void AcceptProposals(MetadataProposalStatus status, ProposalGroupViewModel? group)
    {
        DateTime? now = DateTime.UtcNow;
        var writeTimeIds = new List<int>();
        using (var context = PaperbunkrDb.CreateContext())
        {
            if (status == MetadataProposalStatus.Pending)
            {
                // Write-time proposals: a Series-field one moves the issue, and a series-scoped one writes its Series field.
                writeTimeIds = ProposalsIn(context, status, group).Where(p => p.Field == MetadataProposalField.Series || p.SeriesId != null).Select(p => p.Id).ToList();
                ProposalsIn(context, status, group)
                    .Where(p => p.Field != MetadataProposalField.Series && p.SeriesId == null)
                    .ExecuteUpdate(s => s
                        .SetProperty(p => p.Status, MetadataProposalStatus.Accepted)
                        .SetProperty(p => p.ResolvedAt, now)
                        .SetProperty(p => p.ReviewedAt, now));
            }
            else
            {
                ProposalsIn(context, status, group).ExecuteUpdate(s => s.SetProperty(p => p.ReviewedAt, now));
            }
        }

        foreach (int id in writeTimeIds)
        {
            ResolveProposal(id, accept: true);
        }

        FinishProposalBulkAction(status, group);
    }

    /// <summary>
    /// Reject every proposal of a kind (or one group's). Plain Issue-scoped ones are one <c>UPDATE</c>; series-scoped ones
    /// also write their field back to the pre-proposal value, so they go through <see cref="ResolveProposal"/> one by one.
    /// </summary>
    private void RejectProposals(MetadataProposalStatus status, ProposalGroupViewModel? group)
    {
        DateTime? now = DateTime.UtcNow;
        List<int> writeTimeIds;
        using (var context = PaperbunkrDb.CreateContext())
        {
            writeTimeIds = ProposalsIn(context, status, group).Where(p => p.SeriesId != null).Select(p => p.Id).ToList();
            ProposalsIn(context, status, group)
                .Where(p => p.SeriesId == null)
                .ExecuteUpdate(s => s.SetProperty(p => p.Status, MetadataProposalStatus.Rejected).SetProperty(p => p.ResolvedAt, now));
        }

        foreach (int id in writeTimeIds)
        {
            ResolveProposal(id, accept: false);
        }

        FinishProposalBulkAction(status, group);
    }

    /// <summary>
    /// After a bulk action on proposals. Deferred a tick: it runs from a Button inside the group's own row (or the section
    /// header), and removing that row synchronously would detach the clicking control mid-route (CLAUDE.md "routed event" gotcha).
    /// </summary>
    private void FinishProposalBulkAction(MetadataProposalStatus status, ProposalGroupViewModel? group)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (group is null)
            {
                Refresh(Queue.Proposals);
                return;
            }

            (status == MetadataProposalStatus.Pending ? PendingProposalGroups : AppliedProposalGroups).Remove(group);
            NotifyProposalCountsChanged();
        });
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

        // Accepting an already-applied proposal changes nothing but the review mark: it is applied already (and for a Series-field
        // one the issue has already moved), so re-running that would be pointless at best. Marking it reviewed is what makes the
        // per-row Accept durable - before ReviewedAt it flipped to "Accepted" on screen and came straight back on the next refresh.
        if (accept && proposal.Status == MetadataProposalStatus.Accepted)
        {
            proposal.ReviewedAt = DateTime.UtcNow;
            context.SaveChanges();
            return;
        }

        // Whether the proposal's value is in the Series field right now: only one that was applied has anything to revert, and only
        // one that was still waiting has anything left to apply (smart features §6.1, §6.2 - series proposals can now be Pending).
        bool wasApplied = proposal.Status == MetadataProposalStatus.Accepted;

        proposal.Status = accept ? MetadataProposalStatus.Accepted : MetadataProposalStatus.Rejected;
        proposal.ResolvedAt = DateTime.UtcNow;
        if (accept)
        {
            proposal.ReviewedAt = proposal.ResolvedAt;
        }

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
        if (!accept && proposal.SeriesId is not null && wasApplied)
        {
            RevertSeriesField(context, proposal);
        }

        // A series-scoped proposal that was waiting for review (a synopsis genre suggestion, say) is written to the Series field
        // when it is accepted - the same write MetadataLinkResolver does up front for the ones that apply themselves.
        if (accept && proposal.SeriesId is not null && !wasApplied && proposal.Field != MetadataProposalField.Series)
        {
            ApplySeriesField(context, proposal);
        }
    }

    /// <summary>Writes <see cref="MetadataProposal.ProposedValue"/> into the Series field the proposal is about.</summary>
    private static void ApplySeriesField(PaperbunkrDbContext context, MetadataProposal proposal)
    {
        var series = context.Series.Find(proposal.SeriesId);
        if (series is null)
        {
            return;
        }

        switch (proposal.Field)
        {
            case MetadataProposalField.Summary:
                series.Summary = proposal.ProposedValue;
                break;
            case MetadataProposalField.Genre:
                series.Genre = proposal.ProposedValue;
                break;
            case MetadataProposalField.Creator:
                series.Creator = proposal.ProposedValue;
                break;
            case MetadataProposalField.Status:
                if (Enum.TryParse<SeriesStatus>(proposal.ProposedValue, out var status))
                {
                    series.Status = status;
                }

                break;
        }

        context.SaveChanges();
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
            // Was missing: rejecting a Creator proposal left the provider's value in place (smart features §6.1).
            case MetadataProposalField.Creator:
                series.Creator = proposal.CurrentValue;
                break;
            case MetadataProposalField.Status:
                series.Status = string.IsNullOrEmpty(proposal.CurrentValue)
                    ? SeriesStatus.Unknown
                    : Enum.Parse<SeriesStatus>(proposal.CurrentValue);
                break;
        }

        context.SaveChanges();
    }

}
