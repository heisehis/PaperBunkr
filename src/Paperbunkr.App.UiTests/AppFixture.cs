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

    /// <summary>The isolated SQLite file this fixture's exe was launched against - lets a test seed
    /// data directly (via a separate <c>PaperbunkrDbContext</c> pointed at the same path) rather than
    /// only through UI-driven flows, for scenarios UI alone can't produce (e.g. a real in-progress
    /// reading position, docs/superpowers/specs/2026-08-18-home-screen-design.md).</summary>
    public string DbPath => _dbPath;

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

        DismissOnboardingOverlayIfPresent();
        DismissMigrationOverlayIfPresent();
    }

    /// <summary>
    /// The onboarding overlay now auto-opens on *every* fresh (empty) database (App.axaml.cs) -
    /// no longer gated on a detected ComicRack CE install - so this fixture's throwaway test
    /// database triggers it on every single launch, unlike the CE-only migration overlay below.
    /// Every existing UI test assumes it lands straight on the real app shell, so this has to run
    /// unconditionally, not just defensively.
    /// </summary>
    private void DismissOnboardingOverlayIfPresent()
    {
        var closeButton = Window.FindFirstDescendant(cf => cf.ByAutomationId("OnboardingOverlayCloseButton"));
        closeButton?.AsButton().Invoke();
    }

    /// <summary>
    /// Defensive only - the migration overlay itself is only ever reached (post-onboarding) when a
    /// real ComicRack CE install is found at its default %AppData% path (App.axaml.cs), which a
    /// throwaway test database never causes on its own. Kept as a safety net in case this ever runs
    /// on a machine that genuinely has CE installed.
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
    /// Resolves the compiled Paperbunkr.App.exe next to this test assembly's own output (both build
    /// under <c>src/*/bin/{Configuration}/</c> in the same solution) rather than a hardcoded absolute
    /// path, so Debug/Release both work without configuration. The target framework folder is
    /// discovered rather than hardcoded - the app (<c>net10.0</c>) and this test project
    /// (<c>net10.0-windows</c>) don't share a TFM folder name, and a stale build from a previous
    /// retarget must not be picked up.
    /// </summary>
    private static string FindAppExePath()
    {
        // AppContext.BaseDirectory: .../src/Paperbunkr.App.UiTests/bin/{Config}/{tfm}/
        var netDir = new DirectoryInfo(AppContext.BaseDirectory);
        string config = netDir.Parent?.Name
            ?? throw new InvalidOperationException($"Could not determine build configuration from '{netDir.FullName}'.");
        string? srcDir = netDir.Parent?.Parent?.Parent?.Parent?.FullName;
        if (srcDir is null)
        {
            throw new InvalidOperationException($"Could not locate the src/ directory from '{netDir.FullName}'.");
        }

        string appBinDir = Path.Combine(srcDir, "Paperbunkr.App", "bin", config);
        if (!Directory.Exists(appBinDir))
        {
            throw new FileNotFoundException(
                $"Paperbunkr.App build output not found at '{appBinDir}' - build the solution first.", appBinDir);
        }

        // Newest wins, so a leftover exe under an old TFM folder from a previous retarget can't
        // shadow the current build.
        string? exePath = Directory
            .EnumerateFiles(appBinDir, "Paperbunkr.App.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (exePath is null)
        {
            throw new FileNotFoundException(
                $"Paperbunkr.App.exe not found under '{appBinDir}' - build the solution first.", appBinDir);
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
