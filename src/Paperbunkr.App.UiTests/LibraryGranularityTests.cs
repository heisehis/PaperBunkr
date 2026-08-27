using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of Library's Series/Issue card-content Granularity toggle (docs/
/// superpowers/specs/2026-08-18-library-book-centric-redesign-design.md Slice 3 + the same-session
/// follow-up that restored series cards as a switchable option). Post-4b the toggle and the
/// Sort/Group field lists live in the "View &amp; Sort" tabbed popup. Drives the real compiled exe
/// via FlaUI/UIA3 (see <see cref="AppFixture"/>).
/// </summary>
public class LibraryGranularityTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SwitchToSeriesGranularity_SwapsSortGroupOptionsToSeriesLevelFields()
    {
        var window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        LibraryToolbarDriver.SelectViewMode(window, "LibraryGranularityOption_Series");

        // The Sort tab should now offer series-card fields (LibrarySortField), not IssueList's.
        LibraryToolbarDriver.SelectSort(window, "LibrarySortOption_IssueCount");
        Assert.Contains("Issue Count", LibraryToolbarDriver.SortChipText(window));

        // Same for Group - series-level LibraryGroupField, not IssueListGroupField.
        LibraryToolbarDriver.SelectGroup(window, "LibraryGroupOption_Alphabetical");
        Assert.Contains("Alphabetical", LibraryToolbarDriver.GroupChipText(window));
    }

    [Fact]
    public void SwitchBackToIssueGranularity_RestoresIssueListSortGroupOptions()
    {
        var window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        LibraryToolbarDriver.SelectViewMode(window, "LibraryGranularityOption_Series");
        LibraryToolbarDriver.SelectViewMode(window, "LibraryGranularityOption_Issue");

        // Back to Issue granularity - the Sort tab offers IssueList's fields again, and the
        // series-only fields are no longer reachable (their StackPanel is collapsed).
        LibraryToolbarDriver.OpenSortTab(window);
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListSortOption_Title")));
        Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortOption_IssueCount")));
    }
}
