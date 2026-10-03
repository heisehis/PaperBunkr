# Input Service — Design

One application-wide `IInputService` replaces the scattered keyboard/mouse/gamepad handling. Every
physical input (key, mouse button, wheel direction, gamepad button or axis) resolves, through one
user-editable keymap, to a **semantic `InputAction`**; views and ViewModels only ever see actions.

Scoped and sequenced via a `/grilling` pass (rounds 1–2, 2026-10-03). **Status: both phases built (2026-10-03);** §13 records where the
implementation differs from or goes beyond what is written below, and §9 is rewritten to describe what was actually migrated. Two phases:

- **Phase 1 — the service.** Purely additive: service, config model, default keymap, binding matching,
  gamepad processing, legacy-bindings importer, and a headless test suite. One inert hook in
  `MainWindow`. **No behavior change** in the running app.
- **Phase 2 — the migrations.** Move every hotkey-style handler onto actions, rewrite the Preferences
  editor, then delete the old registry/service. Its per-screen task list is derived in the phase 2
  implementation plan (`writing-plans`), using the boundary rule in §9.

## 1. Goals

1. **Single source of truth.** All default bindings live in one place (`DefaultKeymap`); all user
   changes live in one serializable config (`keymap.json`).
2. **Semantic actions.** ViewModels and views never reference `Key.Right` or `PointerWheelEventArgs`.
3. **Root interception.** Hardware input is intercepted once, at `MainWindow`, with
   `RoutingStrategies.Tunnel`, so shortcuts work regardless of which control has focus.
4. **Focus safety.** Hotkeys are ignored while a text-entry control has focus, with a per-action
   exception list (§5.5).
5. **One gesture space for every device.** Keyboard, mouse buttons, wheel, and gamepad bind the same
   way and are remappable the same way — also the foundation for later controller support.

## 2. Background — what exists today (verified 2026-10-03)

- `KeyboardCommandRegistry` (`Models/`): ~40 remappable *reader* commands (id, group, label, default
  `KeyGesture`, `ConflictContext`). `KeyBindingService` persists user remaps in the `KeyBinding` EF table
  (multi-key per command). Preferences > Keyboard Shortcuts edits them (curated `KeyOptions` list,
  conflict detection, JSON import/export, Reset).
- `MainWindow.OnMainWindowKeyDown`: a Tunnel handler with a hardcoded `if/else` chain — Escape,
  `BrowserBack`, Ctrl+P, Ctrl+`,`, Ctrl+Tab / Ctrl+Shift+Tab, Ctrl+Z / Ctrl+Y, Ctrl+Q, Ctrl+B. Everything after
  the `e.Source is TextBox` early-return is suppressed while typing; Escape, `BrowserBack` and Ctrl+P sit
  *before* it and deliberately fire in text boxes.
- `MainWindow.OnWindowPointerWheelChanged`: horizontal-swipe → Back/Forward (`Delta.X` threshold 1.5).
- ~30 files own further `KeyDown` / wheel / pointer handlers (inventory in §9).
- `PageCanvas` (~3000 lines) matches gestures itself in `OnKeyDown`; Ctrl+wheel zoom is anchored at the cursor
  and scaled by wheel delta; plain wheel feeds a touchpad page-turn accumulator (a multi-flip bug fix) or
  proportional continuous scroll. XButton1/2 turn pages when `AppSettings.ExtraMouseButtonsTurnPages` (default on).
- `LibraryActionCatalog.KeyMap` is a *second* central keymap (Library bulk actions).
- `GamepadMapper`/`GamepadPoller`: fixed Xbox layout; digital buttons with key-repeat (400 ms, then 90 ms),
  analog right stick (pan) and triggers (zoom); polled only while the reader is visible; gated by
  `AppSettings.GamepadEnabled`.
- No DI container: services are constructed by hand (`MainViewModel` is built in `App.axaml.cs`); many tests
  construct `new ReaderScreenViewModel(goBack)` with default constructors.

## 3. CE parity (`_reference/ComicRackCE`)

- CE already uses **one gesture space for all devices**: `CommandKey` (cYo.Common.Windows) holds keys, mouse
  buttons and doubles (`MouseLeft/Middle/Right`, `MouseButton4/5`, `MouseDouble*`), `MouseWheelUp/Down`,
  horizontal `MouseTiltLeft/Right`, touch taps and flicks, OR'd with `Ctrl/Shift/Alt`. Our `InputBinding`
  mirrors this (minus touch/gesture marks, which Paperbunkr has no source for).
- `Ctrl+MouseWheelUp/Down` = Zoom In/Out (`MainForm.cs`); `MouseButton4/5` = Previous/Next list;
  `Ctrl+F` = FocusQuickSearch; `F5` = View Refresh; `Shift+F6` = toggle sidebar; `Ctrl+Q` = Exit.
  These are the defaults for the matching actions below.
- CE binds **plain wheel** as ordinary commands too (`MoveUp = Up | MouseWheelUp`), carrying a magnitude
  (`scrollLines = |delta| × MouseWheelSpeed`). **Deliberate deviation:** the *model* supports unmodified wheel
  bindings, but phase 2 keeps plain-wheel scrolling/page-turn inside `PageCanvas`/scroll viewers (accumulator,
  proportional scroll, cursor-anchored pan). Revisit later by adding default wheel bindings — no model change.
- CE's per-command binding cap is 4; Paperbunkr already dropped it (multi-binding, spec 2026-09-07). Kept.

## 4. Decisions (from the grilling pass)

