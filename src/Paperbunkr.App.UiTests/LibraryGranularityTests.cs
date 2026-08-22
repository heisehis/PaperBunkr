using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of Library's Series/Issue card-content Granularity toggle (docs/
/// superpowers/specs/2026-08-18-library-book-centric-redesign-design.md Slice 3 made every Display
/// mode per-issue by default with no way back to the original one-card-per-series view; the
/// same-session follow-up restored that as a real, switchable option via the Display dropdown's
/// "Card content" section). Drives the real compiled exe via FlaUI/UIA3 (see
/// <see cref="AppFixture"/>) - LibraryScreenViewModelTests already covers the ViewModel-level
/// Covers/Groups/SortField/GroupField behavior; this only needs to confirm the XAML actually wires
/// the toggle and swaps the Sort/Group popups, not re-verify the data pipeline itself.
/// </summary>
public class LibraryGranularityTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SwitchToSeriesGranularity_SwapsSortGroupOptionsToSeriesLevelFields()
    {
        var window = _fixture.Window;
        // Home is the app's default launch screen now (docs/superpowers/specs/
        // 2026-08-18-home-screen-design.md) - navigate to Library first.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryRailButton"))!.AsButton().Invoke();

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDisplayButton"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGranularityOption_Series"))!.AsButton().Invoke();

        // The Sort popup should now offer series-card fields (LibrarySortField), not IssueList's.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!.AsButton().Invoke();
        var issueCountOption = window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortOption_IssueCount"));
        Assert.NotNull(issueCountOption);
        issueCountOption!.AsButton().Invoke();

        var sortButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!;
        Assert.Contains("Issue Count", sortButton.Name);

        // Same for Group - series-level LibraryGroupField, not IssueListGroupField.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGroupButton"))!.AsButton().Invoke();
        var alphabeticalOption = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGroupOption_Alphabetical"));
        Assert.NotNull(alphabeticalOption);
        alphabeticalOption!.AsButton().Invoke();

        var groupButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGroupButton"))!;
        Assert.Contains("Alphabetical", groupButton.Name);
    }

    [Fact]
    public void SwitchBackToIssueGranularity_RestoresIssueListSortGroupOptions()
    {
        var window = _fixture.Window;
        // Home is the app's default launch screen now (docs/superpowers/specs/
        // 2026-08-18-home-screen-design.md) - navigate to Library first.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryRailButton"))!.AsButton().Invoke();

        // The Display dropdown stays open across a granularity click (only the popup's own light
        // dismiss or its own toggle button close it) - re-opening it here would instead toggle it
        // shut, since it's still open from the first click.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDisplayButton"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGranularityOption_Series"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGranularityOption_Issue"))!.AsButton().Invoke();

        // Back to Issue granularity - the Sort popup should offer IssueList's fields again, and the
        // series-only fields should no longer be reachable.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!.AsButton().Invoke();
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListSortOption_Title")));
        Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortOption_IssueCount")));
    }
}
