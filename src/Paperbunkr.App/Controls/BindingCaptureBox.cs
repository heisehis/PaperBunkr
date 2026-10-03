using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Controls;

/// <summary>
/// The "press your combination" box in Preferences &gt; Keyboard Shortcuts: once shown it takes focus and reports the next key (with its modifiers), middle or thumb mouse button
/// press, or wheel turn as an <see cref="InputBinding"/> through <see cref="CaptureCommand"/>. Escape cancels (through <see cref="CancelCommand"/>), as does clicking away. It
/// declares itself an <see cref="InputSuppression.All"/> suppressor, so the input service lets every key, button and wheel turn through to it instead of acting on them: pressing
/// Escape or Mouse 4 here does not close the screen or navigate back. Left and right clicks are ignored (a click is how the box gets focus), and a bare modifier key waits for the key
/// it modifies. ComicRack's own shortcut editor offers the same capture dialog; this replaces the curated key list the previous editor used, which could not express a mouse binding.
/// </summary>
public sealed class BindingCaptureBox : Border, IInputSuppressor
{
    public static readonly StyledProperty<ICommand?> CaptureCommandProperty =
        AvaloniaProperty.Register<BindingCaptureBox, ICommand?>(nameof(CaptureCommand));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<BindingCaptureBox, ICommand?>(nameof(CancelCommand));

    static BindingCaptureBox()
    {
        FocusableProperty.OverrideDefaultValue<BindingCaptureBox>(true);

        // A Border with no background is only hit where its child draws, which would make most of the box deaf to the wheel and the mouse buttons it is waiting for.
        // A default (not a local value), so the theme's style can still colour it.
        BackgroundProperty.OverrideDefaultValue<BindingCaptureBox>(Avalonia.Media.Brushes.Transparent);
    }

    public BindingCaptureBox()
    {
        Child = new TextBlock
        {
            Text = "Press a key, a mouse button, or turn the wheel…  (Esc cancels)",
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        // Clicking away abandons the capture. The event rather than an override: the override's parameter type differs between Avalonia versions.
        LostFocus += (_, _) => Cancel();
    }

    /// <summary>Executed with the captured <see cref="InputBinding"/> as its parameter.</summary>
    public ICommand? CaptureCommand
    {
        get => GetValue(CaptureCommandProperty);
        set => SetValue(CaptureCommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public InputSuppression Suppression => InputSuppression.All;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            // Deferred: the box becomes visible in the same layout pass that makes it focusable.
            Dispatcher.UIThread.Post(() => Focus());
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        e.Handled = true;
        if (IsModifierKey(e.Key))
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            Cancel();
            return;
        }

        Capture(InputBinding.ForKey(e.Key, e.KeyModifiers));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var button = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.MiddleButtonPressed => MouseButton.Middle,
            PointerUpdateKind.XButton1Pressed => MouseButton.XButton1,
            PointerUpdateKind.XButton2Pressed => MouseButton.XButton2,
            _ => MouseButton.None,
        };

        if (button == MouseButton.None)
        {
            return;
        }

        e.Handled = true;

        // Always a single click: Avalonia's click count is not per button (a thumb-button press followed quickly by a middle press counts as a double), so trusting it here would
        // record a double-click binding nobody meant. A double-click binding can still be set by importing a layout or editing keymap.json.
        Capture(InputBinding.ForMouseButton(button, e.KeyModifiers, clicks: 1));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.X == 0 && e.Delta.Y == 0)
        {
            return;
        }

        var direction = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)
            ? (e.Delta.X < 0 ? WheelDirection.Left : WheelDirection.Right)
            : (e.Delta.Y > 0 ? WheelDirection.Up : WheelDirection.Down);
        e.Handled = true;
        Capture(InputBinding.ForWheel(direction, e.KeyModifiers));
    }

    private void Capture(InputBinding binding)
    {
        if (CaptureCommand is { } command && command.CanExecute(binding))
        {
            command.Execute(binding);
        }
    }

    private void Cancel()
    {
        if (CancelCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
}
