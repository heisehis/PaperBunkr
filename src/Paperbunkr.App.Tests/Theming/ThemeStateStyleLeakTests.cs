using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// The state-skin styles are created once and never removed (see <c>ThemeStateStyleHost</c>), and their setters are bound to long-lived
/// holder objects. That must not keep every button that was ever styled by them alive: a closed window has to be collectable whether or
/// not a skinning theme was ever applied in the process.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeStateStyleLeakTests : IDisposable
{
    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pb_theme_leak_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ThemeStateStyleLeakTests()
    {
        TestAppBuilder.EnsureInitialized();
        Directory.CreateDirectory(_root);
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "l.db")}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            new ThemeService(() => new PaperbunkrDbContext(_dbOptions)).ApplyTheme("default");
        }
        catch (InvalidOperationException)
        {
        }

        ThemePaths.InstalledDirectory = _originalInstalledDirectory;
        ThemePaths.ExtractedDirectory = _originalExtractedDirectory;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Shows and closes a window of skinnable buttons, returning only weak references to it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> ShowAndCloseButtons(int count)
    {
        var references = new List<WeakReference>();
        var panel = new StackPanel();
        for (int i = 0; i < count; i++)
        {
            var button = new Button { Classes = { i % 2 == 0 ? "primary" : "ghost" }, Content = "Go" };
            panel.Children.Add(button);
            references.Add(new WeakReference(button));
        }

        var window = new Window { Width = 300, Height = 600, Content = panel };
        window.Show();
        RunLayout(window);
        window.Close();
        TestDispatcher.Drain();
        return references;
    }

    private static int Alive(List<WeakReference> references)
    {
        for (int i = 0; i < 4; i++)
        {
            TestDispatcher.Drain();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        GC.Collect();
        return references.Count(r => r.IsAlive);
    }

    [Fact]
    public void ButtonsStyledWhileASkinningThemeIsActive_AreCollected_OnceTheirWindowIsClosed()
    {
        var service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));

        WithThemeAndTokens(() =>
        {
            using var resources = AppResources();
            using var styles = AppStyles();

            service.ApplyTheme("default");
            int baseline = Alive(ShowAndCloseButtons(20));          // what this host keeps alive with no skinning theme involved

            service.ApplyTheme("tokyo_midnight:dark");             // skins button.primary, including a hover state
            int withSkins = Alive(ShowAndCloseButtons(20));

            service.ApplyTheme("default");                         // the styles stay registered after switching away
            int afterwards = Alive(ShowAndCloseButtons(20));

            Assert.True(withSkins <= baseline, $"{withSkins} of 20 buttons survived their window with a skinning theme active (baseline {baseline}).");
            Assert.True(afterwards <= baseline, $"{afterwards} of 20 buttons survived their window after a skinning theme had been used (baseline {baseline}).");
        });
    }
}
