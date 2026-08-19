using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of the Story Arc / Series Group fields (docs/superpowers/specs/
/// 2026-08-18-library-book-centric-redesign-design.md) - the fields that actually address the
/// motivating "Warhammer 40" case, where grouping by Series alone can't separate issues because
/// they all share one generic series name. Drives the real compiled exe via FlaUI/UIA3 (see
/// <see cref="AppFixture"/>).
/// </summary>
public class ComicListStoryArcFieldsTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ComicListMode_SortAndGroupPopups_OfferStoryArcAndSeriesGroup()
    {
        var window = _fixture.Window;
        // Home is the app's default launch screen now (docs/superpowers/specs/
        // 2026-08-18-home-screen-design.md) - navigate to Library first.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryRailButton"))!.AsButton().Invoke();

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDisplayButton"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryViewModeOption_IssueList"))!.AsButton().Invoke();

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGroupButton"))!.AsButton().Invoke();
        var storyArcOption = window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListGroupOption_StoryArc"));
        Assert.NotNull(storyArcOption);
        storyArcOption!.AsButton().Invoke();

        var groupButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGroupButton"))!;
        Assert.Contains("Story Arc", groupButton.Name);

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!.AsButton().Invoke();
        var seriesGroupOption = window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListSortOption_SeriesGroup"));
        Assert.NotNull(seriesGroupOption);
    }
}
