using System.Text;
using Avalonia.Input;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Human-readable names for bindings, for chips in Preferences, tooltips and the command palette. <see cref="InputBinding.ToString"/> is the stable text stored in <c>keymap.json</c>
/// (<c>Ctrl+OemComma</c>, <c>Mouse4</c>, <c>WheelUp</c>); this is what a person reads (<c>Ctrl+,</c>, <c>Mouse 4 (back)</c>, <c>Wheel up</c>). The display text is never parsed.
/// </summary>
public static class InputBindingDisplay
{
    public static string Format(InputBinding binding)
    {
        if (!binding.IsDefined)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        if (binding.Kind != InputBindingKind.Pad)
        {
            AppendModifiers(text, binding.Modifiers);
        }

        switch (binding.Kind)
        {
            case InputBindingKind.Key:
                text.Append(KeyName(binding.Key));
                break;
            case InputBindingKind.MouseButton:
                text.Append(ButtonName(binding.Button, binding.Clicks));
                break;
            case InputBindingKind.Wheel:
                text.Append(binding.Wheel switch
                {
                    WheelDirection.Up => "Wheel up",
                    WheelDirection.Down => "Wheel down",
                    WheelDirection.Left => "Swipe left",
                    _ => "Swipe right",
                });
                break;
            default:
                text.Append("Pad ").Append(PadName(binding.Pad));
                break;
        }

        return text.ToString();
    }

    /// <summary>Ctrl, Alt, Shift, Win: the order the app's menu hints and tooltips have always used (Ctrl+Shift+C, Alt+Shift+R), not the storage order of <see cref="InputBinding.ToString"/>.</summary>
    private static void AppendModifiers(StringBuilder text, KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            text.Append("Ctrl+");
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            text.Append("Alt+");
        }

        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            text.Append("Shift+");
        }

        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            text.Append("Win+");
        }
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "+",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemTilde => "`",
        Key.Back => "Backspace",
        Key.BrowserBack => "Browser Back",
        Key.BrowserForward => "Browser Forward",
        Key.MediaNextTrack => "Media Next",
        Key.MediaPreviousTrack => "Media Previous",
        Key.MediaPlayPause => "Media Play/Pause",
        Key.MediaStop => "Media Stop",
        Key.Return => "Enter",
        _ => key.ToString(),
    };

    private static string ButtonName(MouseButton button, int clicks)
    {
        bool dbl = clicks >= 2;
        return button switch
        {
            MouseButton.Left => dbl ? "Double-click" : "Left click",
            MouseButton.Middle => dbl ? "Double middle-click" : "Middle click",
            MouseButton.Right => dbl ? "Double right-click" : "Right click",
            MouseButton.XButton1 => dbl ? "Double-click Mouse 4 (back)" : "Mouse 4 (back)",
            MouseButton.XButton2 => dbl ? "Double-click Mouse 5 (forward)" : "Mouse 5 (forward)",
            _ => button.ToString(),
        };
    }

    private static string PadName(GamepadInput input) => input switch
    {
        GamepadInput.DPadUp => "D-pad up",
        GamepadInput.DPadDown => "D-pad down",
        GamepadInput.DPadLeft => "D-pad left",
        GamepadInput.DPadRight => "D-pad right",
        GamepadInput.LeftShoulder => "left bumper",
        GamepadInput.RightShoulder => "right bumper",
        GamepadInput.LeftThumb => "left stick click",
        GamepadInput.RightThumb => "right stick click",
        GamepadInput.LeftStickLeft => "left stick left",
        GamepadInput.LeftStickRight => "left stick right",
        GamepadInput.LeftStickUp => "left stick up",
        GamepadInput.LeftStickDown => "left stick down",
        GamepadInput.LeftStickX => "left stick (horizontal)",
        GamepadInput.LeftStickY => "left stick (vertical)",
        GamepadInput.RightStickX => "right stick (horizontal)",
        GamepadInput.RightStickY => "right stick (vertical)",
        GamepadInput.Triggers => "triggers",
        GamepadInput.Start => "Start",
        GamepadInput.Back => "Back",
        _ => input.ToString(),
    };
}
