using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Services.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// The canvas's side of the input service (docs/superpowers/specs/2026-10-03-input-service-design.md §6). Nothing here knows a physical key, wheel direction or controller
/// button: the service resolves those to actions through the user's keymap and offers each to this control, which owns the state the view-state actions act on (zoom, pan,
/// scroll position, page turns). Replaces the canvas's own <c>OnKeyDown</c>, its per-command gesture properties and its thumb-button and Ctrl+wheel special cases.
/// </summary>
public partial class PageCanvas
{
    /// <summary>The input service to register with; when unset, the application's (<see cref="InputServiceLocator.Current"/>), which the PDF reader's canvas relies on.</summary>
    public static readonly StyledProperty<IInputService?> InputServiceProperty =
        AvaloniaProperty.Register<PageCanvas, IInputService?>(nameof(InputService));

    /// <summary>
    /// Whether reading-order page turns (next/previous page, first/last page) apply here. Off by default so the Novels PDF reader, which shares this control and binds none of
    /// the comic reader's commands, keeps ignoring PageDown, Space, the media keys and the controller's A/B.
    /// </summary>
    public static readonly StyledProperty<bool> ReadingOrderTurnsEnabledProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(ReadingOrderTurnsEnabled));

    private AttachedInputRegistration? _inputRegistration;

    public IInputService? InputService
    {
        get => GetValue(InputServiceProperty);
        set => SetValue(InputServiceProperty, value);
    }

    public bool ReadingOrderTurnsEnabled
    {
        get => GetValue(ReadingOrderTurnsEnabledProperty);
        set => SetValue(ReadingOrderTurnsEnabledProperty, value);
    }

    /// <summary>
    /// Reports that an overlay which takes the keyboard and the controller for itself (the command palette, the bad-page reason picker) is open, in which case this canvas stands
    /// down. Read each time input is resolved, so it needs no change notification; set by the hosting screen.
    /// </summary>
    public Func<bool>? IsInputSuspended { get; set; }

    /// <summary>Wires the property-changed handler for <see cref="InputService"/> once per type; called from the static constructor.</summary>
    private static void RegisterInputProperties() =>
        InputServiceProperty.Changed.AddClassHandler<PageCanvas>((canvas, _) => canvas.RefreshInputService());

    private void InitializeInput() =>
        _inputRegistration = new AttachedInputRegistration(
            this, InputScope.Reader, OnInputAction, GetInputContext, InputService ?? InputServiceLocator.Current, () => this.FindAncestorOfType<UserControl>() ?? (Visual)this);

    private void RefreshInputService()
    {
        if (_inputRegistration is { } registration)
        {
            registration.Service = InputService ?? InputServiceLocator.Current;
        }
    }

    /// <summary>The reader state the service filters actions by: arrows mean "turn the page" unzoomed, "pan" zoomed in and "scroll" in continuous mode.</summary>
    private InputContext GetInputContext()
    {
        if (IsInputSuspended?.Invoke() == true)
        {
            return InputContext.None;
        }

        if (IsContinuous)
        {
            return InputContext.Continuous;
        }

        // CanPan reads the decoded page's size; with no page loaded there is nothing to pan.
        return Page is not null && CanPan() ? InputContext.PagedZoomed : InputContext.PagedUnzoomed;
    }

    private void OnInputAction(InputActionEventArgs e)
    {
        if (e.Device == InputDevice.Gamepad && e.Action.Id is not (InputActionIds.PanHorizontal or InputActionIds.PanVertical or InputActionIds.ZoomAxis))
        {
            Services.Reader.ReaderPerfStats.Current.RecordInput("gamepad button");
        }

        switch (e.Action.Id)
        {
            // ----- Always-context commands: each simply runs the command the screen bound. -----
            case InputActionIds.ToggleFullscreen:
                e.Handled = TryExecute(FullscreenToggleCommand);
                break;
            case InputActionIds.RotateClockwise:
                e.Handled = TryExecute(RotateClockwiseCommand);
                break;
            case InputActionIds.RotateCounterClockwise:
                e.Handled = TryExecute(RotateCounterClockwiseCommand);
                break;
            case InputActionIds.PreviousBookmark:
                e.Handled = TryExecute(PreviousBookmarkCommand);
                break;
            case InputActionIds.NextBookmark:
                e.Handled = TryExecute(NextBookmarkCommand);
                break;
            case InputActionIds.JumpBack:
                e.Handled = TryExecute(JumpBackCommand);
                break;
            case InputActionIds.ReportBadPage:
                e.Handled = TryExecute(ReportBadPageCommand);
                break;
            case InputActionIds.CommandPalette:
                e.Handled = TryExecute(CommandPaletteCommand);
                break;
            case InputActionIds.GoToPage:
                e.Handled = TryExecute(GoToPageCommand);
                break;
            case InputActionIds.ToggleSessionHud:
                e.Handled = TryExecute(ToggleSessionHudCommand);
                break;
            case InputActionIds.ToggleWarmShift:
                e.Handled = TryExecute(ToggleWarmShiftCommand);
                break;
            case InputActionIds.CopyPage:
                e.Handled = TryExecute(CopyPageCommand);
                break;
            case InputActionIds.NextProfile:
                e.Handled = TryExecute(NextProfileCommand);
                break;
            case InputActionIds.ToggleGuidedView:
                e.Handled = TryExecute(ToggleGuidedViewCommand);
                break;
            case InputActionIds.ToggleAutoScroll:
                e.Handled = TryExecute(ToggleAutoScrollCommand);
                break;

            // ----- Zoom and fit -----
            case InputActionIds.ZoomIn:
            case InputActionIds.ZoomOut:
                e.Handled = HandleZoomAction(e);
                break;
            case InputActionIds.FitOriginal:
                e.Handled = TryExecuteFit(ImageFitMode.Original);
                break;
            case InputActionIds.FitAll:
                e.Handled = TryExecuteFit(ImageFitMode.Fit);
                break;
            case InputActionIds.FitWidth:
                e.Handled = TryExecuteFit(ImageFitMode.FitWidth);
                break;
            case InputActionIds.FitHeight:
                e.Handled = TryExecuteFit(ImageFitMode.FitHeight);
                break;
            case InputActionIds.FitBest:
                e.Handled = TryExecuteFit(ImageFitMode.BestFit);
                break;

            // ----- Reading-order turns (paged), and a screen's worth of scrolling (continuous). Both are what the mouse's side buttons and the controller's A/B do. -----
            case InputActionIds.NextPage:
                e.Handled = HandleReadingOrderTurn(e, forward: true);
                break;
            case InputActionIds.PreviousPage:
                e.Handled = HandleReadingOrderTurn(e, forward: false);
                break;
            case InputActionIds.ScrollPageDown:
                e.Handled = HandleScrollPage(e, forward: true);
                break;
            case InputActionIds.ScrollPageUp:
                e.Handled = HandleScrollPage(e, forward: false);
                break;

            // ----- Spatial page turns (unzoomed paged reading) -----
            case InputActionIds.PageTurnLeft:
                e.Handled = !PadIgnoresHorizontalTurn(e) && ExecuteTurn(forward: false);
                break;
            case InputActionIds.PageTurnRight:
                e.Handled = !PadIgnoresHorizontalTurn(e) && ExecuteTurn(forward: true);
                break;

            // Vertical paged reading (docs/superpowers/specs/2026-08-27-vertical-paged-reading-mode-design.md §3): the up and down arrows turn the page; otherwise they stay free.
            case InputActionIds.PageTurnUp:
                e.Handled = IsPagedVertical && ExecuteTurn(forward: false);
                break;
            case InputActionIds.PageTurnDown:
                e.Handled = IsPagedVertical && ExecuteTurn(forward: true);
                break;

            // ----- Zoomed paged reading: the arrows pan, and at the edge of the page they turn it -----
            case InputActionIds.PanLeft:
                e.Handled = HandleZoomedArrow(dx: -1, dy: 0);
                break;
            case InputActionIds.PanRight:
                e.Handled = HandleZoomedArrow(dx: 1, dy: 0);
                break;
            case InputActionIds.PanUp:
                e.Handled = HandleZoomedArrow(dx: 0, dy: -1);
                break;
            case InputActionIds.PanDown:
                e.Handled = HandleZoomedArrow(dx: 0, dy: 1);
                break;

            // ----- Continuous reading -----
            case InputActionIds.ScrollLeft:
                e.Handled = HandleContinuousArrow(e, dx: -1, dy: 0);
                break;
            case InputActionIds.ScrollRight:
                e.Handled = HandleContinuousArrow(e, dx: 1, dy: 0);
                break;
            case InputActionIds.ScrollUp:
                e.Handled = HandleContinuousArrow(e, dx: 0, dy: -1);
                break;
            case InputActionIds.ScrollDown:
                e.Handled = HandleContinuousArrow(e, dx: 0, dy: 1);
                break;
            case InputActionIds.ScrollToStart:
                ScrollOffset = 0;
                e.Handled = true;
                break;
            case InputActionIds.ScrollToEnd:
                ScrollOffset = ClampScrollOffset(double.MaxValue);
                e.Handled = true;
                break;

            // ----- The controller's analogue sticks and triggers: continuous values scaled by the frame time -----
            case InputActionIds.PanHorizontal:
                GamepadAnalog(e.Value, 0, e.Elapsed);
                e.Handled = true;
                break;
            case InputActionIds.PanVertical:
                GamepadAnalog(0, e.Value, e.Elapsed);
                e.Handled = true;
                break;
            case InputActionIds.ZoomAxis:
                GamepadZoom(e.Value, e.Elapsed);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Zoom in or out. A wheel turn carries its amount and the cursor position, so it glides a cursor-anchored zoom (docs/superpowers/specs/2026-09-12-continuous-mode-cursor-
    /// anchored-zoom-design.md section 4): the wheel moves a zoom goal and PageCanvas.Smooth.cs eases the view to it, solving the same anchor each frame so the content under the
    /// cursor stays under it. Anything else (the Z key, a toolbar button, a mouse button) takes one step through the bound command.
    /// </summary>
    private bool HandleZoomAction(InputActionEventArgs e)
    {
        if (e.Device == InputDevice.Mouse && e.WheelDelta.Y != 0)
        {
            SmoothZoomBy(ZoomPanMath.WheelZoomFactor(e.WheelDelta.Y, WheelZoomStep), e.GetPosition(this));
            return true;
        }

        return TryExecute(e.Action.Id == InputActionIds.ZoomIn ? ZoomInCommand : ZoomOutCommand);
    }

    private bool TryExecuteFit(ImageFitMode mode)
    {
        if (SetFitModeCommand?.CanExecute(mode) != true)
        {
            return false;
        }

        SetFitModeCommand.Execute(mode);
        return true;
    }

    /// <summary>
    /// Next or previous page in reading order, whichever way the book reads (design 2026-09-25 F1 section 2). Handled only where this canvas is a comic reader, and, for the mouse's
    /// side buttons, only while <see cref="ExtraMouseButtonsTurnPages"/> is on (otherwise the press falls through to back/forward navigation).
    /// </summary>
    private bool HandleReadingOrderTurn(InputActionEventArgs e, bool forward)
    {
        if (!ReadingOrderTurnsEnabled || !MouseButtonAllowed(e))
        {
            return false;
        }

        NoteDeviceInput(e);
        return ExecuteReadingOrderTurn(forward);
    }

    /// <summary>A screen's worth of scrolling in continuous mode (PageDown, the controller's A/B, the mouse's side buttons).</summary>
    private bool HandleScrollPage(InputActionEventArgs e, bool forward)
    {
        if (!MouseButtonAllowed(e))
        {
            return false;
        }

        NoteDeviceInput(e);
        double screen = (ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? Bounds.Height : Bounds.Width) * PageJumpFraction;
        ScrollOffset = ClampScrollOffset(ScrollOffset + (forward ? screen : -screen));
        return true;
    }

    /// <summary>The mouse side buttons act only while <see cref="ExtraMouseButtonsTurnPages"/> is on; every other device is unaffected.</summary>
    private bool MouseButtonAllowed(InputActionEventArgs e) => e.Device != InputDevice.Mouse || ExtraMouseButtonsTurnPages;

    /// <summary>The controller's D-pad and left stick used to ignore the horizontal axis while pages turn vertically; the keys never did. Kept so a vertical book is not turned by an accidental sideways flick.</summary>
    private bool PadIgnoresHorizontalTurn(InputActionEventArgs e) => e.Device == InputDevice.Gamepad && IsPagedVertical;

    /// <summary>Records media keys and the thumb buttons (which the OS or a driver may route elsewhere) for the perf overlay's "last input" line, so a device that never arrives is diagnosable.</summary>
    private static void NoteDeviceInput(InputActionEventArgs e)
    {
        var binding = e.Binding;
        if (binding.Kind == InputBindingKind.Key && binding.Key is Key.MediaNextTrack or Key.MediaPreviousTrack or Key.MediaPlayPause or Key.MediaStop)
        {
            Services.Reader.ReaderPerfStats.Current.RecordInput($"media key {binding.Key}");
        }
        else if (binding.Kind == InputBindingKind.MouseButton && binding.Button is MouseButton.XButton1 or MouseButton.XButton2)
        {
            Services.Reader.ReaderPerfStats.Current.RecordInput(binding.Button == MouseButton.XButton2 ? "mouse side button 2 (forward)" : "mouse side button 1 (back)");
        }
    }

    /// <summary>
    /// An arrow on a zoomed page (<paramref name="dx"/>, <paramref name="dy"/> each -1, 0 or 1; right and down positive): pans the page and, at its edge, turns it. In guided view
    /// (the canvas has framed a panel) up and down step in reading order and left and right turn, as they always have.
    /// </summary>
    private bool HandleZoomedArrow(int dx, int dy)
    {
        if (Page is null)
        {
            return false;
        }

        if (!GuidedSteps)
        {
            PanOrTurnFromKey(-dx * KeyPanStep, -dy * KeyPanStep);
            return true;
        }

        if (dx == 0 && ExecuteReadingOrderTurn(forward: dy > 0))
        {
            return true;
        }

        if (dx == 0)
        {
            return IsPagedVertical && ExecuteTurn(forward: dy > 0);
        }

        return ExecuteTurn(forward: dx > 0);
    }

    /// <summary>
    /// An arrow in continuous mode. A key scrolls only along the reading axis (Down/Up for a vertical strip, Right/Left for a horizontal one); the controller's D-pad also nudges the
    /// cross axis, as its pan always has.
    /// </summary>
    private bool HandleContinuousArrow(InputActionEventArgs e, int dx, int dy)
    {
        if (e.Device == InputDevice.Gamepad)
        {
            ApplyContinuousMove(dx, dy, WheelScrollStepPixels, KeyPanStep);
            return true;
        }

        bool vertical = ContinuousAxis == ReaderLayoutModel.Axis.Vertical;
        int along = vertical ? dy : dx;
        if (along == 0)
        {
            return false;
        }

        ScrollOffset = ClampScrollOffset(ScrollOffset + (along * WheelScrollStepPixels));
        return true;
    }
}
