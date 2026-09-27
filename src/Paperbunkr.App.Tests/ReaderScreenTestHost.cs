using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Shared scaffolding for headless tests of the real reader screen: builds a <see cref="ReaderScreen"/> (faking the design tokens App.axaml would supply), shows it in a window, pumps the dispatcher and presses keys.
/// The same approach <c>ReaderScreenPanelTests</c> uses privately; new tests share this one.
/// </summary>
internal static class ReaderScreenTestHost
{
    public static ReaderScreen CreateScreen(ReaderScreenViewModel vm)
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

    /// <summary>Opens the screen in a 900x700 window with the view model's issue loaded and the canvas focused and laid out.</summary>
    public static (Window Window, ReaderScreen Screen, PageCanvas Canvas) Open(ReaderScreenViewModel vm, int issueId)
    {
        var screen = CreateScreen(vm);
        var window = new Window { Width = 900, Height = 700, Content = screen };
        window.Show();
        vm.LoadIssue(issueId);
        screen.PageCanvasControl.Focus();
        Pump(window, () => screen.PageCanvasControl.Page is not null && screen.PageCanvasControl.Bounds.Width > 0);
        return (window, screen, screen.PageCanvasControl);
    }

    public static void Pump(Window window, Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            TestDispatcher.Drain();
            window.UpdateLayout();
            Thread.Sleep(10);
        }

        TestDispatcher.Drain();
    }

    public static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        string physical = key is Key.Left or Key.Right or Key.Up or Key.Down ? $"Arrow{key}" : key.ToString();
        window.KeyPress(key, modifiers, (PhysicalKey)Enum.Parse(typeof(PhysicalKey), physical), null);
    }
}
