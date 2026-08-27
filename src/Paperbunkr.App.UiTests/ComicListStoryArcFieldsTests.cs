using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of the Story Arc / Series Group fields (docs/superpowers/specs/
/// 2026-08-18-library-book-centric-redesign-design.md) - the fields that address the motivating
/// "Warhammer 40" case. Post-4b they live in the "View &amp; Sort" popup's Sort/Group tabs. Drives
/// the real compiled exe via FlaUI/UIA3 (see <see cref="AppFixture"/>).
/// </summary>
public class ComicListStoryArcFieldsTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ComicListMode_SortAndGroupPopups_OfferStoryArcAndSeriesGroup()
    {
        var window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        LibraryToolbarDriver.SelectViewMode(window, "LibraryViewModeOption_IssueList");

        LibraryToolbarDriver.SelectGroup(window, "ComicListGroupOption_StoryArc");
        Assert.Contains("Story Arc", LibraryToolbarDriver.GroupChipText(window));

        LibraryToolbarDriver.OpenSortTab(window);
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListSortOption_SeriesGroup")));
    }
}
