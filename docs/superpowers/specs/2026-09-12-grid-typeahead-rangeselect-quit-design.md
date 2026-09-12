# Grid type-ahead, Shift+arrow range-select, and Ctrl+Q quit

**Date:** 2026-09-12
**Status:** Approved, ready for planning
**Scope:** Beta backlog (keyboard operability follow-up), not Alpha P0–P7.

## 1. Background

[`2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md`](2026-08-31-app-wide-and-library-keyboard-shortcuts-design.md)
shipped app-wide/Library shortcuts and sidebar arrow-nav, explicitly deferring four items as "real
interaction/design work, not plumbing": command palette, type-ahead-by-letter, Shift+arrow
range-select, and a Ctrl+Q quit binding. Command palette shipped separately (`Ctrl+P` Quick Open,
2026-09-03). This spec closes the remaining three.

All three are real, well-specified CE features — verified against `_reference/ComicRackCE`, not
assumed:

- **Type-ahead** (`cYo.Common.Windows/Forms/KeySearch.cs`): buffered multi-character prefix search
  over the currently-displayed items' text, 2.5s idle-reset timeout, case-insensitive, leading
  articles ignored (CE's default list: `"the, der, die, das, le, la, les, l'"`), jump-to-first-match
  (single-select, clears any existing selection — not additive). Wired only to the main library
  `ItemView` (`ComicBrowserControl.cs:867`), not to any Detail-page sub-list.
- **Shift+arrow range-select** (`cYo.Common.Windows/Forms/ItemView.cs:4611-4637`,
  `SelectFromAnchorItem` at 1852-1888): an anchor set on any plain (non-Shift/Ctrl) focus move,
  reused across successive Shift+arrow/Home/End/PageUp/PageDown presses to select the inclusive
  range between anchor and new focus — the same anchor Shift+Click already uses.
- **Ctrl+Q** (`ComicRack/MainForm.Designer.cs:653`): File>Exit's menu accelerator, main window only
  (not the reader's own bare-`Q` "Exit" `KeyboardCommandRegistry` binding, a separate, already-
  out-of-scope-here remappable reader shortcut). Routes through the same tray-aware close path as
  clicking the window's own X (`MainForm.cs`'s `ControlExit`/`CloseMinimizesToTray` check).

**Not new plumbing, in one case:** `Paperbunkr.Common.Text.StringUtility` already carries a verbatim
port of CE's `Articles`/`IsArticle`/`StartsWith(a, b, comparisonType, ignoreArticles)` — including
the identical default article list — with zero call sites today (the same "ported early, never
wired" pattern several other pieces in this codebase have hit). Type-ahead's matching reuses this
directly rather than reimplementing article-skipping.

## 2. Current Paperbunkr state (verified)

- `GridKeyboardNavigation.TryHandleArrowKey(ItemsControl, Control fromControl, Key key)`
  ([`GridKeyboardNavigation.cs`](../../../src/Paperbunkr.App/Views/GridKeyboardNavigation.cs)) is a
  pure focus-mover — no Shift/selection awareness, no text matching. It's the one shared entry point
  5 screens call: `LibraryScreen.axaml.cs`, `BooksScreen.axaml.cs`, `BookDetailScreen.axaml.cs`,
  `SmartScreen.axaml.cs`, `DetailTabs.axaml.cs`.
- `TileSelectionController<T>.Toggle(IList<T> orderedItems, T item, bool isShiftHeld)`
  already implements the exact anchor-range extend logic Shift+arrow needs — currently invoked only
  from mouse-click handlers. Screens with a real controller instance to extend: `LibraryScreenViewModel`
  (`Selection`), `BooksScreenViewModel` (`Selection`), `DetailTabsViewModel` (its own selection state,
  confirmed by its existing Shift+Click handler in `DetailTabs.axaml.cs`). `SmartScreenViewModel` and
  `BookDetailScreen`'s owning VM have no multi-select at all — single-focus browse only.
- `MainWindow.axaml.cs`'s `OnMainWindowKeyDown` is the established app-wide-shortcut mechanism
  (`Ctrl+,`, `Ctrl+Tab`/`Ctrl+Shift+Tab` already live there) — declarative `<Window.KeyBindings>` was
  tried and found unreliable (documented in that file), so this project deliberately uses a manual
  Tunnel `KeyDown` handler instead of Avalonia's `HotKeyManager`/`KeyGesture`. `OnWindowClosing`
  already implements CE's own tray-aware close semantics (minimize-to-tray unless
  `_allowRealClose`); a plain `Close()` call already flows through it correctly.

## 3. What ships

### 3a. Type-ahead

New `Controls/TypeAheadSearch.cs` — same shape as `EntranceAnimation`/`SharedElement` (a small,
reusable, declaratively-attached class), holding CE's exact buffer/timeout state machine
(`KeySearch.Select(char)`'s logic, ported not reinvented) and matching via
`StringUtility.StartsWith(text, buffer, StringComparison.OrdinalIgnoreCase, ignoreArticles: true)`.
Wired via each grid's `TextInput` event (Avalonia's equivalent of WinForms `KeyPress`) rather than
`KeyDown`, since it needs the actual typed character, not a `Key` enum value.

