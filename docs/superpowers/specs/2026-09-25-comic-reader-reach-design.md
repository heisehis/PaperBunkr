# Comic reader — Reach (slice F1) design

Date: 2026-09-25. Status: **built 2026-09-25** (uncommitted; on-screen check by the user pending, see "Implementation notes" at the end).
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`, slice F. Slice F was split into three specs by decision: **F1 "Reach"** (this one: #19
command palette, #20 extra input devices including a gamepad, #30 tap-zone layouts), **F2 "Profiles"** (#15) and **F3 "Comfort"** (#17 session HUD, #22
copy/share, #18 break nudges). Slices A, B and G are built (uncommitted); F1 builds on the same working tree.

## Facts this design rests on (verified 2026-09-25)

- **Quick Open exists but is inert in the reader.** `QuickOpenOverlay` / `QuickOpenViewModel` / `QuickOpenService` (Ctrl+P) is hosted in `OverlayShell`;
  `MainWindow.axaml.cs:131-139` deliberately ignores Ctrl+P while the reader is showing (spec `2026-09-03-quick-open-command-palette-design.md`).
  `QuickOpenMatcher.Score(query, target)` is a public static subsequence scorer that a reader palette can reuse. There is no go-to-page UI anywhere in the
  comic reader (only `GoToPage(int)`), and no reader "rate" command (only the end card's `EndCardRate`, which opens Quick Rate).
- **Reader key routing.** `PageCanvas` has one bindable `KeyGesture` list plus one `ICommand` per remappable command (`PageCanvas.cs:113-225`, bound in
  `ReaderScreen.axaml`); the registry is `KeyboardCommandRegistry` (28 reader commands), each with **one** `DefaultGesture`
  (`KeyBindingService.GetKeys` returns the user's rows or `[DefaultGesture]`). `Reader.PageTurnLeft/Right` are **spatial** (the VM flips them for RTL), so
  PageUp/PageDown/Space, media keys and clicker keys, which mean "next/previous in reading order", cannot simply be added as extra gestures on them.
  The in-canvas bad-page picker from slice B (`ReaderScreen.axaml` overlay + tunnel key handler + focus hand-back) is the proven way to host a modal in the reader.
- **Pointer buttons are not checked.** `PageCanvas.OnPointerPressed` (:2239) never looks at which button was pressed, so a right or middle press starts
  a drag, a page-turn zone or a double-click zoom, and right-click (which `ContextMenuHost` handles on release) very likely also turns the page.
  Continuous mode starts a drag on every press and has no click detection, so it has no tap zones at all.
- **Current zones:** touch = three columns (left third back, right third forward, middle third toggles chrome via `ToggleChromeCommand`); mouse = two halves
  (`PageTurnGestureMath.ResolveZone`, `InvokeTouchZone`/`InvokeZoneCommand`). Nothing is configurable; there is no zone setting in `AppSettings`.
  Zones are physical; RTL is handled by the spatial commands (`GoLeft`/`GoRight` flip when `_isRightToLeft`).
- **Avalonia 12.1.1 supports the inputs** (checked by reflection): `Key.MediaNextTrack`/`MediaPreviousTrack`/`MediaPlayPause`/`MediaStop`,
  `PointerUpdateKind.XButton1/2Pressed`, `PointerPointProperties.IsXButton1/2Pressed`. Whether Windows delivers media keys to the focused control (versus the
  media session) is unverified: needs the user's device. There is **no gamepad code** and no XInput/SDL/`Windows.Gaming` dependency; `App.csproj` is plain
  `net10.0`; `Services/BatteryStatusInterop.cs` is the P/Invoke precedent (guarded by `OperatingSystem.IsWindows()`); the app is effectively Windows-only.
- **Mihon's tap-zone model** (fetched from `mihonapp/mihon`, `ui/reader/viewer/navigation/*.kt` and `ReaderPreferences.kt`, 2026-09-25) is the model the user
  asked for. Layouts are lists of `RectF` regions on a 3×3 grid, each mapped to PREV / NEXT / MENU (or directional LEFT / RIGHT); anything unmatched is MENU.
  `LNavigation`: (0,.33,.33,.66)→PREV, (0,0,1,.33)→PREV, (.66,.33,1,.66)→NEXT, (0,.66,1,1)→NEXT. `KindlishNavigation`: (.33,.33,1,1)→NEXT, (0,.33,.33,1)→PREV.
  `EdgeNavigation`: (0,0,.33,1)→NEXT, (.33,.66,.66,1)→PREV, (.66,0,1,1)→NEXT. `RightAndLeftNavigation`: (0,0,.33,1)→LEFT, (.66,0,1,1)→RIGHT.
  `DisabledNavigation`: no regions. Separate settings for pager and webtoon (`navigationModePager/Webtoon`), a manual `TappingInvertMode` per mode
  (NONE/HORIZONTAL/VERTICAL/BOTH), and a coloured zone overlay (`showNavigationOverlayNewUser`, `showNavigationOverlayOnStart`). Volume-key reading and long-tap are
  mobile-only and are not adopted.
- **CE precedent:** `GestureHitTest` (`ImageDisplayControl.cs:2171`) nine 80 px hot spots mapped through `CommandKey.Gesture1..9`; `CommandKey` also has
  Media* keys and `MouseButton4/5`; mouse buttons 4/5 are bound to list back/forward in `MainForm.cs:1787-1793`. CE has no gamepad support.

## Scope

In: the pointer-button policy fix; reading-order next/previous commands with clicker, media-key and extra-mouse-button support; a gamepad (XInput);
Mihon-style tap-zone layouts for paged and continuous mode including mouse clicks, invert modes and a zone overlay; a Ctrl+K reader command palette with
go-to-page.

Out: remappable gamepad buttons and remappable mouse buttons (fixed defaults first, tuned on the user's device; remapping needs a new binding kind);
free-form 3×3 zone editing; volume-key reading; long-tap; a global gamepad while the app is unfocused; making Quick Open (Ctrl+P) work inside the reader;
reader profiles ("Apply profile" arrives with F2).

## 1. Pointer-button policy

A pure `PointerButtonPolicy` decides, from the press's `PointerUpdateKind` and pointer type, what the press may do: **primary** (left mouse, touch, pen) may
drag-pan, double-click zoom and use tap zones; **right** does nothing here (it only opens the context menu); **middle** does nothing; **X1/X2** are page turns
(section 2). `PageCanvas.OnPointerPressed` and its release/move counterparts consult it before any action. Continuous mode gains **click detection**: a press
that is released within a small movement threshold (6 px) and a short time (400 ms) counts as a tap for tap zones; anything else stays a drag.
Verified by pure tests (the bug itself cannot be shown without running the app).

## 2. Reading-order page commands, media keys, clickers, extra mouse buttons

- Two new remappable commands, `Reader.NextPage` and `Reader.PreviousPage`, are **reading-order** (they call the VM's `NextPage`/`PreviousPage`, which
  already exist), unlike the spatial `PageTurnLeft/Right`. Defaults: next = `PageDown`, `Space`, `MediaNextTrack`; previous = `PageUp`, `Shift+Space`,
  `MediaPreviousTrack`. Paged mode only (`ConflictContext.Paged`); continuous mode keeps PageUp/PageDown as scroll steps. A presentation clicker (which sends
  PageUp/PageDown, sometimes F5/B/period) therefore works with no setup.
- `KeyboardCommandDescriptor` gains optional `AdditionalDefaults` so one command can ship several default gestures; `KeyBindingService.GetKeys` returns
  `[DefaultGesture, ..AdditionalDefaults]` when the user has no rows. The key picker (`KeyOption`) gains the media keys.
- `PageCanvas` gets the usual trio for each command (`NextPageGesture`/`NextPageCommand`, `PreviousPageGesture`/`PreviousPageCommand`), VM key properties and
  `ReaderScreen.axaml` bindings, like `Reader.JumpBack`.
- **Extra mouse buttons:** `XButton1` = previous page, `XButton2` = next page, paged mode; in continuous mode the same scroll one screen (section 4).
  Fixed defaults with one setting, `ExtraMouseButtonsTurnPages` (default on). Consistent with CE, which binds buttons 4/5 to back/forward.

## 3. Gamepad (XInput)

- `Services/Input/XInputInterop.cs` (P/Invoke `XInputGetState`, `OperatingSystem.IsWindows()` guarded, tries `xinput1_4.dll` then `xinput9_1_0.dll`) and
  `GamepadPoller` (a 60 Hz `DispatcherTimer` that runs **only while the reader is the visible screen and the window is active**; it probes the four user
  slots every 2 s and polls only the connected one so a missing controller costs nothing).
- A pure `GamepadMapper` turns successive `XInputState` snapshots into reader actions: button edges (with 400 ms initial / 90 ms repeat for held turn buttons),
  a 25 % stick dead zone, and analogue values for scroll and zoom. Defaults (to be tuned on the user's Xbox controller): D-pad and left stick left/right =
  spatial page turn (or pan when zoomed), D-pad up/down = pan or scroll; **A** next page, **B** previous page, **RB/LB** next/previous page, **Y** toggle
  chrome, **X** fullscreen, **Start** command palette, **Back** leave the reader; **right stick** pans or scrolls, **triggers** zoom in/out.
- One setting, `GamepadEnabled` (default on). Not remappable in F1. PlayStation and Switch pads work only when they present as XInput (Steam Input, DS4Windows).
- The poller raises reader commands through the same command objects the keyboard uses (`NextPage`, `GoLeft`, `ToggleChrome`, `ToggleFullscreen`, ...), so RTL,
  page skipping and the jump-back chip behave identically. It never runs while the palette or another modal is open.

## 4. Tap-zone layouts (Mihon-style)

- **Model.** `TapZoneLayout` = `Default`, `LShaped`, `Kindlish`, `Edge`, `RightAndLeft`, `Disabled`, with the region tables above. `TapAction` = `Previous`, `Next`,
  `Left`, `Right`, `Menu`, `None`. `TapZoneInvert` = `None`, `Horizontal`, `Vertical`, `Both`. A pure `TapZoneResolver.Resolve(point, size, layout, invert,
  input, readingDirection)` returns the action (unit-tested per layout, per cell, per invert mode).
- **`Default` preserves today's behaviour exactly:** for touch it is the three columns with the middle third toggling chrome; for **mouse** it is the two
  halves. Any other layout applies to touch, pen **and mouse** alike (the user needs zones for mouse clicks). Uncovered ("Menu") area toggles chrome for touch and
  pen; for mouse it does nothing, so a stray click cannot hide the toolbar.
- **Reading direction.** `Previous`/`Next` are reading-order: the resolver mirrors the layout horizontally for right-to-left reading (paged and
  continuous) before applying the manual invert, then the canvas invokes the new reading-order commands. `Left`/`Right` (RightAndLeft) are spatial and execute the
  existing spatial commands, which the VM already flips for RTL, exactly as Mihon's direction-independent layout does. In vertical paged mode `Previous`/`Next` use
  the same rectangles unmirrored.
- **Two independent settings groups:** paged (`PagedTapZoneLayout`, `PagedTapZoneInvert`) and continuous (`ContinuousTapZoneLayout`, `ContinuousTapZoneInvert`,
  default `Disabled` = today's behaviour, no zones). In continuous mode `Previous`/`Next` scroll about 90 % of the viewport along the main axis (through the
  existing scroll paths); `Menu` toggles chrome.
- **Mouse toggle:** `TapZonesForMouse` (default **on**): when off, the mouse keeps the plain two halves whatever the layout.
- **Overlay.** A `TapZoneOverlay` control draws the layout's regions coloured by action (previous / next / menu / left / right) with the layout name. It is used in
  three places: a live preview in Preferences → Reader (a small canvas that re-draws when the layout, invert or mode changes), a palette command "Show tap zones"
  that flashes it over the page for about 3 seconds, and the same flash once whenever the layout is changed. No first-run flash.
- **Hint for one-handed use:** `RightAndLeft`, `LShaped`, `Kindlish` and `Edge` already give large thumb-reachable regions; no separate one-handed mode.

## 5. Reader command palette (#19)

- **Trigger:** `Ctrl+K` and a new remappable `Reader.CommandPalette`; `Ctrl+G` (`Reader.GoToPage`, remappable) opens the same palette in go-to-page mode. Quick Open (Ctrl+P)
  stays inert in the reader.
- **UI:** an in-canvas overlay in `ReaderScreen.axaml` (not a Popup: `OverlayPopups = true`, the digit and letter keys must not reach `PageCanvas`), like the bad-page
  picker: a text box, a ranked list, Up/Down/Enter/Esc handled by a tunnel key handler in the code-behind, focus handed back to `PageCanvas` when it closes.
  List changes that remove or replace rows run deferred one dispatcher tick (CLAUDE.md routed-event rule).
- **Entries** (`ReaderPaletteCatalog`, pure): every remappable reader command (from `KeyboardCommandRegistry`, showing its current shortcut), the reading actions
  (toggle reading direction, set reading mode, toggle double-page, fit modes, auto-rotate, page transition style), "Show tap zones", "Rate this issue…" (opens
  Quick Rate), "Report a bad page", "Save page as…", and **Go to page N**: typing a bare number (or `page 40`, `p40`) yields a single "Go to page 40 of 120" entry that
  jumps through `JumpToPage`, so a jump of more than 5 pages shows the "Back to page N" chip from slice A. Matching and ranking reuse `QuickOpenMatcher.Score`.
- **State:** the palette opens with the reader still showing behind it; choosing an entry runs its command after the overlay closes (deferred one tick).

## Settings and migration

One migration `AddReaderInputSettings` with a **no-op `Down()`** and `HasDefaultValue` for every column: `PagedTapZoneLayout` (string, `Default`),
`PagedTapZoneInvert` (`None`), `ContinuousTapZoneLayout` (`Disabled`), `ContinuousTapZoneInvert` (`None`), `TapZonesForMouse` (true),
`ExtraMouseButtonsTurnPages` (true), `GamepadEnabled` (true). Preferences → Reader gets a new "TAP ZONES & INPUT" group (`Tag="reader.tapZones"` plus its
`PreferenceIndex` entry): two layout pickers (`SuggestBox IsStrict`), two invert pickers, the mouse toggle, the extra-mouse-buttons toggle, the gamepad toggle and
the live preview. Layout and invert changes raise the existing `ReaderDisplaySettingsChanged` so an open reader picks them up without a reload.

## Build order

1. Pointer-button policy and click detection (pure, then wired), with tests.
2. Reading-order commands + additional defaults + media keys + clicker keys + extra mouse buttons.
3. Tap-zone model, resolver, settings + migration, Preferences group, canvas wiring for paged mode, then continuous mode, then the overlay.
4. Command palette and go-to-page.
5. Gamepad (interop, mapper, poller, settings).
6. Docs, wiki, memory, review checklist.

Each step is built, tested and verified before the next; the user checks on screen after steps 3, 4 and 5 (mouse clicks with a non-default layout, the palette, the Xbox
controller) and reports what needs tuning.

## Testing

- **Pure:** `PointerButtonPolicy`; `TapZoneResolver` for all six layouts on every grid cell, both inputs, RTL mirroring, vertical paged mode and all four invert
  modes; `GamepadMapper` (edges, repeat timing, dead zone, trigger zoom, slot changes) with hand-built state snapshots; `ReaderPaletteCatalog` (entries, page-number
  parsing, ranking).
- **Service / view model:** `KeyBindingService` returns the additional defaults and honours user rows; the registry has no duplicate default gesture within a context;
  `ReaderScreenViewModel` go-to-page uses the jump path and shows the chip; the Preferences persistence tests for every new setting and a migration test.
- **Not testable here:** actual delivery of media keys, X buttons, the controller and the real click feel. These are the user's on-screen checks; a debug line in the
  Ctrl+Shift+P overlay shows the last input event received (kind, button/key) to make device problems diagnosable.
- Headless tests assert state synchronously; timer behaviour goes through `internal void On…Tick` seams (the poller exposes `Poll()`); `Get-Process testhost` is checked
  before each run and another session's host is never killed. No FlaUI scripting without asking.

## Open risks

- **Media keys may never reach the control** (Windows can route them to the media session). If the user's test shows that, the fallback is a low-level keyboard hook,
  which is a separate decision, not part of F1.
- **`Default` for mouse differs from touch** by design; it must stay that way or existing users' mouse behaviour changes.
- **Extra default gestures** (`Space`, `PageDown`) must not collide with continuous-mode scroll keys or with palette/text-box focus; the paged-only context and the
  overlay's key swallowing cover this, and a registry test asserts it.
- **Gamepad polling on a machine with no controller** must cost nothing; the 2 s slot probe and the visible-and-active gate are the guard.
- **Shared working tree:** `PageCanvas.cs`, `ReaderScreenViewModel.cs`, `ReaderScreen.axaml`, `KeyboardCommandRegistry.cs`, `AppSettings.cs` and the model snapshot are
  already modified by earlier slices; edits stay narrow and every new migration's `Up()` is read before it is kept.

## Implementation notes (2026-09-25)

Built in the order given under "Build order" (plan: `2026-09-25-comic-reader-slice-f-plan.md`). Where the code differs from the design:

- **Reading-order page turns do not use a command per key.** `PageCanvas` expresses a reading-order turn spatially (`ExecuteReadingOrderTurn`: forward = spatial forward, flipped when the new bindable
  `SpatialTurnsFlipped` is set, i.e. right-to-left reading with the reversal setting on) so it shares `ExecuteTurn`'s part stepping and transition animation. The trio is therefore a gesture list
  per command (`NextPageGesture`, `PreviousPageGesture`) with no `ICommand`. The new `ConflictContext.Paged` (both paged states) and `ConflictContexts.MayOverlap` make the Preferences
  conflict check and a registry-wide "no two overlapping commands share a default" test correct.
- **Enums are public** (`PointerRole`, `TapAction`, `TapInput`) so the public test classes can take them as parameters; the resolver and policy stay internal.
- **Pen counts as touch** for tap zones (before, only touch did; a pen behaved like a mouse). With the default layout nothing else changes.
- **The tap zone flash on a layout change** only runs for a profile switch (F2): a Preferences change happens while the reader is hidden, where the live preview is the feedback. The palette's
  "Show tap zones" always flashes. Continuous mode has a known interaction: a named layout plus a double tap scrolls twice and then zooms (`OnDoubleTapped`), so layouts are for readers who do not double-tap.
- **`TapZoneOverlay` samples the resolver** on a 36x36 grid and merges equal runs, so the preview and the flash show exactly what a tap does, including RTL mirroring and invert.
- **Palette state lives in `ReaderCommandPaletteViewModel`** (rows, selection, query, go-to-page), not in the reader view model; `ReaderScreenViewModel.Palette.cs` builds the entries by hand from the reader's
  commands (about 45, with current shortcuts) rather than deriving them from the whole registry, and adds the tap zone flash. Ctrl+K while open closes it; a game controller drives it too (D-pad up/down, A runs, B closes).
- **Gamepad:** `GamepadMapper` (pure), `GamepadPoller` (60 Hz timer, 2 s slot probe, `IGamepadSource` seam) and `XInputSource` (P/Invoke). Analogue pan, scroll and zoom are `PageCanvas.GamepadAnalog` /
  `GamepadZoom`; D-pad and left stick reuse arrow-key semantics (`GamepadDirection`). Avalonia 12 raises no public notification when a control's `IsEffectivelyVisible` changes, so the screen runs a
  500 ms supervisor timer that starts and stops the poller (reader visible, window active, setting on).
- **The Ctrl+Shift+P overlay gains a "last input" line** (`ReaderPerfStats.RecordInput`) for media keys, mouse side buttons and gamepad buttons, so a device that never arrives is diagnosable.
- **Migration** `AddReaderInputSettings` (no-op `Down()`); the seven settings and their Preferences group `reader.tapZones` are as designed.
- **Not verified:** media keys reaching the control, the mouse side buttons, the controller and the feel of every layout. Those are on-screen checks; nothing here was viewed or felt.
