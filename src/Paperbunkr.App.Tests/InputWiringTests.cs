using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>The pure helpers the book reader and Preferences rely on: the DOM-key map and the display names.</summary>
public class WebKeyMapTests
{
    [Theory]
    [InlineData(Key.Left, "ArrowLeft")]
    [InlineData(Key.Right, "ArrowRight")]
    [InlineData(Key.Up, "ArrowUp")]
    [InlineData(Key.Down, "ArrowDown")]
    [InlineData(Key.PageDown, "PageDown")]
    [InlineData(Key.PageUp, "PageUp")]
    [InlineData(Key.Space, " ")]
    [InlineData(Key.A, "a")]
    [InlineData(Key.W, "w")]
    [InlineData(Key.D0, "0")]
    [InlineData(Key.D9, "9")]
    [InlineData(Key.F1, "F1")]
    [InlineData(Key.F12, "F12")]
    [InlineData(Key.Escape, "Escape")]
    [InlineData(Key.OemComma, ",")]
    public void KeysRoundTripBetweenAvaloniaAndTheDom(Key key, string jsKey)
    {
        Assert.True(WebKeyMap.TryToJsKey(key, out string js));
        Assert.Equal(jsKey, js);
        Assert.True(WebKeyMap.TryFromJsKey(jsKey, out var back));
        Assert.Equal(key, back);
    }

    [Fact]
    public void EveryMappedKey_RoundTrips()
    {
        foreach (var key in Enum.GetValues<Key>())
        {
            if (WebKeyMap.TryToJsKey(key, out string js))
            {
                Assert.True(WebKeyMap.TryFromJsKey(js, out var back), $"{key} -> '{js}' did not come back");
                Assert.Equal(key, back);
            }
        }
    }

    [Fact]
    public void ASingleLetter_IsMatchedInEitherCase_ButNothingElseIs()
    {
        Assert.True(WebKeyMap.TryFromJsKey("W", out var upper));
        Assert.Equal(Key.W, upper);
        Assert.False(WebKeyMap.TryFromJsKey("", out _));
        Assert.False(WebKeyMap.TryFromJsKey("Dead", out _));
        Assert.False(WebKeyMap.TryFromJsKey("arrowleft", out _));   // multi-character names are exact, as the DOM reports them
    }

    [Theory]
    [InlineData("w", true, false, true, false, "ctrl+shift+w")]
    [InlineData("W", true, false, true, false, "ctrl+shift+w")]
    [InlineData("ArrowRight", false, false, false, false, "ArrowRight")]
    [InlineData(" ", false, false, false, false, " ")]
    [InlineData("PageDown", true, true, true, true, "ctrl+alt+shift+meta+PageDown")]
    public void Normalize_BuildsTheStringThePageAndTheAppCompare(string key, bool ctrl, bool alt, bool shift, bool meta, string expected) =>
        Assert.Equal(expected, WebKeyMap.Normalize(key, ctrl, alt, shift, meta));

    [Fact]
    public void NormalizedKeysFor_ListsOnlyKeyboardBindings_WithoutDuplicates()
    {
        var keys = WebKeyMap.NormalizedKeysFor(
        [
            InputBinding.ForKey(Key.Right),
            InputBinding.ForKey(Key.Right),
            InputBinding.ForKey(Key.PageDown),
            InputBinding.ForKey(Key.Space),
            InputBinding.ForKey(Key.W, KeyModifiers.Control | KeyModifiers.Shift),
            InputBinding.ForMouseButton(MouseButton.XButton1),
            InputBinding.ForWheel(WheelDirection.Up, KeyModifiers.Control),
            InputBinding.ForPad(GamepadInput.A),
        ]);

        Assert.Equal(["ArrowRight", "PageDown", " ", "ctrl+shift+w"], keys);
    }

    [Fact]
    public void TheBookReadersDefaults_ProduceExactlyTheKeysItsOldHardcodedScriptHandled()
    {
        var catalog = InputActionCatalog.CreateWithCoreActions();
        var bindings = catalog.All.Where(i => i.Scope.Name == InputScope.BookReader.Name).SelectMany(i => i.Defaults);

        var keys = WebKeyMap.NormalizedKeysFor(bindings);

        Assert.Equal(["ArrowRight", "PageDown", " ", "ArrowLeft", "PageUp", "ctrl+shift+w"], keys);
    }

