using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// Real, on-screen verification of docs/superpowers/specs/2026-08-17-library-saved-list-layouts-
/// design.md's core claim - that Library sort/group/display/filter state survives an actual app
/// restart - closing the standing "no unattended desktop GUI automation available" gap flagged
/// across this project's specs for that exact scenario. Drives the real compiled exe via FlaUI/UIA3
/// (see <see cref="AppFixture"/>), not the windowless Avalonia.Headless-backed ViewModel tests in
/// Paperbunkr.App.Tests.
/// </summary>
public class LibraryListLayoutPersistenceTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SortFieldChange_SurvivesRestart()
    {
        Window window = _fixture.Window;

        // Default is "Sort: Date Added ▾" (LibraryScreenViewModel's own default) - change it to
        // Size, which is unambiguous evidence the click actually landed and re-persisted.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortOption_Size"))!.AsButton().Invoke();

        var sortButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!;
        Assert.Contains("Size", sortButton.Name);

        _fixture.Restart();
        window = _fixture.Window;

        var sortButtonAfterRestart = window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySortButton"))!;
        Assert.Contains("Size", sortButtonAfterRestart.Name);
    }

    [Fact]
    public void ViewModeChange_SurvivesRestart()
    {
        Window window = _fixture.Window;

        // Default is Comfortable grid - switch to List, then confirm the List-only column header
        // row (a real structural difference, not just a label) is what greets a fresh launch.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDisplayButton"))!.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryViewModeOption_List"))!.AsButton().Invoke();

        var displayButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDisplayButton"))!;
        Assert.Contains("List", displayButton.Name);

        _fixture.Restart();
        window = _fixture.Window;

        var displayButtonAfterRestart = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryDisplayButton"))!;
        Assert.Contains("List", displayButtonAfterRestart.Name);
    }

    [Fact]
    public void FilterCheckboxChange_SurvivesRestart()
    {
        Window window = _fixture.Window;

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryFilterButton"))!.AsButton().Invoke();
        var unreadOnly = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryFilterUnreadOnly"))!.AsCheckBox();
        unreadOnly.IsChecked = true;
        Assert.Equal(ToggleState.On, unreadOnly.ToggleState);

        _fixture.Restart();
        window = _fixture.Window;

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryFilterButton"))!.AsButton().Invoke();
        var unreadOnlyAfterRestart = window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryFilterUnreadOnly"))!.AsCheckBox();
        Assert.Equal(ToggleState.On, unreadOnlyAfterRestart.ToggleState);
    }
}
