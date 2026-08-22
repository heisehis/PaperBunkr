using FlaUI.Core.AutomationElements;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of the CE-faithful multi-mode search selector added to Library's toolbar
/// this session (mirrors CE's real QuickSearch mode dropdown - see
/// docs/superpowers/specs/2026-08-18-library-book-centric-redesign-design.md for the source
/// research). Drives the real compiled exe via FlaUI/UIA3 (see <see cref="AppFixture"/>).
/// </summary>
public class LibrarySearchModeTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SwitchSearchMode_UpdatesButtonLabel()
    {
        var window = _fixture.Window;
        // Home is the app's default launch screen now (docs/superpowers/specs/
        // 2026-08-18-home-screen-design.md) - navigate to Library first.
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryRailButton"))!.AsButton().Invoke();

        var searchModeButton = window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySearchModeButton"))!;
        Assert.Contains("All", searchModeButton.Name);

        searchModeButton.AsButton().Invoke();
        window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySearchModeOption_Writer"))!.AsButton().Invoke();

        var searchModeButtonAfter = window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySearchModeButton"))!;
        Assert.Contains("Writer", searchModeButtonAfter.Name);
    }
}
