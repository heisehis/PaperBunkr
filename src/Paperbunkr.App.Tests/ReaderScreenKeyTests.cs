using System.Text.RegularExpressions;
using Avalonia;
using Microsoft.EntityFrameworkCore;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>Real key presses through the reader screen's routing (the view-model tests call commands directly and cannot see a routing bug).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderScreenKeyTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    /// <summary>Redirects the app database to a temp file (the reader's palette, profiles and settings read it), so these tests never touch the real per-user database.</summary>
    public ReaderScreenKeyTests()
    {
        _originalDbPathOverride = Paperbunkr.Data.PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_key_test_{Guid.NewGuid():N}.db");
        Paperbunkr.Data.PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Paperbunkr.Data.PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new Paperbunkr.Data.PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Paperbunkr.Data.PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// The headless test application does not carry <c>App.axaml</c>'s design tokens, which <c>ReaderScreen.axaml</c> references as static resources. Rather than duplicate them, every key it reports
    /// missing is added with a placeholder of the right kind (guessed from its name) and the screen is created again; routing does not depend on the values.
    /// </summary>
    private static ReaderScreen CreateScreen(ReaderScreenViewModel vm)
    {
        var resources = Application.Current!.Resources;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                return new ReaderScreen { DataContext = vm };
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex.InnerException is KeyNotFoundException)
            {
                var match = Regex.Match(ex.ToString(), @"Static resource '([^']+)' not found");
                if (!match.Success)
                {
                    throw;
                }

                string key = match.Groups[1].Value;
                resources[key] = key switch
                {
                    _ when key.Contains("Ease") => new Avalonia.Animation.Easings.CubicEaseOut(),
                    _ when key.Contains("Motion") || key.Contains("Duration") => TimeSpan.FromMilliseconds(150),
                    _ when key.Contains("Radius") => new CornerRadius(4),
                    _ when key.Contains("Brush") => new SolidColorBrush(Colors.Gray),
                    _ when key.Contains("Thickness") || key.Contains("Padding") => new Thickness(4),
                    _ => 12.0,
                };
            }
        }

        throw new InvalidOperationException("Too many missing resources.");
    }

    private static (Window Window, ReaderScreenViewModel Vm) Show()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { }, ReaderTestInput.Create());
        var window = new Window { Width = 900, Height = 700, Content = CreateScreen(vm) };
        InputHost.Attach(window, vm.Input);   // what MainWindow does for the app
        window.Show();
        // The reader keeps focus on its page canvas while reading; key presses are routed to the focused element.
        ((ReaderScreen)window.Content!).PageCanvasControl.Focus();
        return (window, vm);
    }

    [Fact]
    public void CtrlShiftP_TogglesThePerfOverlayOnce_NotTwice()
    {
        var (window, vm) = Show();
        try
        {
            window.KeyPress(Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P, "P");
            Assert.True(vm.PerfOverlayVisible);

            window.KeyPress(Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P, "P");
            Assert.False(vm.PerfOverlayVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void H_ShowsTheStatsChip_OnScreen()
    {
        var (window, vm) = Show();
        try
        {
            var screen = (ReaderScreen)window.Content!;
            Assert.False(vm.IsSessionHudVisible);

            window.KeyPress(Key.H, RawInputModifiers.None, PhysicalKey.H, "h");

            Assert.True(vm.IsSessionHudVisible);
            var chip = screen.GetVisualDescendants().OfType<Border>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Reading stats");
            Assert.True(chip.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void CtrlK_OpensThePalette_OnScreen_AndEscapeClosesIt()
    {
        var (window, vm) = Show();
        try
        {
            var screen = (ReaderScreen)window.Content!;

            window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            Assert.True(vm.Palette.IsOpen);
            var palette = screen.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "CommandPalette");
            Assert.True(palette.IsEffectivelyVisible);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(vm.Palette.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void W_TogglesTheWarmTint_AndSaysSo()
    {
        var (window, vm) = Show();
        try
        {
            var toasts = new List<Paperbunkr.App.Models.ToastRequest>();
            vm.ToastRequested += toasts.Add;

            window.KeyPress(Key.W, RawInputModifiers.None, PhysicalKey.W, "w");

            Assert.Equal("Warm tint on", Assert.Single(toasts).Title);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void P_SwitchesProfile_AndSaysSo()
    {
        var (window, vm) = Show();
        try
        {
            new Paperbunkr.App.Services.WorkspaceService().EnsureBuiltInsSeeded();
            var toasts = new List<Paperbunkr.App.Models.ToastRequest>();
            vm.ToastRequested += toasts.Add;

            window.KeyPress(Key.P, RawInputModifiers.None, PhysicalKey.P, "p");

            Assert.StartsWith("Profile: ", Assert.Single(toasts).Title);
        }
        finally
        {
            window.Close();
        }
    }
}
