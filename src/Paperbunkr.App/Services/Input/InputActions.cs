using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// The central table of built-in actions and their default bindings (docs/superpowers/specs/2026-10-03-input-service-design.md §5.1, §5.4): the one place a
/// default key, mouse or gamepad binding is defined. Replaces <c>KeyboardCommandRegistry</c>; every action that existed there keeps its id and its defaults
/// exactly (including the clicker/media/Space extras on next/previous page), and the extras that used to live in hardcoded handlers (F11, Ctrl+wheel zoom,
/// the thumb buttons, the whole gamepad layout, the shell shortcuts) are listed here too.
/// </summary>
public static class InputActions
{
    public const string NavigationGroup = "Navigation";
    public const string ZoomFitGroup = "Zoom & Fit";
    public const string DisplayGroup = "Display";
    public const string GeneralGroup = "General";
    public const string LibraryGroup = "Library";
    public const string GamepadGroup = "Gamepad";
    public const string BookReaderGroup = "Book reader";
    public const string CompareGroup = "Compare";

    /// <summary>Every built-in action, in the order Preferences lists them within a group.</summary>
    public static IReadOnlyList<InputActionInfo> Core { get; } = Build();

    private static InputBinding K(Key key, KeyModifiers modifiers = KeyModifiers.None) => InputBinding.ForKey(key, modifiers);

    private static InputBinding Wheel(WheelDirection direction, KeyModifiers modifiers = KeyModifiers.None) => InputBinding.ForWheel(direction, modifiers);

    private static InputBinding Mouse(MouseButton button) => InputBinding.ForMouseButton(button);

    private static InputBinding Pad(GamepadInput input) => InputBinding.ForPad(input);

    private static InputActionInfo Reader(string id, string group, string label, InputContext context, params InputBinding[] defaults) =>
        new(id, group, label, InputScope.Reader, context, defaults);

    private static InputActionInfo App(string id, string group, string label, InputScope scope, bool firesInTextInput, params InputBinding[] defaults) =>
        new(id, group, label, scope, InputContext.Always, defaults, FiresInTextInput: firesInTextInput);

