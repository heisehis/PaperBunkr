using System.Collections.Generic;
using Avalonia.Input;

namespace Paperbunkr.App.Models;

/// <summary>
/// Which reader states a command is reachable in, for conflict detection (see
/// <c>PreferencesScreenViewModel.RecomputeKeyBindingConflict</c>). <see cref="Always"/> commands
/// are checked unconditionally, ahead of every mode branch, in <c>PageCanvas.OnKeyDown</c> - so an
/// <see cref="Always"/> command sharing a gesture with any mode-specific one is a real conflict (it
/// silently shadows the mode-specific handler). <see cref="PagedUnzoomed"/>/<see cref="PagedZoomed"/>/
/// <see cref="Continuous"/> are mutually exclusive at runtime (exactly one is ever active), so two
/// commands sharing a gesture across different ones of these three is never actually a conflict.
/// </summary>
public enum ConflictContext
{
    Always,
    PagedUnzoomed,
    PagedZoomed,
    Continuous,

    /// <summary>Every paged state (zoomed or not), for reading-order commands such as <c>Reader.NextPage</c> (design 2026-09-25 F1 §2); overlaps <see cref="PagedUnzoomed"/> and <see cref="PagedZoomed"/>, never <see cref="Continuous"/>.</summary>
    Paged,
}

/// <summary>Whether two <see cref="ConflictContext"/>s can be active at the same time, i.e. whether a shared gesture is a real conflict.</summary>
public static class ConflictContexts
{
    public static bool MayOverlap(ConflictContext a, ConflictContext b)
    {
        if (a == ConflictContext.Always || b == ConflictContext.Always || a == b)
        {
            return true;
        }

        return (a == ConflictContext.Paged && b is ConflictContext.PagedUnzoomed or ConflictContext.PagedZoomed)
            || (b == ConflictContext.Paged && a is ConflictContext.PagedUnzoomed or ConflictContext.PagedZoomed);
    }
}

/// <summary>
/// One remappable keyboard command: a stable id, a display group/label for Preferences, its out-of-the-box gesture, and which reader states it's reachable in.
/// <paramref name="AdditionalDefaults"/> ships further default gestures for the same command (clicker, media and Space keys for the reading-order page commands);
/// <see cref="AllDefaults"/> is what a never-customised command resolves to.
/// </summary>
public sealed record KeyboardCommandDescriptor(string Id, string Group, string Label, KeyGesture DefaultGesture, ConflictContext Context, IReadOnlyList<KeyGesture>? AdditionalDefaults = null)
{
    public IReadOnlyList<KeyGesture> AllDefaults => AdditionalDefaults is { Count: > 0 } more ? [DefaultGesture, .. more] : [DefaultGesture];
}

/// <summary>
/// Every remappable keyboard command in the app (Preferences &gt; Keyboard Shortcuts,
/// docs/Paperbunkr-Roadmap.md P5 follow-up, extended by docs/superpowers/specs/
/// 2026-08-16-remappable-reader-shortcuts-design.md). The extensible seam this whole feature
/// exists for: adding a new remappable action anywhere in the app means adding one entry here plus
/// wiring the consuming control to call <c>KeyBindingService.GetKeys(Id)</c> instead of a hardcoded
/// gesture - no migration needed, since <c>KeyBinding</c> only stores rows for commands a user has
/// actually remapped, and the Preferences list (data-driven off this registry) picks up the new
/// entry automatically.
///
/// Defaults mirror CE's actual keymap (_reference/ComicRackCE/ComicRack/MainForm.cs
/// InitializeKeyboard) except the two PageTurn commands, which predate this extension and keep
/// their already-shipped Left/Right defaults rather than CE's PageUp/Alt+Left - a prior deliberate
/// deviation, not touched here.
/// </summary>
public static class KeyboardCommandRegistry
{
    public const string ReaderPageTurnLeft = "Reader.PageTurnLeft";
    public const string ReaderPageTurnRight = "Reader.PageTurnRight";
    public const string ReaderNextPage = "Reader.NextPage";
    public const string ReaderPreviousPage = "Reader.PreviousPage";
    public const string ReaderCommandPalette = "Reader.CommandPalette";
    public const string ReaderGoToPage = "Reader.GoToPage";
    public const string ReaderNextProfile = "Reader.NextProfile";
    public const string ReaderToggleSessionHud = "Reader.ToggleSessionHud";
    public const string ReaderToggleWarmShift = "Reader.ToggleWarmShift";
    public const string ReaderCopyPage = "Reader.CopyPage";
    public const string ReaderToggleGuidedView = "Reader.ToggleGuidedView";
    public const string ReaderToggleInfoPanel = "Reader.ToggleInfoPanel";
    public const string ReaderPinPage = "Reader.PinPage";
    public const string ReaderClipRegion = "Reader.ClipRegion";
    public const string ReaderPanLeft = "Reader.PanLeft";
    public const string ReaderPanRight = "Reader.PanRight";
    public const string ReaderPanUp = "Reader.PanUp";
    public const string ReaderPanDown = "Reader.PanDown";
    public const string ReaderScrollLeft = "Reader.ScrollLeft";
    public const string ReaderScrollRight = "Reader.ScrollRight";
    public const string ReaderScrollUp = "Reader.ScrollUp";
    public const string ReaderScrollDown = "Reader.ScrollDown";
    public const string ReaderScrollPageUp = "Reader.ScrollPageUp";
    public const string ReaderScrollPageDown = "Reader.ScrollPageDown";
    public const string ReaderScrollToStart = "Reader.ScrollToStart";
    public const string ReaderScrollToEnd = "Reader.ScrollToEnd";
    public const string ReaderToggleAutoScroll = "Reader.ToggleAutoScroll";
    public const string ReaderPreviousBookmark = "Reader.PreviousBookmark";
    public const string ReaderNextBookmark = "Reader.NextBookmark";
    public const string ReaderJumpBack = "Reader.JumpBack";
    public const string ReaderReportBadPage = "Reader.ReportBadPage";
    public const string ReaderToggleFullscreen = "Reader.ToggleFullscreen";
    public const string ReaderRotateClockwise = "Reader.RotateClockwise";
    public const string ReaderRotateCounterClockwise = "Reader.RotateCounterClockwise";
    public const string ReaderZoomIn = "Reader.ZoomIn";
    public const string ReaderZoomOut = "Reader.ZoomOut";
    public const string ReaderFitOriginal = "Reader.FitOriginal";
    public const string ReaderFitAll = "Reader.FitAll";
    public const string ReaderFitWidth = "Reader.FitWidth";
    public const string ReaderFitHeight = "Reader.FitHeight";
    public const string ReaderFitBest = "Reader.FitBest";

