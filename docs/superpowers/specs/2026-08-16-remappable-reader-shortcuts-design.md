# Remappable reader keyboard shortcuts

**Date:** 2026-08-16
**Status:** Approved, pending implementation plan
**Backlog ref:** `docs/alpha-roadmap.md` reader-polish backlog ("remappable keyboard shortcuts,
auto-scrolling/hands-free mode... remain open"); `docs/alpha-todo.md`'s double-page-spread entry
also lists it as still-open.

## Context

P5 (commit `8e1bf55`) shipped the extensible seam for this feature — `KeyboardCommandRegistry`,
`KeyBindingService`, `KeyBindingRowViewModel`, a Preferences → Keyboard Shortcuts list — but
deliberately populated it with only two commands (`Reader.PageTurnLeft`/`Right`), per its own
documented philosophy: *"grows as those features do, not by front-loading placeholder commands
with nothing behind them."* Since then, Paperbunkr has shipped fit modes, zoom presets, manual
rotation, fullscreen toggle, and continuous/webtoon scroll — all still hardcoded to fixed keys in
`PageCanvas.OnKeyDown`/`OnPointerWheelChanged`. This spec brings those under the same remappable
registry.

CE parity source: `_reference/ComicRackCE/ComicRack/MainForm.cs:1559-1794` (`InitializeKeyboard`),
backed by `cYo.Common.Windows.KeyboardShortcuts`/`KeyboardCommand`, editable via
`PreferencesDialog.keyboardShortcutEditor`. CE's `KeyboardMap.Commands` is a flat list with
first-match-wins dispatch; a single `Up`/`Down`/`Left`/`Right` command's handler
(`ComicDisplay.ScrollUp` etc.) itself decides what "up" means for the current display state,
rather than separate per-mode commands. Paperbunkr's `PageCanvas.OnKeyDown` already has the
equivalent branching (continuous scroll / zoomed pan / plain page-turn are mutually exclusive at
runtime), so this spec formalizes that branching into the registry — as **separate, independently
remappable commands per mode** (not unified, per explicit direction) rather than collapsing them
into one command like CE does.

## 1. Command inventory

24 commands total: the 2 existing plus 22 new. IDs use the existing `Reader.` prefix.

| Command ID | Default gesture | Conflict context | UI group |
|---|---|---|---|
| `Reader.PageTurnLeft` | Left *(unchanged)* | PagedUnzoomed | Navigation |
| `Reader.PageTurnRight` | Right *(unchanged)* | PagedUnzoomed | Navigation |
| `Reader.PanLeft` | Left | PagedZoomed | Navigation |
| `Reader.PanRight` | Right | PagedZoomed | Navigation |
| `Reader.PanUp` | Up | PagedZoomed | Navigation |
| `Reader.PanDown` | Down | PagedZoomed | Navigation |
| `Reader.ScrollLeft` | Left | Continuous | Navigation |
| `Reader.ScrollRight` | Right | Continuous | Navigation |
| `Reader.ScrollUp` | Up | Continuous | Navigation |
| `Reader.ScrollDown` | Down | Continuous | Navigation |
| `Reader.ScrollPageUp` | Page Up | Continuous | Navigation |
| `Reader.ScrollPageDown` | Page Down | Continuous | Navigation |
| `Reader.ScrollToStart` | Home | Continuous | Navigation |
| `Reader.ScrollToEnd` | End | Continuous | Navigation |
| `Reader.ToggleFullscreen` | F | Always | Display |
| `Reader.RotateClockwise` | R | Always | Display |
| `Reader.RotateCounterClockwise` | Shift+R | Always | Display |
| `Reader.ZoomIn` | Z | Always | Zoom & Fit |
| `Reader.ZoomOut` | Shift+Z | Always | Zoom & Fit |
| `Reader.FitOriginal` | 1 | Always | Zoom & Fit |
| `Reader.FitAll` | 2 | Always | Zoom & Fit |
| `Reader.FitWidth` | 3 | Always | Zoom & Fit |
| `Reader.FitHeight` | 4 | Always | Zoom & Fit |
| `Reader.FitBest` | 5 | Always | Zoom & Fit |

Defaults mirror CE's `MainForm.cs` exactly except the two pre-existing page-turn commands (already
shipped with Left/Right rather than CE's PageUp/Alt+Left default — a prior deliberate deviation,
not touched here).

**`F11` stays hardcoded** as a fixed, non-remappable secondary fullscreen trigger (OS-level
convention, not really "a shortcut" in the user-facing sense) — `Reader.ToggleFullscreen`'s own
gesture (default `F`) is what's remappable.

**`Reader.RotateCounterClockwise` is new capability**, not just a new key on existing logic — no
CCW rotate exists anywhere in Paperbunkr today (`ReaderScreenViewModel` only has
`RotateClockwise`). Ships alongside a small toolbar button next to the existing rotate button
(`ReaderScreen.axaml` line ~159), matching the existing pattern, so it isn't a keyboard-only
invisible feature. Everything else in this list wires up already-existing, already-hardcoded
behavior — no other new reader capability.

## 2. Conflict detection