    private static IReadOnlyList<InputActionInfo> Build()
    {
        const KeyModifiers Ctrl = KeyModifiers.Control;
        const KeyModifiers Shift = KeyModifiers.Shift;
        const KeyModifiers Alt = KeyModifiers.Alt;
        const InputContext Always = InputContext.Always;
        const InputContext Paged = InputContext.Paged;
        const InputContext Unzoomed = InputContext.PagedUnzoomed;
        const InputContext Zoomed = InputContext.PagedZoomed;
        const InputContext Continuous = InputContext.Continuous;

        return new List<InputActionInfo>
        {
            // ----- Reader: navigation. Spatial turns (arrows) are separate from reading-order ones (PageDown, Space, clickers) because a right-to-left book flips the former. -----
            Reader(InputActionIds.PageTurnLeft, NavigationGroup, "Page back (spatial left)", Unzoomed, K(Key.Left), Pad(GamepadInput.DPadLeft), Pad(GamepadInput.LeftStickLeft)),
            Reader(InputActionIds.PageTurnRight, NavigationGroup, "Page forward (spatial right)", Unzoomed, K(Key.Right), Pad(GamepadInput.DPadRight), Pad(GamepadInput.LeftStickRight)),
            Reader(InputActionIds.PageTurnUp, NavigationGroup, "Page back (vertical paging)", Unzoomed, K(Key.Up), Pad(GamepadInput.DPadUp), Pad(GamepadInput.LeftStickUp)),
            Reader(InputActionIds.PageTurnDown, NavigationGroup, "Page forward (vertical paging)", Unzoomed, K(Key.Down), Pad(GamepadInput.DPadDown), Pad(GamepadInput.LeftStickDown)),
            Reader(
                InputActionIds.NextPage, NavigationGroup, "Next page (reading order)", Paged,
                K(Key.PageDown), K(Key.Space), K(Key.MediaNextTrack), Pad(GamepadInput.A), Pad(GamepadInput.RightShoulder), Mouse(MouseButton.XButton2)),
            Reader(
                InputActionIds.PreviousPage, NavigationGroup, "Previous page (reading order)", Paged,
                K(Key.PageUp), K(Key.Space, Shift), K(Key.MediaPreviousTrack), Pad(GamepadInput.B), Pad(GamepadInput.LeftShoulder), Mouse(MouseButton.XButton1)),
            Reader(InputActionIds.FirstPage, NavigationGroup, "First page", Paged, K(Key.Home)),
            Reader(InputActionIds.LastPage, NavigationGroup, "Last page", Paged, K(Key.End)),
            Reader(InputActionIds.GoToPage, NavigationGroup, "Go to page…", Always, K(Key.G, Ctrl)),

            Reader(InputActionIds.PanLeft, NavigationGroup, "Pan left (zoomed in)", Zoomed, K(Key.Left), Pad(GamepadInput.DPadLeft), Pad(GamepadInput.LeftStickLeft)),
            Reader(InputActionIds.PanRight, NavigationGroup, "Pan right (zoomed in)", Zoomed, K(Key.Right), Pad(GamepadInput.DPadRight), Pad(GamepadInput.LeftStickRight)),
            Reader(InputActionIds.PanUp, NavigationGroup, "Pan up (zoomed in)", Zoomed, K(Key.Up), Pad(GamepadInput.DPadUp), Pad(GamepadInput.LeftStickUp)),
            Reader(InputActionIds.PanDown, NavigationGroup, "Pan down (zoomed in)", Zoomed, K(Key.Down), Pad(GamepadInput.DPadDown), Pad(GamepadInput.LeftStickDown)),

            Reader(InputActionIds.ScrollLeft, NavigationGroup, "Scroll left (continuous)", Continuous, K(Key.Left), Pad(GamepadInput.DPadLeft), Pad(GamepadInput.LeftStickLeft)),
            Reader(InputActionIds.ScrollRight, NavigationGroup, "Scroll right (continuous)", Continuous, K(Key.Right), Pad(GamepadInput.DPadRight), Pad(GamepadInput.LeftStickRight)),
            Reader(InputActionIds.ScrollUp, NavigationGroup, "Scroll up (continuous)", Continuous, K(Key.Up), Pad(GamepadInput.DPadUp), Pad(GamepadInput.LeftStickUp)),
            Reader(InputActionIds.ScrollDown, NavigationGroup, "Scroll down (continuous)", Continuous, K(Key.Down), Pad(GamepadInput.DPadDown), Pad(GamepadInput.LeftStickDown)),
            Reader(
                InputActionIds.ScrollPageUp, NavigationGroup, "Scroll up a page (continuous)", Continuous,
                K(Key.PageUp), Pad(GamepadInput.B), Pad(GamepadInput.LeftShoulder), Mouse(MouseButton.XButton1)),
            Reader(
                InputActionIds.ScrollPageDown, NavigationGroup, "Scroll down a page (continuous)", Continuous,
                K(Key.PageDown), Pad(GamepadInput.A), Pad(GamepadInput.RightShoulder), Mouse(MouseButton.XButton2)),
            Reader(InputActionIds.ScrollToStart, NavigationGroup, "Scroll to start (continuous)", Continuous, K(Key.Home)),
            Reader(InputActionIds.ScrollToEnd, NavigationGroup, "Scroll to end (continuous)", Continuous, K(Key.End)),
            Reader(InputActionIds.ToggleAutoScroll, NavigationGroup, "Toggle auto-scroll (continuous)", Continuous, K(Key.S)),

            // Mirrors CE's defaults (MainForm.cs InitializeKeyboard: MoveToPrevBookmark/MoveToNextBookmark); CE has no default for *setting* a bookmark.
            Reader(InputActionIds.PreviousBookmark, NavigationGroup, "Previous bookmark", Always, K(Key.PageUp, Ctrl)),
            Reader(InputActionIds.NextBookmark, NavigationGroup, "Next bookmark", Always, K(Key.PageDown, Ctrl)),
            Reader(InputActionIds.JumpBack, NavigationGroup, "Back to previous position (after a jump)", Always, K(Key.Left, Alt)),
            Reader(InputActionIds.LeaveReader, NavigationGroup, "Close the reader", Always, Pad(GamepadInput.Back)),
            Reader(InputActionIds.ReportBadPage, NavigationGroup, "Report a bad page", Always, K(Key.X)),
            Reader(InputActionIds.CommandPalette, NavigationGroup, "Reader command palette", Always, K(Key.K, Ctrl), Pad(GamepadInput.Start)),

            // ----- Reader: view -----
            Reader(InputActionIds.ZoomIn, ZoomFitGroup, "Zoom in", Always, K(Key.Z), Wheel(WheelDirection.Up, Ctrl)),
            Reader(InputActionIds.ZoomOut, ZoomFitGroup, "Zoom out", Always, K(Key.Z, Shift), Wheel(WheelDirection.Down, Ctrl)),
            Reader(InputActionIds.ResetZoom, ZoomFitGroup, "Reset zoom", Always),
            Reader(InputActionIds.FitOriginal, ZoomFitGroup, "Fit: Original size", Always, K(Key.D1)),
            Reader(InputActionIds.FitAll, ZoomFitGroup, "Fit: Fit all", Always, K(Key.D2)),
            Reader(InputActionIds.FitWidth, ZoomFitGroup, "Fit: Fit width", Always, K(Key.D3)),
            Reader(InputActionIds.FitHeight, ZoomFitGroup, "Fit: Fit height", Always, K(Key.D4)),
            Reader(InputActionIds.FitBest, ZoomFitGroup, "Fit: Best fit", Always, K(Key.D5)),
            Reader(InputActionIds.RotateClockwise, DisplayGroup, "Rotate clockwise", Always, K(Key.R)),
            Reader(InputActionIds.RotateCounterClockwise, DisplayGroup, "Rotate counter-clockwise", Always, K(Key.R, Shift)),
            Reader(InputActionIds.ToggleDoublePageMode, DisplayGroup, "Toggle double-page mode", Always),
            Reader(InputActionIds.ToggleReadingDirection, DisplayGroup, "Toggle reading direction (LTR/RTL)", Always),
            Reader(InputActionIds.ToggleGuidedView, DisplayGroup, "Toggle guided panel view", Paged, K(Key.G)),

            // ----- Reader: tools -----
            Reader(InputActionIds.ToggleInfoPanel, DisplayGroup, "Toggle info panel", Always, K(Key.I)),
            Reader(InputActionIds.PinPage, DisplayGroup, "Pin this page as a reference", Always, K(Key.P, Shift)),
            Reader(InputActionIds.ClipRegion, DisplayGroup, "Clip a region of this page", Paged, K(Key.C, Ctrl | Shift)),
            Reader(InputActionIds.CopyPage, DisplayGroup, "Copy page (or spread) to the clipboard", Always, K(Key.C, Ctrl)),
            Reader(InputActionIds.NextProfile, DisplayGroup, "Next reader profile", Always, K(Key.P)),
            Reader(InputActionIds.ToggleSessionHud, DisplayGroup, "Toggle reading stats", Always, K(Key.H)),
            Reader(InputActionIds.ToggleWarmShift, DisplayGroup, "Toggle warm tint", Always, K(Key.W)),
            Reader(InputActionIds.ToggleChrome, DisplayGroup, "Show or hide the toolbar", Always, Pad(GamepadInput.Y)),
            Reader(InputActionIds.TogglePerfOverlay, DisplayGroup, "Toggle the performance overlay", Always, K(Key.P, Ctrl | Shift)),

            // ----- Reader: fullscreen (F is the remappable default; F11 was a hardcoded OS-convention extra) -----
            Reader(InputActionIds.ToggleFullscreen, DisplayGroup, "Toggle fullscreen", Always, K(Key.F), K(Key.F11), Pad(GamepadInput.X)),

            // ----- Reader: analogue axes (gamepad). The right stick pans (down positive), the triggers zoom (right minus left). -----
            new(InputActionIds.PanHorizontal, GamepadGroup, "Pan / scroll horizontally (right stick)", InputScope.Reader, Always, [Pad(GamepadInput.RightStickX)], InputActionKind.Axis),
            new(InputActionIds.PanVertical, GamepadGroup, "Pan / scroll vertically (right stick)", InputScope.Reader, Always, [Pad(GamepadInput.RightStickY)], InputActionKind.Axis),
            new(InputActionIds.ZoomAxis, GamepadGroup, "Zoom (triggers)", InputScope.Reader, Always, [Pad(GamepadInput.Triggers)], InputActionKind.Axis),

            // ----- Global and layout. Escape, the browser-back key and quick open also work while a text box has focus, as the old shell handler allowed. -----
            App(InputActionIds.CloseCurrentView, GeneralGroup, "Close the current overlay or view", InputScope.Global, true, K(Key.Escape)),
            App(InputActionIds.NavigateBack, GeneralGroup, "Navigate back", InputScope.Global, true, K(Key.BrowserBack), Mouse(MouseButton.XButton1), Wheel(WheelDirection.Left)),
            App(InputActionIds.NavigateForward, GeneralGroup, "Navigate forward", InputScope.Global, false, K(Key.BrowserForward), Mouse(MouseButton.XButton2), Wheel(WheelDirection.Right)),
            App(InputActionIds.OpenQuickOpen, GeneralGroup, "Quick open", InputScope.Global, true, K(Key.P, Ctrl)),
            App(InputActionIds.OpenSettings, GeneralGroup, "Open Preferences", InputScope.Global, false, K(Key.OemComma, Ctrl)),
            App(InputActionIds.CycleScreenForward, GeneralGroup, "Next screen", InputScope.Global, false, K(Key.Tab, Ctrl)),
            App(InputActionIds.CycleScreenBackward, GeneralGroup, "Previous screen", InputScope.Global, false, K(Key.Tab, Ctrl | Shift)),
            App(InputActionIds.Undo, GeneralGroup, "Undo metadata edit", InputScope.Global, false, K(Key.Z, Ctrl)),
            App(InputActionIds.Redo, GeneralGroup, "Redo metadata edit", InputScope.Global, false, K(Key.Y, Ctrl)),
            App(InputActionIds.Quit, GeneralGroup, "Quit", InputScope.Global, false, K(Key.Q, Ctrl)),
            App(InputActionIds.FocusSearch, GeneralGroup, "Focus the search box", InputScope.Global, false, K(Key.F, Ctrl), K(Key.OemQuestion)),
            App(InputActionIds.ToggleSidebar, GeneralGroup, "Toggle the sidebar", InputScope.Global, false, K(Key.F6, Shift)),

            // ----- Book reader. Right/PageDown/Space and Left/PageUp turn pages (a real gap found by manual testing 2026-09-02: the reader had no keyboard paging at all). -----
            new(InputActionIds.BookNextPage, BookReaderGroup, "Next page", InputScope.BookReader, Always, [K(Key.Right), K(Key.PageDown), K(Key.Space)]),
            new(InputActionIds.BookPreviousPage, BookReaderGroup, "Previous page", InputScope.BookReader, Always, [K(Key.Left), K(Key.PageUp)]),
            new(InputActionIds.BookAnnouncePosition, BookReaderGroup, "Announce reading position", InputScope.BookReader, Always, [K(Key.W, Ctrl | Shift)]),

            // ----- Compare. Mirrors the keys the screen documented: Left/Right and PageUp/PageDown turn A's page, Space or F flips, M switches mode, 0 resets the view, 1 and 2 keep A or B. -----
            new(InputActionIds.CompareNextPage, CompareGroup, "Next page", InputScope.Compare, Always, [K(Key.Right), K(Key.PageDown)]),
            new(InputActionIds.ComparePreviousPage, CompareGroup, "Previous page", InputScope.Compare, Always, [K(Key.Left), K(Key.PageUp)]),
            new(InputActionIds.CompareFirstPage, CompareGroup, "First page", InputScope.Compare, Always, [K(Key.Home)]),
            new(InputActionIds.CompareFlip, CompareGroup, "Flip between the two", InputScope.Compare, Always, [K(Key.Space), K(Key.F)]),
            new(InputActionIds.CompareToggleMode, CompareGroup, "Switch comparison mode", InputScope.Compare, Always, [K(Key.M)]),
            new(InputActionIds.CompareResetView, CompareGroup, "Reset zoom and pan", InputScope.Compare, Always, [K(Key.D0)]),
            new(InputActionIds.CompareKeepA, CompareGroup, "Keep A", InputScope.Compare, Always, [K(Key.D1)]),
            new(InputActionIds.CompareKeepB, CompareGroup, "Keep B", InputScope.Compare, Always, [K(Key.D2)]),

            // ----- Library -----
            App(InputActionIds.RefreshLibrary, LibraryGroup, "Refresh", InputScope.Library, false, K(Key.F5)),
            App(InputActionIds.ToggleLibraryPreview, LibraryGroup, "Toggle the preview panel", InputScope.Library, false, K(Key.B, Ctrl)),

            // The selection actions: what each runs for the issues or series currently selected is LibraryActionCatalog's call, so the menu, the bar and these keys cannot drift apart.
            App(InputActionIds.LibraryEdit, LibraryGroup, "Edit properties (bulk edit for several)", InputScope.Library, false, K(Key.I, Ctrl)),
            App(InputActionIds.LibraryRate0, LibraryGroup, "Rating: none", InputScope.Library, false, K(Key.D0, Alt | Shift)),
            App(InputActionIds.LibraryRate1, LibraryGroup, "Rating: 1 star", InputScope.Library, false, K(Key.D1, Alt | Shift)),
            App(InputActionIds.LibraryRate2, LibraryGroup, "Rating: 2 stars", InputScope.Library, false, K(Key.D2, Alt | Shift)),
            App(InputActionIds.LibraryRate3, LibraryGroup, "Rating: 3 stars", InputScope.Library, false, K(Key.D3, Alt | Shift)),
            App(InputActionIds.LibraryRate4, LibraryGroup, "Rating: 4 stars", InputScope.Library, false, K(Key.D4, Alt | Shift)),
            App(InputActionIds.LibraryRate5, LibraryGroup, "Rating: 5 stars", InputScope.Library, false, K(Key.D5, Alt | Shift)),
            App(InputActionIds.LibraryMarkRead, LibraryGroup, "Mark as read", InputScope.Library, false, K(Key.R, Alt | Shift)),
            App(InputActionIds.LibraryMarkUnread, LibraryGroup, "Mark as unread", InputScope.Library, false, K(Key.U, Alt | Shift)),
            App(InputActionIds.LibraryReveal, LibraryGroup, "Show in Explorer", InputScope.Library, false, K(Key.G, Ctrl)),
            App(InputActionIds.LibraryCopyData, LibraryGroup, "Copy data", InputScope.Library, false, K(Key.C, Ctrl)),
            App(InputActionIds.LibraryPasteData, LibraryGroup, "Paste data", InputScope.Library, false, K(Key.V, Ctrl)),
            App(InputActionIds.LibraryCopyPaths, LibraryGroup, "Copy file paths", InputScope.Library, false, K(Key.C, Ctrl | Shift)),
            App(InputActionIds.LibrarySelectAll, LibraryGroup, "Select all", InputScope.Library, false, K(Key.A, Ctrl)),
            App(InputActionIds.LibraryDeleteSelection, LibraryGroup, "Delete the selection", InputScope.Library, false, K(Key.Delete)),
        };
    }
}
