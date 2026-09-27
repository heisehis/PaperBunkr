using Paperbunkr.App.Scraper;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Organizing;

namespace Paperbunkr.App.Tests;

/// <summary>Implementation plan (docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-plan.md)
/// Step 7 verification.</summary>
public sealed class ScraperScrapeBatchHeaderViewModelTests
{
    [Fact]
    public void ProgressLabel_reflects_current_index_and_total()
    {
        var vm = new ScrapeBatchHeaderViewModel(5, () => { });

        vm.ReportProgress(ScrapePhase.Single, 5, 3, "Batman #3", null);

        Assert.Equal("Book 3 of 5", vm.ProgressLabel);
        Assert.Equal("Batman #3", vm.CurrentBookLabel);
    }

    [Fact]
    public void ProgressLabel_reflects_the_two_auto_choose_phases_separately()
    {
        // 2026-09-25 grilling round (Two labeled phases, Recommended) - a two-phase auto-choose run's
        // Reviewing phase gets its own distinct label and its own separate count, not a continuation
        // of AutoMatching's total (which would make the total visibly shrink, looking like a glitch).
        var vm = new ScrapeBatchHeaderViewModel(10, () => { });

        vm.ReportProgress(ScrapePhase.AutoMatching, 10, 4, "Batman #4", null);
        Assert.Equal("Auto-matching 4 of 10", vm.ProgressLabel);

        vm.ReportProgress(ScrapePhase.Reviewing, 3, 1, "Batman #7", null);
        Assert.Equal("Reviewing 1 of 3", vm.ProgressLabel);
    }

    [Fact]
    public void ReportProgress_with_null_cover_bytes_leaves_CurrentCover_null()
    {
        var vm = new ScrapeBatchHeaderViewModel(1, () => { });

        vm.ReportProgress(ScrapePhase.Single, 1, 1, "Batman #1", null);

        Assert.Null(vm.CurrentCover);
    }

    [Fact]
    public void ReportProgress_with_malformed_cover_bytes_leaves_CurrentCover_null_rather_than_throwing()
    {
        var vm = new ScrapeBatchHeaderViewModel(1, () => { });

        vm.ReportProgress(ScrapePhase.Single, 1, 1, "Batman #1", new byte[] { 1, 2, 3 });

        Assert.Null(vm.CurrentCover);
    }

    [Fact]
    public void Cancel_invokes_the_supplied_callback_exactly_once()
    {
        int calls = 0;
        var vm = new ScrapeBatchHeaderViewModel(1, () => calls++);

        vm.CancelCommand.Execute(null);

        Assert.Equal(1, calls);
    }
}
