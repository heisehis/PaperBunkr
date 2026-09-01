using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// Throwaway diagnostic - drives the real compiled exe via FlaUI/UIA3 genuine SendInput keyboard
/// simulation (not the SetForegroundWindow/PowerShell approach that fights Windows' anti-focus-
/// stealing protections) to determine whether Window.KeyBindings actually fire at all, independent
/// of any manual on-screen testing ambiguity.
/// </summary>
public class KeyboardShortcutDiagnosticTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void CtrlComma_OpensPreferences()
    {
        var window = _fixture.Window;
        LibraryToolbarDriver.GoToLibrary(window);

        window.Focus();
        Keyboard.TypeSimultaneously(new[] { VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_COMMA });

        var prefsBox = LibraryToolbarDriver.TryFind(window, "PreferencesSearchBox");
        Assert.NotNull(prefsBox);
    }
}
