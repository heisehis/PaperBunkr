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

        Assert.Same(vm.Candidates[0], vm.SelectedCandidate);
        Assert.True(vm.Candidates[0].IsSelected);
        Assert.True(vm.Candidates[0].IsTopMatch);
        Assert.False(vm.Candidates[1].IsTopMatch);
        Assert.True(vm.Candidates[1].IsFirstOtherResult);
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
    public void ProfileSelectDialog_choosing_a_profile_resolves_with_that_profile()
    {
        var alpha = new OrganizerProfile { Id = 1, Name = "Alpha" };
        var beta = new OrganizerProfile { Id = 2, Name = "Beta" };
        OrganizerProfile? resolved = null;
        var vm = new ProfileSelectDialogViewModel(new[] { alpha, beta }, p => resolved = p);

        vm.Profiles[1].ChooseCommand.Execute(null);

        Assert.Same(beta, resolved);
    }

    [Fact]
    public void ProfileSelectDialog_cancel_resolves_with_null()
    {
        OrganizerProfile? resolved = new OrganizerProfile { Id = 1, Name = "Placeholder" };
        var vm = new ProfileSelectDialogViewModel(Array.Empty<OrganizerProfile>(), p => resolved = p);

        vm.CancelCommand.Execute(null);

        Assert.Null(resolved);
    }
}
