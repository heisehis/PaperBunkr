using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
// UseWindowsForms (needed for the Accessibility.dll interop reference FlaUI.UIA3 needs - see the
// .csproj comment) implicitly brings System.Windows.Forms.Application into scope, colliding with
// FlaUI.Core.Application - disambiguated with an explicit alias rather than fully-qualifying every
// call site.
using Application = FlaUI.Core.Application;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// Launches the real compiled Paperbunkr.App.exe against an isolated, throwaway SQLite database
/// (docs/superpowers/specs/2026-08-17-library-saved-list-layouts-design.md's on-screen-verification
/// gap) via UIA3/FlaUI - drives the actual rendered window, unlike the windowless
/// Avalonia.Headless bootstrap Paperbunkr.App.Tests uses for ViewModel-level tests. One instance
/// per test (not a shared collection fixture), so each test gets its own clean database and
/// process rather than leaking state between tests.
/// </summary>
public sealed class AppFixture : IDisposable
{
    private readonly string _dbPath;
    private Application? _app;
    private UIA3Automation? _automation;

    public Window Window { get; private set; } = null!;

    public AppFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_uitest_{Guid.NewGuid():N}.db");
        Launch();
    }

    private void Launch()
    {
        string exePath = FindAppExePath();
        var psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
        // Out-of-process redirect for PaperbunkrDbContext.DatabasePathOverride's field initializer
        // - the in-process static setter tests use elsewhere can't reach a separately-launched exe.
        psi.EnvironmentVariables["PAPERBUNKR_DB_PATH"] = _dbPath;

        _app = Application.Launch(psi);
        _automation = new UIA3Automation();
        Window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(20))
            ?? throw new InvalidOperationException("Paperbunkr main window did not appear within 20s.");

        DismissMigrationOverlayIfPresent();
    }

    /// <summary>
    /// Defensive only - the migration overlay auto-opens on a fresh (empty) database solely when a
    /// real ComicRack CE install is also found at its default %AppData% path (App.axaml.cs), which
    /// a throwaway test database never causes on its own. Kept as a safety net in case this ever
    /// runs on a machine that genuinely has CE installed.
    /// </summary>
    private void DismissMigrationOverlayIfPresent()
    {
        var closeButton = Window.FindFirstDescendant(cf => cf.ByAutomationId("MigrationOverlayCloseButton"));
        closeButton?.AsButton().Invoke();
    }

    /// <summary>Closes the running app and relaunches it against the SAME database file, simulating an app restart - the actual scenario the Saved List Layouts spec needed verified on-screen.</summary>
    public void Restart()
    {
        CloseCurrentProcess();
        Launch();
    }

    private void CloseCurrentProcess()
    {
        if (_app is null)
        {
            return;
        }

        _automation?.Dispose();
        _automation = null;

        try
        {
            _app.Close();
        }
        catch
        {
            // best-effort graceful close; the Kill() fallback below covers the rest
        }

        for (int i = 0; i < 50 && !_app.HasExited; i++)
        {
            Thread.Sleep(100);
        }

        if (!_app.HasExited)
        {
            _app.Kill();
        }

        _app.Dispose();
        _app = null;
    }

    /// <summary>
    /// Resolves the compiled Paperbunkr.App.exe next to this test assembly's own output
    /// (both build to <c>src/*/bin/{Configuration}/net8.0/</c> under the same solution) rather than
    /// a hardcoded absolute path, so Debug/Release both work without configuration.
    /// </summary>
    private static string FindAppExePath()
    {
        // AppContext.BaseDirectory: .../src/Paperbunkr.App.UiTests/bin/{Config}/net8.0/
        var netDir = new DirectoryInfo(AppContext.BaseDirectory);
        string config = netDir.Parent?.Name
            ?? throw new InvalidOperationException($"Could not determine build configuration from '{netDir.FullName}'.");
        string? srcDir = netDir.Parent?.Parent?.Parent?.Parent?.FullName;
        if (srcDir is null)
        {
            throw new InvalidOperationException($"Could not locate the src/ directory from '{netDir.FullName}'.");
        }

        string exePath = Path.Combine(srcDir, "Paperbunkr.App", "bin", config, "net8.0", "Paperbunkr.App.exe");
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException(
                $"Paperbunkr.App.exe not found at '{exePath}' - build the solution first.", exePath);
        }

        return exePath;
    }

    public void Dispose()
    {
        CloseCurrentProcess();

        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
        }
    }
}
