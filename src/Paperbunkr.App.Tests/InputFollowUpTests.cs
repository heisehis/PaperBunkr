using Avalonia.Input;
using Paperbunkr.App.Services.Input;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The remappable "every screen" keys, controller capture and plugin-contributed actions (docs/superpowers/specs/2026-10-03-input-service-design.md §14).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class InputFollowUpTests
{
    private static InputService NewService() =>
        new(InputActionCatalog.CreateWithCoreActions(), new MemoryKeymapStore(), new AvaloniaInputSuppressionProbe());

    // ----- canonical keys -----

    [Fact]
    public void ThePlainKeyOfAnEveryScreenAction_PassesThroughWhileItIsBound()
    {
        var input = NewService();
        var seen = new List<string>();
        using var _ = input.Register(InputScope.Global, e => seen.Add(e.Action.Id));

        Assert.False(input.ProcessKey(Key.Down, KeyModifiers.None));      // the focused control gets its own Down
        Assert.False(input.ProcessKey(Key.Enter, KeyModifiers.None));
        Assert.False(input.ProcessKey(Key.Delete, KeyModifiers.None));
        Assert.DoesNotContain(InputActionIds.FocusDown, seen);
        Assert.DoesNotContain(InputActionIds.Activate, seen);
    }

    [Fact]
    public void AKeyBoundInsteadOfTheCanonicalOne_IsDeliveredAsTheAction_AndTheCanonicalKeyIsSwallowed()
    {
        var input = NewService();
        var seen = new List<string>();
        using var _ = input.Register(InputScope.Global, e =>
        {
            seen.Add(e.Action.Id);
            e.Handled = true;
        });
        input.SetBindings(new InputAction(InputActionIds.FocusDown), [InputBinding.ForKey(Key.J, KeyModifiers.Control)]);

        Assert.True(input.ProcessKey(Key.J, KeyModifiers.Control));
        Assert.Contains(InputActionIds.FocusDown, seen);

        // The plain Down arrow no longer belongs to the action, so it does nothing (that is what taking it away means)...
        Assert.True(input.ProcessKey(Key.Down, KeyModifiers.None));
        // ...except while typing in a text box, where an arrow is the caret's,
        Assert.False(input.ProcessKey(Key.Down, KeyModifiers.None, InputSuppression.TextEntry));
        // and a modified arrow (Shift+Down extending a selection) was never the action's key.
        Assert.False(input.ProcessKey(Key.Down, KeyModifiers.Shift));
    }

    [Fact]
    public void TheCanonicalKeyIsRestoredByResettingTheAction()
    {
        var input = NewService();
        input.SetBindings(new InputAction(InputActionIds.Activate), []);
        Assert.True(input.ProcessKey(Key.Enter, KeyModifiers.None));       // unbound: swallowed

        input.ResetBindings(new InputAction(InputActionIds.Activate));

        Assert.False(input.ProcessKey(Key.Enter, KeyModifiers.None));
    }

    [Fact]
    public void ASentCanonicalKey_IsNeverSwallowed_SoAMovedActionStillReachesItsControl()
    {
        var input = NewService();
        input.SetBindings(new InputAction(InputActionIds.Activate), [InputBinding.ForKey(Key.Q)]);

        // UiNavigation raises the key it forwards while IsSending; the service must let it through to the control.
        var window = new Avalonia.Controls.Window();
        var button = new Avalonia.Controls.Button();
        window.Content = button;
        window.Show();
        button.Focus();
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        var host = InputHost.Attach(window, input);
        using var global = input.Register(InputScope.Global, e => e.Handled = UiNavigation.TryHandle(window, e));

        Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window, Key.Q, RawInputModifiers.None, PhysicalKey.Q, null);

        Assert.Equal(1, clicks);
        host.Dispose();
        window.Close();
    }

    // ----- controller capture -----

    private static GamepadState Pad(GamepadButtons buttons = GamepadButtons.None) => new(buttons, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void WhileCapturing_ThePressedButtonIsReported_AndNoActionRuns()
    {
        var input = NewService();
        var actions = new List<string>();
        using var _ = input.Register(InputScope.Global, e => actions.Add(e.Action.Id));
        var captured = new List<GamepadInput>();
        var frame = TimeSpan.FromMilliseconds(16);

        using (input.BeginGamepadCapture(captured.Add))
        {
            input.ProcessGamepad(Pad(GamepadButtons.A), frame);
            input.ProcessGamepad(Pad(), frame);
        }

        Assert.Equal([GamepadInput.A], captured);
        Assert.Empty(actions);

        // After the capture ends the button is an ordinary action again.
        input.ProcessGamepad(Pad(GamepadButtons.A), frame);
        Assert.Contains(InputActionIds.Activate, actions);
    }

    [Fact]
    public void AnAnalogueAxis_IsNotOfferedAsAPressToBind()
    {
        var input = NewService();
        var captured = new List<GamepadInput>();
        using var _ = input.BeginGamepadCapture(captured.Add);

        input.ProcessGamepad(new GamepadState(GamepadButtons.None, 0, 255, 0, 0, 0, 30000), TimeSpan.FromMilliseconds(16));

        Assert.DoesNotContain(GamepadInput.Triggers, captured);
        Assert.DoesNotContain(GamepadInput.RightStickY, captured);
    }

    // ----- plugin actions -----

    private static CSharpCommand Cmd(string plugin, string key, string? shortcut = null, bool enabled = true) =>
        new() { PluginKey = plugin, Hook = "Library", Key = key, Name = key, Shortcut = shortcut, Enabled = enabled, ScriptPath = "x.csx" };

    [Fact]
    public void PluginCommands_GetCesF1ToF12Defaults_ExceptWhereTheyDeclareTheirOwn()
    {
        var infos = PluginInputActions.Build([Cmd("p", "a"), Cmd("p", "b", "Ctrl+Alt+K"), Cmd("p", "c"), Cmd("p", "d", "not a key")]);

        Assert.Equal(4, infos.Count);
        Assert.Equal([InputBinding.ForKey(Key.F1, KeyModifiers.Control | KeyModifiers.Shift)], infos[0].Defaults);
        Assert.Equal([InputBinding.ForKey(Key.K, KeyModifiers.Control | KeyModifiers.Alt)], infos[1].Defaults);
        Assert.Equal([InputBinding.ForKey(Key.F2, KeyModifiers.Control | KeyModifiers.Shift)], infos[2].Defaults);
        Assert.Equal([InputBinding.ForKey(Key.F3, KeyModifiers.Control | KeyModifiers.Shift)], infos[3].Defaults);       // unparseable declaration: the host default
        Assert.All(infos, i => Assert.Equal(PluginInputActions.Group, i.Group));
        Assert.All(infos, i => Assert.Equal(InputScope.Library, i.Scope));
        Assert.Equal("Plugin.p.a", infos[0].Id);
    }

    [Fact]
    public void ADisabledCommand_GetsNoAction_AndOnlyTwelveGetAutomaticKeys()
    {
        var many = Enumerable.Range(0, 14).Select(i => Cmd("p", $"c{i}")).ToList();
        many.Add(Cmd("p", "off", enabled: false));

        var infos = PluginInputActions.Build(many);

        Assert.Equal(14, infos.Count);
        Assert.Equal(12, infos.Count(i => i.Defaults.Count == 1));
        Assert.DoesNotContain(infos, i => i.Id == "Plugin.p.off");
    }

    [Fact]
    public void Sync_AddsAndRemovesActions_AndKeepsTheUsersRemapAcrossAnOffOnCycle()
    {
        var input = NewService();
        PluginInputActions.Sync(input.Actions, [Cmd("p", "a"), Cmd("p", "b")]);
        Assert.NotNull(input.Actions.Find(new InputAction("Plugin.p.a")));
        input.SetBindings(new InputAction("Plugin.p.a"), [InputBinding.ForKey(Key.G, KeyModifiers.Alt)]);

        PluginInputActions.Sync(input.Actions, [Cmd("p", "b")]);
        Assert.Null(input.Actions.Find(new InputAction("Plugin.p.a")));
        Assert.NotNull(input.Actions.Find(new InputAction("Plugin.p.b")));

        PluginInputActions.Sync(input.Actions, [Cmd("p", "a"), Cmd("p", "b")]);
        Assert.Equal([InputBinding.ForKey(Key.G, KeyModifiers.Alt)], input.GetBindings(new InputAction("Plugin.p.a")));
    }

    [Fact]
    public void APluginAction_IsDeliveredToTheLibraryScope()
    {
        var input = NewService();
        PluginInputActions.Sync(input.Actions, [Cmd("p", "a")]);
        var seen = new List<string>();
        using var _ = input.Register(InputScope.Library, e =>
        {
            seen.Add(e.Action.Id);
            e.Handled = true;
        });

        Assert.True(input.ProcessKey(Key.F1, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Equal(["Plugin.p.a"], seen);
    }
}
