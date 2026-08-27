using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of the CE-faithful multi-mode search selector in Library's toolbar
/// (mirrors CE's real QuickSearch mode dropdown - see
/// docs/superpowers/specs/2026-08-18-library-book-centric-redesign-design.md for the source
/// research). Post-4b the selector sits at the right edge of the search box. Drives the real
/// compiled exe via FlaUI/UIA3 (see <see cref="AppFixture"/>).
/// </summary>
public class LibrarySearchModeTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SwitchSearchMode_UpdatesButtonLabel()
    {
        var window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        var searchModeButton = LibraryToolbarDriver.Find(window, "LibrarySearchModeButton");
        Assert.Contains("All", searchModeButton.Name);

        searchModeButton.AsButton().Invoke();
        LibraryToolbarDriver.Invoke(window, "LibrarySearchModeOption_Writer");

        Assert.Contains("Writer", LibraryToolbarDriver.Find(window, "LibrarySearchModeButton").Name);
    }
}
