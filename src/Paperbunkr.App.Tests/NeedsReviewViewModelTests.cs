using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="NeedsReviewViewModel"/>'s "Metadata Proposals" section (docs/superpowers/
/// specs/2026-08-17-metadata-model-phase2a-metadata-proposals-design.md) - the newest of its four
/// sections. Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite
/// file, same approach as <see cref="DetailScreenViewModelTests"/> since <see cref="NeedsReviewViewModel"/>
/// has no injected context-factory seam of its own; joins <see cref="AvaloniaTestCollection"/> for
/// the same reason.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class NeedsReviewViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly int _issueId;

    public NeedsReviewViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_needsreviewvm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();

        // ContentType.Comic (not the default Unknown) - keeps this seeded series out of the
        // unrelated "Content Type" Needs Review section, so HasPendingItems tests below reflect
        // only the Metadata Proposals section under test.
        var series = new Series { Name = "Kilo Station", ContentType = ContentType.Comic };
        context.Series.Add(series);
        var issue = new Issue { Series = series, Number = null };
        context.Issues.Add(issue);
        context.SaveChanges();
        _issueId = issue.Id;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static NeedsReviewViewModel CreateViewModel() => new(onOpenSeriesDetail: _ => { });

    private void AddProposal(MetadataProposalField field, string proposedValue, MetadataProposalStatus status)
    {
        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        context.MetadataProposals.Add(new MetadataProposal
        {
            IssueId = _issueId,
            Field = field,
            ProposedValue = proposedValue,
            Source = MetadataProposalSource.FilenameParser,
            Confidence = 0.6m,
            Status = status,
        });
        context.SaveChanges();
    }

    [Fact]
    public void Refresh_NoProposals_SectionEmpty()
    {
        var vm = CreateViewModel();

        Assert.False(vm.HasPendingProposalItems);
        Assert.Empty(vm.PendingProposalGroups);
    }

    [Fact]
    public void Refresh_PendingProposal_AppearsInSection()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);

        var vm = CreateViewModel();

        Assert.True(vm.HasPendingProposalItems);
        var row = Assert.Single(PendingRows(vm));
        Assert.Equal("Number", row.FieldLabel);
        Assert.Equal("12", row.ProposedValue);
        Assert.False(row.IsAlreadyAccepted);
        Assert.False(row.IsResolved);
    }

    [Fact]
    public void Refresh_AcceptedProposal_StillAppears_MarkedAlreadyAccepted()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Accepted);

        var vm = CreateViewModel();

        Assert.True(vm.HasAppliedProposalItems);
        var row = Assert.Single(AppliedRows(vm));
        Assert.True(row.IsAlreadyAccepted);
        Assert.False(row.IsResolved); // still actionable, not "done"
        Assert.Empty(vm.PendingProposalGroups); // audit history, not work to do
    }

    [Fact]
    public void AppliedProposals_DoNotCountAsPending()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Accepted);

        var vm = CreateViewModel();

        Assert.False(vm.HasPendingItems);
        Assert.Equal(0, vm.PendingCount);
    }

    [Fact]
    public void PendingCount_SumsEveryQueue_AdPagesPerPage()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);
        AddProposal(MetadataProposalField.Year, "2001", MetadataProposalStatus.Pending);
        AddProposal(MetadataProposalField.Volume, "3", MetadataProposalStatus.Accepted);
        SeedAdProposals(); // 2 pages on one ad + 1 page on another = 3 pages in 2 groups

        var vm = CreateViewModel();

        Assert.Equal(2, vm.PendingProposalCount);
        Assert.Equal(2, vm.AdPageGroupItems.Count);
        // 2 pending proposals + 3 ad pages; the Accepted proposal and anything else in the fixture adds nothing.
        Assert.Equal(2 + 3 + vm.ContentTypeItems.Count + vm.SeriesConflicts.Count + vm.DuplicateGroupItems.Count, vm.PendingCount);
        Assert.True(vm.HasPendingItems);
    }

    // --- Bulk actions: Accept All / Reject All on the pending list. Two-step confirm: the first Trigger arms, the second commits. ---

    private static void Confirm(TwoStepConfirm confirm)
    {
        confirm.TriggerCommand.Execute(null); // arm
        Assert.True(confirm.IsArmed);
        confirm.TriggerCommand.Execute(null); // commit
        TestDispatcher.Drain(); // the list refresh is deferred one dispatcher tick
    }

    [Fact]
    public void AcceptAllProposals_FirstClickOnlyArms_NothingChangesYet()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();

        vm.AcceptAllProposalsConfirm.TriggerCommand.Execute(null);
        TestDispatcher.Drain();

        Assert.True(vm.AcceptAllProposalsConfirm.IsArmed);
        using var context = Ctx();
        Assert.Equal(MetadataProposalStatus.Pending, context.MetadataProposals.Single().Status);
    }

    [Fact]
    public void AcceptAllProposals_AcceptsEveryPending_AndTheyLeaveTheQueue()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);
        AddProposal(MetadataProposalField.Year, "2001", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();

        Confirm(vm.AcceptAllProposalsConfirm);

        Assert.Empty(vm.PendingProposalGroups);
        Assert.Empty(vm.AppliedProposalGroups); // accepted by the user = applied AND reviewed, so nothing lands in the Applied list
        Assert.False(vm.HasPendingItems);
        using var context = Ctx();
        Assert.All(context.MetadataProposals, p => { Assert.Equal(MetadataProposalStatus.Accepted, p.Status); Assert.NotNull(p.ResolvedAt); Assert.NotNull(p.ReviewedAt); });
    }

    [Fact]
    public void AcceptAllProposals_SeriesFieldProposal_ActuallyMovesTheIssue()
    {
        AddProposal(MetadataProposalField.Series, "Renamed Series", MetadataProposalStatus.Pending);
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();

        Confirm(vm.AcceptAllProposalsConfirm);

        using var context = Ctx();
        Assert.Equal("Renamed Series", context.Issues.Include(i => i.Series).Single(i => i.Id == _issueId).Series!.Name);
        Assert.All(context.MetadataProposals, p => Assert.Equal(MetadataProposalStatus.Accepted, p.Status));
    }

    [Fact]
    public void RejectAllProposals_RejectsPending_LeavesAppliedUntouched()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);
        AddProposal(MetadataProposalField.Year, "2001", MetadataProposalStatus.Accepted);
        var vm = CreateViewModel();

        Confirm(vm.RejectAllProposalsConfirm);

        Assert.Empty(vm.PendingProposalGroups);
        var applied = Assert.Single(AppliedRows(vm)); // rejecting an applied one reverts written data, so it stays per-row
        Assert.Equal("Year", applied.FieldLabel);
        using var context = Ctx();
        Assert.Equal(MetadataProposalStatus.Rejected, context.MetadataProposals.Single(p => p.Field == MetadataProposalField.Number).Status);
        Assert.Equal(MetadataProposalStatus.Accepted, context.MetadataProposals.Single(p => p.Field == MetadataProposalField.Year).Status);
    }

    [Fact]
    public void AcceptAllAdPages_AcceptsEveryGroup()
    {
        SeedAdProposals(); // 2 pages on one ad + 1 on another
        var vm = CreateViewModel();
        Assert.Equal(2, vm.AdPageGroupItems.Count);

        Confirm(vm.AcceptAllAdPagesConfirm);

        Assert.Empty(vm.AdPageGroupItems);
        using var context = Ctx();
        Assert.Equal(3, context.AdPageProposals.Count(p => p.Status == AdPageProposalStatus.Accepted));
    }

    [Fact]
    public void RejectAllAdPages_RejectsEveryGroup()
    {
        SeedAdProposals();
        var vm = CreateViewModel();

        Confirm(vm.RejectAllAdPagesConfirm);

        Assert.Empty(vm.AdPageGroupItems);
        using var context = Ctx();
        Assert.Equal(3, context.AdPageProposals.Count(p => p.Status == AdPageProposalStatus.Rejected));
    }

    // --- Library Health sub-tabs work (docs/superpowers/specs/2026-09-26-library-health-subtabs-design.md): direct Content Type
    // query, slim duplicate load, background/per-queue refresh, grouped Pending proposals ---

    [Fact]
    public void ContentType_ListsUnknownSeriesThatHaveIssues_AndNothingElse()
    {
        using (var context = Ctx())
        {
            var mystery = new Series { Name = "Mystery", ContentType = ContentType.Unknown };
            mystery.Issues.Add(new Issue { Number = "1" });
            context.Series.Add(mystery);
            context.Series.Add(new Series { Name = "Empty Unknown", ContentType = ContentType.Unknown }); // no issues: never listed
            var manga = new Series { Name = "Known", ContentType = ContentType.Manga };
            manga.Issues.Add(new Issue { Number = "1" });
            context.Series.Add(manga);
            context.SaveChanges();
        }

        var vm = CreateViewModel();

        var item = Assert.Single(vm.ContentTypeItems);
        Assert.Equal("Mystery", item.SeriesName);
    }

    [Fact]
    public void Duplicates_SlimLoad_GroupsAndLabelsLikeTheFullLoad()
    {
        int bigA, smallA;
        using (var context = Ctx())
        {
            var series = context.Series.Single();
            var a1 = new Issue { SeriesId = series.Id, Number = "12", FilePath = @"C:\lib\a1.cbz", FileSize = 50_000_000 };
            var a2 = new Issue { SeriesId = series.Id, Number = "12", FilePath = @"C:\lib\a2.cbz", FileSize = 10_000_000 };
            var single = new Issue { SeriesId = series.Id, Number = "13", FilePath = @"C:\lib\b.cbz", FileSize = 1 };
            context.Issues.AddRange(a1, a2, single);
            context.SaveChanges();
            bigA = a1.Id;
            smallA = a2.Id;
        }

        var vm = CreateViewModel();

        var group = Assert.Single(vm.DuplicateGroupItems);
        Assert.Equal("Kilo Station #12 · 2 copies", group.GroupLabel);
        Assert.Equal(new[] { bigA, smallA }, group.IssueIds); // largest first, same default-keep order as before

        using var verify = Ctx();
        var full = Paperbunkr.Data.SmartLists.SmartListQueryBuilder.BuildDuplicateGroups(verify.Issues.Include(i => i.Series).Where(i => !i.IsPlaceholder).ToList());
        Assert.Equal(full.Select(g => g.Select(i => i.Id).ToList()).ToList(), vm.DuplicateGroupItems.Select(g => g.IssueIds.ToList()).ToList());
    }

    [Fact]
    public void Refresh_OneQueue_LeavesTheOthersUntouched()
    {
        var vm = CreateViewModel();
        Assert.Empty(vm.PendingProposalGroups);
        SeedAdProposals();
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);

        vm.Refresh(NeedsReviewViewModel.Queue.AdPages);

        Assert.Equal(2, vm.AdPageGroupItems.Count);
        Assert.Empty(vm.PendingProposalGroups); // the proposals queue was not reloaded

        vm.Refresh(NeedsReviewViewModel.Queue.Proposals);

        Assert.Single(vm.PendingProposalGroups);
    }

    [Fact]
    public void CountLabels_ShowAnEllipsisUntilTheFirstRefreshHasFinished()
    {
        var vm = new NeedsReviewViewModel(_ => { }, loadOnConstruction: false);

        Assert.False(vm.HasLoaded);
        Assert.True(vm.AreCountsLoading);
        Assert.Equal("…", vm.PendingCountLabel);
        Assert.Equal("…", vm.DuplicateCountLabel);
        Assert.Equal("…", vm.ProposalSummaryLabel);

        vm.Refresh();

        Assert.True(vm.HasLoaded);
        Assert.Equal("0", vm.PendingCountLabel);
        Assert.Equal("0 pending · 0 applied", vm.ProposalSummaryLabel);
    }

    private static void Await(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            TestDispatcher.Drain(); // the result is applied through the UI thread's dispatcher
            Thread.Sleep(5);
        }

        TestDispatcher.Drain();
        Assert.True(task.IsCompleted, "the refresh did not finish");
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void RefreshAsync_LoadsInTheBackground_ThenAppliesTheResult()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);
        var vm = new NeedsReviewViewModel(_ => { }, loadOnConstruction: false);

        var task = vm.RefreshAsync();
        Assert.True(vm.IsRefreshing);
        Assert.False(vm.HasLoaded);
        Await(task);

        Assert.False(vm.IsRefreshing);
        Assert.True(vm.HasLoaded);
        Assert.Equal(1, vm.PendingProposalCount);
        Assert.Equal("1", vm.PendingCountLabel);
    }

    [Fact]
    public void RefreshAsync_ASynchronousRefreshThatSupersedesIt_WinsAndClearsIsRefreshing()
    {
        var vm = new NeedsReviewViewModel(_ => { }, loadOnConstruction: false);
        var task = vm.RefreshAsync();
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);

        vm.Refresh(); // newer than the async one, so the async result must be dropped, not applied over it
        Await(task);

        Assert.False(vm.IsRefreshing);
        Assert.Equal(1, vm.PendingProposalCount);
    }

    [Fact]
    public void PendingProposals_AreBucketedByFieldAndSource_WithPerGroupAcceptAndReject()
    {
        AddProposal(MetadataProposalField.Number, "1", MetadataProposalStatus.Pending);
        AddProposal(MetadataProposalField.Number, "2", MetadataProposalStatus.Pending);
        AddProposal(MetadataProposalField.Year, "2001", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();

        Assert.Equal(2, vm.PendingProposalGroups.Count);
        Assert.Equal(3, vm.PendingProposalCount);
        var number = vm.PendingProposalGroups.Single(g => g.Field == MetadataProposalField.Number);
        Assert.True(number.IsPending);
        Assert.Equal(2, number.Count);

        Confirm(number.AcceptAllConfirm);

        var remaining = Assert.Single(vm.PendingProposalGroups);
        Assert.Equal(MetadataProposalField.Year, remaining.Field);
        using var context = Ctx();
        Assert.Equal(2, context.MetadataProposals.Count(p => p.Field == MetadataProposalField.Number && p.Status == MetadataProposalStatus.Accepted && p.ReviewedAt != null));
        Assert.Equal(MetadataProposalStatus.Pending, context.MetadataProposals.Single(p => p.Field == MetadataProposalField.Year).Status);
    }

    [Fact]
    public void RejectAllOnAPendingGroup_RejectsOnlyThatGroup()
    {
        AddProposal(MetadataProposalField.Number, "1", MetadataProposalStatus.Pending);
        AddProposal(MetadataProposalField.Year, "2001", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();
        var year = vm.PendingProposalGroups.Single(g => g.Field == MetadataProposalField.Year);

        Confirm(year.RejectAllConfirm);

        Assert.Equal(MetadataProposalField.Number, Assert.Single(vm.PendingProposalGroups).Field);
        using var context = Ctx();
        Assert.Equal(MetadataProposalStatus.Rejected, context.MetadataProposals.Single(p => p.Field == MetadataProposalField.Year).Status);
    }

    [Fact]
    public void AcceptAllOnAPendingSeriesFieldGroup_ActuallyMovesTheIssue()
    {
        AddProposal(MetadataProposalField.Series, "Renamed Series", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();

        Confirm(Assert.Single(vm.PendingProposalGroups).AcceptAllConfirm);

        using var context = Ctx();
        Assert.Equal("Renamed Series", context.Issues.Include(i => i.Series).Single(i => i.Id == _issueId).Series!.Name);
    }

    // --- Applied proposals: bucketed by field + source, rows built only on expand, per-row Accept is durable (ReviewedAt) ---

    private static IReadOnlyList<MetadataProposalRowViewModel> PendingRows(NeedsReviewViewModel vm)
    {
        var group = Assert.Single(vm.PendingProposalGroups);
        if (!group.IsExpanded)
        {
            group.ToggleExpandedCommand.Execute(null);
        }

        return group.Rows;
    }

    private static IReadOnlyList<MetadataProposalRowViewModel> AppliedRows(NeedsReviewViewModel vm)
    {
        var group = Assert.Single(vm.AppliedProposalGroups);
        if (!group.IsExpanded)
        {
            group.ToggleExpandedCommand.Execute(null);
        }

        return group.Rows;
    }

    private void AddManyApplied(MetadataProposalField field, int count, MetadataProposalSource source = MetadataProposalSource.FilenameParser)
    {
        using var context = Ctx();
        for (int i = 0; i < count; i++)
        {
            context.MetadataProposals.Add(new MetadataProposal
            {
                IssueId = _issueId,
                Field = field,
                ProposedValue = i.ToString(),
                Source = source,
                Confidence = 0.6m,
                Status = MetadataProposalStatus.Accepted,
                CreatedAt = DateTime.UtcNow.AddSeconds(i),
            });
        }

        context.SaveChanges();
    }

    [Fact]
    public void Refresh_AppliedProposals_AreBucketedByFieldAndSource_WithCounts()
    {
        AddManyApplied(MetadataProposalField.Number, 3);
        AddManyApplied(MetadataProposalField.Year, 2);
        AddManyApplied(MetadataProposalField.Year, 1, MetadataProposalSource.MetadataProvider);

        var vm = CreateViewModel();

        Assert.Equal(3, vm.AppliedProposalGroups.Count);
        Assert.Equal(6, vm.AppliedProposalCount);
        var biggest = vm.AppliedProposalGroups[0]; // biggest bucket first
        Assert.Equal("Number · FilenameParser", biggest.Title);
        Assert.Equal(3, biggest.Count);
        Assert.Empty(biggest.Rows); // nothing built until expanded
    }

    [Fact]
    public void ExpandingAGroup_BuildsOnlyTheLatestRows_AndSaysSo()
    {
        AddManyApplied(MetadataProposalField.Number, ProposalGroupViewModel.RowLimit + 5);
        var vm = CreateViewModel();
        var group = Assert.Single(vm.AppliedProposalGroups);
        Assert.False(group.HasHiddenRows);

        group.ToggleExpandedCommand.Execute(null);

        Assert.Equal(ProposalGroupViewModel.RowLimit, group.Rows.Count);
        Assert.True(group.HasHiddenRows);
        Assert.Contains("Accept All / Reject All cover the whole group", group.HiddenRowsLabel);
    }

    [Fact]
    public void AcceptOnAnAppliedRow_IsDurable_ItLeavesTheListAndStaysApplied()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Accepted);
        var vm = CreateViewModel();
        var row = Assert.Single(AppliedRows(vm));
        var group = vm.AppliedProposalGroups[0];

        row.AcceptCommand.Execute(null);

        Assert.Equal(0, group.Count);
        using (var context = Ctx())
        {
            var proposal = context.MetadataProposals.Single();
            Assert.Equal(MetadataProposalStatus.Accepted, proposal.Status); // still applied, still feeds the Effective* resolvers
            Assert.NotNull(proposal.ReviewedAt);
        }

        Assert.Empty(CreateViewModel().AppliedProposalGroups); // and it does not come back on the next refresh
    }

    [Fact]
    public void AcceptAllApplied_MarksEverythingReviewed_KeepsStatusAccepted()
    {
        AddManyApplied(MetadataProposalField.Number, 3);
        AddManyApplied(MetadataProposalField.Year, 2);
        var vm = CreateViewModel();

        vm.AcceptAllAppliedCommand.Execute(null);
        TestDispatcher.Drain();

        Assert.Empty(vm.AppliedProposalGroups);
        using var context = Ctx();
        Assert.Equal(5, context.MetadataProposals.Count(p => p.Status == MetadataProposalStatus.Accepted && p.ReviewedAt != null));
    }

    [Fact]
    public void AcceptAllOnAGroup_OnlyTouchesThatGroup()
    {
        AddManyApplied(MetadataProposalField.Number, 3);
        AddManyApplied(MetadataProposalField.Year, 2);
        var vm = CreateViewModel();
        var numberGroup = vm.AppliedProposalGroups.Single(g => g.Field == MetadataProposalField.Number);

        numberGroup.AcceptAllCommand.Execute(null);
        TestDispatcher.Drain();

        var remaining = Assert.Single(vm.AppliedProposalGroups);
        Assert.Equal(MetadataProposalField.Year, remaining.Field);
        using var context = Ctx();
        Assert.Equal(3, context.MetadataProposals.Count(p => p.Field == MetadataProposalField.Number && p.ReviewedAt != null));
        Assert.Equal(0, context.MetadataProposals.Count(p => p.Field == MetadataProposalField.Year && p.ReviewedAt != null));
    }

    [Fact]
    public void RejectAllOnAGroup_NeedsTheSecondClick_ThenRejectsOnlyThatGroup()
    {
        AddManyApplied(MetadataProposalField.Number, 3);
        AddManyApplied(MetadataProposalField.Year, 2);
        var vm = CreateViewModel();
        var numberGroup = vm.AppliedProposalGroups.Single(g => g.Field == MetadataProposalField.Number);

        numberGroup.RejectAllConfirm.TriggerCommand.Execute(null);
        TestDispatcher.Drain();
        using (var context = Ctx())
        {
            Assert.Equal(0, context.MetadataProposals.Count(p => p.Status == MetadataProposalStatus.Rejected)); // only armed
        }

        numberGroup.RejectAllConfirm.TriggerCommand.Execute(null);
        TestDispatcher.Drain();

        Assert.Equal(MetadataProposalField.Year, Assert.Single(vm.AppliedProposalGroups).Field);
        using (var context = Ctx())
        {
            Assert.Equal(3, context.MetadataProposals.Count(p => p.Field == MetadataProposalField.Number && p.Status == MetadataProposalStatus.Rejected));
            Assert.Equal(2, context.MetadataProposals.Count(p => p.Field == MetadataProposalField.Year && p.Status == MetadataProposalStatus.Accepted));
        }
    }

    [Fact]
    public void RejectAllApplied_RevertsSeriesScopedFields_AndRejectsTheRest()
    {
        using (var context = Ctx())
        {
            context.Series.Single().Summary = "Provider-written summary.";
            context.SaveChanges();
        }

        AddSeriesProposal(MetadataProposalField.Summary, currentValue: "My own hand-written summary.", proposedValue: "Provider-written summary.");
        AddManyApplied(MetadataProposalField.Number, 2);
        var vm = CreateViewModel();

        Confirm(vm.RejectAllAppliedConfirm);

        Assert.Empty(vm.AppliedProposalGroups);
        using var verify = Ctx();
        Assert.Equal("My own hand-written summary.", verify.Series.Single().Summary); // the written field is restored
        Assert.All(verify.MetadataProposals, p => Assert.Equal(MetadataProposalStatus.Rejected, p.Status));
    }

    [Fact]
    public void AcceptOnAPendingRow_AlsoMarksItReviewed()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();

        Assert.Single(PendingRows(vm)).AcceptCommand.Execute(null);

        using var context = Ctx();
        Assert.NotNull(context.MetadataProposals.Single().ReviewedAt);
        Assert.Empty(CreateViewModel().AppliedProposalGroups);
    }

    [Fact]
    public void ToggleAppliedProposalsExpanded_FlipsFlag_DefaultsCollapsed()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsAppliedProposalsExpanded);

        vm.ToggleAppliedProposalsExpandedCommand.Execute(null);
        Assert.True(vm.IsAppliedProposalsExpanded);

        vm.ToggleAppliedProposalsExpandedCommand.Execute(null);
        Assert.False(vm.IsAppliedProposalsExpanded);
    }

    [Fact]
    public void Refresh_RejectedProposal_DoesNotAppear()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Rejected);

        var vm = CreateViewModel();

        Assert.False(vm.HasPendingProposalItems);
        Assert.Empty(vm.PendingProposalGroups);
    }

    [Fact]
    public void AcceptCommand_PendingProposal_UpdatesStatusInDatabase_AndRowState()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();
        var row = Assert.Single(PendingRows(vm));

        row.AcceptCommand.Execute(null);

        Assert.True(row.IsResolved);
        Assert.Equal("Accepted", row.ResolutionLabel);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var proposal = Assert.Single(context.MetadataProposals);
        Assert.Equal(MetadataProposalStatus.Accepted, proposal.Status);
        Assert.NotNull(proposal.ResolvedAt);
    }

    [Fact]
    public void RejectCommand_AcceptedProposal_UpdatesStatusInDatabase_AndRowState()
    {
        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Accepted);
        var vm = CreateViewModel();
        var row = Assert.Single(AppliedRows(vm));

        row.RejectCommand.Execute(null);

        Assert.True(row.IsResolved);
        Assert.Equal("Rejected", row.ResolutionLabel);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var proposal = Assert.Single(context.MetadataProposals);
        Assert.Equal(MetadataProposalStatus.Rejected, proposal.Status);
    }

    [Fact]
    public void HasPendingItems_ReflectsMetadataProposalSection()
    {
        Assert.False(CreateViewModel().HasPendingItems);

        AddProposal(MetadataProposalField.Number, "12", MetadataProposalStatus.Pending);

        Assert.True(CreateViewModel().HasPendingItems);
    }

    // --- Series-field proposals (docs/superpowers/specs/2026-08-17-metadata-model-phase2b-series-
    // reassignment-design.md) - write-time, unlike every other field above ---

    [Fact]
    public void AcceptCommand_SeriesProposal_ActuallyMovesTheIssue_NotJustFlipsStatus()
    {
        AddProposal(MetadataProposalField.Series, "Renamed Series", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();
        var row = Assert.Single(PendingRows(vm));

        row.AcceptCommand.Execute(null);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var issue = context.Issues.Include(i => i.Series).Single(i => i.Id == _issueId);
        Assert.Equal("Renamed Series", issue.Series!.Name);
        // The original "Kilo Station" series had only this one issue - it's gone now.
        Assert.DoesNotContain(context.Series, s => s.Name == "Kilo Station");
    }

    [Fact]
    public void RejectCommand_SeriesProposal_LeavesIssueOnItsOriginalSeries()
    {
        AddProposal(MetadataProposalField.Series, "Renamed Series", MetadataProposalStatus.Pending);
        var vm = CreateViewModel();
        var row = Assert.Single(PendingRows(vm));

        row.RejectCommand.Execute(null);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var issue = context.Issues.Include(i => i.Series).Single(i => i.Id == _issueId);
        Assert.Equal("Kilo Station", issue.Series!.Name);
        Assert.DoesNotContain(context.Series, s => s.Name == "Renamed Series");
    }

    // --- Series-scoped Summary/Status/Genre proposals (docs/superpowers/specs/2026-08-23-apply-
    // from-provider-design.md) - written directly on accept, unlike every Issue-scoped field above,
    // so Reject needs a real revert step rather than just a status flip. ---

    private int AddSeriesProposal(MetadataProposalField field, string? currentValue, string proposedValue)
    {
        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        int seriesId = context.Issues.Single(i => i.Id == _issueId).SeriesId;
        var proposal = new MetadataProposal
        {
            SeriesId = seriesId,
            Field = field,
            CurrentValue = currentValue,
            ProposedValue = proposedValue,
            Source = MetadataProposalSource.MetadataProvider,
            ProviderKey = ExternalMetadataProvider.MangaBaka,
            Confidence = 1.0m,
            Status = MetadataProposalStatus.Accepted,
            ResolvedAt = DateTime.UtcNow,
        };
        context.MetadataProposals.Add(proposal);
        context.SaveChanges();
        return proposal.Id;
    }

    [Fact]
    public void Refresh_SeriesScopedProposal_LabelIsJustTheSeriesName()
    {
        AddSeriesProposal(MetadataProposalField.Summary, null, "A synopsis.");

        var vm = CreateViewModel();

        var row = Assert.Single(AppliedRows(vm));
        Assert.Equal("Kilo Station", row.IssueLabel);
        Assert.True(row.IsAlreadyAccepted);
    }

    [Fact]
    public void RejectCommand_SeriesScopedProposal_RevertsFieldToSnapshottedCurrentValue()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            var series = context.Series.Single();
            series.Summary = "Provider-written summary.";
            context.SaveChanges();
        }

        AddSeriesProposal(MetadataProposalField.Summary, currentValue: "My own hand-written summary.", proposedValue: "Provider-written summary.");
        var vm = CreateViewModel();
        var row = Assert.Single(AppliedRows(vm));

        row.RejectCommand.Execute(null);

        using var verifyContext = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        Assert.Equal("My own hand-written summary.", verifyContext.Series.Single().Summary);
        Assert.Equal(MetadataProposalStatus.Rejected, verifyContext.MetadataProposals.Single().Status);
    }

    [Fact]
    public void RejectCommand_SeriesScopedStatusProposal_RevertsToParsedPriorEnumValue()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            var series = context.Series.Single();
            series.Status = SeriesStatus.Ongoing;
            context.SaveChanges();
        }

        AddSeriesProposal(MetadataProposalField.Status, currentValue: "Completed", proposedValue: "Ongoing");
        var vm = CreateViewModel();
        var row = Assert.Single(AppliedRows(vm));

        row.RejectCommand.Execute(null);

        using var verifyContext = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        Assert.Equal(SeriesStatus.Completed, verifyContext.Series.Single().Status);
    }

    // ===== Advertisement pages (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md 5) =====

    private PaperbunkrDbContext Ctx() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    /// <summary>Two pending proposals matching one ad, and one matching a second ad.</summary>
    private (int FirstAd, int SecondAd, int[] ProposalIds) SeedAdProposals()
    {
        using var context = Ctx();
        var first = new AdPageHash { Hash = 1, SourceIssueId = _issueId, SourcePageNumber = 7, CreatedAt = DateTime.UtcNow };
        var second = new AdPageHash { Hash = 2, CreatedAt = DateTime.UtcNow };
        context.AdPageHashes.AddRange(first, second);
        context.SaveChanges();
        var proposals = new[]
        {
            new AdPageProposal { IssueId = _issueId, PageNumber = 18, MatchedAdHashId = first.Id, Distance = 0, CreatedAt = DateTime.UtcNow },
            new AdPageProposal { IssueId = _issueId, PageNumber = 19, MatchedAdHashId = first.Id, Distance = 3, CreatedAt = DateTime.UtcNow },
            new AdPageProposal { IssueId = _issueId, PageNumber = 2, MatchedAdHashId = second.Id, Distance = 5, CreatedAt = DateTime.UtcNow },
        };
        context.AdPageProposals.AddRange(proposals);
        context.SaveChanges();
        return (first.Id, second.Id, proposals.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void AdPages_NoProposals_MeansNoSectionAndNothingPending()
    {
        var vm = CreateViewModel();

        Assert.Empty(vm.AdPageGroupItems);
        Assert.False(vm.HasAdPageItems);
        Assert.False(vm.HasPendingItems);
    }

    [Fact]
    public void AdPages_ProposalsAreGroupedByTheAdTheyMatched()
    {
        var (firstAd, secondAd, _) = SeedAdProposals();
        var vm = CreateViewModel();

        Assert.True(vm.HasAdPageItems);
        Assert.True(vm.HasPendingItems);
        Assert.Equal(2, vm.AdPageGroupItems.Count);
        var big = Assert.Single(vm.AdPageGroupItems, g => g.AdHashId == firstAd);
        Assert.Equal(2, big.MatchCount);
        Assert.Equal("2 pages match this ad", big.CountLabel);
        Assert.Equal("Kilo Station #? · page 8", big.SourceLabel); // source page 7 (0-based) shown as page 8
        var small = Assert.Single(vm.AdPageGroupItems, g => g.AdHashId == secondAd);
        Assert.Equal("1 page matches this ad", small.CountLabel);
        Assert.Equal("Ad from a removed issue", small.SourceLabel);
    }

    [Fact]
    public void AdPages_OnlyPendingProposalsAreListed()
    {
        var (_, _, ids) = SeedAdProposals();
        using (var context = Ctx())
        {
            context.AdPageProposals.Find(ids[0])!.Status = AdPageProposalStatus.Rejected;
            context.AdPageProposals.Find(ids[1])!.Status = AdPageProposalStatus.Accepted;
            context.SaveChanges();
        }

        var vm = CreateViewModel();

        var group = Assert.Single(vm.AdPageGroupItems);
        Assert.Equal(1, group.MatchCount);
    }

    [Fact]
    public void AdPages_PageRowsShowTheirMatchQuality()
    {
        SeedAdProposals();
        var vm = CreateViewModel();

        var pages = vm.AdPageGroupItems.SelectMany(g => g.Pages).OrderBy(p => p.Distance).ToList();

        Assert.Equal("Exact match", pages[0].MatchLabel);
        Assert.Equal("3 bits off", pages[1].MatchLabel);
        Assert.Equal("Kilo Station #? · page 19", pages.Single(p => p.PageNumber == 18).Label);
    }

    [Fact]
    public void AcceptAll_TagsEveryPageInTheGroup_ButNotTheOtherGroup()
    {
        var (firstAd, _, _) = SeedAdProposals();
        var vm = CreateViewModel();
        var group = vm.AdPageGroupItems.Single(g => g.AdHashId == firstAd);

        group.AcceptAllCommand.Execute(null);
        TestDispatcher.Drain(); // the refresh is deferred one dispatcher tick

        using var context = Ctx();
        Assert.Equal(new[] { 18, 19 }, context.IssuePages.OrderBy(p => p.PageNumber).Select(p => p.PageNumber).ToArray());
        Assert.All(context.IssuePages.ToList(), p => Assert.Equal(PageType.Advertisement, p.PageType));
        Assert.Equal(2, context.AdPageProposals.Count(p => p.Status == AdPageProposalStatus.Accepted));
        var remaining = Assert.Single(vm.AdPageGroupItems);
        Assert.NotEqual(firstAd, remaining.AdHashId);
    }

    [Fact]
    public void RejectAll_WritesNoTags_AndRemembersTheRejection()
    {
        var (firstAd, _, _) = SeedAdProposals();
        var vm = CreateViewModel();

        vm.AdPageGroupItems.Single(g => g.AdHashId == firstAd).RejectAllCommand.Execute(null);
        TestDispatcher.Drain();

        using var context = Ctx();
        Assert.Empty(context.IssuePages.ToList());
        Assert.Equal(2, context.AdPageProposals.Count(p => p.Status == AdPageProposalStatus.Rejected));
        Assert.Single(vm.AdPageGroupItems);
    }

    [Fact]
    public void SinglePage_Accept_AndReject_ResolveJustThatPage()
    {
        var (firstAd, _, _) = SeedAdProposals();
        var vm = CreateViewModel();
        var group = vm.AdPageGroupItems.Single(g => g.AdHashId == firstAd);
        var exact = group.Pages.Single(p => p.PageNumber == 18);
        var near = group.Pages.Single(p => p.PageNumber == 19);

        exact.AcceptCommand.Execute(null);
        TestDispatcher.Drain();
        near.RejectCommand.Execute(null);
        TestDispatcher.Drain();

        using var context = Ctx();
        var tag = Assert.Single(context.IssuePages.ToList());
        Assert.Equal(18, tag.PageNumber);
        Assert.Equal(AdPageProposalStatus.Accepted, context.AdPageProposals.Single(p => p.PageNumber == 18).Status);
        Assert.Equal(AdPageProposalStatus.Rejected, context.AdPageProposals.Single(p => p.PageNumber == 19).Status);
        Assert.Single(vm.AdPageGroupItems); // only the second ad's page is still pending
    }

    [Fact]
    public void AdPages_PendingThenResolved_ClearsHasPendingItems()
    {
        SeedAdProposals();
        var vm = CreateViewModel();
        Assert.True(vm.HasPendingItems);

        foreach (var group in vm.AdPageGroupItems.ToList())
        {
            group.RejectAllCommand.Execute(null);
            TestDispatcher.Drain();
        }

        Assert.False(vm.HasAdPageItems);
        Assert.False(vm.HasPendingItems);
    }

    [Fact]
    public void ToggleExpanded_ShowsAndHidesThePageList()
    {
        SeedAdProposals();
        var group = CreateViewModel().AdPageGroupItems.First();
        Assert.False(group.IsExpanded);

        group.ToggleExpandedCommand.Execute(null);
        Assert.True(group.IsExpanded);
        group.ToggleExpandedCommand.Execute(null);
        Assert.False(group.IsExpanded);
    }

    private sealed class NoOpFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }
}