| # | Decision |
|---|---|
| Q1 | **Replace** `KeyboardCommandRegistry`, `KeyBindingService`, the shell key chain, and (in phase 2) the Preferences editor's data layer. Replace, not extend. |
| Q2 | `InputAction` covers every existing command plus the new ones; each action's id equals the old registry id where one existed. **Revised after review:** a string-id struct, not a closed enum (§5.1). |
| Q3 | Two phases (above). |
| Q4 | Event args carry `Handled`; claim-based `Register`. **Revised after review:** no scope stack — scope activation is a registration lifetime (§5.2). |
| Q5 | Only *modified* wheel gestures and horizontal tilt are default actions; plain wheel stays in the reader/scroll viewers. |
| Q6 | XButton1/2 → `NavigateBack`/`NavigateForward` (CE parity); in the reader they turn pages while `ExtraMouseButtonsTurnPages` is on. |
| Q7 | Per-action `FiresInTextInput`; focus checked on the focused element, not `e.Source`. **Revised after review:** suppression is an `IInputSuppressor` interface with `TextEntry`/`All` levels (§5.5). |
| Q8 | Mouse/wheel bindings are remappable — in the model, matching and persistence in phase 1; Preferences capture UI in phase 2. |
| Q9 | Gamepad feeds the same service: digital buttons + analog axes; service owns edge/repeat; live poller migrates in phase 2. |
| Q10 | Phase 1 is inert/additive (§1). |
| Q11 | `keymap.json` stores overrides only; legacy DB rows imported once in phase 2; **DB table not dropped** (worktrees share the dev DB). |
| Q12 | Own `InputBinding` type (key, mouse, wheel, gamepad); capture-based editing replaces the curated key list in phase 2. |
| Q13 | Gamepad: buttons → actions, sticks/triggers → axis actions with `Value`; dead zones/repeat timings become config with today's numbers. |
| Q14 | `IDisposable Register(scope, handler, context?)` — one call both activates the scope and attaches the handler; `PageCanvas` registers for view-state actions, the reader ViewModel for the rest. |
| Q15 | Shell shortcuts become named, remappable actions. |

## 5. Architecture (phase 1)

All new code lives under `src/Paperbunkr.App/Services/Input/` (next to the existing gamepad files).
Pure logic (no Avalonia types) is kept separate from the thin Avalonia adapters so most tests run without a UI.

### 5.1 `InputAction` and its metadata

An action is a **stable string id wrapped in a struct** — not a closed enum — so plugins (and any later
feature) can mint actions at runtime without recompiling core. Core actions are `const` ids, so handlers
still `switch` on them and a typo in core code is a compile error.

```csharp
public readonly record struct InputAction(string Id)
{
    public static implicit operator InputAction(string id) => new(id);
    public override string ToString() => Id;
}

public static class InputActionIds          // core actions: one const per action (grouped below)
{
    public const string NextPage = "Reader.NextPage";
    public const string ZoomIn   = "Reader.ZoomIn";
    public const string NavigateBack = "App.NavigateBack";
    // …
}

public enum InputActionKind { Button, Axis }

public sealed record InputActionInfo(
    InputAction Action,                       // its Id is the persisted key, e.g. "Reader.ZoomIn"
    string Group, string Label,               // Preferences display
    InputScope Scope,
    InputContext Context,                     // reader sub-state(s) it is reachable in
    IReadOnlyList<InputBinding> Defaults,     // out-of-the-box bindings (the "default keymap", per action)
    InputActionKind Kind = InputActionKind.Button,
    bool FiresInTextInput = false,
    IReadOnlyList<string>? FormerIds = null); // old ids this action replaces; saved overrides are re-keyed on load

public interface IInputActionCatalog
{
    IReadOnlyList<InputActionInfo> All { get; }
    InputActionInfo? Find(InputAction action);
    void Register(InputActionInfo info);      // plugins / later features; rejects a duplicate Id
}
```

`InputActions.Core` is the central table of built-in actions (replaces `KeyboardCommandRegistry.Commands`); it
seeds the catalog, and the "default keymap" is simply the union of every `InputActionInfo.Defaults`. Built-in
ids keep the old registry ids where one existed (`Reader.ZoomIn`, …), so stored bindings stay valid. Groups:

1. **Reader — navigation:** `NextPage`, `PreviousPage`, `FirstPage`, `LastPage`, `PageTurnLeft`, `PageTurnRight`
   (spatial; kept distinct from reading-order `NextPage`/`PreviousPage` because of RTL), `PanLeft/Right/Up/Down`,
   `ScrollLeft/Right/Up/Down/PageUp/PageDown/ToStart/ToEnd`, `ToggleAutoScroll`, `PreviousBookmark`, `NextBookmark`,
   `JumpBack`, `GoToPage`.
2. **Reader — view:** `ZoomIn`, `ZoomOut`, `ResetZoom`, `RotateClockwise`, `RotateCounterClockwise`,
   `ToggleDoublePageMode`, `ToggleReadingDirection`, the five `Fit*` actions, `ToggleGuidedView`.
3. **Reader — tools:** `CommandPalette`, `ToggleInfoPanel`, `PinPage`, `ClipRegion`, `CopyPage`, `ReportBadPage`,
   `NextProfile`, `ToggleSessionHud`, `ToggleWarmShift`, `ToggleChrome` (the reader toolbar; today only the
   gamepad `Y` button reaches it, so it ships with just that default).
4. **Reader — fullscreen:** `ToggleFullscreen` (Reader scope; defaults `F`, `F11`, matching today).
5. **Global & layout:** `ToggleSidebar`, `OpenSettings`, `CloseCurrentView`, `FocusSearch`, `OpenQuickOpen`,
   `Undo`, `Redo`, `Quit`, `CycleScreenForward`, `CycleScreenBackward`, `ToggleLibraryPreview`.
6. **Library & navigation:** `NavigateBack`, `NavigateForward`, `RefreshLibrary`.
7. **Axes (gamepad):** `PanHorizontal`, `PanVertical`, `ZoomAxis` (`Kind = Axis`).

`ToggleDoublePageMode` and `ToggleReadingDirection` are new (no existing command); they ship **unbound** in
phase 1 until the reader has a handler (phase 2 wires them to the existing page-layout and RTL settings).

### 5.2 Scopes and contexts

- `InputScope` — `readonly record struct InputScope(string Name, int Priority, bool IsModal = false)`: an open set of
  named scopes (`Global` priority 0, always active; `Reader`/`Library`/… screen scopes at 100; `Overlay` at 200 and
  modal). **There is no stack.** A scope is *active* exactly while it has at least one live registration
  (`Register`, §5.5); active scopes are consulted highest `Priority` first (ties: newest registration first). If any
  active scope is `IsModal`, only the highest modal scope and `Global` are consulted, so an open overlay cannot be
  driven by the screen behind it, while `CloseCurrentView` (Global) still works. Because activation is a
  registration lifetime, disposing in any order — or twice, or never in a given order — cannot orphan or reorder
  another scope.
