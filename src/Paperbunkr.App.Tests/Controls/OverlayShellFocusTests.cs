using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;
using static Paperbunkr.App.Tests.Theming.FoundationStyleTests;

namespace Paperbunkr.App.Tests.Controls;

/// <summary>
/// The 2026-11-12 focus audit: an open <see cref="OverlayShell"/> is modal for the keyboard. It takes focus on open (never its ✕), keeps arrow
/// keys and Tab inside, pulls a stray key back in, and gives focus back to whatever opened it. And a wheel turn inside a popup no longer
/// scrolls the page behind it (<see cref="PopupWheelContainment"/>).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class OverlayShellFocusTests
{
    private static void Run(Action body)
    {
        var previous = MotionPolicy.Preference;
        MotionPolicy.SetPreference(MotionPreferenceMode.Off);   // closes collapse synchronously
        try
        {
            WithFoundation("default", body);
        }
        finally
        {
            MotionPolicy.SetPreference(previous);
        }
    }

    private sealed record Hosted(Window Window, OverlayShell Shell, Button Behind, Button First, Button Second, List<Key> ReachedWindow);

    private static Hosted HostShell()
    {
        var first = new Button { Content = "First" };
        var second = new Button { Content = "Second" };
        // No ✕: on a card this small it overlaps the buttons and becomes their nearest neighbour (a real dialog's DialogCard hides it anyway).
        var shell = new OverlayShell { ShowCloseButton = false, Content = new StackPanel { Spacing = 8, Children = { first, second } } };
        var behind = new Button { Content = "Behind", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var root = new Panel { Children = { behind, shell } };
        var window = Host(root);
        var reached = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => reached.Add(e.Key), RoutingStrategies.Bubble);
        behind.Focus(NavigationMethod.Tab);
        return new Hosted(window, shell, behind, first, second, reached);
    }

    [Fact]
    public void Opening_PutsFocusOnTheFirstControlInTheDialog_NotTheCloseButton()
    {
        Run(() =>
        {
            var h = HostShell();
            Assert.True(h.Behind.IsFocused);

            h.Shell.IsOpen = true;
            RunLayout(h.Window);

            Assert.True(h.First.IsFocused);
            h.Window.Close();
        });
    }

    [Fact]
    public void Closing_GivesFocusBackToWhatOpenedIt()
    {
        Run(() =>
        {
            var h = HostShell();
            h.Shell.IsOpen = true;
            RunLayout(h.Window);
            Assert.True(h.First.IsFocused);

            h.Shell.IsOpen = false;
            RunLayout(h.Window);

            Assert.True(h.Behind.IsFocused);
            h.Window.Close();
        });
    }

    [Fact]
    public void ArrowKeys_MoveInsideTheDialog_AndNeverLeaveIt()
    {
        Run(() =>
        {
            var h = HostShell();
            h.Shell.IsOpen = true;
            RunLayout(h.Window);

            Assert.True(h.First.IsFocused);
            Press(h.Window, Key.Down);
            Assert.True(h.Second.IsFocused);

            // Nothing further down or to the left: the key stays in the dialog instead of reaching the window (whose fallback sends Left to the nav rail).
            Press(h.Window, Key.Down);
            Press(h.Window, Key.Left);
            Assert.True(h.Second.IsFocused);
            Assert.Empty(h.ReachedWindow);
            h.Window.Close();
        });
    }

    [Fact]
    public void Tab_CyclesInsideTheOpenDialog()
    {
        Run(() =>
        {
            var h = HostShell();
            h.Shell.IsOpen = true;
            RunLayout(h.Window);
            h.Second.Focus(NavigationMethod.Tab);

            Press(h.Window, Key.Tab);

            Assert.True(h.First.IsFocused);                             // round to the start, never to the button behind
            Assert.True(FocusIsInside(h.Window, h.Shell));
            h.Window.Close();
        });
    }

    [Fact]
    public void AKeyPressedWithFocusLeftBehind_IsRedirectedIntoTheDialog()
    {
        Run(() =>
        {
            var h = HostShell();
            h.Shell.IsOpen = true;
            RunLayout(h.Window);
            h.Behind.Focus(NavigationMethod.Tab);                       // e.g. the dialog swapped its content and focus fell back to the page

            Assert.Same(h.Shell, OverlayShell.TopmostOpen(h.Window));
            Assert.True(OverlayShell.TryRedirectKey(h.Window, Key.Down, KeyModifiers.None));
            Assert.True(h.First.IsFocused);

            Assert.False(OverlayShell.TryRedirectKey(h.Window, Key.Down, KeyModifiers.None));   // already inside: the key is the dialog's own
            h.Window.Close();
        });
    }

    [Fact]
    public void AClosedDialog_IsNotModal()
    {
        Run(() =>
        {
            var h = HostShell();
            h.Shell.IsOpen = true;
            RunLayout(h.Window);
            h.Shell.IsOpen = false;
            RunLayout(h.Window);

            Assert.Null(OverlayShell.TopmostOpen(h.Window));
            Assert.False(OverlayShell.TryRedirectKey(h.Window, Key.Down, KeyModifiers.None));
            h.Window.Close();
        });
    }

    [Fact]
    public void AWheelTurnInsideAPopup_DoesNotReachThePageBehindIt()
    {
        Run(() =>
        {
            PopupWheelContainment.Install();
            var inside = new Border { Width = 120, Height = 80, Background = Avalonia.Media.Brushes.Gray };
            var anchor = new Button { Content = "Open" };
            var popup = new Popup { Child = inside, PlacementTarget = anchor };
            var page = new StackPanel { Children = { anchor, popup } };
            var window = Host(page);
            int reachedPage = 0;
            page.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => reachedPage++, RoutingStrategies.Bubble);

            popup.IsOpen = true;
            RunLayout(window);
            var wheel = new PointerWheelEventArgs(inside, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), window, default, 0,
                new PointerPointProperties(), KeyModifiers.None, new Vector(0, -1))
            {
                RoutedEvent = InputElement.PointerWheelChangedEvent,
            };
            inside.RaiseEvent(wheel);

            Assert.True(wheel.Handled);
            Assert.Equal(0, reachedPage);
            popup.IsOpen = false;
            window.Close();
        });
    }
}
