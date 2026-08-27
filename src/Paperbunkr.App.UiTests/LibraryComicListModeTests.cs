using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification that Comic List is a Library view mode, not its own rail-nav screen nor
/// its own second Sort/Group toolbar (revisiting docs/superpowers/specs/
/// 2026-08-18-issue-list-pluggable-sort-group-design.md's separate-screen choice per explicit user
/// feedback). Post-4b the Sort/Group controls live in the "View &amp; Sort" tabbed popup and the
/// active field surfaces as a chip. Drives the real compiled exe via FlaUI/UIA3 (see
/// <see cref="AppFixture"/>).
/// </summary>
public class LibraryComicListModeTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SwitchToComicListMode_UnifiesSortGroupToolbarWithLibrarysOwn()
    {
        var window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        LibraryToolbarDriver.SelectViewMode(window, "LibraryViewModeOption_IssueList");
        Assert.Contains("Comic List", LibraryToolbarDriver.ViewSortButtonName(window));

        // Comic List mode must not render a second Sort/Group toolbar of its own - the embedded
        // IssueListScreen no longer has one at all.
        Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListSortButton")));

        // Library's single Sort control (the View & Sort popup's Sort tab) offers issue-level fields;
        // picking one surfaces it as the "Sorted: …" chip.
        LibraryToolbarDriver.SelectSort(window, "ComicListSortOption_Title");
        Assert.Contains("Title", LibraryToolbarDriver.SortChipText(window));

        LibraryToolbarDriver.SelectGroup(window, "ComicListGroupOption_Publisher");
        Assert.Contains("Publisher", LibraryToolbarDriver.GroupChipText(window));
    }
}