- **Why not derive the active scope from the focused element** (considered in review): goal 3 is that shortcuts work
  *regardless of focus*, and focus is routinely null or in the wrong place (after a click on a non-focusable
  area, a removed control, a popup closing — the long keyboard-focus-reclaim arc exists because of this). A
  focus-derived scope would silently disable hotkeys in exactly those moments. Scope follows the *screen's
  lifetime* (its handlers are registered while attached), never focus.
- `InputContext` ([Flags]: `Always`, `PagedUnzoomed`, `PagedZoomed`, `Continuous`, `Paged`) generalizes today's
  `ConflictContext`. A scope may supply a `Func<InputContext>` reporting its current state; an action is a
  candidate only if its context overlaps the scope's current context. This preserves today's behavior where
  `Right` is `PageTurnRight` (unzoomed), `PanRight` (zoomed) or `ScrollRight` (continuous) — per-context action
  variants are **kept**, not collapsed.
- Conflict rule (carried over): two actions sharing a binding conflict only if their scopes can be active
  together and their contexts can overlap (`Always` overlaps everything) — `InputKeymap.FindConflicts`.

### 5.3 `InputBinding` and the config model

`InputBinding` (immutable `readonly record struct`) is one of:

| Device | Fields | Text form |
|---|---|---|
| Keyboard | `Key`, `KeyModifiers` | `Ctrl+Shift+K`, `PageDown`, `BrowserBack` |
| Mouse button | `MouseButton`, modifiers, `Clicks` (1 or 2) | `Ctrl+Mouse4`, `DoubleLeft` (`Mouse4/5` = XButton1/2) |
| Wheel | direction (`Up`/`Down`/`Left`/`Right`), modifiers | `Ctrl+WheelUp`, `WheelLeft` |
| Gamepad | button, or axis (`LeftStickX/Y`, `RightStickX/Y`, `Triggers` = right − left) | `Pad:A`, `Pad:DPadLeft`, `Pad:RightStickX`, `Pad:Triggers` |

Text form is parsed/formatted by our own parser, **not** `KeyGesture.Parse` (it cannot express wheel/mouse/pad,
and digit-key parsing is a known trap in this codebase).

```csharp
public sealed class KeymapConfig
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, List<string>> Overrides { get; set; } = new();   // actionId -> binding text list
    public InputTuning Tuning { get; set; } = new();
    public bool LegacyBindingsImported { get; set; }                             // set by the phase 2 importer
}

public sealed class InputTuning   // today's numbers become the defaults
{
    public double SwipeThreshold { get; set; } = 1.5;          // horizontal tilt → WheelLeft/Right
    public double StickDeadZone { get; set; } = 0.25;
    public double StickDigitalThreshold { get; set; } = 0.5;
    public double TriggerDeadZone { get; set; } = 0.12;
    public double RepeatInitialMs { get; set; } = 400;
    public double RepeatIntervalMs { get; set; } = 90;
}
```

- **Overrides only.** `DefaultKeymap` (code) is the base; an entry in `Overrides` *replaces* that action's whole
  binding list (an empty list = explicitly unbound). New defaults in later releases still reach users for
  every action they have not customized.
- `IKeymapStore` (`Load()` / `Save(KeymapConfig)`); `JsonKeymapStore` writes `keymap.json` in the same folder
  as the database (`Path.GetDirectoryName(PaperbunkrDbContext.GetDefaultDatabasePath())`, so
  `PAPERBUNKR_DB_PATH` overrides apply), via write-to-temp-then-replace. A corrupt or newer-version file is
  ignored with a logged warning and defaults are used; the bad file is renamed `keymap.json.bad`, never overwritten silently.
- `InputKeymap` — the resolved, queryable keymap: `GetBindings(action)`, `Resolve(binding) → candidate actions`
  (index rebuilt on change), `FindConflicts(action, binding)`, `Set/Reset/ResetAll`, `Changed` event.

### 5.4 Default keymap

Existing reader/library defaults carry over **verbatim** from `KeyboardCommandRegistry` (including the extra
clicker/media/Space defaults on `NextPage`/`PreviousPage`). New or changed defaults:

| Action | Default bindings | Basis |
|---|---|---|
| `ZoomIn` / `ZoomOut` | existing `Z` / `Shift+Z` **plus** `Ctrl+WheelUp` / `Ctrl+WheelDown` | CE `MainForm.cs`; spec §3 |
| `NavigateBack` | `BrowserBack`, `Mouse4`, `WheelLeft` | today's `BrowserBack` + swipe; CE MouseButton4 |
| `NavigateForward` | `BrowserForward`, `Mouse5`, `WheelRight` | CE MouseButton5; swipe |
| `PreviousPage` / `NextPage` (Reader scope) | existing + `Mouse4` / `Mouse5` | claimed only while `ExtraMouseButtonsTurnPages`; otherwise falls through to `NavigateBack/Forward` |
| `FocusSearch` | `Ctrl+F`, `/` | CE FocusQuickSearch; Library's existing `/` |
| `RefreshLibrary` | `F5` | CE View Refresh |
| `ToggleSidebar` | `Shift+F6` | CE miSidebar |
| `CloseCurrentView` | `Escape` | today |
| `OpenSettings` | `Ctrl+,` | today |
| `OpenQuickOpen`, `Undo`, `Redo`, `Quit`, `CycleScreen*`, `ToggleLibraryPreview` | `Ctrl+P`, `Ctrl+Z`, `Ctrl+Y`, `Ctrl+Q`, `Ctrl+Tab`/`Ctrl+Shift+Tab`, `Ctrl+B` | today |
| `FirstPage` / `LastPage` | `Home` / `End` (paged scope) | brief; continuous keeps `ScrollToStart/End` |
| `PanHorizontal` / `PanVertical` / `ZoomAxis` | `Pad:RightStickX` / `Pad:RightStickY` / `Pad:Triggers` | today's `GamepadMapper` |
| Pad buttons | `A`/`RightShoulder` → `NextPage`; `B`/`LeftShoulder` → `PreviousPage`; D-pad and left-stick directions bound to the same per-context variants as the arrow keys (`PageTurnLeft`/`PanLeft`/`ScrollLeft`, … — context decides which fires, exactly as the reader decides today); `Y` → `ToggleChrome`; `X` → `ToggleFullscreen`; `Start` → `CommandPalette`; `Back` → `CloseCurrentView` | today's `GamepadMapper` |

