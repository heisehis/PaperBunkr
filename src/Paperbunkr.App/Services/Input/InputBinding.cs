using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Avalonia.Input;

namespace Paperbunkr.App.Services.Input;

/// <summary>What kind of physical input a binding names.</summary>
public enum InputBindingKind
{
    /// <summary>A keyboard key with modifiers.</summary>
    Key,

    /// <summary>A mouse button press (single or double) with modifiers.</summary>
    MouseButton,

    /// <summary>A wheel turn or horizontal tilt in one direction, with modifiers.</summary>
    Wheel,

    /// <summary>A gamepad button, stick direction or analogue axis.</summary>
    Pad,
}

/// <summary>Direction of a wheel binding. <see cref="Left"/>/<see cref="Right"/> are the horizontal tilt or two-finger swipe.</summary>
public enum WheelDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// A controller input a binding can name. The digital members are edges (with key-repeat for the directional and face/shoulder ones); the stick-direction
/// members are the left stick pushed past the digital threshold, so it can stand in for the D-pad; the last group are analogue axes, which only
/// <see cref="InputActionKind.Axis"/> actions are bound to.
/// </summary>
public enum GamepadInput
{
    None = 0,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    Start,
    Back,
    LeftThumb,
    RightThumb,
    LeftShoulder,
    RightShoulder,
    A,
    B,
    X,
    Y,
    LeftStickLeft,
    LeftStickRight,
    LeftStickUp,
    LeftStickDown,

    /// <summary>Left stick horizontal axis, -1 (left) to 1 (right), after the dead zone.</summary>
    LeftStickX,

    /// <summary>Left stick vertical axis, -1 (up) to 1 (down), after the dead zone.</summary>
    LeftStickY,

    /// <summary>Right stick horizontal axis, -1 (left) to 1 (right), after the dead zone.</summary>
    RightStickX,

    /// <summary>Right stick vertical axis, -1 (up) to 1 (down), after the dead zone.</summary>
    RightStickY,

    /// <summary>Right trigger minus left trigger, -1 to 1, after the dead zone.</summary>
    Triggers,
}

