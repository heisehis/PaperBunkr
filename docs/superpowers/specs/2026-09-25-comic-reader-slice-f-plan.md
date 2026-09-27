# Comic reader — Slice F (F1 Reach, F2 Profiles, F3 Comfort) — Implementation Plan
*Implements: `2026-09-25-comic-reader-reach-design.md`, `...-profiles-design.md`, `...-comfort-design.md`. Built back to back, in that order.*

**Status 2026-09-25:** F1, F2 and F3 built and unit tested (see each spec's "Implementation notes" for deviations); **on-screen checks by the user outstanding**. All work is uncommitted in a shared working tree (slices A/B/G and other sessions):
edit narrowly, `git diff` shared files before/after, never revert another hunk. Test runs: check `Get-Process testhost` first and wait for a foreign
host instead of killing it, redirect `dotnet test` output to a log file, kill only test hosts created after your own start. Each new migration's `Up()` is read
before it is kept. Avalonia UI steps read the matching `avalonia-pro-max` subskill from disk (not `Skill`) and run its review checklist before close-out.

## Survey corrections (folded into the specs' "Implementation notes" at the end)

- In `PageCanvas`, `ExecuteTurn(forward)` is **spatial** (`forward` = `RightCommand` = the VM's `GoRight`, which the VM flips for RTL). Mouse/touch zones and flicks feed it
  spatially. The new reading-order commands call the VM's `NextPage`/`PreviousPage` (both `[RelayCommand]`s) directly.
- The paged path's `OnPointerPressed` runs the zone on **press** (not release) when the page can't pan; a pan-capable page starts a drag on press instead.

## F1 — Reach
1. **Pointer-button policy + click detection.** `Views/PointerButtonPolicy.cs` (pure) + `PageCanvas` press/move/release consult it; continuous-mode tap detection
   (6 px / 400 ms). Tests: `PointerButtonPolicyTests`. *Verify:* right/middle never start a drag/zone/double-click; X buttons classified.
2. **Reading-order commands + media keys + extra defaults + X buttons.** `KeyboardCommandDescriptor.AdditionalDefaults`, `KeyBindingService.GetKeys`,
   `Reader.NextPage`/`Reader.PreviousPage`, `KeyOption` media keys, `PageCanvas` gesture/command trio, VM key props, axaml bindings, `ExtraMouseButtonsTurnPages`
   setting read at load, X1/X2 turn (continuous: scroll a screen). Tests: registry/binding tests, VM.
3. **Tap zones.** `Views/TapZones.cs` (enums, region tables, pure `TapZoneResolver`), settings (`PagedTapZoneLayout/Invert`, `ContinuousTapZoneLayout/Invert`,
   `TapZonesForMouse`, `ExtraMouseButtonsTurnPages`, `GamepadEnabled`) + migration `AddReaderInputSettings`, Preferences group + live-preview `TapZoneOverlay`,
   canvas wiring (paged then continuous), flash on layout change. Tests: resolver per layout/cell/input/RTL/invert, Preferences persistence, migration.
4. **Command palette + go-to-page.** `Services/Reader/ReaderPaletteCatalog.cs` (pure), `Reader.CommandPalette` (Ctrl+K) and `Reader.GoToPage` (Ctrl+G) commands,
   in-canvas overlay in `ReaderScreen.axaml` + tunnel key handler, `JumpToPage` for go-to-page. Tests: catalog, page-number parsing, VM.
5. **Gamepad.** `Services/Input/XInputInterop.cs`, pure `GamepadMapper`, `GamepadPoller` (visible + active gate, 2 s slot probe), `GamepadEnabled`.
   Tests: mapper with hand-built states.
6. **Docs/wiki/memory + review checklist.** Perf overlay "last input" line.

## F2 — Profiles
1. `ReaderProfileState` + JSON, `ReaderProfileOverlay`, `ReaderProfileResolution`, `WorkspaceScreen.Reader`, built-in seeding (pure + service tests).
2. Migration `AddReaderProfiles` (`Series.ReaderProfileId`, `AppSettings.DefaultReaderProfileId`), migration test.
3. Reader integration: resolve at `Load`/`RefreshDisplaySettings`, session profile apply/clear, `ToastRequested`, `Reader.NextProfile`, capture. VM tests.
4. Drawer section + persist actions + `PromptWorkspaceName` wiring in `MainViewModel`.
5. Detail-screen series profile row, Preferences PROFILES group, palette entries.
6. Docs/wiki/memory.

## F3 — Comfort
1. Settings + migration `AddReaderComfortSettings` + Preferences COMFORT group + profile fields.
2. `ReadingSessionClock`, `SessionHudFormatter`, VM wiring, HUD chip, `Reader.ToggleSessionHud`.
3. `BreakNudgePolicy`, `ToastCloseRequested`, nudge wiring.
4. `WarmShiftSchedule`, matrix `warmth`, `AdjustmentVisualData`/`PageCanvas`/handler plumbing, `Reader.ToggleWarmShift`.
5. `SpreadComposer`, `ClipboardHelper.CopyBitmapAsync`, export refactor, `Reader.CopyPage`, context menu + palette entries.
6. Docs/wiki/memory.

## Checklist
- F1: [x] 1  [x] 2  [x] 3  [x] 4  [x] 5  [x] 6
- F2: [x] 1  [x] 2  [x] 3  [x] 4  [x] 5  [x] 6
- F3: [x] 1  [x] 2  [x] 3  [x] 4  [x] 5  [x] 6