`FirstPage`/`LastPage` are new (no existing reader command) and are given handlers in phase 2.

### 5.5 `IInputService`

```csharp
public interface IInputService
{
    // Raised after an action resolves, for observers (Handled reflects the final claim state).
    event EventHandler<InputActionEventArgs>? ActionTriggered;
    event EventHandler? BindingsChanged;

    // Avalonia entry points — MainWindow forwards its Tunnel events here. Each sets e.Handled when claimed.
    bool ProcessKeyDown(KeyEventArgs e);
    bool ProcessPointerWheel(PointerWheelEventArgs e);
    bool ProcessPointerPressed(PointerPressedEventArgs e);

    // Gamepad entry point (poller or tests).
    bool ProcessGamepad(GamepadState state, TimeSpan elapsed);

    // Programmatic dispatch (UI buttons, command palette, plugins, future devices).
    bool Dispatch(InputAction action, InputPayload payload = default);

    // ONE call activates the scope AND attaches the handler; disposing it removes both. A scope is active
    // while it has >= 1 live registration (§5.2). `context` reports the scope's current InputContext.
    IDisposable Register(InputScope scope, Action<InputActionEventArgs> handler, Func<InputContext>? context = null);

    // The action catalog (core + plugin-registered actions) and its resolved keymap.
    IInputActionCatalog Actions { get; }

    // Dynamic binding management (Preferences, import/export).
    IReadOnlyList<InputBinding> GetBindings(InputAction action);
    void SetBindings(InputAction action, IReadOnlyList<InputBinding> bindings);
    void ResetBindings(InputAction action);
    void ResetAll();
    IReadOnlyList<InputConflict> FindConflicts(InputAction action, InputBinding binding);
}

public sealed class InputActionEventArgs : EventArgs
{
    public InputAction Action { get; }
    public InputDevice Device { get; }       // Keyboard | Mouse | Gamepad | Programmatic
    public double Value { get; }             // axis value -1..1; 1 for buttons
    public Vector WheelDelta { get; }        // wheel/tilt only
    public KeyModifiers Modifiers { get; }
    public bool Handled { get; set; }

    // Pointer-derived actions only (mouse button, wheel/tilt): the cursor position in `relativeTo`'s own
    // coordinate space, e.g. a PageCanvas passes `this` and gets the zoom anchor in canvas coordinates. The
    // adapter wraps the raw event's `GetPosition(visual)`; null for keyboard, gamepad and programmatic
    // dispatch. Valid only during the synchronous handler call — a handler that defers work with
    // Dispatcher.UIThread.Post must read the position first.
    public Point? GetPosition(Visual relativeTo);
}
```

The raw `PointerEventArgs` is deliberately **not** exposed (ViewModels must not see Avalonia pointer types);
only the position-resolving delegate crosses the boundary. The core resolution logic takes a
`Func<Visual, Point>?` from the thin Avalonia adapter, so tests supply a fake without a window.

**Resolution** (`ProcessKeyDown` and siblings): build the `InputBinding` → text-entry check (below) → look up
candidate actions → filter by active scopes (highest priority first, `Global` last; modal rule per §5.2) and by
each scope's current `InputContext` → for each candidate, invoke registered handlers of its scope (newest registration first) until one
sets `Handled` → raise `ActionTriggered` → set `e.Handled`/return `true` only if claimed. An action with no
claiming handler leaves the event **unhandled**, so keys the app does not use (and arrow-key focus navigation)
keep working. Handlers run synchronously on the UI thread; a handler that closes a popup or mutates the
collection backing the clicking control must defer with `Dispatcher.UIThread.Post` (CLAUDE.md's detach-inside-event
gotcha).

**Input suppression (the text-entry rule).** Controls opt out of hotkeys through an interface, not a hardcoded
type list:

```csharp
public enum InputSuppression { TextEntry, All }

public interface IInputSuppressor          // implemented by any control that wants raw keys
{
    InputSuppression Suppression { get; }
}
```

- `TextEntry` — ordinary typing: keyboard actions are ignored unless the action's `FiresInTextInput` is set (exactly
  today's set: `CloseCurrentView`, `NavigateBack` via the `BrowserBack` key, `OpenQuickOpen`).
- `All` — the control must see every key, including Escape and `FiresInTextInput` actions. Needed by the Preferences
  "press your combination" capture box (phase 2) and any custom canvas that edits text.
- `IInputSuppressionProbe.GetActive(TopLevel)` inspects the **focused element** (`TopLevel.FocusManager`, never
  `e.Source`) and its visual ancestors and returns the nearest suppressor's level. Avalonia's own text controls
  (`TextBox`, `AutoCompleteBox`, `NumericUpDown`, and anything inside a `SuggestBox`) can't implement our
  interface, so the default probe treats them as `TextEntry` — this is the **only** place concrete control types
  are named; every Paperbunkr custom control opts in via the interface. Tests use a stub probe.
- Pointer, wheel and gamepad events are not subject to suppression.

**Gamepad.** `ProcessGamepad` runs the edge detection and key-repeat that `GamepadMapper` does today (its
`Repeater` moves into the service, parameterized by `InputTuning`), turns buttons and stick-as-D-pad into
`Pad:*` bindings that resolve like any other binding, and emits axis actions (`Value` in −1..1, after dead zone)
each poll while non-zero. `GamepadMapper`/`GamepadPoller`/`XInputSource` stay in place for phase 1 and keep
driving the reader; in phase 2 the service takes over the poller (running while the main window is active and
`GamepadEnabled`) and `GamepadMapper` is deleted.

### 5.6 Window wiring

`InputHost.Attach(topLevel, service)` adds the three `RoutingStrategies.Tunnel` handlers (key down, wheel, pointer pressed) to a
window and forwards each event to the service when it is not already handled:

```csharp
topLevel.AddHandler(InputElement.KeyDownEvent,           OnKeyDown, RoutingStrategies.Tunnel);   // → service.ProcessKeyDown(e)
topLevel.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);   // → service.ProcessPointerWheel(e)
topLevel.AddHandler(InputElement.PointerPressedEvent,    OnPressed, RoutingStrategies.Tunnel);   // → service.ProcessPointerPressed(e)
```