The existing `RecomputeKeyBindingConflict` groups by `Group` and flags any same-group,
same-key repeat — correct when there were 2 commands in 1 group, wrong now: `PageTurnLeft`,
`PanLeft`, and `ScrollLeft` all default to Left and **must not** flag, since exactly one of
PagedUnzoomed/PagedZoomed/Continuous is ever active at a time. An `Always`-context command
colliding with any mode-specific one **is** a real conflict — `Always` commands are checked
unconditionally in `OnKeyDown`, ahead of the mode branches, so they'd silently shadow the
mode-specific handler.

Each `KeyboardCommandDescriptor` gains a `ConflictContext` field (`Always`, `PagedUnzoomed`,
`PagedZoomed`, `Continuous`) — separate from `Group`, which now only drives the Preferences UI
section header (see §4). Two bound gestures conflict when equal **and** (`contexts match` **or**
`either is Always`). `RecomputeKeyBindingConflict` is rewritten as a pairwise check over this rule
instead of `GroupBy(Group)`.

## 3. Data model & wiring

- `KeyOption`, `KeyboardCommandDescriptor.DefaultKey`, `KeyBindingService.GetKey`/`SetKey`, and the
  stored `KeyBinding.Key` column all move from `Avalonia.Input.Key` to `Avalonia.Input.KeyGesture`
  (Avalonia's built-in type — `Parse`/`ToString`/`Matches(KeyEventArgs)` for free, no custom
  serialization). Existing stored rows (e.g. `"Left"`) parse unchanged as a gesture with no
  modifier, so **no data migration is needed** — same column, richer string format.
- `KeyOptions.All` grows from 12 to 23 entries: existing 12 plus Home, End, F, R, Shift+R, Z,
  Shift+Z, 1, 2, 3, 4, 5. Still one flat curated list shared by every row's dropdown (no per-command
  filtering) — same as today, just bigger.
- `PageCanvas` gains bound `KeyGesture` properties for all 22 new commands (mirroring the existing
  `LeftKey`/`RightKey` pattern) and bound `ICommand` properties for `SetFitMode`,
  `RotateClockwise`, `RotateCounterClockwise`, `ZoomIn`, `ZoomOut` (Fullscreen already has this
  shape via `FullscreenToggleCommand`). `SetFitMode` is one bound `ICommand` taking an
  `ImageFitMode` parameter — not five separate bound commands — `OnKeyDown` matches the pressed
  gesture against each `Fit*` `KeyGesture` property and calls
  `SetFitModeCommand.Execute(ImageFitMode.X)` for whichever matches.
- `OnKeyDown` ordering: `Always`-context gestures are checked first (alongside the existing
  F/F11 fullscreen check, which becomes gesture-matched too), *then* the existing
  continuous/pan/page-turn mode branches, now gesture-matched instead of switching on hardcoded
  `Key.Left` etc. This ordering is what makes the conflict model in §2 correct — `Always` really
  does get first refusal.
- `ReaderScreenViewModel` loads/exposes all 24 gestures from `KeyBindingService` the same way it
  already does for the existing 2 (`PageTurnLeftKey`/`PageTurnRightKey` → generalizes to a
  gesture per command, refreshed in the same place `Load` already refreshes the existing two).

## 4. Preferences UI

The single "Keyboard Shortcuts" `groupBox` splits into three, matching the existing
`Border.groupBox`/`Border.groupHeader` convention already used elsewhere on this screen (Skins,
Install Skin, Font, etc.):

- **Navigation** (14 rows: page-turn, pan, scroll, PageUp/PageDown, Home/End)
- **Zoom & Fit** (7 rows: zoom in/out, 5 fit modes)
- **Display** (3 rows: fullscreen, rotate CW/CCW)

`PreferencesScreenViewModel` exposes three `ObservableCollection<KeyBindingRowViewModel>`
properties instead of one, each populated by filtering `KeyboardCommandRegistry.Commands` by
`Group` in `RefreshKeyBindings`. The conflict-error banner stays a single shared element above all
three (a conflict is a conflict regardless of which section it's in).

## 5. Testing

- `KeyBindingServiceTests`/`KeyBindingRowViewModelTests` (data model) extended for `KeyGesture`
  storage round-tripping (including modifier gestures) instead of bare `Key`.
- `PreferencesScreenViewModelTests` extended for the new pairwise conflict rule — cases: same
  context + same gesture (flag), different context + same gesture (no flag), `Always` +
  mode-specific + same gesture (flag).
- `ReaderScreenViewModelTests` extended for the new gesture properties loading/persisting
  correctly, and for `RotateCounterClockwiseCommand`'s rotation math (mirrors the existing
  `RotateClockwise` test).
- No new pure-math test file (unlike `PageTransitionMathTests`/`SpreadLayoutMathTests`) — this is
  dispatch/wiring logic, not a math formula; coverage stays in the ViewModel/service test layers
  above plus existing `PageCanvas`-level behavior tests.
- **Manual on-screen verification still pending** for the actual keypress-to-action wiring (same
  standing caveat as every prior reader spec — no unattended desktop GUI automation available for
  this project).

## Explicitly out of scope

Everything in CE's keymap without a real Paperbunkr feature behind it yet: magnifier, auto-scroll
(next slice, per user direction), bookmarks-as-navigation, tab switching, undock, single-page-back/
forward (Shift+PageUp/PageDown, CE's non-double-page page-turn variant — no double-page-aware
paging distinction exists in Paperbunkr's page-turn today). A capture-any-key picker UI (replacing
the curated dropdown) is also out of scope — 23 curated entries is still manageable as a dropdown.