Applies to exactly 3 grids (matching CE's own narrow scope to the main browser, not Detail-page
sub-lists): **Library** (both issue- and series-granularity cards, matching against each row's
`SeriesName`/equivalent primary display text), **Books** grid, **Smart Lists** results (whichever of
its three result kinds — issue/series/novel — is currently shown). Matching a found item: clears
grid selection, focuses it, and scrolls it into view — reusing the exact same focus/virtualization
dual-path (`ContainerFromIndex`/`INavigableContainer`) `GridKeyboardNavigation.TryHandleArrowKey`
already has for this, rather than duplicating it.

Explicitly **not** wired to `BookDetailScreen` or `DetailTabs` — CE has no equivalent for a
Detail-page sub-list, and neither browses enough same-session items for alphabetical jump-to to earn
its keep.

### 3b. Shift+arrow range-select

`GridKeyboardNavigation.TryHandleArrowKey` gains modifier-awareness and an optional
selection-extend hook, invoked with the computed navigation target when Shift is held. Wired at
exactly the 3 call sites with a real selection controller to extend — **Library**, **Books**,
**Detail's Issue tiles (`DetailTabs`)** — each passing its own `ViewModel.ToggleXSelection`/
`Selection.Toggle` as the hook, mirroring how each of those screens' existing Shift+Click handler
already calls the same method. `SmartScreen.axaml.cs` and `BookDetailScreen.axaml.cs` pass no hook
(Shift is a no-op there, same as today — nothing to extend).

Applies to Left/Right/Up/Down/Home/End alike, matching CE's own scope (not just Up/Down) — the
anchor is `TileSelectionController`'s own existing `_lastToggledIndex`, already the right shape,
reused as-is rather than adding a second, competing anchor concept.

### 3c. Ctrl+Q

One new branch in `MainWindow.axaml.cs`'s existing `OnMainWindowKeyDown`, alongside `Ctrl+,`/
`Ctrl+Tab`: `Ctrl+Q` calls the same `Close()` the window's own title-bar X already triggers, which
already flows through `OnWindowClosing`'s tray-aware logic — matching CE's own File>Exit exactly, no
new close-path logic needed. Not reader-scoped (CE's Ctrl+Q is the main-window menu accelerator, a
distinct binding from the reader's own remappable bare-`Q` `KeyboardCommandRegistry` entry, which
already exists and is untouched by this spec).

## 4. Explicitly out of scope

- Any change to the reader's own bare-`Q` exit-to-library binding (`KeyboardCommandRegistry`) —
  already exists, already remappable, unrelated to this main-window Ctrl+Q.
- Type-ahead/range-select on `BookDetailScreen` or `DetailTabs`' non-issue sub-lists (Related tab,
  etc.) — no CE precedent, no real evidence of need.
- A configurable article list (CE's is technically settable via `Program.cs`/`EngineConfiguration`);
  Paperbunkr has no equivalent settings surface for this and none is being added — the ported
  `StringUtility.Articles` default is used as-is.
- Type-ahead cycling through multiple same-prefix matches on repeated single-key presses (classic
  WinForms-native list type-ahead sometimes does this) — CE's actual `KeySearch` doesn't do this
  either (verified against its source above), so this isn't a deviation, it's parity.

## 5. Testing

- New `TypeAheadSearchTests` (or similar) covering the pure buffer/match logic in isolation:
  multi-character buffering, timeout reset, article-skipping (via `StringUtility`, already tested
  elsewhere or covered fresh here), backspace shrinking the buffer, no-match leaves the buffer
  unchanged (mirrors `KeySearch.Select`'s own `if (flag) currentText = arg;` semantics).
- `GridKeyboardNavigationTests.cs` (exists) — new cases: Shift+arrow extends from the existing
  anchor across Left/Right/Up/Down/Home/End; a plain arrow move resets the anchor to the new focus
  target, matching CE's `if (!e.Control && !e.Shift) anchorItem = viewableItem;`.
- `LibraryScreenViewModelTests`/`BooksScreenViewModelTests`/`DetailTabsViewModelTests`: a
  Shift+arrow-equivalent case (calling the selection-extend hook directly) selects the correct
  inclusive range.
- Ctrl+Q's actual key-routing lives in `MainWindow.axaml.cs`'s code-behind `OnMainWindowKeyDown`, the
  same place `Ctrl+,`/`Ctrl+Tab` live — that routing isn't unit-tested today (no `MainWindowTests`
  file exists; `Ctrl+Tab`'s own cycle-forward/back *logic* is tested at the `MainViewModelTests`
  level, on the VM command it dispatches to, not the key-routing itself). Matching that precedent:
  no new code-behind test for the routing; this is on-screen-only, same as the existing shortcuts.

## 6. On-screen verification

Standing caveat: no computer-use available this session. Type-ahead's tooltip-less-but-visible jump
behavior, Shift+arrow's felt range-select across all three grids, and Ctrl+Q's tray-minimize-vs-quit
behavior all need a manual click-through pass before being marked verified.