`MainWindow` calls it once in its constructor (and registers its Global-scope handler for the shell actions: close, back/forward, quick
open, settings, screen cycling, undo/redo, quit, sidebar). The headless test hosts call the same helper, so the app and the tests cannot
drift apart. `App.axaml.cs` constructs the single `InputService` by hand once the database is ready (there is no DI container), imports
the legacy bindings once, and publishes it as `InputServiceLocator.Current`; view models and views take the service through an optional
constructor/property that falls back to the locator, which is the do-nothing `NullInputService` until startup sets it — so the many tests
that call `new ReaderScreenViewModel(goBack)` keep compiling and behave as before.

## 6. Usage pattern

A screen registers with `AttachedInputRegistration`, which keeps one `Register` alive while the control is attached, makes it dormant
while the control is hidden (screens here are hidden, not destroyed) and — with `focusRoot` — while focus sits in a dialog or popup
outside the screen:

```csharp
_libraryInput = new AttachedInputRegistration(this, InputScope.Library, OnLibraryInputAction,
                                              service: InputServiceLocator.Current, focusRoot: () => this);

private void OnLibraryInputAction(InputActionEventArgs e)
{
    switch (e.Action.Id)
    {
        case InputActionIds.FocusSearch:        Toolbar.FocusSearchBox(); e.Handled = true; return;
        case InputActionIds.LibrarySelectAll:   vm.SelectAllVisibleCommand.Execute(null); e.Handled = true; return;
        // …
    }
}
```

`PageCanvas` registers the same way for the view-state actions: cursor-anchored zoom uses `e.GetPosition(this)` (canvas coordinates, no
window-transform math) scaled by `e.WheelDelta`; plus pan, scroll, page turns, rotate, fit and the controller's axes. The reader screen
registers a second handler for the actions that need only a view-model command, and a third (the overlay driver) that is dormant except
while the command palette or the bad-page picker is open.

## 7. Error handling

- Unparseable binding text in `keymap.json`: that binding is dropped with a logged warning; the action keeps
  its remaining bindings (or its default if none parse). One bad entry never discards the file.
- Renamed action: when a catalog entry lists the old id in `FormerIds`, an override saved under the old id is
  **re-keyed to the new id on load** (and saved that way), so renames never drop a user's binding. If both the old
  and new id carry overrides, the new id's wins.
- Schema changes: `SchemaVersion` increments are handled by an ordered chain of `IKeymapMigration` steps (v1 → v2 → …)
  run on load; a file *newer* than the app understands is treated as corrupt (below), never partially read.
- Truly unknown action id (removed action, or a plugin that is not installed): ignored but **preserved** on the
  next save, so uninstalling and reinstalling a plugin, or a downgrade/upgrade round-trip, loses nothing.
- Handler exceptions propagate to the caller as today (no swallowing); the service holds no locks across handlers.
- `Register` disposables are idempotent; disposing in any order is safe (no stack to corrupt, §5.2).

## 8. Testing (phase 1)

New `InputServiceTests` etc. in `Paperbunkr.App.Tests`, headless (no real window):

- **Binding parse/format round-trip** for every device and modifier combination, including `BrowserBack`,
  `Mouse4`, `Ctrl+WheelUp`, `Pad:RightStickX`; malformed text rejected.
- **Keymap:** defaults load; override replaces a whole action's list; empty override unbinds; reset; conflict
  detection incl. the `Always`/scope-overlap rules (port of today's `ConflictContexts` tests); unknown ids
  preserved; corrupt/newer file falls back and is renamed.
- **Resolution:** highest-priority scope wins; unclaimed action leaves the event unhandled; claim stops propagation;
  context filtering (`Right` → `PageTurnRight` vs `PanRight` vs `ScrollRight`); `Register` disposal stops
  delivery; `ActionTriggered` raised once per resolution.
- **Scope lifetime:** a scope is active only while it has a live registration; registering A, B, C and disposing
  B, then A, then C (and a double dispose) leaves routing correct at every step; an active modal scope hides
  lower non-global scopes but not `Global`.
- **Dynamic actions:** a catalog-registered plugin action with a default binding resolves, remaps and
  round-trips through `keymap.json` like a core one; duplicate `Id` registration is rejected; an override saved
  under a `FormerIds` entry is re-keyed on load; an override for an uninstalled action survives a save.
- **Pointer position:** `GetPosition(visual)` returns the adapter's value for wheel/mouse actions (verified with
  a fake delegate returning different points per visual) and `null` for keyboard/gamepad/programmatic.
- **Suppression:** `TextEntry` suppresses keyboard actions except `FiresInTextInput` ones; `All` suppresses those
  too (Escape included); the nearest `IInputSuppressor` ancestor wins; wheel/pointer/gamepad unaffected.
- **Wheel/pointer:** `Ctrl+WheelUp/Down` → `ZoomIn/Out` with `WheelDelta` and `Position` populated; horizontal
  tilt below `SwipeThreshold` ignored, at/above it → `NavigateBack/Forward`; `Mouse4/5` → navigation, and
  page-turn in the reader only when claimed.
- **Gamepad:** port `GamepadMapper`'s existing edge/repeat/dead-zone tests to `ProcessGamepad` with hand-built
  snapshots; axis actions deliver `Value`; `Pad:*` bindings remap.
- **Legacy importer** (pure function, invoked in phase 2): `KeyBinding` rows → overrides; old
  `{CommandId, Gesture}` JSON accepted.
- **Smoke / integration:** the headless reader tests press real keys through `InputHost` → service → `PageCanvas`/reader screen
  (`ReaderScreenKeyTests`, `ReaderScreenPanelTests`, …); `GamepadTests` drives poller → service end to end.

## 9. Phase 2 — migration scope and boundary rule (as built)

**Rule.** Anything a user would plausibly want to see in a shortcuts list or remap, and that acts on the application or a whole screen, is an
**action** and was migrated. Control-internal behavior stays inside the control: caret and selection keys, arrow navigation among the tiles
or rows of a focused list, Enter/Space to open the focused item, Delete or F2 on the focused row, type-ahead character input,
Enter-to-commit / Escape-to-cancel inside a text box or inline rename, drag/rubber-band pointer mechanics, and `FocusReclaimer` directional
focus moves. Those need the identity of the *focused item*, which a window-level action does not have.

**Migrated:**