    [Fact]
    public void ModifiersFor_CombinesTheFlags()
    {
        Assert.Equal(KeyModifiers.None, WebKeyMap.ModifiersFor(false, false, false, false));
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, WebKeyMap.ModifiersFor(true, false, true, false));
        Assert.Equal(KeyModifiers.Alt | KeyModifiers.Meta, WebKeyMap.ModifiersFor(false, true, false, true));
    }

    [Fact]
    public void AKeyForwardedFromTheWebView_ReachesTheServicesHandlers()
    {
        var service = new InputService(InputActionCatalog.CreateWithCoreActions(), new MemoryKeymapStore());
        var seen = new List<string>();
        using var reg = service.Register(InputScope.BookReader, e => { seen.Add(e.Action.Id); e.Handled = true; });

        Assert.True(WebKeyMap.TryFromJsKey("ArrowRight", out var key));
        Assert.True(service.ProcessKey(key, WebKeyMap.ModifiersFor(false, false, false, false)));
        Assert.True(WebKeyMap.TryFromJsKey("W", out var w));
        Assert.True(service.ProcessKey(w, WebKeyMap.ModifiersFor(true, false, true, false)));

        Assert.Equal([InputActionIds.BookNextPage, InputActionIds.BookAnnouncePosition], seen);
    }
}

public class InputBindingDisplayTests
{
    [Theory]
    [InlineData("Left", "Left")]
    [InlineData("Ctrl+Shift+C", "Ctrl+Shift+C")]
    [InlineData("PageDown", "PageDown")]
    [InlineData("D3", "3")]
    [InlineData("Alt+Shift+D3", "Alt+Shift+3")]
    [InlineData("Shift+Alt+D3", "Alt+Shift+3")]
    [InlineData("Ctrl+Alt+Shift+K", "Ctrl+Alt+Shift+K")]
    [InlineData("Ctrl+OemComma", "Ctrl+,")]
    [InlineData("OemQuestion", "/")]
    [InlineData("BrowserBack", "Browser Back")]
    [InlineData("MediaNextTrack", "Media Next")]
    [InlineData("Back", "Backspace")]
    [InlineData("Mouse4", "Mouse 4 (back)")]
    [InlineData("Mouse5", "Mouse 5 (forward)")]
    [InlineData("MouseMiddle", "Middle click")]
    [InlineData("MouseDoubleLeft", "Double-click")]
    [InlineData("Ctrl+WheelUp", "Ctrl+Wheel up")]
    [InlineData("WheelLeft", "Swipe left")]
    [InlineData("Pad:A", "Pad A")]
    [InlineData("Pad:DPadLeft", "Pad D-pad left")]
    [InlineData("Pad:RightShoulder", "Pad right bumper")]
    [InlineData("Pad:Triggers", "Pad triggers")]
    public void Format_GivesAReadableName(string text, string expected) =>
        Assert.Equal(expected, InputBindingDisplay.Format(InputBinding.Parse(text)));

    [Fact]
    public void Format_OfNothing_IsEmpty() => Assert.Equal(string.Empty, InputBindingDisplay.Format(default));

    [Fact]
    public void EveryDefaultBinding_HasADisplayName_DifferentFromNothing()
    {
        foreach (var info in InputActions.Core)
        {
            foreach (var binding in info.Defaults)
            {
                Assert.False(string.IsNullOrWhiteSpace(InputBindingDisplay.Format(binding)), $"{info.Id}: '{binding}'");
            }
        }
    }
}

