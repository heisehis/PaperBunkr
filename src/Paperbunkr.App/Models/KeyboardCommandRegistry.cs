using System.Collections.Generic;
using Avalonia.Input;

namespace Paperbunkr.App.Models;

/// <summary>One remappable keyboard command: a stable id, a display group/label for Preferences, and its out-of-the-box key.</summary>
public sealed record KeyboardCommandDescriptor(string Id, string Group, string Label, Key DefaultKey);

/// <summary>
/// Every remappable keyboard command in the app (Preferences &gt; Keyboard Shortcuts,
/// docs/alpha-roadmap.md P5 follow-up). The extensible seam this whole feature exists for: adding
/// a new remappable action anywhere in the app means adding one entry here plus wiring the
/// consuming control to call <c>KeyBindingService.GetKey(Id)</c> instead of a hardcoded
/// <c>Avalonia.Input.Key</c> - no migration needed, since <c>KeyBinding</c> only stores rows for
/// commands a user has actually remapped, and the Preferences list (data-driven off this registry)
/// picks up the new entry automatically.
///
/// Only two commands exist today because only the Reader's spatial page-turn is a genuinely
/// user-remappable action right now - the CE-source triage in docs/alpha-roadmap.md found nearly
/// everything else in CE's own ~40-80-command keymap targets Reader features (fit modes, zoom,
/// magnifier, auto-scroll, tabs, bookmarks...) Paperbunkr doesn't have yet. This list grows as
/// those features do, not by front-loading placeholder commands with nothing behind them.
/// </summary>
public static class KeyboardCommandRegistry
{
    public const string ReaderPageTurnLeft = "Reader.PageTurnLeft";
    public const string ReaderPageTurnRight = "Reader.PageTurnRight";

    public const string ReaderGroup = "Reader";

    public static readonly IReadOnlyList<KeyboardCommandDescriptor> Commands =
    [
        new(ReaderPageTurnLeft, ReaderGroup, "Page back (spatial left)", Key.Left),
        new(ReaderPageTurnRight, ReaderGroup, "Page forward (spatial right)", Key.Right),
    ];
}
