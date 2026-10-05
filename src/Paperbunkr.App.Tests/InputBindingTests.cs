using Avalonia.Input;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>The unified gesture value (docs/superpowers/specs/2026-10-03-input-service-design.md §5.3): text form, parsing, equality.</summary>
public class InputBindingTests
{
    public static TheoryData<InputBinding, string> RoundTrips => new()
    {
        { InputBinding.ForKey(Key.PageDown), "PageDown" },
        { InputBinding.ForKey(Key.K, KeyModifiers.Control), "Ctrl+K" },
        { InputBinding.ForKey(Key.C, KeyModifiers.Control | KeyModifiers.Shift), "Ctrl+Shift+C" },
        { InputBinding.ForKey(Key.Left, KeyModifiers.Alt), "Alt+Left" },
        { InputBinding.ForKey(Key.D1), "D1" },
        { InputBinding.ForKey(Key.BrowserBack), "BrowserBack" },
        { InputBinding.ForKey(Key.OemComma, KeyModifiers.Control), "Ctrl+OemComma" },
        { InputBinding.ForKey(Key.F11), "F11" },
        { InputBinding.ForKey(Key.Q, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta), "Ctrl+Shift+Alt+Meta+Q" },
        { InputBinding.ForMouseButton(MouseButton.XButton1), "Mouse4" },
        { InputBinding.ForMouseButton(MouseButton.XButton2), "Mouse5" },
        { InputBinding.ForMouseButton(MouseButton.Left, clicks: 2), "MouseDoubleLeft" },
        { InputBinding.ForMouseButton(MouseButton.Middle), "MouseMiddle" },
        { InputBinding.ForMouseButton(MouseButton.Right, KeyModifiers.Control), "Ctrl+MouseRight" },
        { InputBinding.ForMouseButton(MouseButton.XButton1, clicks: 2), "MouseDouble4" },
        { InputBinding.ForWheel(WheelDirection.Up, KeyModifiers.Control), "Ctrl+WheelUp" },
        { InputBinding.ForWheel(WheelDirection.Down, KeyModifiers.Control), "Ctrl+WheelDown" },
        { InputBinding.ForWheel(WheelDirection.Left), "WheelLeft" },
        { InputBinding.ForWheel(WheelDirection.Right, KeyModifiers.Shift), "Shift+WheelRight" },
        { InputBinding.ForPad(GamepadInput.A), "Pad:A" },
        { InputBinding.ForPad(GamepadInput.RightStickX), "Pad:RightStickX" },
        { InputBinding.ForPad(GamepadInput.Triggers), "Pad:Triggers" },
        { InputBinding.ForPad(GamepadInput.LeftStickLeft), "Pad:LeftStickLeft" },
    };

    [Theory]
    [MemberData(nameof(RoundTrips))]
    public void ToString_ProducesTheTextForm_AndParsingReturnsTheSameBinding(InputBinding binding, string text)
    {
        Assert.Equal(text, binding.ToString());
        Assert.True(InputBinding.TryParse(text, out var parsed));
        Assert.Equal(binding, parsed);
    }

    [Theory]
    [InlineData("ctrl+k", "Ctrl+K")]
    [InlineData("CTRL + SHIFT + c", "Ctrl+Shift+C")]
    [InlineData("control+k", "Ctrl+K")]
    [InlineData("Esc", "Escape")]
    [InlineData("pgdn", "PageDown")]
    [InlineData("ctrl+wheelup", "Ctrl+WheelUp")]
    [InlineData("mouse4", "Mouse4")]
    [InlineData("MouseXButton1", "Mouse4")]
    [InlineData("pad:a", "Pad:A")]
    [InlineData("Cmd+K", "Meta+K")]
    public void Parse_IsCaseInsensitive_AndAcceptsAliases(string text, string canonical)
    {
        Assert.True(InputBinding.TryParse(text, out var binding));
        Assert.Equal(canonical, binding.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+K")]
    [InlineData("Banana")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Fizz+K")]
    [InlineData("65")]
    [InlineData("Pad:")]
    [InlineData("Pad:Nope")]
    [InlineData("Pad:None")]
    [InlineData("Ctrl+Pad:A")]
    [InlineData("WheelSideways")]
    [InlineData("Mouse9")]
    [InlineData("None")]
    public void Parse_RejectsGarbage(string text)
    {
        Assert.False(InputBinding.TryParse(text, out var binding));
        Assert.False(binding.IsDefined);
        Assert.Throws<FormatException>(() => InputBinding.Parse(text));
    }

    [Fact]
    public void TryParse_Null_IsFalse() => Assert.False(InputBinding.TryParse(null, out _));

    [Fact]
    public void Default_NamesNoInput()
    {
        InputBinding none = default;

        Assert.False(none.IsDefined);
        Assert.Equal(string.Empty, none.ToString());
    }

    [Fact]
    public void Equality_IsByValue_AcrossKinds()
    {
        Assert.Equal(InputBinding.ForKey(Key.A, KeyModifiers.Control), InputBinding.ForKey(Key.A, KeyModifiers.Control));
        Assert.NotEqual(InputBinding.ForKey(Key.A), InputBinding.ForKey(Key.A, KeyModifiers.Control));

        // Key.Left (the arrow) and the mouse Left button are different inputs, though both are called "Left".
        Assert.NotEqual(InputBinding.ForKey(Key.Left), InputBinding.ForMouseButton(MouseButton.Left));
        Assert.NotEqual(InputBinding.ForMouseButton(MouseButton.Left), InputBinding.ForMouseButton(MouseButton.Left, clicks: 2));
        Assert.NotEqual(InputBinding.ForWheel(WheelDirection.Left), InputBinding.ForKey(Key.Left));
    }

    [Fact]
    public void Clicks_AreNormalisedToOneOrTwo()
    {
        Assert.Equal(InputBinding.ForMouseButton(MouseButton.Left, clicks: 2), InputBinding.ForMouseButton(MouseButton.Left, clicks: 3));
        Assert.Equal(InputBinding.ForMouseButton(MouseButton.Left, clicks: 1), InputBinding.ForMouseButton(MouseButton.Left, clicks: 0));
    }

    [Fact]
    public void Device_FollowsTheKind()
    {
        Assert.Equal(InputDevice.Keyboard, InputBinding.ForKey(Key.A).Device);
        Assert.Equal(InputDevice.Mouse, InputBinding.ForMouseButton(MouseButton.XButton1).Device);
        Assert.Equal(InputDevice.Mouse, InputBinding.ForWheel(WheelDirection.Up).Device);
        Assert.Equal(InputDevice.Gamepad, InputBinding.ForPad(GamepadInput.A).Device);
    }

    [Fact]
    public void FromGesture_ConvertsALegacyKeyGesture()
    {
        var binding = InputBinding.FromGesture(KeyGesture.Parse("Ctrl+Shift+C"));

        Assert.Equal(InputBinding.ForKey(Key.C, KeyModifiers.Control | KeyModifiers.Shift), binding);
    }

    [Fact]
    public void EveryDefaultBinding_SurvivesATextRoundTrip()
    {
        foreach (var info in InputActions.Core)
        {
            foreach (var binding in info.Defaults)
            {
                Assert.True(InputBinding.TryParse(binding.ToString(), out var parsed), $"{info.Id}: '{binding}' did not parse");
                Assert.Equal(binding, parsed);
            }
        }
    }
}
