using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of the Slice 1 CE field additions to Comic List's Sort/Group (docs/
/// superpowers/specs/2026-08-18-library-book-centric-redesign-design.md) - confirms the new fields
/// actually render in Library's unified Sort/Group toolbar (see <see cref="LibraryComicListModeTests"/>
/// for that unification) and are clickable, not just present in the field catalog dictionaries.
/// Drives the real compiled exe via FlaUI/UIA3 (see <see cref="AppFixture"/>).
/// </summary>
public class ComicListSlice1FieldsTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ComicListMode_SortAndGroupPopups_OfferNewSlice1Fields()
    {
        var window = _fixture.Window;
        // Home is the app's default launch screen now (docs/superpowers/specs/
        // 2026-08-18-home-screen-design.md) - navigate to Library first.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryRailButton"))!.AsButton().Invoke();

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDisplayButton"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryViewModeOption_IssueList"))!.AsButton().Invoke();

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!.AsButton().Invoke();
        var pencillerOption = window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListSortOption_Penciller"));
        Assert.NotNull(pencillerOption);
        pencillerOption!.AsButton().Invoke();

        var sortButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!;
        Assert.Contains("Penciller", sortButton.Name);

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGroupButton"))!.AsButton().Invoke();
        var readOption = window.FindFirstDescendant(cf => cf.ByAutomationId("ComicListGroupOption_Read"));
        Assert.NotNull(readOption);
        readOption!.AsButton().Invoke();

        var groupButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryGroupButton"))!;
        Assert.Contains("Read", groupButton.Name);
    }
}
