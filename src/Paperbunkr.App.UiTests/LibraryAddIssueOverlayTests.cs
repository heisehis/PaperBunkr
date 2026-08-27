using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of the Phase 4b Add-issue overlay (docs/superpowers/specs/2026-08-27-
/// library-browsing-4b-toolbar-rework-design.md §6) - the "+" button opens a centered FloatingPanel
/// over the grid, Cancel closes it. Drives the real compiled exe via FlaUI/UIA3 (see
/// <see cref="AppFixture"/>).
/// </summary>
public class LibraryAddIssueOverlayTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void PlusButton_OpensOverlay_CancelCloses()
    {
        var window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        LibraryToolbarDriver.Invoke(window, "LibraryAddIssueButton");

        // The overlay's Add / Cancel buttons are present while it's open.
        LibraryToolbarDriver.Find(window, "LibraryAddIssueConfirm");
        LibraryToolbarDriver.Invoke(window, "LibraryAddIssueCancel");

        Assert.Null(LibraryToolbarDriver.TryFind(window, "LibraryAddIssueConfirm"));
    }
}
