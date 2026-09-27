using System.Threading;
using Paperbunkr.App.Scraper;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Organizing;
using Paperbunkr.App.Scraper;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Organizing;
using Paperbunkr.App.Scraper;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Organizing;

namespace Paperbunkr.App.Tests;

/// <summary>Implementation plan Phase 3 Step 3.5 verification - the dialog ViewModels' resolve
/// callback wiring, independent of any real Avalonia rendering.</summary>
public sealed class ScraperDialogViewModelTests
{
    [Fact]
    public void FileConflictDialog_replace_resolves_with_Replace_and_the_checkbox_state()
    {
        (CollisionResolution Resolution, bool ApplyToAllRemaining)? captured = null;
        var vm = new FileConflictDialogViewModel("Incoming", "Existing", "C:\\dest.cbz", result => captured = result);
        vm.ApplyToAllRemaining = true;

        vm.ReplaceCommand.Execute(null);

        Assert.Equal((CollisionResolution.Replace, true), captured);
    }

    [Fact]
    public void FileConflictDialog_rename_resolves_with_Rename()
    {
        (CollisionResolution Resolution, bool ApplyToAllRemaining)? captured = null;
        var vm = new FileConflictDialogViewModel("Incoming", "Existing", "C:\\dest.cbz", result => captured = result);

        vm.RenameCommand.Execute(null);

        Assert.Equal(CollisionResolution.Rename, captured!.Value.Resolution);
        Assert.False(captured.Value.ApplyToAllRemaining);
    }