/// <summary>
/// One physical input in the unified gesture space (docs/superpowers/specs/2026-10-03-input-service-design.md §5.3): a key with modifiers, a mouse button
/// (single or double click) with modifiers, a wheel direction with modifiers, or a gamepad input. Mirrors ComicRack's <c>CommandKey</c>, which puts keys, mouse
/// buttons, wheel and tilt in one enum OR'd with Ctrl/Shift/Alt. Immutable and compared by value, so it is the lookup key of the keymap index.
/// </summary>
/// <remarks>
/// The text form (<see cref="ToString"/> / <see cref="TryParse"/>) is what <c>keymap.json</c> stores. It is deliberately not <c>KeyGesture.Parse</c>: that cannot
/// express wheel, mouse or gamepad bindings. Examples: <c>Ctrl+Shift+K</c>, <c>PageDown</c>, <c>BrowserBack</c>, <c>Mouse4</c>, <c>MouseDoubleLeft</c>,
/// <c>Ctrl+WheelUp</c>, <c>WheelLeft</c>, <c>Pad:A</c>. Key names are Avalonia's <see cref="Avalonia.Input.Key"/> members (digits are <c>D1</c>…<c>D9</c>).
/// </remarks>
public readonly record struct InputBinding
{
    private InputBinding(InputBindingKind kind, Key key, KeyModifiers modifiers, MouseButton button, int clicks, WheelDirection wheel, GamepadInput pad)
    {
        Kind = kind;
        Key = key;
        Modifiers = modifiers;
        Button = button;
        Clicks = clicks;
        Wheel = wheel;
        Pad = pad;
    }

    public InputBindingKind Kind { get; }

    /// <summary>The key, for <see cref="InputBindingKind.Key"/> bindings; <see cref="Avalonia.Input.Key.None"/> otherwise.</summary>
    public Key Key { get; }

    /// <summary>Modifier keys held; always <see cref="KeyModifiers.None"/> for <see cref="InputBindingKind.Pad"/>.</summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>The button, for <see cref="InputBindingKind.MouseButton"/> bindings (<see cref="MouseButton.XButton1"/>/<see cref="MouseButton.XButton2"/> are the thumb buttons).</summary>
    public MouseButton Button { get; }

    /// <summary>1 for a single click, 2 for a double click; 0 for everything that is not a mouse button.</summary>
    public int Clicks { get; }

    /// <summary>The direction, for <see cref="InputBindingKind.Wheel"/> bindings.</summary>
    public WheelDirection Wheel { get; }

    /// <summary>The controller input, for <see cref="InputBindingKind.Pad"/> bindings.</summary>
    public GamepadInput Pad { get; }

    /// <summary>The device class this binding belongs to.</summary>
    public InputDevice Device => Kind switch
    {
        InputBindingKind.Key => InputDevice.Keyboard,
        InputBindingKind.Pad => InputDevice.Gamepad,
        _ => InputDevice.Mouse,
    };

    /// <summary>False only for <see langword="default"/>, which names no input.</summary>
    public bool IsDefined => Kind switch
    {
        InputBindingKind.Key => Key != Key.None,
        InputBindingKind.MouseButton => Button != MouseButton.None,
        InputBindingKind.Pad => Pad != GamepadInput.None,
        _ => true,
    };

    public static InputBinding ForKey(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        new(InputBindingKind.Key, key, modifiers, MouseButton.None, 0, default, GamepadInput.None);

    /// <param name="clicks">1 for a single click, 2 for a double click; anything above 2 counts as 2.</param>
    public static InputBinding ForMouseButton(MouseButton button, KeyModifiers modifiers = KeyModifiers.None, int clicks = 1) =>
        new(InputBindingKind.MouseButton, Key.None, modifiers, button, clicks >= 2 ? 2 : 1, default, GamepadInput.None);

    public static InputBinding ForWheel(WheelDirection direction, KeyModifiers modifiers = KeyModifiers.None) =>
        new(InputBindingKind.Wheel, Key.None, modifiers, MouseButton.None, 0, direction, GamepadInput.None);

    public static InputBinding ForPad(GamepadInput input) =>
        new(InputBindingKind.Pad, Key.None, KeyModifiers.None, MouseButton.None, 0, default, input);

    /// <summary>The text form stored in <c>keymap.json</c>, e.g. <c>Ctrl+WheelUp</c>.</summary>
    public override string ToString()
    {
        if (!IsDefined)
        {
            return string.Empty;
        }

        if (Kind == InputBindingKind.Pad)
        {
            return PadPrefix + Pad;
        }

        var text = new StringBuilder();
        AppendModifiers(text, Modifiers);
        text.Append(Kind switch
        {
            InputBindingKind.Key => Key.ToString(),
            InputBindingKind.MouseButton => ButtonName(Button, Clicks),
            _ => "Wheel" + Wheel,
        });
        return text.ToString();
    }

    /// <summary>Parses the text form; returns false (and <see langword="default"/>) for anything unrecognised. Names are case-insensitive.</summary>
    public static bool TryParse(string? text, out InputBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.StartsWith(PadPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse(text.AsSpan(PadPrefix.Length), ignoreCase: true, out GamepadInput pad) && pad != GamepadInput.None && Enum.IsDefined(pad))
            {
                binding = ForPad(pad);
                return true;
            }

            return false;
        }

        string[] parts = text.Split('+');
        var modifiers = KeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!TryParseModifier(parts[i].Trim(), out var modifier) || (modifiers & modifier) != 0)
            {
                return false;
            }

            modifiers |= modifier;
        }

        string name = parts[^1].Trim();
        if (name.Length == 0)
        {
            return false;
        }

        if (TryParseWheel(name, out var direction))
        {
            binding = ForWheel(direction, modifiers);
            return true;
        }

        if (TryParseMouseButton(name, out var button, out int clicks))
        {
            binding = ForMouseButton(button, modifiers, clicks);
            return true;
        }

        if (TryParseKey(name, out var key))
        {
            binding = ForKey(key, modifiers);
            return true;
        }

        return false;
    }

    /// <summary>Parses the text form, or throws <see cref="FormatException"/>.</summary>
    public static InputBinding Parse(string text) =>
        TryParse(text, out var binding) ? binding : throw new FormatException($"'{text}' is not a valid input binding.");

    /// <summary>Converts a legacy Avalonia <see cref="KeyGesture"/> (what the old KeyBinding table stored) to a keyboard binding.</summary>
    public static InputBinding FromGesture(KeyGesture gesture) => ForKey(gesture.Key, gesture.KeyModifiers);

    private const string PadPrefix = "Pad:";

    private static void AppendModifiers(StringBuilder text, KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            text.Append("Ctrl+");
        }

        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            text.Append("Shift+");
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            text.Append("Alt+");
        }

        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            text.Append("Meta+");
        }
    }

    private static string ButtonName(MouseButton button, int clicks)
    {
        string name = button switch
        {
            MouseButton.Left => "Left",
            MouseButton.Middle => "Middle",
            MouseButton.Right => "Right",
            MouseButton.XButton1 => "4",
            MouseButton.XButton2 => "5",
            _ => button.ToString(),
        };

        // "Mouse4"/"Mouse5" for the thumb buttons (CE's MouseButton4/5); "MouseLeft"/"MouseDoubleLeft" for the rest. A double-click thumb press is "MouseDouble4".
        return clicks >= 2 ? "MouseDouble" + name : "Mouse" + name;
    }

    private static bool TryParseModifier(string text, out KeyModifiers modifier)
    {
        switch (text.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                modifier = KeyModifiers.Control;
                return true;
            case "shift":
                modifier = KeyModifiers.Shift;
                return true;
            case "alt":
                modifier = KeyModifiers.Alt;
                return true;
            case "meta":
            case "win":
            case "windows":
            case "cmd":
            case "command":
                modifier = KeyModifiers.Meta;
                return true;
            default:
                modifier = KeyModifiers.None;
                return false;
        }
    }

    private static bool TryParseWheel(string name, out WheelDirection direction)
    {
        direction = default;
        return name.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(name.AsSpan(5), ignoreCase: true, out direction)
            && Enum.IsDefined(direction);
    }

    private static bool TryParseMouseButton(string name, out MouseButton button, out int clicks)
    {
        button = MouseButton.None;
        clicks = 1;
        if (!name.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string rest = name[5..];
        if (rest.StartsWith("Double", StringComparison.OrdinalIgnoreCase))
        {
            clicks = 2;
            rest = rest[6..];
        }

        switch (rest.ToLowerInvariant())
        {
            case "left":
                button = MouseButton.Left;
                return true;
            case "middle":
                button = MouseButton.Middle;
                return true;
            case "right":
                button = MouseButton.Right;
                return true;
            case "4":
            case "xbutton1":
                button = MouseButton.XButton1;
                return true;
            case "5":
            case "xbutton2":
                button = MouseButton.XButton2;
                return true;
            default:
                return false;
        }
    }

    private static readonly Dictionary<string, Key> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = Key.Escape,
        ["Return"] = Key.Enter,
        ["Del"] = Key.Delete,
        ["Ins"] = Key.Insert,
        ["PgUp"] = Key.PageUp,
        ["PgDn"] = Key.PageDown,
        ["Backspace"] = Key.Back,
        ["Spacebar"] = Key.Space,
    };

    private static bool TryParseKey(string name, [NotNullWhen(true)] out Key key)
    {
        key = Key.None;
        if (KeyAliases.TryGetValue(name, out key))
        {
            return true;
        }

        // Reject numeric spellings ("65"): Enum.TryParse would accept any integer, and a binding named by a raw key code is never what a user typed.
        if (char.IsDigit(name[0]) || name[0] == '-')
        {
            return false;
        }

        return Enum.TryParse(name, ignoreCase: true, out key) && key != Key.None && Enum.IsDefined(key);
    }
}
