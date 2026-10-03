using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The "every screen" actions and the app-wide controller host (docs/superpowers/specs/2026-10-03-input-service-design.md §14): a controller button reaches the focused control as
/// the key it already understands, a screen can take an action first, and the poller follows the setting and the window.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class UiNavigationTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private static GamepadState Pad(GamepadButtons buttons = GamepadButtons.None, short ry = 0) => new(buttons, 0, 0, 0, 0, 0, ry);

    /// <summary>A window wired the way MainWindow is: the input host, and a Global handler that runs the "every screen" actions.</summary>
    private sealed class Rig : IDisposable
    {
        public Rig(Action<StackPanel>? fill = null, IInputService? input = null)
        {
            Input = input ?? ReaderTestInput.Create();
            Panel = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
            if (fill is null)
            {
                for (int i = 0; i < 3; i++)
                {
                    int index = i;
                    var button = new Button { Content = $"Item {i}", Focusable = true };
                    button.Click += (_, _) => Clicks.Add(index);
                    Panel.Children.Add(button);
                }
            }
            else
            {
                fill(Panel);
            }

            Window = new Window { Content = Panel, Width = 400, Height = 300 };
            Host = InputHost.Attach(Window, Input);
            Handler = Input.Register(InputScope.Global, e => e.Handled = UiNavigation.TryHandle(Window, e));
            Window.Show();
            Window.UpdateLayout();
        }

        public IInputService Input { get; }

        public StackPanel Panel { get; }

        public Window Window { get; }

        public InputHost Host { get; }

        public IDisposable Handler { get; }

        public List<int> Clicks { get; } = [];

        public Button ButtonAt(int index) => (Button)Panel.Children[index];

        public void Press(GamepadButtons buttons)
        {
            Input.ProcessGamepad(Pad(buttons), Frame);
            Input.ProcessGamepad(Pad(), Frame);      // release, so the next press is a new one
        }

        public void Dispose()
        {
            Handler.Dispose();
            Host.Dispose();
            Window.Close();
        }
    }

    [Fact]
    public void ControllerBindings_AreTheDefaults()
    {
        var input = ReaderTestInput.Create();

        Assert.Contains(InputBinding.ForPad(GamepadInput.A), input.GetBindings(new InputAction(InputActionIds.Activate)));
        Assert.Contains(InputBinding.ForPad(GamepadInput.DPadDown), input.GetBindings(new InputAction(InputActionIds.FocusDown)));
        Assert.Contains(InputBinding.ForPad(GamepadInput.B), input.GetBindings(new InputAction(InputActionIds.CloseCurrentView)));
        Assert.Contains(InputBinding.ForPad(GamepadInput.RightShoulder), input.GetBindings(new InputAction(InputActionIds.TabNext)));
        Assert.Contains(InputBinding.ForPad(GamepadInput.RightStickY), input.GetBindings(new InputAction(InputActionIds.ScrollVertical)));
        Assert.DoesNotContain(input.GetBindings(new InputAction(InputActionIds.DeleteItem)), b => b.Kind == InputBindingKind.Pad);     // nothing destructive on a pad by default
    }

    [Fact]
    public void PadA_PressesTheFocusedButton()
    {
        using var rig = new Rig();
        rig.ButtonAt(1).Focus();

        rig.Press(GamepadButtons.A);

        Assert.Equal([1], rig.Clicks);
    }

    [Fact]
    public void DPadDown_MovesFocusToTheNextControl_AndUp_MovesBack()
    {
        using var rig = new Rig();
        rig.ButtonAt(0).Focus();

        rig.Press(GamepadButtons.DPadDown);
        Assert.True(rig.ButtonAt(1).IsFocused, "D-pad down should move to the second button");

        rig.Press(GamepadButtons.DPadDown);
        Assert.True(rig.ButtonAt(2).IsFocused);

        rig.Press(GamepadButtons.DPadUp);
        Assert.True(rig.ButtonAt(1).IsFocused);
    }

    [Fact]
    public void DPad_WithNothingFocused_FocusesTheFirstControl()
    {
        using var rig = new Rig();
        rig.Window.Focus();

        rig.Press(GamepadButtons.DPadDown);

        Assert.NotNull(rig.Window.FocusManager!.GetFocusedElement());
    }

    [Fact]
    public void ADirectionalKey_ReachesTheFocusedControlsOwnHandler_BeforeAnyFallback()
    {
        // A control that uses the arrow itself (a grid tile, a slider) must get the key, not have focus moved out from under it.
        var seen = new List<Key>();
        using var rig = new Rig(panel =>
        {
            var tile = new Button { Content = "tile", Focusable = true };
            tile.KeyDown += (_, e) =>
            {
                seen.Add(e.Key);
                e.Handled = true;
            };
            panel.Children.Add(tile);
            panel.Children.Add(new Button { Content = "other", Focusable = true });
        });
        rig.ButtonAt(0).Focus();

        rig.Press(GamepadButtons.DPadDown);

        Assert.Equal([Key.Down], seen);
        Assert.True(rig.ButtonAt(0).IsFocused);
    }

    [Fact]
    public void DPadRight_LeavesATextBox_BecauseAPadHasNoCaret()
    {
        TextBox? box = null;
        using var rig = new Rig(panel =>
        {
            box = new TextBox { Text = "abc", Focusable = true };
            panel.Orientation = Orientation.Horizontal;
            panel.Children.Add(box);
            panel.Children.Add(new Button { Content = "next", Focusable = true });
        });
        box!.Focus();

        rig.Press(GamepadButtons.DPadRight);

        Assert.False(box.IsFocused);
    }

    [Fact]
    public void UserBindingTheRealEnterKeyToActivate_ClicksOnce_NotInALoop()
    {
        using var rig = new Rig();
        rig.Input.SetBindings(new InputAction(InputActionIds.Activate), [InputBinding.ForKey(Key.Enter)]);
        rig.ButtonAt(0).Focus();

        rig.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        // The action sends Enter to the button; that sent Enter must not resolve to the action again, and the real one the user pressed is consumed by it.
        Assert.Single(rig.Clicks);
    }

    [Fact]
    public void AScreenScopedHandler_TakesAnActionBeforeTheGlobalFallback()
    {
        using var rig = new Rig();
        bool screenGotIt = false;
        using var screen = rig.Input.Register(InputScope.Library, e =>
        {
            if (e.Action.Id == InputActionIds.Activate)
            {
                screenGotIt = true;
                e.Handled = true;
            }
        });
        rig.ButtonAt(0).Focus();

        // A Global action reaches every active scope, most specific first: the Library scope (priority 100) is asked before the Global handler that would press the button.
        rig.Input.Dispatch(new InputAction(InputActionIds.Activate));

        Assert.True(screenGotIt);
        Assert.Empty(rig.Clicks);
    }

    [Fact]
    public void RightStick_ScrollsTheScrollViewerAroundTheFocusedControl()
    {
        // A ScrollViewer has no template, so no extent, without a theme.
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        try
        {
            RightStickScrolls();
        }
        finally
        {
            Application.Current!.Styles.Remove(theme);
        }
    }

    private static void RightStickScrolls()
    {
        ScrollViewer? scroller = null;
        using var rig = new Rig(panel =>
        {
            var content = new StackPanel();
            for (int i = 0; i < 40; i++)
            {
                content.Children.Add(new Button { Content = $"Row {i}", Height = 40, Focusable = true });
            }

            scroller = new ScrollViewer { Content = content, Height = 200 };
            panel.Children.Add(scroller);
        });
        rig.Window.UpdateLayout();
        ((Button)((StackPanel)scroller!.Content!).Children[0]).Focus();

        rig.Input.ProcessGamepad(Pad(ry: -30000), Frame);      // stick pushed down (XInput reports down as negative Y; the processor flips it)
        rig.Input.ProcessGamepad(Pad(ry: -30000), Frame);

        Assert.True(scroller.Offset.Y > 0, $"offset {scroller.Offset} extent {scroller.Extent} viewport {scroller.Viewport} focused {rig.Window.FocusManager!.GetFocusedElement()?.GetType().Name}");
    }

    [Fact]
    public void GamepadActivity_IsRaisedOnlyWhenThePadIsTouched()
    {
        var input = ReaderTestInput.Create();
        int raised = 0;
        input.GamepadActivity += (_, _) => raised++;

        input.ProcessGamepad(Pad(), Frame);
        Assert.Equal(0, raised);

        input.ProcessGamepad(Pad(GamepadButtons.X), Frame);
        Assert.Equal(1, raised);
    }

    // ----- The app-wide poller -----

    private sealed class FakeSource : IGamepadSource
    {
        public GamepadState State;

        public bool TryGetState(int slot, out GamepadState state)
        {
            state = State;
            return slot == 0;
        }
    }

    [Fact]
    public void Host_PollsOnlyWhileTheSettingIsOn_AndStopsWhenItIsTurnedOff()
    {
        var input = ReaderTestInput.Create();
        var window = new Window { Width = 200, Height = 100 };
        window.Show();
        using var host = new AppGamepadHost(window, input, new FakeSource());

        Assert.False(host.Poller.IsRunning, "off by default until the setting says otherwise");

        input.GamepadEnabled = true;
        Assert.Equal(window.IsActive, host.Poller.IsRunning);

        input.GamepadEnabled = false;
        Assert.False(host.Poller.IsRunning);
        window.Close();
    }

    [Fact]
    public void Host_DeliversPolledSnapshotsToTheInputService()
    {
        var input = ReaderTestInput.Create();
        var window = new Window { Width = 200, Height = 100 };
        window.Show();
        var source = new FakeSource { State = Pad(GamepadButtons.A) };
        using var host = new AppGamepadHost(window, input, source);
        var seen = new List<string>();
        input.ActionTriggered += (_, e) => seen.Add(e.Action.Id);

        host.Poller.Poll(GamepadPoller.ProbeInterval);       // finds the controller in slot 0
        host.Poller.Poll(Frame);

        Assert.Contains(InputActionIds.Activate, seen);
        window.Close();
    }
}