| Area | What moved |
|---|---|
| Shell (`MainWindow`) | The Escape / Back / Quick open / Settings / screen-cycle / Undo / Redo / Quit chain and the horizontal-swipe wheel handler → Global actions (`CloseCurrentView`, `NavigateBack/Forward`, …); new `ToggleSidebar` |
| Comic reader | `PageCanvas.OnKeyDown`, all its per-command gesture properties, `ExtraKeyBindings`/`KeyCommandBinding`, the Ctrl+wheel zoom and thumb-button special cases, `ReaderScreen`'s Ctrl+Shift+P chord and gamepad frame handling, the reader view model's ~40 gesture properties |
| PDF reader | Rides the same `PageCanvas` registration (it binds none of the comic-only commands) |
| Book reader | Page-turn keys and Ctrl+Shift+W (Avalonia side) and the JavaScript key forwarding inside the web view (`WebKeyMap`) |
| Library | `/` search focus, Ctrl+B preview, Ctrl+A, Delete, and the whole `LibraryActionCatalog.KeyMap` (now `KeyActions`, with menu/bar hints read live from the service) |
| Compare | Its eight local keys |
| Preferences | The editor is rebuilt on actions (`ShortcutsEditorViewModel`, capture box, layout import/export) |
| Gamepad | `GamepadMapper` and the fixed `GamepadFrame` removed; `GamepadPoller` hands raw state to the service |

**Left as control behavior (by the rule above):** `BooksScreen`, `SmartScreen`, `EventMapView`, `ReadingGalleryView`, `ReadingListPageView`,
`HomeScreen`, `ContinuityOverviewView`, `PosterTile` (arrow/Enter/Delete/F2/type-ahead among their own items); `SuggestBox`,
`AccordionPanel`, `SmoothScrollViewer`, `OverlayShell`, `ContextMenuHost`; the `FocusReclaimer` users; XAML `TextBox.KeyBindings` that only
commit on Enter; the reader's palette and picker internal keys (Up/Down/Enter/Esc, 1–4), which are modal-widget keys.

**Escape.** `CloseCurrentView` is claimed by `MainWindow`, which runs `MainViewModel.Escape()` — its existing ordered overlay-closing chain
unchanged. Replacing that chain with an overlay stack is out of scope.

**Cleanup done:** `KeyboardCommandRegistry`, `KeyBindingService`, `KeyBindingIO`, `KeyBindingRowViewModel`, `KeyOption(s)`,
`KeyCommandBinding`, `PageCanvas.KeyBindings`, `GamepadMapper` and their tests are deleted; the legacy importer runs once at startup. The
`KeyBinding` table is left in place (dead): dropping it is a separate, later migration because worktrees and other checkouts share the
development database.

## 10. Non-goals

- Touch/gesture marks (CE's `Gesture1–9`, `Touch*`, `Flick*`).
- An overlay/dialog stack replacing the `Escape` chain.
- Capturing a *gamepad* input from Preferences (the config, service and editor display and remove pad bindings; adding one by pressing a
  button needs the editor to listen to the poller, a follow-up).
- A service-owned, app-wide gamepad poller. The poller still starts and stops with the reader screen (gamepad settings are read from the
  database when a reader loads); the service already accepts pad state at any time, so app-wide controller navigation is a matter of
  starting the poller elsewhere and adding bindings.
- Default plain-wheel bindings (model supports them; §3).
- A plugin-facing API for contributing actions. The *core* seam exists (`IInputActionCatalog.Register`, string ids);
  exposing it through the Plugin API, its permissions and Preferences presentation is a separate design.
- Dropping the `KeyBinding` table.

## 11. External design review (2026-10-03)

| Finding | Outcome |
|---|---|
| `Position` in window coordinates breaks cursor-anchored zoom | **Accepted.** `GetPosition(Visual relativeTo)` delegate; raw event not exposed (§5.5). |
| Closed enum blocks plugin actions | **Accepted.** String-id `InputAction` struct + `InputActionIds` consts + runtime `IInputActionCatalog` (§5.1). Supersedes the original brief's "enum" wording; removes the enum↔persisted-id mapping. |
| Global LIFO scope stack is fragile | **Accepted in substance, different fix.** The stack is gone; scope = live registration, priority + modal rule (§5.2). |
| Derive active scope from the focused visual tree | **Rejected.** Contradicts goal 3 (works regardless of focus); focus is routinely null/wrong in this app (§5.2). |
| `PushScope` + `Register` desync risk | **Accepted.** Single `Register` (§5.5, §6). |
| Interface-based text probing | **Accepted, extended** with a second level (`All`) for the key-capture box (§5.5). |
| Rename-safe config | **Accepted.** `FormerIds` re-keying + `IKeymapMigration` chain (§7). |

## 12. Open risks

1. **Behavior parity of the Reader** — `PageCanvas.OnKeyDown`'s ordering ("Always" before mode branches)
   must be reproduced by scope/context resolution; mitigated by porting the conflict tests and by doing the
   reader last in phase 2.
2. **Double handling during phase 2** — a screen must not be partly migrated; each task removes the old
   handler in the same step that registers the action handler.
3. **Wheel anchor/accumulator regressions** — mitigated by Q5 (plain wheel stays in the control).

## 13. As built — differences from the design above

1. **Delivery of Global actions.** An action in a screen scope goes to that scope's handlers; a *Global* action goes to the handlers of every
   active scope, highest priority first, so the screen the user is on can refine it (`RefreshLibrary`, `FocusSearch`).
2. **Context is an intersection.** A scope's context is the AND of what every live registration reports (the reader screen reports
   "always", the canvas reports its paged/zoomed/continuous state). `InputContext.None` marks a registration **dormant** — it neither
   activates its scope nor receives anything — which is how a hidden screen stays registered for its life without being asked. Avalonia 12
   raises no notification for `IsEffectivelyVisible`, so visibility is read when input is resolved, never tracked.
3. **Conflicts are same-scope.** Two actions conflict only when they share a scope and their contexts overlap. Sharing a binding across
   scopes is layering (the thumb button turns pages in the reader and navigates back elsewhere), not a conflict.
4. **Focus-containment guard.** `AttachedInputRegistration` can be told a `focusRoot`; it goes dormant while keyboard focus is outside that
   visual. This restores the reach the old per-screen handlers had (they only saw events routed through their own subtree), so Delete or
   Ctrl+A in the Library cannot act while a dialog has focus. No focus at all counts as inside.