    public const string NavigationGroup = "Navigation";
    public const string ZoomFitGroup = "Zoom & Fit";
    public const string DisplayGroup = "Display";

    public static readonly IReadOnlyList<KeyboardCommandDescriptor> Commands =
    [
        new(ReaderPageTurnLeft, NavigationGroup, "Page back (spatial left)", new KeyGesture(Key.Left), ConflictContext.PagedUnzoomed),
        new(ReaderPageTurnRight, NavigationGroup, "Page forward (spatial right)", new KeyGesture(Key.Right), ConflictContext.PagedUnzoomed),
        // Reading-order (design 2026-09-25 F1 §2): next/previous page whichever way the book reads, unlike the two spatial commands above. The extra defaults are
        // what a presentation clicker (PageUp/PageDown), a media remote and Space-reading habits send; paged mode only, continuous mode keeps them as scroll steps.
        new(ReaderNextPage, NavigationGroup, "Next page (reading order)", new KeyGesture(Key.PageDown), ConflictContext.Paged,
            [new KeyGesture(Key.Space), new KeyGesture(Key.MediaNextTrack)]),
        new(ReaderPreviousPage, NavigationGroup, "Previous page (reading order)", new KeyGesture(Key.PageUp), ConflictContext.Paged,
            [new KeyGesture(Key.Space, KeyModifiers.Shift), new KeyGesture(Key.MediaPreviousTrack)]),
        new(ReaderPanLeft, NavigationGroup, "Pan left (zoomed in)", new KeyGesture(Key.Left), ConflictContext.PagedZoomed),
        new(ReaderPanRight, NavigationGroup, "Pan right (zoomed in)", new KeyGesture(Key.Right), ConflictContext.PagedZoomed),
        new(ReaderPanUp, NavigationGroup, "Pan up (zoomed in)", new KeyGesture(Key.Up), ConflictContext.PagedZoomed),
        new(ReaderPanDown, NavigationGroup, "Pan down (zoomed in)", new KeyGesture(Key.Down), ConflictContext.PagedZoomed),
        new(ReaderScrollLeft, NavigationGroup, "Scroll left (continuous)", new KeyGesture(Key.Left), ConflictContext.Continuous),
        new(ReaderScrollRight, NavigationGroup, "Scroll right (continuous)", new KeyGesture(Key.Right), ConflictContext.Continuous),
        new(ReaderScrollUp, NavigationGroup, "Scroll up (continuous)", new KeyGesture(Key.Up), ConflictContext.Continuous),
        new(ReaderScrollDown, NavigationGroup, "Scroll down (continuous)", new KeyGesture(Key.Down), ConflictContext.Continuous),
        new(ReaderScrollPageUp, NavigationGroup, "Scroll up a page (continuous)", new KeyGesture(Key.PageUp), ConflictContext.Continuous),
        new(ReaderScrollPageDown, NavigationGroup, "Scroll down a page (continuous)", new KeyGesture(Key.PageDown), ConflictContext.Continuous),
        new(ReaderScrollToStart, NavigationGroup, "Scroll to start (continuous)", new KeyGesture(Key.Home), ConflictContext.Continuous),
        new(ReaderScrollToEnd, NavigationGroup, "Scroll to end (continuous)", new KeyGesture(Key.End), ConflictContext.Continuous),
        new(ReaderToggleAutoScroll, NavigationGroup, "Toggle auto-scroll (continuous)", new KeyGesture(Key.S), ConflictContext.Continuous),
        // Mirrors CE's actual defaults exactly (_reference/ComicRackCE/ComicRack/MainForm.cs
        // InitializeKeyboard: MoveToPrevBookmark/MoveToNextBookmark) - CE has no default shortcut
        // for *setting* a bookmark itself (menu/toolbar only), not reproduced here either.
        new(ReaderPreviousBookmark, NavigationGroup, "Previous bookmark", new KeyGesture(Key.PageUp, KeyModifiers.Control), ConflictContext.Always),
        new(ReaderNextBookmark, NavigationGroup, "Next bookmark", new KeyGesture(Key.PageDown, KeyModifiers.Control), ConflictContext.Always),
        // Paperbunkr-original (2026-09-21 comic reader pitch #10; CE has no jump-back). Browser-style "back" after a big jump.
        new(ReaderJumpBack, NavigationGroup, "Back to previous position (after a jump)", new KeyGesture(Key.Left, KeyModifiers.Alt), ConflictContext.Always),
        // Paperbunkr-original (2026-09-21 comic reader pitch #12; CE has no bad-page report). Opens the reason picker for the current page.
        new(ReaderReportBadPage, NavigationGroup, "Report a bad page", new KeyGesture(Key.X), ConflictContext.Always),
        // Paperbunkr-original (2026-09-25 comic reader pitch #19; CE has no command palette). Ctrl+K opens the reader's command palette, Ctrl+G its go-to-page prompt.
        new(ReaderCommandPalette, NavigationGroup, "Reader command palette", new KeyGesture(Key.K, KeyModifiers.Control), ConflictContext.Always),
        new(ReaderGoToPage, NavigationGroup, "Go to page…", new KeyGesture(Key.G, KeyModifiers.Control), ConflictContext.Always),
        new(ReaderToggleFullscreen, DisplayGroup, "Toggle fullscreen", new KeyGesture(Key.F), ConflictContext.Always),
        // Paperbunkr-original (2026-09-25 comic reader pitch #15; CE has display workspaces but no hotkey). Cycles Standard and the reader profiles for this reading visit.
        new(ReaderNextProfile, DisplayGroup, "Next reader profile", new KeyGesture(Key.P), ConflictContext.Always),
        // Paperbunkr-original (2026-09-25 comic reader pitch #17, #18, #22; CE has none of them).
        new(ReaderToggleSessionHud, DisplayGroup, "Toggle reading stats", new KeyGesture(Key.H), ConflictContext.Always),
        new(ReaderToggleWarmShift, DisplayGroup, "Toggle warm tint", new KeyGesture(Key.W), ConflictContext.Always),
        new(ReaderCopyPage, DisplayGroup, "Copy page (or spread) to the clipboard", new KeyGesture(Key.C, KeyModifiers.Control), ConflictContext.Always),
        // Paperbunkr-original (2026-09-25 comic reader pitch #1; CE has no panel detection). Guided panel view: next/previous step panel by panel. Paged modes only.
        new(ReaderToggleGuidedView, DisplayGroup, "Toggle guided panel view", new KeyGesture(Key.G), ConflictContext.Paged),
        // Paperbunkr-original (2026-09-26 comic reader pitch #28, #29, #7; CE's reader has no info panel, pin or clip).
        new(ReaderToggleInfoPanel, DisplayGroup, "Toggle info panel", new KeyGesture(Key.I), ConflictContext.Always),
        new(ReaderPinPage, DisplayGroup, "Pin this page as a reference", new KeyGesture(Key.P, KeyModifiers.Shift), ConflictContext.Always),
        new(ReaderClipRegion, DisplayGroup, "Clip a region of this page", new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift), ConflictContext.Paged),
        new(ReaderRotateClockwise, DisplayGroup, "Rotate clockwise", new KeyGesture(Key.R), ConflictContext.Always),
        new(ReaderRotateCounterClockwise, DisplayGroup, "Rotate counter-clockwise", new KeyGesture(Key.R, KeyModifiers.Shift), ConflictContext.Always),
        new(ReaderZoomIn, ZoomFitGroup, "Zoom in", new KeyGesture(Key.Z), ConflictContext.Always),
        new(ReaderZoomOut, ZoomFitGroup, "Zoom out", new KeyGesture(Key.Z, KeyModifiers.Shift), ConflictContext.Always),
        new(ReaderFitOriginal, ZoomFitGroup, "Fit: Original size", new KeyGesture(Key.D1), ConflictContext.Always),
        new(ReaderFitAll, ZoomFitGroup, "Fit: Fit all", new KeyGesture(Key.D2), ConflictContext.Always),
        new(ReaderFitWidth, ZoomFitGroup, "Fit: Fit width", new KeyGesture(Key.D3), ConflictContext.Always),
        new(ReaderFitHeight, ZoomFitGroup, "Fit: Fit height", new KeyGesture(Key.D4), ConflictContext.Always),
        new(ReaderFitBest, ZoomFitGroup, "Fit: Best fit", new KeyGesture(Key.D5), ConflictContext.Always),
    ];
}
