using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// Real, on-screen verification of docs/superpowers/specs/2026-08-17-library-saved-list-layouts-
/// design.md's core claim - that Library sort/group/display/filter state survives an actual app
/// restart. Post-4b the sort/display controls live in the row-1 switches and the row-2 chips; the active
/// sort field surfaces as a chip and the active display mode as the Display button's
/// accessible name. Drives the real compiled exe via FlaUI/UIA3 (see <see cref="AppFixture"/>).
/// </summary>
public class LibraryListLayoutPersistenceTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SortFieldChange_SurvivesRestart()
    {
        Window window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        // Default sort is "Date Added" desc. Change it to File Size - unambiguous evidence the
        // click landed and re-persisted (the "Sort: …" chip names the field).
        LibraryToolbarDriver.SelectSort(window, "LibrarySortOption_FileSize");
        Assert.Contains("File Size", LibraryToolbarDriver.SortChipText(window));

        _fixture.Restart();
        window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        Assert.Contains("File Size", LibraryToolbarDriver.SortChipText(window));
    }

    [Fact]
    public void ViewModeChange_SurvivesRestart()
    {
        Window window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        LibraryToolbarDriver.SelectViewMode(window, "LibraryViewSwitch_List");
        Assert.Contains("List", LibraryToolbarDriver.DisplayButtonName(window));

        _fixture.Restart();
        window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        Assert.Contains("List", LibraryToolbarDriver.DisplayButtonName(window));
    }

    [Fact]
    public void FilterCheckboxChange_SurvivesRestart()
    {
        Window window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        var unreadOnly = LibraryToolbarDriver.Find(window, "LibraryFilterUnreadOnly").AsCheckBox();
        unreadOnly.IsChecked = true;
        Assert.Equal(ToggleState.On, unreadOnly.ToggleState);

        _fixture.Restart();
        window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        var unreadOnlyAfterRestart = LibraryToolbarDriver.Find(window, "LibraryFilterUnreadOnly").AsCheckBox();
        Assert.Equal(ToggleState.On, unreadOnlyAfterRestart.ToggleState);
    }
}