/// <summary>The Avalonia-facing parts of the service, in a real (headless) window: the host's Tunnel hook, scope registration that follows visibility and focus, and the capture box.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class InputWiringTests
{
    private static InputService NewService() =>
        new(InputActionCatalog.CreateWithCoreActions(), new MemoryKeymapStore(), new AvaloniaInputSuppressionProbe());

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        string physical = key switch
        {
            Key.LeftCtrl => "ControlLeft",
            Key.LeftShift => "ShiftLeft",
            Key.LeftAlt => "AltLeft",
            _ => key.ToString(),
        };
        window.KeyPress(key, modifiers, (PhysicalKey)Enum.Parse(typeof(PhysicalKey), physical), null);
    }

    // ----- InputHost -----

    [Fact]
    public void TheHost_ForwardsKeysFromAnyFocusedControl_ToTheService()
    {
        var service = NewService();
        var seen = new List<string>();
        using var reg = service.Register(InputScope.Global, e => { seen.Add(e.Action.Id); e.Handled = true; });
        var button = new Button { Content = "x" };
        var window = new Window { Content = button };
        using var host = InputHost.Attach(window, service);
        window.Show();
        try
        {
            button.Focus();
            Press(window, Key.Q, RawInputModifiers.Control);

            Assert.Equal([InputActionIds.Quit], seen);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ATextBoxKeepsItsKeys_ExceptTheFewActionsThatWorkWhileTyping()
    {
        var service = NewService();
        var seen = new List<string>();
        using var reg = service.Register(InputScope.Global, e => { seen.Add(e.Action.Id); e.Handled = true; });
        var box = new TextBox();
        var window = new Window { Content = box };
        using var host = InputHost.Attach(window, service);
        window.Show();
        try
        {
            box.Focus();
            Press(window, Key.Z, RawInputModifiers.Control);   // Undo: left to the text box (its own undo)
            Press(window, Key.Q, RawInputModifiers.Control);   // Quit: ditto
            Press(window, Key.Escape);                          // Escape works while typing
            Press(window, Key.P, RawInputModifiers.Control);   // Quick open too

            Assert.Equal([InputActionIds.CloseCurrentView, InputActionIds.OpenQuickOpen], seen);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void TheHost_StopsForwardingOnceDisposed()
    {
        var service = NewService();
        int count = 0;
        using var reg = service.Register(InputScope.Global, e => { count++; e.Handled = true; });
        var button = new Button();
        var window = new Window { Content = button };
        var host = InputHost.Attach(window, service);
        window.Show();
        try
        {
            button.Focus();
            Press(window, Key.Q, RawInputModifiers.Control);
            host.Dispose();
            host.Dispose();
            Press(window, Key.Q, RawInputModifiers.Control);

            Assert.Equal(1, count);
        }
        finally
        {
            window.Close();
        }
    }

    // ----- AttachedInputRegistration -----

    [Fact]
    public void ARegistration_IsLiveWhileAttachedAndVisible_AndDormantWhenHidden_OrWhenDetached()
    {
        var service = NewService();
        var seen = new List<string>();
        var owner = new Border();
        var window = new Window { Content = owner };
        using var registration = new AttachedInputRegistration(owner, InputScope.Library, e => { seen.Add(e.Action.Id); e.Handled = true; }, service: service);
        Assert.False(registration.IsRegistered);   // not attached yet
        window.Show();
        try
        {
            Assert.True(registration.IsRegistered);
            Assert.True(service.ProcessKey(Key.F5, KeyModifiers.None));

            owner.IsVisible = false;
            Assert.False(service.ProcessKey(Key.F5, KeyModifiers.None));   // registered but dormant

            owner.IsVisible = true;
            Assert.True(service.ProcessKey(Key.F5, KeyModifiers.None));

            window.Content = null;   // detached
            Assert.False(registration.IsRegistered);
            Assert.False(service.ProcessKey(Key.F5, KeyModifiers.None));
        }
        finally
        {
            window.Close();
        }

        Assert.Equal([InputActionIds.Refresh, InputActionIds.Refresh], seen);
    }

    [Fact]
    public void ARegistrationWithAFocusRoot_GoesDormantWhileFocusIsInADialogOutsideTheScreen()
    {
        var service = NewService();
        int count = 0;
        var screenButton = new Button { Content = "in the screen" };
        var screen = new StackPanel { Children = { screenButton } };
        var dialogButton = new Button { Content = "in a dialog" };
        var window = new Window { Content = new StackPanel { Children = { screen, dialogButton } } };
        using var registration = new AttachedInputRegistration(
            screen, InputScope.Library, e => { count++; e.Handled = true; }, service: service, focusRoot: () => screen);
        window.Show();
        try
        {
            screenButton.Focus();
            Assert.True(service.ProcessKey(Key.F5, KeyModifiers.None));

            dialogButton.Focus();
            Assert.False(service.ProcessKey(Key.F5, KeyModifiers.None));

            screenButton.Focus();
            Assert.True(service.ProcessKey(Key.F5, KeyModifiers.None));
            Assert.Equal(2, count);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ARegistration_FollowsAReplacedService()
    {
        var first = NewService();
        var second = NewService();
        int count = 0;
        var owner = new Border();
        var window = new Window { Content = owner };
        using var registration = new AttachedInputRegistration(owner, InputScope.Library, e => { count++; e.Handled = true; }, service: first);
        window.Show();
        try
        {
            registration.Service = second;

            Assert.False(first.ProcessKey(Key.F5, KeyModifiers.None));
            Assert.True(second.ProcessKey(Key.F5, KeyModifiers.None));
            Assert.Equal(1, count);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ADisposedRegistration_NeverRegistersAgain()
    {
        var service = NewService();
        var owner = new Border();
        var window = new Window { Content = owner };
        var registration = new AttachedInputRegistration(owner, InputScope.Library, e => e.Handled = true, service: service);
        window.Show();
        try
        {
            registration.Dispose();
            registration.Dispose();
            owner.IsVisible = false;
            owner.IsVisible = true;

            Assert.False(registration.IsRegistered);
            Assert.False(service.ProcessKey(Key.F5, KeyModifiers.None));
        }
        finally
        {
            window.Close();
        }
    }

    // ----- BindingCaptureBox -----

    private static (Window Window, BindingCaptureBox Box, List<InputBinding> Captured, Func<int> Cancels) NewCaptureWindow(InputService? service = null)
    {
        var captured = new List<InputBinding>();
        int cancels = 0;
        var box = new BindingCaptureBox
        {
            CaptureCommand = new RelayCommand<InputBinding>(captured.Add),
            CancelCommand = new RelayCommand(() => cancels++),
        };
        var window = new Window { Width = 300, Height = 120, Content = box };
        if (service is not null)
        {
            InputHost.Attach(window, service);
        }

        window.Show();
        box.Focus();
        return (window, box, captured, () => cancels);
    }

    [Fact]
    public void TheCaptureBox_ReportsAKeyWithItsModifiers()
    {
        var (window, _, captured, _) = NewCaptureWindow();
        try
        {
            Press(window, Key.J, RawInputModifiers.Control | RawInputModifiers.Shift);

            Assert.Equal([InputBinding.ForKey(Key.J, KeyModifiers.Control | KeyModifiers.Shift)], captured);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void TheCaptureBox_WaitsForTheKeyAModifierModifies()
    {
        var (window, _, captured, _) = NewCaptureWindow();
        try
        {
            Press(window, Key.LeftCtrl, RawInputModifiers.Control);
            Press(window, Key.LeftShift, RawInputModifiers.Shift);

            Assert.Empty(captured);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void EscapeCancelsTheCapture_AndIsNotCapturedAsABinding()
    {
        var (window, _, captured, cancels) = NewCaptureWindow();
        try
        {
            Press(window, Key.Escape);

            Assert.Empty(captured);
            Assert.Equal(1, cancels());
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void TheCaptureBox_ReportsWheelTurnsAndMouseSideButtons()
    {
        var (window, _, captured, _) = NewCaptureWindow();
        try
        {
            var inside = new Point(100, 50);
            window.MouseWheel(inside, new Vector(0, 1), RawInputModifiers.Control);
            window.MouseWheel(inside, new Vector(0, -1), RawInputModifiers.None);
            window.MouseDown(inside, MouseButton.XButton1);
            window.MouseUp(inside, MouseButton.XButton1);
            window.MouseDown(inside, MouseButton.Middle);
            window.MouseUp(inside, MouseButton.Middle);

            Assert.Equal(
                [InputBinding.ForWheel(WheelDirection.Up, KeyModifiers.Control), InputBinding.ForWheel(WheelDirection.Down),
                 InputBinding.ForMouseButton(MouseButton.XButton1), InputBinding.ForMouseButton(MouseButton.Middle)],
                captured);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void WhileTheCaptureBoxHasFocus_TheServiceActsOnNothing_SoEscapeAndTheThumbButtonAndWheelReachTheBox()
    {
        var service = NewService();
        var acted = new List<string>();
        using var reg = service.Register(InputScope.Global, e => { acted.Add(e.Action.Id); e.Handled = true; });
        var (window, _, captured, cancels) = NewCaptureWindow(service);
        try
        {
            var inside = new Point(100, 50);
            Press(window, Key.Q, RawInputModifiers.Control);   // would quit
            window.MouseDown(inside, MouseButton.XButton1);    // would navigate back
            window.MouseUp(inside, MouseButton.XButton1);
            window.MouseWheel(inside, new Vector(-2, 0), RawInputModifiers.None);   // a swipe: would navigate back
            Press(window, Key.Escape);                          // would close the current view

            Assert.Empty(acted);
            Assert.Contains(InputBinding.ForKey(Key.Q, KeyModifiers.Control), captured);
            Assert.Contains(InputBinding.ForMouseButton(MouseButton.XButton1), captured);
            Assert.Equal(1, cancels());
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ShowingTheCaptureBox_FocusesIt()
    {
        var box = new BindingCaptureBox { IsVisible = false };
        var window = new Window { Content = box };
        window.Show();
        try
        {
            box.IsVisible = true;
            TestDispatcher.Drain();

            Assert.True(box.IsFocused);
            Assert.Equal(InputSuppression.All, box.Suppression);
        }
        finally
        {
            window.Close();
        }
    }
}