5. **`InputSuppression.All` also suppresses pointer and wheel input** (not just keys), so the shortcut capture box receives a thumb-button
   press or wheel turn instead of the service acting on it. Gamepad input is never suppressed.
6. **Event args** also carry `Elapsed` (frame time for gamepad axes) and `Binding` (the physical input that fired it, for diagnostics).
7. **Interface additions:** `ProcessKey(Key, KeyModifiers)` (keys forwarded from an embedded web view), `ResetGamepad()`, `Tuning`.
8. **New actions beyond the first table:** `PageTurnUp`/`PageTurnDown` (vertical paged reading), `LeaveReader` (the gamepad Back button —
   `CloseCurrentView` only closes overlays), `TogglePerfOverlay`, the Library selection actions, `BookReader.*`, `Compare.*`.
9. **Rows can be unbound.** The old editor forbade removing a command's last gesture; an empty override now means "deliberately unbound".
10. **Gamepad edge-case parity.** The controller's D-pad/left stick now drives every direction variant a key does (it used to ignore the
    horizontal axis in vertical paged mode and the vertical axis in guided steps); only the vertical-paged horizontal case is kept
    controller-quiet.
11. **Not verified on screen.** Everything is covered by headless tests, but no on-screen pass (real keyboard, mouse thumb buttons, a real
    controller, the web view's key forwarding) was possible in this environment; those are the checks to do by hand.

## 14. Every screen and the controller (2026-10-03, follow-up)

The first migration (§9) moved window-level shortcuts and left each screen's focused-item keys with its controls, which meant most screens had
nothing registered and the controller only worked inside the comic reader. Decisions (user, 2026-10-03: "go with recommended"):

1. **All screens are in scope.** Home, Books, Reading Lists, Smart Lists, the comic/manga/book detail screens, Continuity, Insights, Wanted,
   Preferences and the three editors now register a scope. Not wired: the metadata-entity detail page (no code-behind to hang a registration on)
   and the PDF reader, whose page canvas already registers the Reader scope and so has the reader's keys, zoom and thumb buttons.
2. **Per-item keys are remappable without touching the controls.** `FocusUp/Down/Left/Right`, `FocusFirst/Last` (Home/End), `ItemPageUp/Down`, `Activate` (Enter),
   `ToggleSelect` (Space), `RenameItem` (F2) and `DeleteItem` (Delete) each have a *canonical key* (`InputActionInfo.CanonicalKey`): the plain key every focused control already
   understands (`OnCardKeyDown`, `FocusReclaimer`, the grid and list handlers). Three rules in `InputService.ProcessKey` make them remappable everywhere at once:
   (a) while the canonical key is bound to its action, the key passes through untouched (default behaviour is unchanged); (b) a key the user binds instead is delivered as the action,
   and the global handler *sends the canonical key* to the focused control (`UiNavigation.SendKey`), so a control that only knows Enter still opens on `Q`; (c) when the user takes the
   plain key off the action it does nothing (swallowed), except in a text box, with a modifier, or while the service itself is sending a key (`UiNavigation.IsSending`).
   Controller defaults: D-pad and left stick move, A activates, Y opens the context menu; Rename, Delete and Toggle have no pad button by default so nothing destructive sits on a pad.
   If no handler takes a sent arrow, `Move` falls back to a plain directional focus move, and with nothing focused it focuses the first element. A text box is always left (a pad
   has no caret). Type-ahead is typing, not a shortcut, so it stays with the control.
3. **Shared screen actions are Global, handled per screen.** `Refresh` (F5; the old `Library.Refresh` is a `FormerId`, so a saved remap
   carries over), `NewItem` (Ctrl+N), `Save` (Ctrl+S, fires in text input), plus the existing `FocusSearch`. A Global action reaches the handlers
   of every active scope, most specific first, so each screen claims what it can do *right now* and returns false otherwise (the next screen, or
   the shell's fallback, then sees it). Remapping Ctrl+F once applies everywhere.
4. **Tabs.** `TabPrevious`/`TabNext` (left/right bumper, Ctrl+PageUp/PageDown). `TabStrip.Step` walks the visible buttons classed `tab`,
   `segToggle`, `ipTab` or `prefNavItem` and runs the next one's command, wrapping; Home steps its spotlight carousel instead. On a screen with
   no strip the action falls to `MainWindow`, which cycles screens, so the bumpers always do something.
5. **Controller is app-wide.** `AppGamepadHost` (owned by `MainWindow`) is the one poller: it runs while `IInputService.GamepadEnabled` (the
   Preferences > Reader toggle, now app-wide) is on and the window is active, and hands each snapshot to the service. The reader's own poller is
   gone; the reader counts a touched pad as presence through `IInputService.GamepadActivity`. `B` also closes the current view (the reader's
   own `B` = previous page still wins there, because the reader scope is asked first), `Start` opens quick open (the reader's palette wins in
   the reader), and the right stick scrolls the nearest scroll viewer (`ScrollVertical`/`ScrollHorizontal` axes).
6. **Per-screen extras:** Books `SelectAll`/`EditSelection`/`DeleteSelection` (Ctrl+A / Ctrl+I / Delete, acting only with a selection); Smart
   Lists `Duplicate` (Ctrl+D), `NewItem`, `Save` (user lists only); detail screens `Continue` (Ctrl+Enter) and `Edit` (Ctrl+I, the selected
   issues, as the toolbar button does); Reading Lists `NewItem` runs the gallery button's command; editors `Save`.
7. **Also fixed on the way:** Library tiles are `Button`s and a Button marks a left press handled in its own class handler before instance
   handlers run, so Ctrl/Shift-click selection in the Library never ran. `LibraryScreen` now tunnels the press from the screen root and
   forwards to the existing tile handlers.

Verified by headless tests only (`UiNavigationTests`, `ScreenInputTests`); not seen on screen. Wanted, Continuity, Manga/Book detail and the
editors are covered by the smoke test (they register and decline when empty) but not driven with real data.

### 14.1 Follow-up: controller capture, plugin actions, focus rings (2026-10-03)

8. **Controller buttons can be added in Preferences.** `IInputService.BeginGamepadCapture` routes presses to a callback instead of to actions until disposed; `BindingCaptureBox` uses it
   while it is showing, so "Add shortcut…" records a pad button as well as a key, mouse button or wheel turn. Analogue axes are not offered (an axis binding is fixed). The app-wide poller
   only runs while "Use a game controller" is on, and the box's prompt says so when it is off.
9. **Plugin commands are actions (Plugin API 4.3).** Every enabled, working `Library`-hook command becomes `Plugin.{pluginKey}.{commandKey}` in the Library scope under "Plugins"
   (`PluginInputActions`). Default binding: the manifest's new `shortcut` attribute (or `INativeCommandRegistrar.OnLibrary(..., shortcut:)`) when it parses, otherwise, as in CE's
   `PluginEngine` (Ctrl+Shift+F1 to F12 for the first twelve enabled commands), the next free Ctrl+Shift+F key. The action runs the command on the current selection, and the Plugins
   submenu shows the shortcut. `PluginHostService.CommandsChanged` (discovery, a command or package switched on or off) re-syncs the catalog through the new
   `IInputActionCatalog.Unregister`; a user's remap is stored under the same id, so it survives the plugin being switched off and on. Only Library-hook commands are covered: the
   other hooks do not operate on a selection.
10. **The shared focus ring was clipped to a speck on every plain Button.** A `Button` clips to its own bounds, and Avalonia clips an adorner to its adorned element's clip, so the
    ring (painted outside the control) was cut down to a corner dot (the Insights tabs) wherever a control relied on the app-wide adorner. `AdornerLayer.IsClipEnabled="False"` on the
    adorner template fixes it for every such control. The Home carousel had added its own inner 2px border on focus, which insets the cover by its thickness and left a dark gap, and
    would now double the ring; it was removed. `FocusRingPaintTests` checks pixels outside a focused button.

### 14.2 Keyboard gaps found by hand (2026-10-03)

11. **Plain-wrap grids ignored Up/Down.** `GridKeyboardNavigation` gave every `INavigableContainer` panel to Avalonia's own navigation, but a plain `WrapPanel` implements that interface and its
    `GetControl` ignores Up and Down. Only virtualizing panels (whose unrealised items the spatial search cannot see) take that path now.
12. **A Button handles Enter and Space before a XAML `KeyDown=` handler on it runs** (and a left press before a XAML `PointerPressed=`). The Library cards lost their bound `Command` when a
    click stopped navigating, so Enter and Space no longer opened a tile. `LibraryScreen` now tunnels both from the screen root to `OnCardKeyDown`. Cards that still have a `Command`
    (Books, Smart Lists, Book/Manga detail) are unaffected: the Button's own click runs it.
13. **Detail pages:** Esc leaves a detail page when nothing else is open and there is somewhere to go back to (`MainViewModel.Escape`), and `Detail.Back` (Backspace) does the same from the
    comic, manga and book detail screens. Backspace stays out of text boxes (the service suppresses it) and out of the Library/Books type-ahead (it is a Detail-scope action).
14. **Arrow keys on every screen and the rail.** Continuity, Wanted, Preferences and the three editors got the directional fallback the other screens already had. `MainWindow` handles arrows
    nothing else used: on the nav rail Up/Down/Home/End step through the buttons and Right goes into the screen (its contextual sidebar, else the first non-text control); anywhere else a Left that
    nothing used lands on the rail. Enter and Space on a rail button open its screen. A text box still keeps Left and Right for its caret.

The headless harness does not click a plain Button on Space (with or without the input host), so Space on a rail button is not covered by a test; Space on a Library card is (it goes through the
tunnelled handler, not the Button).

### 14.3 More gaps found by hand (2026-10-03, later)

15. **The focus-ring fix from §14 (item 10) had a side effect.** Turning off the adorner's clipping let the ring of a control *partly* scrolled out of its list draw over the page header (the
    "invisible focus at the top" in Continuity). `Controls/FocusRingAdorner` is now the root of the ring template: still unclipped by the adorned control itself (so a Button's ring shows), but clipped to
    the viewport of every `ScrollViewer` the control sits in. `FocusRingPaintTests` covers both directions (ring visible outside a button; no ring over the header for a half-scrolled one).
16. **Other doubled or misplaced rings.** The Home carousel and Detail issue tiles each drew a hand-made 2px accent border on `:focus-visible` as well as the shared glow; both borders are gone (a
    selected Detail tile that has focus hides its selection border so the glow stands alone). The Insights hero tiles hardcoded an orange ring for hover and focus, so they ignored the skin's accent;
    they now use the `PbGlowRing` token, and the two tiles whose face is a Button let that Button fill the card so the ring sits on the card's edge instead of inside its padding.
17. **Up/Down in Wanted and other row lists.** Avalonia's directional search only weighs controls that overlap the focused one horizontally, so it could skip a whole row whose buttons were in
    other columns, or find nothing. `FocusReclaimer.TryMoveDirectionally` now also looks for the nearest row above or below and takes whichever is nearer, and scrolls half a viewport (then
    retries) when a virtualizing list has no row left to land on.
18. **Edges of a grid.** `GridKeyboardNavigation` returns false at the edge instead of swallowing the key, so Up from the first row and Left from the first column carry on (to the Reading Lists
    Continue Reading card, the Books toolbar, the nav rail). Tile-select checkboxes (Books, Library) are mouse-only now: as tab stops they also sat "above" the neighbouring card for the arrows.
19. **Dropdowns.** A read-only `SuggestBox` opened its list merely by receiving keyboard focus, and Down on a closed one opened it and swallowed the key, so arrowing or tabbing through a form popped
    each dropdown open. It now opens on a click, Enter, Space or Alt+Down; a bare Down passes through for navigation; its chevron button is not a tab stop. Open dropdowns still keep the arrows.
20. **Continuity.** Enter or Space on a sidebar event or continuity opens it and now moves focus into the screen (`ContinuityScreen.FocusEntry`), and Right from any contextual-sidebar row enters the
    current screen, as it does from the rail. (A Button handles Enter and Space before an ancestor's bubbling handler runs, so the activation flag is set from a tunnel handler.)
21. **Not keyboard, found in the same pass.** The Library empty state's button ran `ClearAllFilters`, which by design leaves the search text alone, so it did nothing when a search was the only cause;
    it now clears the filters and the search ("Clear search" when only a search is active). The Merge series dialog showed placeholder gradients instead of covers; its rows now carry the cover key
    of each series' cover issue and load the real cover.
