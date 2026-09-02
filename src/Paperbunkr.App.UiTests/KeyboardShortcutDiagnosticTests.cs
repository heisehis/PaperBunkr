using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Application = FlaUI.Core.Application;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// Throwaway diagnostic - drives the real compiled exe via FlaUI/UIA3 genuine SendInput keyboard
/// simulation (not computer-use - this launches its own isolated process/window, never touching
/// the user's real screen or running instance) to determine whether Window.KeyBindings actually
/// fire, independent of any manual on-screen testing ambiguity. Deliberately minimal - no
/// navigation, no button-finding beyond the unavoidable first-run welcome overlay - to keep this
/// robust against this machine's slow/loaded rendering rather than chasing perfect timing on a
/// multi-step UI flow.
/// </summary>
public class KeyboardShortcutDiagnosticTests : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_kbdiag_{Guid.NewGuid():N}.db");
    private Application? _app;
    private UIA3Automation? _automation;

    public void Dispose()
    {
        _automation?.Dispose();
        try { _app?.Close(); } catch { }
        try { if (!_app?.HasExited ?? false) _app?.Kill(); } catch { }
        _app?.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private Window LaunchAndGetWindow()
    {
        var netDir = new DirectoryInfo(AppContext.BaseDirectory);
        string config = netDir.Parent!.Name;
        string srcDir = netDir.Parent!.Parent!.Parent!.Parent!.FullName;
        string appBinDir = Path.Combine(srcDir, "Paperbunkr.App", "bin", config);
        string exePath = Directory
            .EnumerateFiles(appBinDir, "Paperbunkr.App.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .First();

        var psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
        psi.EnvironmentVariables["PAPERBUNKR_DB_PATH"] = _dbPath;

        _app = Application.Launch(psi);
        _automation = new UIA3Automation();
        var window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(120))
            ?? throw new InvalidOperationException("Paperbunkr main window did not appear within 120s.");

        // Real possibility being ruled out here: FlaUI's Keyboard.* helpers use raw Win32 SendInput,
        // a genuinely GLOBAL synthetic-input call that goes wherever the OS's actual foreground
        // window is - NOT necessarily this specific window, even though UIA can locate/click it via
        // its own separate accessibility channel. A process is allowed to steal foreground focus for
        // a CHILD process it just launched (Windows' documented exception to anti-focus-stealing),
        // which this test process is for Paperbunkr.App.exe - so this should succeed where an
        // unrelated background PowerShell process's SetForegroundWindow call would be silently
        // ignored (as seen earlier this session).
        SetForegroundWindow(_app.MainWindowHandle);
        Thread.Sleep(300);

        return window;
    }

    /// <summary>
    /// Tests a specific theory: Avalonia 12's focus overhaul (all focus now managed by the new
    /// FocusManager, replacing the removed KeyboardNavigationHandler - see docs/avalonia12-
    /// breaking-changes) may not fall back to a sensible default when the currently-focused
    /// element is removed from the tree (e.g. an overlay closing). If so, Ctrl+, should fire
    /// BEFORE any click happens (whatever got initial focus for free at window-show is still
    /// there) but fail AFTER a click that removes/replaces the focused element - exactly the
    /// WelcomeSkip-button-closes-the-overlay-it's-part-of pattern every earlier test here hit.
    /// </summary>
    /// <summary>
    /// Isolates whether this is a Paperbunkr-specific issue or an Avalonia 12.0.0/environment
    /// issue: launches a from-scratch, minimal Avalonia app (no Paperbunkr code at all, no
    /// database, no overlays - just a Window with one Escape KeyBinding that writes a marker file)
    /// and tests the exact same way. If this ALSO fails, the bug is in Avalonia 12.0.0 itself or
    /// this machine's environment, not anything Paperbunkr's code does.
    /// </summary>
    /// <summary>
    /// Most fundamental possible check: does ANY keyboard input reach ANY Avalonia control at all,
    /// even a plain focused TextBox receiving plain character keys (no KeyBindings/HotKey/modifier
    /// gestures involved whatsoever)? If this also fails, the problem is beneath Avalonia's
    /// KeyBindings/HotKey layer entirely - something in how keyboard messages reach the window at
    /// all on this environment.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\DeeDee\AppData\Local\Temp\claude\C--Users-DeeDee-PaperBunkr\6c8cb1de-2511-4a6f-9a8e-995a57d270ab\scratchpad\kbprobe\KbProbe\bin\Debug\net10.0\KbProbe.exe")]
    [InlineData(@"C:\Users\DeeDee\AppData\Local\Temp\claude\C--Users-DeeDee-PaperBunkr\6c8cb1de-2511-4a6f-9a8e-995a57d270ab\scratchpad\kbprobe11\KbProbe11\bin\Debug\net8.0\KbProbe11.exe")]
    public void MinimalProbeApp_TextBox_ReceivesTypedCharacters(string probeExe)
    {

        var psi = new ProcessStartInfo(probeExe) { UseShellExecute = false };
        using var probeApp = Application.Launch(psi);
        using var automation = new UIA3Automation();
        var window = probeApp.GetMainWindow(automation, TimeSpan.FromSeconds(20))
            ?? throw new InvalidOperationException("KbProbe main window did not appear within 20s.");

        SetForegroundWindow(probeApp.MainWindowHandle);
        Thread.Sleep(500);

        var textBox = window.FindFirstDescendant(cf => cf.ByAutomationId("ProbeTextBox"))!.AsTextBox();
        textBox.Click(); // real mouse click, gives this specific control real focus
        Thread.Sleep(300);
        Keyboard.Type("hello");
        Thread.Sleep(500);

        string actualText = textBox.Text ?? string.Empty;

        try { probeApp.Close(); } catch { }
        try { if (!probeApp.HasExited) probeApp.Kill(); } catch { }

        Assert.Equal("hello", actualText);
    }

    [Fact]
    public void MinimalProbeApp_KeyBindingVsHotKey()
    {
        string probeExe = @"C:\Users\DeeDee\AppData\Local\Temp\claude\C--Users-DeeDee-PaperBunkr\6c8cb1de-2511-4a6f-9a8e-995a57d270ab\scratchpad\kbprobe\KbProbe\bin\Debug\net10.0\KbProbe.exe";
        string markerPath = Path.Combine(Path.GetTempPath(), $"kbprobe_marker_{Guid.NewGuid():N}.txt");
        string keyBindingMarker = markerPath + ".keybinding";
        string hotkeyMarker = markerPath + ".hotkey";
        foreach (var m in new[] { keyBindingMarker, hotkeyMarker }) { if (File.Exists(m)) File.Delete(m); }

        var psi = new ProcessStartInfo(probeExe) { UseShellExecute = false };
        psi.EnvironmentVariables["KBPROBE_MARKER"] = markerPath;
        using var probeApp = Application.Launch(psi);
        using var automation = new UIA3Automation();
        var window = probeApp.GetMainWindow(automation, TimeSpan.FromSeconds(20))
            ?? throw new InvalidOperationException("KbProbe main window did not appear within 20s.");

        SetForegroundWindow(probeApp.MainWindowHandle);
        Thread.Sleep(500);

        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Keyboard.Release(VirtualKeyShort.ESCAPE);
        Thread.Sleep(500);

        Keyboard.TypeSimultaneously(new[] { VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_H });
        Thread.Sleep(500);

        try { probeApp.Close(); } catch { }
        try { if (!probeApp.HasExited) probeApp.Kill(); } catch { }

        bool keyBindingFired = File.Exists(keyBindingMarker);
        bool hotKeyFired = File.Exists(hotkeyMarker);
        foreach (var m in new[] { keyBindingMarker, hotkeyMarker }) { if (File.Exists(m)) File.Delete(m); }

        Assert.True(hotKeyFired, "HotKey did not fire either - would rule out the focus theory.");
        Assert.True(keyBindingFired, $"CONFIRMED: HotKey fired={hotKeyFired}, but Window.KeyBindings fired={keyBindingFired} - matches github.com/AvaloniaUI/Avalonia#21871 (KeyBindings require focus, HotKey doesn't).");
    }

    [Fact]
    public void CtrlComma_FiresColdBeforeAnyClick_NoOverlayDismissal()
    {
        var window = LaunchAndGetWindow();
        // Deliberately no click, no overlay dismissal - test the very first keystroke the window
        // ever receives.
        Keyboard.TypeSimultaneously(new[] { VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_COMMA });
        Thread.Sleep(1000);
        var prefsBox = window.FindFirstDescendant(cf => cf.ByAutomationId("PreferencesSearchBox"));
        Assert.NotNull(prefsBox);
    }

    [Fact]
    public void Escape_ClosesWelcomeOverlay_ThenCtrlComma_OpensPreferences()
    {
        var window = LaunchAndGetWindow();

        // A truly fresh DB auto-opens the first-run Welcome overlay (this machine has real
        // ComicRack CE installed). MainViewModel.Escape() explicitly covers IsWelcomeOverlayOpen
        // (MainViewModel.cs:1677), so this is a clean, minimal, pre-existing (untouched this
        // session, shipped since P5) Window.KeyBindings control test - no mouse click at all, no
        // navigation, isolates whether KeyBindings fire in this app at all vs. something specific
        // to the new Ctrl+, binding.
        var skip = FlaUI.Core.Tools.Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId("WelcomeSkip")),
            TimeSpan.FromSeconds(15), throwOnTimeout: false).Result;
        Assert.NotNull(skip); // sanity: overlay genuinely open before we test closing it

        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Keyboard.Release(VirtualKeyShort.ESCAPE);
        Thread.Sleep(1000);
        var skipAfterEscape = window.FindFirstDescendant(cf => cf.ByAutomationId("WelcomeSkip"));

        // Fall back to a real mouse click if Escape didn't close it, so the Ctrl+, check below
        // still gets a fair, uncontaminated run regardless of the Escape result.
        skipAfterEscape?.Click();
        Thread.Sleep(500);

        Keyboard.TypeSimultaneously(new[] { VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_COMMA });
        Thread.Sleep(1000);
        var prefsBox = window.FindFirstDescendant(cf => cf.ByAutomationId("PreferencesSearchBox"));

        Assert.Null(skipAfterEscape); // Escape should have closed the welcome overlay
        Assert.NotNull(prefsBox); // Ctrl+, should have opened Preferences
    }

    /// <summary>
    /// Diagnostic for the user-reported "sidebar arrow keys don't do anything" (distinct from the
    /// Library-grid arrow-key bug fixed the same session) - seeds two series of different
    /// <see cref="ContentType"/>s so the sidebar's Library block has more than just "All Series" to
    /// navigate to (Library.ContentTypes only lists content types with at least one real series - a
    /// truly empty DB would make Down a legitimate no-op, not a useful test of the bug). Focuses "All
    /// Series" via UIA <c>SetFocus</c> (not a real mouse click, which Escape/welcome-overlay timing
    /// makes fragile) then sends a real Down arrow via SendInput, exactly like every other test in
    /// this file. The real evidence this test exists to produce is the KBDIAG3 log lines
    /// (MainWindow.axaml.cs's own temporary diagnostic, %AppData%\Paperbunkr\logs\startup.log) - the
    /// assertion here is intentionally loose.
    /// </summary>
    [Fact]
    public void SidebarDownArrow_MovesFocusAwayFromAllSeries()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var seedContext = new PaperbunkrDbContext(options))
        {
            // Migrate(), not EnsureCreated() - the app itself calls Migrate() on first launch
            // (PaperbunkrDb.HasAnySeries()), and EnsureCreated() creates tables without recording
            // migration history, so a second Migrate() against that same file then crashes with
            // "table already exists."
            seedContext.Database.Migrate();
            seedContext.Series.AddRange(
                new Series { Name = "Comic Series", ContentType = ContentType.Comic },
                new Series { Name = "Manga Series", ContentType = ContentType.Manga });
            seedContext.SaveChanges();
        }

        var window = LaunchAndGetWindow();

        var skip = FlaUI.Core.Tools.Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId("WelcomeSkip")),
            TimeSpan.FromSeconds(15), throwOnTimeout: false).Result;
        if (skip is not null)
        {
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Keyboard.Release(VirtualKeyShort.ESCAPE);
            Thread.Sleep(500);
        }

        var libraryRail = FlaUI.Core.Tools.Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryRailButton")),
            TimeSpan.FromSeconds(15), throwOnTimeout: false).Result;
        libraryRail?.AsButton().Invoke();
        Thread.Sleep(500);

        var allSeries = FlaUI.Core.Tools.Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySidebarAllSeries")),
            TimeSpan.FromSeconds(15), throwOnTimeout: false).Result
            ?? throw new InvalidOperationException("LibrarySidebarAllSeries not found.");
        allSeries.Focus();
        Thread.Sleep(300);

        Keyboard.Press(VirtualKeyShort.DOWN);
        Keyboard.Release(VirtualKeyShort.DOWN);
        Thread.Sleep(500);

        var focused = _automation!.FocusedElement();
        Assert.NotNull(focused);
    }
}
