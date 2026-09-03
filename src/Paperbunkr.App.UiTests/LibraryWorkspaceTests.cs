using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of Saved Workspaces (docs/superpowers/specs/2026-09-03-library-saved-
/// workspaces-design.md): applying a built-in from the toolbar switcher takes effect and survives
/// a real app restart, and the label reverts once the view drifts. Drives the real compiled exe
/// via FlaUI/UIA3 (see <see cref="AppFixture"/>). ViewModel-level coverage in
/// <c>Paperbunkr.App.Tests/LibraryWorkspaceTests.cs</c> is the exhaustive gate; this is the
/// end-to-end confirmation.
/// </summary>
public class LibraryWorkspaceTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void FreshProfile_ShowsNeutralWorkspaceLabel()
    {
        Window window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        Assert.Contains("Workspace", LibraryToolbarDriver.WorkspaceButtonName(window));
        Assert.DoesNotContain("Manga", LibraryToolbarDriver.WorkspaceButtonName(window));
    }

    [Fact]
    public void ApplyingABuiltIn_TakesEffect_AndSurvivesRestart()
    {
        Window window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        LibraryToolbarDriver.ApplyWorkspace(window, "Manga");
        Assert.Contains("Manga", LibraryToolbarDriver.WorkspaceButtonName(window));

        _fixture.Restart();
        window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        Assert.Contains("Manga", LibraryToolbarDriver.WorkspaceButtonName(window));

        // Drifting the view drops the label back to the neutral text.
        LibraryToolbarDriver.SelectSort(window, "LibrarySortOption_DateAdded");
        Assert.DoesNotContain("Manga", LibraryToolbarDriver.WorkspaceButtonName(window));
    }
}
