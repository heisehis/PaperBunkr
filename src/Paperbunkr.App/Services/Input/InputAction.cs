using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// One semantic thing the user can ask the app to do ("next page", "zoom in", "go back"), identified by a stable string id
/// (docs/superpowers/specs/2026-10-03-input-service-design.md §5.1). Views and ViewModels deal only in actions, never in physical keys or wheel
/// deltas. A string-id struct rather than an enum so plugins and later features can mint actions at runtime without recompiling core; the core
/// actions are the <c>const</c> ids in <see cref="InputActionIds"/>, so handlers can still <c>switch</c> on them and a typo is a compile error.
/// The id is also the persisted key in <c>keymap.json</c>. Ids are case-sensitive and compared ordinally.
/// </summary>
public readonly record struct InputAction
{
    public InputAction(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    /// <summary>The stable, persisted id, e.g. <c>Reader.ZoomIn</c>. <see langword="null"/> only for <see langword="default"/>.</summary>
    public string Id { get; }

    /// <summary>False only for <see langword="default"/>(<see cref="InputAction"/>), which names no action.</summary>
    public bool IsDefined => Id is not null;

    /// <summary>Lets core code write <c>input.Dispatch(InputActionIds.NextPage)</c>; the string is validated like the constructor.</summary>
    public static implicit operator InputAction(string id) => new(id);

    public override string ToString() => Id ?? string.Empty;
}

/// <summary>
/// The core action ids, one <c>const</c> per built-in action, grouped as in the design spec §5.1. Where an action existed in the old
/// <c>KeyboardCommandRegistry</c> its id is carried over unchanged, so bindings already stored under that id keep working. Reader actions use the
/// <c>Reader.</c> prefix, app-wide ones <c>App.</c>, Library ones <c>Library.</c>.
/// </summary>
public static class InputActionIds
{
    // --- Reader: navigation -------------------------------------------------------------------------------------------------------------------

    /// <summary>Page back in the spatial sense (the left arrow); distinct from <see cref="PreviousPage"/> because a right-to-left book turns the other way.</summary>
    public const string PageTurnLeft = "Reader.PageTurnLeft";

    /// <summary>Page forward in the spatial sense (the right arrow); see <see cref="PageTurnLeft"/>.</summary>
    public const string PageTurnRight = "Reader.PageTurnRight";

    /// <summary>Page back in vertical paged reading (the up arrow); claimed only while the book pages vertically, so the arrow stays free otherwise.</summary>
    public const string PageTurnUp = "Reader.PageTurnUp";

    /// <summary>Page forward in vertical paged reading (the down arrow); see <see cref="PageTurnUp"/>.</summary>
    public const string PageTurnDown = "Reader.PageTurnDown";

    /// <summary>Next page in reading order, whichever way the book reads.</summary>
    public const string NextPage = "Reader.NextPage";

    /// <summary>Previous page in reading order.</summary>
    public const string PreviousPage = "Reader.PreviousPage";

    public const string FirstPage = "Reader.FirstPage";
    public const string LastPage = "Reader.LastPage";
    public const string GoToPage = "Reader.GoToPage";

    public const string PanLeft = "Reader.PanLeft";
    public const string PanRight = "Reader.PanRight";
    public const string PanUp = "Reader.PanUp";
    public const string PanDown = "Reader.PanDown";

    public const string ScrollLeft = "Reader.ScrollLeft";
    public const string ScrollRight = "Reader.ScrollRight";
    public const string ScrollUp = "Reader.ScrollUp";
    public const string ScrollDown = "Reader.ScrollDown";
    public const string ScrollPageUp = "Reader.ScrollPageUp";
    public const string ScrollPageDown = "Reader.ScrollPageDown";
    public const string ScrollToStart = "Reader.ScrollToStart";
    public const string ScrollToEnd = "Reader.ScrollToEnd";
    public const string ToggleAutoScroll = "Reader.ToggleAutoScroll";

    public const string PreviousBookmark = "Reader.PreviousBookmark";
    public const string NextBookmark = "Reader.NextBookmark";

    /// <summary>Browser-style "back" to where the reader was before a big jump.</summary>
    public const string JumpBack = "Reader.JumpBack";

    /// <summary>Closes the reader and returns to the screen it was opened from (the gamepad's Back button; distinct from <see cref="CloseCurrentView"/>, which only closes overlays).</summary>
    public const string LeaveReader = "Reader.Leave";

    // --- Reader: view -------------------------------------------------------------------------------------------------------------------------

    public const string ZoomIn = "Reader.ZoomIn";
    public const string ZoomOut = "Reader.ZoomOut";
    public const string ResetZoom = "Reader.ResetZoom";

    public const string FitOriginal = "Reader.FitOriginal";
    public const string FitAll = "Reader.FitAll";
    public const string FitWidth = "Reader.FitWidth";
    public const string FitHeight = "Reader.FitHeight";
    public const string FitBest = "Reader.FitBest";

    public const string RotateClockwise = "Reader.RotateClockwise";
    public const string RotateCounterClockwise = "Reader.RotateCounterClockwise";
    public const string ToggleDoublePageMode = "Reader.ToggleDoublePageMode";

    /// <summary>Flips the reading direction between left-to-right and right-to-left.</summary>
    public const string ToggleReadingDirection = "Reader.ToggleReadingDirection";

    public const string ToggleGuidedView = "Reader.ToggleGuidedView";

    // --- Reader: tools ------------------------------------------------------------------------------------------------------------------------

    public const string CommandPalette = "Reader.CommandPalette";
    public const string ToggleInfoPanel = "Reader.ToggleInfoPanel";
    public const string PinPage = "Reader.PinPage";
    public const string ClipRegion = "Reader.ClipRegion";
    public const string CopyPage = "Reader.CopyPage";
    public const string ReportBadPage = "Reader.ReportBadPage";
    public const string NextProfile = "Reader.NextProfile";
    public const string ToggleSessionHud = "Reader.ToggleSessionHud";
    public const string ToggleWarmShift = "Reader.ToggleWarmShift";

    /// <summary>Shows or hides the reader toolbar (today only the gamepad Y button reaches it).</summary>
    public const string ToggleChrome = "Reader.ToggleChrome";

    /// <summary>The frame-time / cache diagnostics overlay (Ctrl+Shift+P).</summary>
    public const string TogglePerfOverlay = "Reader.TogglePerfOverlay";

    // --- Reader: fullscreen -------------------------------------------------------------------------------------------------------------------

    public const string ToggleFullscreen = "Reader.ToggleFullscreen";

    // --- Reader: analogue axes (gamepad sticks and triggers; delivered with a continuous Value, see InputActionKind.Axis) -----------------------

    public const string PanHorizontal = "Reader.PanHorizontal";
    public const string PanVertical = "Reader.PanVertical";
    public const string ZoomAxis = "Reader.ZoomAxis";

    // --- Global and layout --------------------------------------------------------------------------------------------------------------------

    public const string ToggleSidebar = "App.ToggleSidebar";
    public const string OpenSettings = "App.OpenSettings";

    /// <summary>Closes the topmost overlay or leaves the current view (the Escape key).</summary>
    public const string CloseCurrentView = "App.CloseCurrentView";

    public const string FocusSearch = "App.FocusSearch";
    public const string OpenQuickOpen = "App.OpenQuickOpen";
    public const string Undo = "App.Undo";
    public const string Redo = "App.Redo";
    public const string Quit = "App.Quit";
    public const string CycleScreenForward = "App.CycleScreenForward";
    public const string CycleScreenBackward = "App.CycleScreenBackward";
    public const string ToggleLibraryPreview = "App.ToggleLibraryPreview";

    // --- Every screen: moving around and acting on the focused item, by keyboard, mouse or controller. These forward the key the control already understands (docs/superpowers/specs/2026-10-03-input-service-design.md §14). ---

    /// <summary>Moves focus (or the caret-free arrow key a control uses) one step up; the controller's D-pad and left stick.</summary>
    public const string FocusUp = "App.FocusUp";

    public const string FocusDown = "App.FocusDown";
    public const string FocusLeft = "App.FocusLeft";
    public const string FocusRight = "App.FocusRight";

    /// <summary>Opens or presses the focused item: sends it Enter. The controller's A button.</summary>
    public const string Activate = "App.Activate";

    /// <summary>Toggles the focused item: sends it Space.</summary>
    public const string ToggleSelect = "App.ToggleSelect";

    /// <summary>Renames the focused item: sends it F2.</summary>
    public const string RenameItem = "App.RenameItem";

    /// <summary>Removes the focused item: sends it Delete.</summary>
    public const string DeleteItem = "App.DeleteItem";

    /// <summary>Opens the focused item's context menu.</summary>
    public const string ContextMenu = "App.ContextMenu";

    /// <summary>Previous tab (or, on a screen with none, the previous screen).</summary>
    public const string TabPrevious = "App.TabPrevious";

    /// <summary>Next tab (or, on a screen with none, the next screen).</summary>
    public const string TabNext = "App.TabNext";

    /// <summary>Scrolls the focused area up or down (the controller's right stick).</summary>
    public const string ScrollVertical = "App.ScrollVertical";

    /// <summary>Jumps the focused list or grid to its first item (Home).</summary>
    public const string FocusFirst = "App.FocusFirst";

    /// <summary>Jumps the focused list or grid to its last item (End).</summary>
    public const string FocusLast = "App.FocusLast";

    /// <summary>Moves a page up in the focused list or grid (Page Up).</summary>
    public const string ItemPageUp = "App.ItemPageUp";

    public const string ItemPageDown = "App.ItemPageDown";

    public const string ScrollHorizontal = "App.ScrollHorizontal";

    // --- Book reader (EPUB, FB2, MOBI): its page turns and the screen-reader "where am I?" announcement ---

    public const string BookNextPage = "BookReader.NextPage";
    public const string BookPreviousPage = "BookReader.PreviousPage";

    /// <summary>Speaks the chapter and position (docs/superpowers/specs/2026-09-01-books-reader-screen-reader-accessibility-design.md); deliberately not Thorium's Ctrl+F10, which has no meaning here.</summary>
    public const string BookAnnouncePosition = "BookReader.AnnouncePosition";

    // --- Compare screen (two versions of an issue side by side) ---

    public const string CompareNextPage = "Compare.NextPage";
    public const string ComparePreviousPage = "Compare.PreviousPage";
    public const string CompareFirstPage = "Compare.FirstPage";
    public const string CompareFlip = "Compare.Flip";
    public const string CompareToggleMode = "Compare.ToggleMode";
    public const string CompareResetView = "Compare.ResetView";
    public const string CompareKeepA = "Compare.KeepA";
    public const string CompareKeepB = "Compare.KeepB";

    // --- Library and navigation ---------------------------------------------------------------------------------------------------------------

    public const string NavigateBack = "App.NavigateBack";
    public const string NavigateForward = "App.NavigateForward";

    // --- Shared across screens: each screen that has the thing handles it (docs/superpowers/specs/2026-10-03-input-service-design.md §14) ---

    /// <summary>Reloads what the screen shows (F5). Was <c>Library.Refresh</c>; the old id is a former id, so a remap carries over.</summary>
    public const string Refresh = "App.Refresh";

    /// <summary>Starts a new list, collection or item on the current screen (Ctrl+N).</summary>
    public const string NewItem = "App.NewItem";

    /// <summary>Saves the edit in progress on the current screen (Ctrl+S).</summary>
    public const string Save = "App.Save";

    // --- Books ---

    public const string BooksEditSelection = "Books.EditSelection";
    public const string BooksDeleteSelection = "Books.DeleteSelection";
    public const string BooksSelectAll = "Books.SelectAll";

    // --- Detail screens (comic series, manga, book) ---

    public const string DetailContinue = "Detail.Continue";
    public const string DetailEdit = "Detail.Edit";

    // --- Smart lists ---

    public const string SmartDuplicate = "SmartLists.Duplicate";

    // --- Library: actions on the current selection (formerly LibraryActionCatalog.KeyMap; the catalog still decides what each one runs for the selection at hand) ---

    public const string LibraryEdit = "Library.Edit";
    public const string LibraryRate0 = "Library.Rate0";
    public const string LibraryRate1 = "Library.Rate1";
    public const string LibraryRate2 = "Library.Rate2";
    public const string LibraryRate3 = "Library.Rate3";
    public const string LibraryRate4 = "Library.Rate4";
    public const string LibraryRate5 = "Library.Rate5";
    public const string LibraryMarkRead = "Library.MarkRead";
    public const string LibraryMarkUnread = "Library.MarkUnread";
    public const string LibraryReveal = "Library.Reveal";
    public const string LibraryCopyData = "Library.CopyData";
    public const string LibraryPasteData = "Library.PasteData";
    public const string LibraryCopyPaths = "Library.CopyPaths";
    public const string LibrarySelectAll = "Library.SelectAll";
    public const string LibraryDeleteSelection = "Library.DeleteSelection";
}

/// <summary>How an action is delivered: a discrete press, or a continuous value in -1..1 each poll while non-zero (gamepad sticks and triggers).</summary>
public enum InputActionKind
{
    Button,
    Axis,
}

/// <summary>
/// Everything the app knows about one action: its id, how Preferences shows it, where and when it is reachable, and its out-of-the-box bindings.
/// The "default keymap" is simply the union of every <see cref="Defaults"/> in the catalog, so a default lives next to the action it belongs to.
/// </summary>
/// <param name="Action">The action; its <see cref="InputAction.Id"/> is the persisted key.</param>
/// <param name="Group">Preferences heading, e.g. "Navigation".</param>
/// <param name="Label">Preferences display name, e.g. "Zoom in".</param>
/// <param name="Scope">Which scope must be active for the action to be considered.</param>
/// <param name="Context">Which sub-states of that scope it is reachable in (see <see cref="InputContext"/>); <see cref="InputContext.Always"/> when state is irrelevant.</param>
/// <param name="Defaults">The bindings an unmodified install ships with; may be empty (unbound by default).</param>
/// <param name="Kind">Whether it is a discrete press or an analogue axis.</param>
/// <param name="FiresInTextInput">True for the few actions that must still work while a text box has focus (Escape, the browser-back key, quick open).</param>
/// <param name="FormerIds">Ids this action replaces; an override saved under one of them is re-keyed to <see cref="Action"/> on load, so a rename never drops a user's binding.</param>
/// <param name="CanonicalKey">
/// For the "every screen" actions only: the plain key the focused controls already understand for this action (Enter for activate, an arrow for a move). The service passes that key
/// through untouched while it is bound to the action, forwards the action as that key when the user has bound something else to it, and swallows the plain key when the user has taken
/// it away, so remapping applies to every control without each one asking the keymap (docs/superpowers/specs/2026-10-03-input-service-design.md §14).
/// </param>
public sealed record InputActionInfo(
    InputAction Action,
    string Group,
    string Label,
    InputScope Scope,
    InputContext Context,
    IReadOnlyList<InputBinding> Defaults,
    InputActionKind Kind = InputActionKind.Button,
    bool FiresInTextInput = false,
    IReadOnlyList<string>? FormerIds = null,
    Key? CanonicalKey = null)
{
    /// <summary>The persisted id, shorthand for <c>Action.Id</c>.</summary>
    public string Id => Action.Id;
}

/// <summary>
/// The set of actions the app knows about: the built-in ones plus anything registered at runtime (plugins, later features). The catalog describes
/// actions; which physical inputs trigger them is the keymap's job.
/// </summary>
public interface IInputActionCatalog
{
    /// <summary>Every registered action, built-in first, in registration order.</summary>
    IReadOnlyList<InputActionInfo> All { get; }

    /// <summary>The registration for <paramref name="action"/>, or <see langword="null"/> when it is not registered (including <see langword="default"/>).</summary>
    InputActionInfo? Find(InputAction action);

    /// <summary>Registers a new action.</summary>
    /// <exception cref="ArgumentException">
    /// An action with the same id is already registered, or the id collides with an entry in anyone's <see cref="InputActionInfo.FormerIds"/>
    /// (which would make a saved override ambiguous).
    /// </exception>
    void Register(InputActionInfo info);

    /// <summary>
    /// Removes a runtime-registered action (a plugin command that was switched off or uninstalled). Returns false when no such action is registered. A user's saved override for it is
    /// kept, so switching the plugin back on restores their shortcut.
    /// </summary>
    bool Unregister(string id);
}