    [Fact]
    public void FileConflictDialog_skip_resolves_with_Skip()
    {
        (CollisionResolution Resolution, bool ApplyToAllRemaining)? captured = null;
        var vm = new FileConflictDialogViewModel("Incoming", "Existing", "C:\\dest.cbz", result => captured = result);

        vm.SkipCommand.Execute(null);

        Assert.Equal(CollisionResolution.Skip, captured!.Value.Resolution);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(1, "1 issue")]
    [InlineData(2, "2 issues")]
    [InlineData(0, "0 issues")]
    public void ComicVineMatchCandidate_IssueCountLabel_singularizes_exactly_one(int? count, string expected)
    {
        var volume = new ComicVineVolumeSearchResult(1, "Batman", null, null, count, null);
        var candidate = new ComicVineMatchCandidateViewModel(volume, 0, _ => { });

        Assert.Equal(expected, candidate.IssueCountLabel);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_orders_candidates_by_descending_score()
    {
        var low = new ComicVineVolumeSearchResult(1, "Low", null, null, null, null);
        var high = new ComicVineVolumeSearchResult(2, "High", null, null, null, null);
        var vm = new ComicVineMatchReviewDialogViewModel(
            "Batman #1",
            "Batman",
            new[] { (low, 10.0), (high, 90.0) },
            NoOpSearch,
            _ => { });

        Assert.Equal("High", vm.Candidates[0].Volume.Name);
        Assert.Equal("Low", vm.Candidates[1].Volume.Name);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_top_candidate_is_pre_selected()
    {
        var low = new ComicVineVolumeSearchResult(1, "Low", null, null, null, null);
        var high = new ComicVineVolumeSearchResult(2, "High", null, null, null, null);
        var vm = new ComicVineMatchReviewDialogViewModel(
            "Batman #1", "Batman", new[] { (low, 10.0), (high, 90.0) }, NoOpSearch, _ => { });

        // "Best match"/"Other results" (IsTopMatch/IsFirstOtherResult) went away with the DataGrid
        // redesign (docs/superpowers/specs/2026-09-24-scraper-review-tables-and-batch-summary-
        // design.md §1.1) - CE's own table has no such split either, just one flat list defaulting to
        // Score descending. Pre-selection (still rank-based) is the only thing left to verify here.
        Assert.Same(vm.Candidates[0], vm.SelectedCandidate);
        Assert.True(vm.Candidates[0].IsSelected);
        Assert.Equal("High", vm.Candidates[0].Volume.Name); // the higher-scored candidate, not insertion order
    }

    [Fact]
    public void ComicVineMatchReviewDialog_selecting_a_candidate_does_not_resolve_the_dialog()
    {
        ComicVineVolumeSearchResult? resolved = null;
        var volume = new ComicVineVolumeSearchResult(1, "Batman", "1990", "DC Comics", 50, null);
        var vm = new ComicVineMatchReviewDialogViewModel("Batman #1", "Batman", new[] { (volume, 100.0) }, NoOpSearch, v => resolved = v);
        // Constructing already pre-selects the top match - re-select explicitly to exercise the
        // command path itself, not just the constructor's own pre-selection.
        vm.Candidates[0].SelectCommand.Execute(null);

        Assert.Null(resolved);
        Assert.Same(vm.Candidates[0], vm.SelectedCandidate);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_confirm_resolves_with_the_selected_volume()
    {
        ComicVineVolumeSearchResult? resolved = null;
        var volume = new ComicVineVolumeSearchResult(1, "Batman", "1990", "DC Comics", 50, null);
        var vm = new ComicVineMatchReviewDialogViewModel("Batman #1", "Batman", new[] { (volume, 100.0) }, NoOpSearch, v => resolved = v);

        vm.ConfirmCommand.Execute(null);

        Assert.Same(volume, resolved);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_confirm_is_disabled_with_no_selection()
    {
        var vm = new ComicVineMatchReviewDialogViewModel(
            "Batman #1", "Batman", Array.Empty<(ComicVineVolumeSearchResult, double)>(), NoOpSearch, _ => { });

        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task ComicVineMatchReviewDialog_show_issues_loads_issues_for_the_selected_volume_and_shows_them_embedded()
    {
        var volume = new ComicVineVolumeSearchResult(1, "Batman", "1990", "DC Comics", 50, null);
        var issue = new ComicVineIssueSummary(1, "1", "First", null);
        int? requestedVolumeId = null;
        var vm = new ComicVineMatchReviewDialogViewModel(
            "Batman #1", "Batman", new[] { (volume, 100.0) }, NoOpSearch, _ => { },
            loadIssues: volumeId =>
            {
                requestedVolumeId = volumeId;
                return Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(new[] { issue });
            });

        Assert.Null(vm.IssuePeek); // not shown until ShowIssues actually runs
        await vm.ShowIssuesCommand.ExecuteAsync(null);

        Assert.Equal(1, requestedVolumeId);
        Assert.NotNull(vm.IssuePeek);
        Assert.True(vm.IssuePeek!.ReadOnlyPeek);
        Assert.Same(issue, Assert.Single(vm.IssuePeek.Candidates).Issue);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_closing_the_issue_peek_clears_it_without_resolving_the_series_dialog()
    {
        var volume = new ComicVineVolumeSearchResult(1, "Batman", "1990", "DC Comics", 50, null);
        var issue = new ComicVineIssueSummary(1, "1", "First", null);
        ComicVineVolumeSearchResult? resolved = null;
        var vm = new ComicVineMatchReviewDialogViewModel(
            "Batman #1", "Batman", new[] { (volume, 100.0) }, NoOpSearch, v => resolved = v,
            loadIssues: _ => Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(new[] { issue }));
        vm.ShowIssuesCommand.Execute(null);
        Assert.NotNull(vm.IssuePeek);

        vm.IssuePeek!.CloseCommand.Execute(null);

        Assert.Null(vm.IssuePeek);
        Assert.Null(resolved); // the series dialog itself is untouched by closing the peek
    }

    [Fact]
    public void ComicVineMatchReviewDialog_skip_resolves_with_null()
    {
        ComicVineVolumeSearchResult? resolved = new(1, "Placeholder", null, null, null, null);
        var vm = new ComicVineMatchReviewDialogViewModel("Batman #1", "Batman", Array.Empty<(ComicVineVolumeSearchResult, double)>(), NoOpSearch, v => resolved = v);

        vm.SkipCommand.Execute(null);

        Assert.Null(resolved);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_skip_permanently_marks_the_book_and_resolves_with_null()
    {
        // docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-plan.md Step 16 - same outcome
        // as a plain Skip for this run, plus the durable marker callback fires exactly once.
        ComicVineVolumeSearchResult? resolved = new(1, "Placeholder", null, null, null, null);
        int markCalls = 0;
        var vm = new ComicVineMatchReviewDialogViewModel(
            "Batman #1", "Batman", Array.Empty<(ComicVineVolumeSearchResult, double)>(), NoOpSearch, v => resolved = v,
            markPermanentlySkipped: () => markCalls++);

        vm.SkipPermanentlyCommand.Execute(null);

        Assert.Null(resolved);
        Assert.Equal(1, markCalls);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_plain_skip_never_calls_the_permanent_skip_marker()
    {
        int markCalls = 0;
        var vm = new ComicVineMatchReviewDialogViewModel(
            "Batman #1", "Batman", Array.Empty<(ComicVineVolumeSearchResult, double)>(), NoOpSearch, _ => { },
            markPermanentlySkipped: () => markCalls++);

        vm.SkipCommand.Execute(null);

        Assert.Equal(0, markCalls);
    }

    [Fact]
    public void ComicVineMatchReviewDialog_SkipLabel_reflects_IsCtrlHeldOverSkip()
    {
        var vm = new ComicVineMatchReviewDialogViewModel("Batman #1", "Batman", Array.Empty<(ComicVineVolumeSearchResult, double)>(), NoOpSearch, _ => { });

        Assert.Equal("Skip this book", vm.SkipLabel);
        vm.IsCtrlHeldOverSkip = true;
        Assert.Equal("Skip this book (always)", vm.SkipLabel);
    }

    [Fact]
    public async Task ComicVineMatchReviewDialog_search_replaces_candidates_with_the_new_results()
    {
        var volume = new ComicVineVolumeSearchResult(1, "Batman Beyond", "1999", "DC Comics", 24, null);
        var vm = new ComicVineMatchReviewDialogViewModel(
            "Batman #1",
            "Batman",
            Array.Empty<(ComicVineVolumeSearchResult, double)>(),
            (query, _) => Task.FromResult<IReadOnlyList<(ComicVineVolumeSearchResult, double)>>(new[] { (volume, 50.0) }),
            _ => { });

        await vm.SearchCommand.ExecuteAsync(null);

        var found = Assert.Single(vm.Candidates);
        Assert.Equal("Batman Beyond", found.Volume.Name);
        Assert.Null(vm.SearchStatus);
    }

    [Fact]
    public async Task ComicVineMatchReviewDialog_search_with_no_results_sets_a_status_message()
    {
        var vm = new ComicVineMatchReviewDialogViewModel(
            "Batman #1",
            "Batmam",
            Array.Empty<(ComicVineVolumeSearchResult, double)>(),
            (query, _) => Task.FromResult<IReadOnlyList<(ComicVineVolumeSearchResult, double)>>(Array.Empty<(ComicVineVolumeSearchResult, double)>()),
            _ => { });

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Empty(vm.Candidates);
        Assert.Equal("No matches found - try a different search.", vm.SearchStatus);
    }

    private static Task<IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>> NoOpSearch(string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(ComicVineVolumeSearchResult, double)>>(Array.Empty<(ComicVineVolumeSearchResult, double)>());

    [Fact]
    public void ComicVineIssueReviewDialog_pre_selects_the_auto_matched_issue()
    {
        var issue1 = new ComicVineIssueSummary(1, "1", "First", null);
        var issue2 = new ComicVineIssueSummary(2, "2", "Second", null);
        var vm = new ComicVineIssueReviewDialogViewModel("Batman #2", new[] { issue1, issue2 }, issue2, readOnlyPeek: false, _ => { });

        Assert.Same(vm.Candidates[1], vm.SelectedCandidate);
        Assert.True(vm.Candidates[1].IsSelected);
        Assert.False(vm.Candidates[0].IsSelected);
    }

    [Fact]
    public void ComicVineIssueReviewDialog_sorts_candidates_naturally_not_by_the_apis_own_order()
    {
        // docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-plan.md Step 15 - ComicVine's
        // search results arrive in whatever order the API returned them, not issue-number order.
        var three = new ComicVineIssueSummary(3, "3", "Three", null);
        var one = new ComicVineIssueSummary(1, "1", "One", null);
        var two = new ComicVineIssueSummary(2, "2", "Two", null);
        var annual = new ComicVineIssueSummary(4, "Annual 1", "Annual", null);
        var vm = new ComicVineIssueReviewDialogViewModel("Batman", new[] { three, one, two, annual }, preSelected: null, readOnlyPeek: false, _ => { });

        Assert.Equal(new[] { "1", "2", "3", "Annual 1" }, vm.Candidates.Select(c => c.Issue.IssueNumber));
    }

    [Theory]
    [InlineData("2", "10", -1)]     // numeric, not lexical - "2" sorts before "10"
    [InlineData("4", "4a", -1)]
    [InlineData("4a", "5", -1)]
    [InlineData("3", "Annual 1", -1)]
    [InlineData("TPB", "TPB 5", -1)] // no embedded number at all vs. one that has one, same text prefix
    [InlineData("5", "5", 0)]        // equal
    public void NaturalKey_ordering_matches_CEs_real_natural_compare_for_realistic_issue_numbers(string a, string b, int expectedSign)
    {
        int cmp = ComicVineIssueReviewDialogViewModel.NaturalKeyComparer.Instance.Compare(
            ComicVineIssueReviewDialogViewModel.NaturalKey(a), ComicVineIssueReviewDialogViewModel.NaturalKey(b));

        Assert.Equal(expectedSign, Math.Sign(cmp));
    }

    [Theory]
    [InlineData("2", "10")]
    [InlineData("4", "4a")]
    [InlineData("4a", "5")]
    [InlineData("3", "Annual 1")]
    [InlineData("TPB", "TPB 5")]
    public void NaturalSortKey_orders_the_same_way_as_NaturalKeyComparer(string a, string b)
    {
        // DataGrid's own sort only supports a single SortMemberPath redirect, not a custom multi-key
        // comparer (docs/superpowers/specs/2026-09-24-scraper-review-tables-and-batch-summary-
        // design.md §1.2) - NaturalSortKey has to reproduce NaturalKeyComparer's ordering through
        // plain ordinal string comparison alone.
        var issueA = new ComicVineIssueCandidateViewModel(new ComicVineIssueSummary(1, a, "A", null), _ => { });
        var issueB = new ComicVineIssueCandidateViewModel(new ComicVineIssueSummary(2, b, "B", null), _ => { });

        Assert.True(string.CompareOrdinal(issueA.NaturalSortKey, issueB.NaturalSortKey) < 0);
    }

    [Fact]
    public void NaturalSortKey_isEqual_ForEqualIssueNumbers()
    {
        var issueA = new ComicVineIssueCandidateViewModel(new ComicVineIssueSummary(1, "5", "A", null), _ => { });
        var issueB = new ComicVineIssueCandidateViewModel(new ComicVineIssueSummary(2, "5", "B", null), _ => { });

        Assert.Equal(issueA.NaturalSortKey, issueB.NaturalSortKey);
    }

    [Fact]
    public void NaturalKey_parses_a_unicode_fraction_between_its_neighboring_whole_numbers()
    {
        // CE's own utils.py:natural_key example (verified): ["5", "5¼", "5½", "6"] sorts in that order.
        var five = ComicVineIssueReviewDialogViewModel.NaturalKey("5");
        var fiveQuarter = ComicVineIssueReviewDialogViewModel.NaturalKey("5¼");
        var fiveHalf = ComicVineIssueReviewDialogViewModel.NaturalKey("5½");
        var six = ComicVineIssueReviewDialogViewModel.NaturalKey("6");

        Assert.True(five.Number < fiveQuarter.Number);
        Assert.True(fiveQuarter.Number < fiveHalf.Number);
        Assert.True(fiveHalf.Number < six.Number);
    }

    [Fact]
    public void ComicVineIssueReviewDialog_falls_back_to_the_first_issue_when_nothing_auto_matched()
    {
        var issue1 = new ComicVineIssueSummary(1, "1", "First", null);
        var vm = new ComicVineIssueReviewDialogViewModel("Batman #2", new[] { issue1 }, preSelected: null, readOnlyPeek: false, _ => { });

        Assert.Same(vm.Candidates[0], vm.SelectedCandidate);
    }

    [Fact]
    public void ComicVineIssueReviewDialog_confirm_resolves_with_the_selected_issue()
    {
        var issue1 = new ComicVineIssueSummary(1, "1", "First", null);
        ComicVineIssueReviewResult? resolved = null;
        var vm = new ComicVineIssueReviewDialogViewModel("Batman #2", new[] { issue1 }, issue1, readOnlyPeek: false, r => resolved = r);

        vm.ConfirmCommand.Execute(null);

        Assert.Equal(ComicVineIssueReviewOutcome.Confirmed, resolved!.Outcome);
        Assert.Same(issue1, resolved.Issue);
    }

    [Fact]
    public void ComicVineIssueReviewDialog_skip_resolves_as_skipped()
    {
        var issue1 = new ComicVineIssueSummary(1, "1", "First", null);
        ComicVineIssueReviewResult? resolved = null;
        var vm = new ComicVineIssueReviewDialogViewModel("Batman #2", new[] { issue1 }, issue1, readOnlyPeek: false, r => resolved = r);

        vm.SkipCommand.Execute(null);

        Assert.Equal(ComicVineIssueReviewOutcome.Skipped, resolved!.Outcome);
        Assert.Null(resolved.Issue);
    }

    [Fact]
    public void ComicVineIssueReviewDialog_skip_permanently_marks_the_book_and_resolves_as_skipped()
    {
        var issue1 = new ComicVineIssueSummary(1, "1", "First", null);
        ComicVineIssueReviewResult? resolved = null;
        int markCalls = 0;
        var vm = new ComicVineIssueReviewDialogViewModel(
            "Batman #2", new[] { issue1 }, issue1, readOnlyPeek: false, r => resolved = r,
            markPermanentlySkipped: () => markCalls++);

        vm.SkipPermanentlyCommand.Execute(null);

        Assert.Equal(ComicVineIssueReviewOutcome.Skipped, resolved!.Outcome);
        Assert.Equal(1, markCalls);
    }

    [Fact]
    public void ComicVineIssueReviewDialog_SkipLabel_reflects_IsCtrlHeldOverSkip()
    {
        var issue1 = new ComicVineIssueSummary(1, "1", "First", null);
        var vm = new ComicVineIssueReviewDialogViewModel("Batman #2", new[] { issue1 }, issue1, readOnlyPeek: false, _ => { });

        Assert.Equal("Skip", vm.SkipLabel);
        vm.IsCtrlHeldOverSkip = true;
        Assert.Equal("Skip (always)", vm.SkipLabel);
    }

    [Fact]
    public void ComicVineIssueReviewDialog_go_back_resolves_as_went_back()
    {
        var issue1 = new ComicVineIssueSummary(1, "1", "First", null);
        ComicVineIssueReviewResult? resolved = null;
        var vm = new ComicVineIssueReviewDialogViewModel("Batman #2", new[] { issue1 }, issue1, readOnlyPeek: false, r => resolved = r);

        vm.GoBackCommand.Execute(null);

        Assert.Equal(ComicVineIssueReviewOutcome.WentBack, resolved!.Outcome);
    }

    [Fact]
    public void ComicVineIssueReviewDialog_peek_mode_close_does_not_affect_orchestrator_state()
    {
        var issue1 = new ComicVineIssueSummary(1, "1", "First", null);
        int resolveCount = 0;
        var vm = new ComicVineIssueReviewDialogViewModel("Batman #2", new[] { issue1 }, issue1, readOnlyPeek: true, _ => resolveCount++);

        Assert.True(vm.ReadOnlyPeek);
        vm.CloseCommand.Execute(null);

        // Close does invoke the caller's own resolve delegate (to end the ShowModalAsync await), but
        // the caller wires a no-op for peek mode - this just confirms Close routes through that one
        // delegate, not a second, orchestrator-visible one.
        Assert.Equal(1, resolveCount);
    }

    [Fact]
    public void ProfileSelectDialog_running_resolves_with_the_ticked_profiles()
    {
        var alpha = new OrganizerProfile { Id = 1, Name = "Alpha" };
        var beta = new OrganizerProfile { Id = 2, Name = "Beta" };
        IReadOnlyList<OrganizerProfile>? resolved = null;
        var vm = new ProfileSelectDialogViewModel(new[] { alpha, beta }, p => resolved = p);

        vm.Profiles[0].IsSelected = false;
        vm.Profiles[1].IsSelected = true;
        vm.RunCommand.Execute(null);

        Assert.Same(beta, Assert.Single(resolved!));
    }

    [Fact]
    public void ProfileSelectDialog_cancel_resolves_with_null()
    {
        IReadOnlyList<OrganizerProfile>? resolved = new List<OrganizerProfile> { new() { Id = 1, Name = "Placeholder" } };
        var vm = new ProfileSelectDialogViewModel(Array.Empty<OrganizerProfile>(), p => resolved = p);

        vm.CancelCommand.Execute(null);

        Assert.Null(resolved);
    }

    // ScrapeBatchSummaryDialogViewModel (docs/superpowers/specs/2026-09-24-scraper-review-tables-and-
    // batch-summary-design.md §3.2) - CE's real FinishForm, the blocking end-of-batch modal, with
    // Paperbunkr's own richer per-bucket breakdown instead of CE's plain scraped/skipped pair.

    [Fact]
    public void ScrapeBatchSummaryDialog_SortsOutcomesIntoTheirOwnBuckets()
    {
        var outcomes = new[]
        {
            new ScrapeBookOutcome(1, "Batman #1", ScrapeOutcomeKind.Applied, "Matched \"Batman\""),
            new ScrapeBookOutcome(2, "Batman #2", ScrapeOutcomeKind.SkippedByUser, "Skipped"),
            new ScrapeBookOutcome(3, "Batman #3", ScrapeOutcomeKind.NoMatchFound, "No search results"),
            new ScrapeBookOutcome(4, "Batman #4", ScrapeOutcomeKind.Failed, "simulated failure"),
        };
        var vm = new ScrapeBatchSummaryDialogViewModel(new ScrapeBatchResult(outcomes), () => { });

        Assert.Equal(1, vm.Applied);
        Assert.Equal(1, vm.SkippedByUser);
        Assert.Equal(1, vm.NoMatchFound);
        Assert.Equal(1, vm.Failed);
        Assert.Equal(4, vm.Total);
        Assert.Equal(outcomes[0], Assert.Single(vm.AppliedOutcomes));
        Assert.Equal(outcomes[1], Assert.Single(vm.SkippedOutcomes));
        Assert.Equal(outcomes[2], Assert.Single(vm.NoMatchOutcomes));
        Assert.Equal(outcomes[3], Assert.Single(vm.FailedOutcomes));
        Assert.True(vm.HasApplied);
        Assert.True(vm.HasSkipped);
        Assert.True(vm.HasNoMatch);
        Assert.True(vm.HasFailed);
    }

    [Fact]
    public void ScrapeBatchSummaryDialog_EmptyBucketsReportAsNotHas()
    {
        var outcomes = new[] { new ScrapeBookOutcome(1, "Batman #1", ScrapeOutcomeKind.Applied, "Matched \"Batman\"") };
        var vm = new ScrapeBatchSummaryDialogViewModel(new ScrapeBatchResult(outcomes), () => { });

        Assert.True(vm.HasApplied);
        Assert.False(vm.HasSkipped);
        Assert.False(vm.HasNoMatch);
        Assert.False(vm.HasFailed);
        Assert.Empty(vm.SkippedOutcomes);
        Assert.Empty(vm.NoMatchOutcomes);
        Assert.Empty(vm.FailedOutcomes);
    }

    [Fact]
    public void ScrapeBatchSummaryDialog_Ok_ResolvesTheDialog()
    {
        bool resolved = false;
        var vm = new ScrapeBatchSummaryDialogViewModel(new ScrapeBatchResult(Array.Empty<ScrapeBookOutcome>()), () => resolved = true);

        vm.OkCommand.Execute(null);

        Assert.True(resolved);
    }
}
