using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification that Comic List is a Library view mode, not its own rail-nav screen nor
/// its own second Sort/Group toolbar (revisiting docs/superpowers/specs/
/// 2026-08-18-issue-list-pluggable-sort-group-design.md's separate-screen choice per explicit user
/// feedback - CE's real "Comic List" is a Detail view mode sharing one filtered query and one
/// toolbar, not a parallel screen with duplicated Sort/Group controls). Drives the real compiled
/// exe via FlaUI/UIA3 (see <see cref="AppFixture"/>).
/// </summary>
public class LibraryComicListModeTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SwitchToComicListMode_UnifiesSortGroupToolbarWithLibrarysOwn()
    {
        var window = _fixture.Window;
        // Home is the app's default launch screen now (docs/superpowers/specs/
        // 2026-08-18-home-screen-design.md) - navigate to Library first.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryRailButton"))!.AsButton().Invoke();

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDisplayButton"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryViewModeOption_IssueList"))!.AsButton().Invoke();

        var displayButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDisplayButton"))!;
        Assert.Contains("Comic List", displayButton.Name);

        // Comic List mode must not render a second Sort/Group toolbar of its own - the embedded
        // IssueListScreen no longer has one at all.
        Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListSortButton")));

        // Library's single top-level Sort button should now offer issue-level fields.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!.AsButton().Invoke();
        var titleOption = window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListSortOption_Title"));
        Assert.NotNull(titleOption);
        titleOption!.AsButton().Invoke();

        var sortButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!;
        Assert.Contains("Title", sortButton.Name);

        // Same for Group.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGroupButton"))!.AsButton().Invoke();
        var publisherOption = window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListGroupOption_Publisher"));
        Assert.NotNull(publisherOption);
        publisherOption!.AsButton().Invoke();

        var groupButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGroupButton"))!;
        Assert.Contains("Publisher", groupButton.Name);
    }
}
