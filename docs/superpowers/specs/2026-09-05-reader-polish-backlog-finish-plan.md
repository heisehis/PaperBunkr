# Reader polish backlog finish — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-05-reader-polish-backlog-finish-design.md*

Continuing on the current branch (`feature/navigation-transition-system`), not a new branch. Do not
touch `DetailScreenViewModel.cs`, `IDetailHeaderSource.cs`, `MangaDetailScreenViewModel.cs`, or
`DetailHero.axaml` — unrelated in-progress work from a different session.

## Step 1: `PagePartMath` pure helper
**Files:** `src/Paperbunkr.App/Views/PagePartMath.cs` (new)
**What:** `internal static class PagePartMath`, same shape as `ZoomPanMath`/`SpreadLayoutMath`
(plain value types, no Avalonia app context beyond `Avalonia.Size`/`PixelSize`/`Point`):
- `(int Cols, int Rows) ComputePartGrid(Size viewport, PixelSize content, double zoom, ImageFitMode fitMode, bool fitOnlyIfOversized)` — scale via `ZoomPanMath.ComputeBaseScale(...) * zoom`; `(1, 1)` when the scaled content fits the viewport in both dimensions (mirrors `ZoomPanMath.HasOverflow`'s own epsilon-guarded check rather than reinventing a second one); otherwise `(ceil(displayedW / viewport.Width), ceil(displayedH / viewport.Height))`.
- `int PartCount((int Cols, int Rows) grid) => grid.Cols * grid.Rows;` (trivial helper, avoids repeating the multiply at call sites).
- `int FindNearestPart((int Cols, int Rows) grid, Size viewport, PixelSize content, double scale, double panX, double panY, bool rightToLeft)` — reconstruct each grid cell's rect the same way `ClampPan`'s displayed-size math does, find the index whose center is nearest `(panX, panY)`'s implied content-space point; RTL reverses column order within each row (rightmost column = index 0 of that row).
- `(double X, double Y) PanForPart(int partIndex, (int Cols, int Rows) grid, Size viewport, PixelSize content, double scale, bool rightToLeft)` — the pan offset that centers cell `partIndex`, clamped through `ZoomPanMath.ClampPan`.
**Depends on:** none (pure addition, reads `ZoomPanMath`'s existing public statics).
**Verify:** none yet — see Step 2.

## Step 2: `PagePartMathTests`
**Files:** `src/Paperbunkr.App.Tests/PagePartMathTests.cs` (new)
**What:** xUnit `[Fact]`s, same flat-class style as `SpreadLayoutMathTests.cs`/`ZoomPanMathTests.cs`:
- `ComputePartGrid` returns `(1,1)` when content fits; returns the right `(Cols, Rows)` for a few
  known oversized cases (e.g. viewport 400×600, content scaled to 800×1800 at zoom 1 → `(2,3)`).
- `FindNearestPart` returns the expected index for pan offsets at/near each cell's center, both LTR
  and RTL (confirms column-order reversal).
- `PanForPart` → `FindNearestPart` round-trips back to the same index for every cell in a 2×2 and a
  3×1 grid.
- Double-page spread case: feed `SpreadLayoutMath.ComputeCombinedSize(a, b).Combined` as `content`
  and confirm the grid accounts for the full combined width, not just one page's width.
**Depends on:** Step 1.
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter PagePartMathTests`

## Step 3: Wire part-stepping into `PageCanvas`
**Files:** `src/Paperbunkr.App/Views/PageCanvas.cs` (edit)
**What:**
- Add `CurrentPartProperty`/`PartCountProperty` (`StyledProperty<int>`, default `0`/`1`, OneWay —
  no external writer, same registration shape as `PageCountProperty` but without
  `defaultBindingMode: TwoWay` since nothing outside this control ever sets them) plus their CLR
  property wrappers, next to `PageCountProperty`.
- New private `UpdatePartLabel()`: computes the active grid via `PagePartMath.ComputePartGrid`
  (using `SpreadLayoutMath.ComputeCombinedSize(...).Combined` in place of `EffectivePixelSize()`
  whenever `SecondaryPage is not null`, otherwise `EffectivePixelSize()` as-is), then
  `FindNearestPart`, and sets `PartCount`/`CurrentPart` accordingly. No-ops (leaves both at their
  current values) when `IsContinuous` or `Page is null`.
- Call `UpdatePartLabel()` unconditionally at the very top of `OnPropertyChanged`, before the
  existing early-return branches (the transition-build return, the continuous re-clamp returns, the
  `BoundsProperty` return) — the safest insertion point given how many of those branches already
  `return` early; a no-op call for unrelated property changes is cheap (bounded grid math, no
  allocation beyond a tuple).
- New private `bool TryStepPart(bool forward)`: recomputes the grid/current part the same way
  `UpdatePartLabel` does; if the grid is `1×1` or the current part is already the last (forward) or
  first (backward) cell, returns `false`. Otherwise pans to the next/previous cell via
  `PagePartMath.PanForPart`, updates `PanOffsetX`/`PanOffsetY`, and returns `true`.
- `ExecuteTurn(bool forward)` becomes:
  ```csharp
  private bool ExecuteTurn(bool forward)
  {
      if (!IsContinuous && TryStepPart(forward))
      {
          return true;
      }

      var (command, direction) = forward ? ForwardTurn : BackwardTurn;
      return ExecuteDirectional(command, direction);
  }
  ```
- Landing position on a genuine turn: in `ExecuteDirectional` (or immediately after, wherever
  `Page`/`SecondaryPage` actually changes as a result of this call succeeding), when the turn was
  reached via exhausted parts, explicitly set `PanOffsetX`/`PanOffsetY` to part 0's pan (forward) or
  the new page's last part's pan (backward) — read the plan's own note in the design doc §1 for
  why a stale carried-over pan offset is wrong here. Concretely: `ExecuteDirectional` already knows
  `forward`; after a successful `TryExecute(command)`, once `PageProperty`'s change has been
  processed (same-frame, since commands run synchronously), recompute the new page's grid and pan
  to cell `0` or `PartCount - 1` accordingly. This needs the *new* page's `PixelSize`, which isn't
  available until the bound `Page` property actually updates — confirm during implementation
  whether this is reachable synchronously right after `TryExecute` returns, or needs to piggyback on
  the existing `PageProperty`-changed branch in `OnPropertyChanged` (which already distinguishes
  "was this change a pending-direction turn" via `_pendingTransitionDirection`) instead of living in
  `ExecuteDirectional` itself.
**Depends on:** Steps 1-2.
**Verify:** existing `PageCanvas`-adjacent tests keep passing (see Step 8 for the full-suite run);
new behavior covered by Step 7's tests.

## Step 4: "Part X/Y" label in the Navigate cluster
**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit)
**What:** next to the existing `PageLabel` `TextBlock` in the Navigate cluster (~line 329), add a
second `TextBlock` bound to `{Binding #PageCanvasControl.CurrentPart}`/
`{Binding #PageCanvasControl.PartCount}` (e.g. `Text="{Binding #PageCanvasControl.CurrentPart,
StringFormat='Part {0}'}"` composed with a `/{PartCount}` suffix — exact composition via a
`MultiBinding`/`StringFormat` or a tiny computed string; match whichever is less XAML ceremony),
`IsVisible` bound so it only shows when `PartCount > 1` (a `FuncValueConverter<int, bool>` static
resource, or reuse an existing greater-than-one converter if one already exists in this file/
`ActivityConverters.cs` — check before adding a new one).
**Depends on:** Step 3 (properties must exist).
**Verify:** manual on-screen check (Step 9's manual pass).

## Step 5: `IBatteryStatusService`/`BatteryStatusService`
**Files:** `src/Paperbunkr.App/Services/IBatteryStatusService.cs` (new),
`src/Paperbunkr.App/Services/BatteryStatusService.cs` (new)
**What:** `IBatteryStatusService { BatteryStatusSample? GetStatus(); }` where
`BatteryStatusSample` is a small `readonly record struct(int Percentage, bool IsCharging)`; `null`
return means no battery present. `BatteryStatusService` P/Invokes Win32 `GetSystemPowerStatus`
(`Kernel32.dll`) directly — a `SYSTEM_POWER_STATUS` struct matching the Win32 API, checking
`BatteryFlag != 128 (BATTERY_FLAG_NO_SYSTEM_BATTERY)` before returning a sample, `BatteryLifePercent`
(0-100, or 255 = unknown → treat as no reading) for `Percentage`, `ACLineStatus == 1` for
`IsCharging`. Windows-only, matching this project's existing Windows-only scope (no `#if` guard
needed - the whole app already assumes Windows per its Win32 interop elsewhere).
**Depends on:** none.
**Verify:** see Step 6's tests (interface makes this substitutable without touching the real API in
tests).

## Step 6: Clock + battery on `ReaderScreenViewModel`
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` (edit — confirm actual filename first)
**What:**
- New constructor overload taking `IBatteryStatusService? batteryStatusService = null` (defaults to
  `new BatteryStatusService()`), matching the existing `KeyBindingService` optional-overload
  pattern (line ~124-128) so tests can substitute a fake.
- `[ObservableProperty] private string _currentTimeLabel = "";` and
  `[ObservableProperty] private string? _batteryStatusLabel;` (null/empty hides the battery
  element via the same `StringConverters.IsNotNullOrEmpty`-style binding already used elsewhere in
  this codebase).
- New `DispatcherTimer? _clockTimer` (interval 60s, same field-nullable/lazy-start pattern as
  `_overlayAutoHideTimer`/`_autoScrollTimer`), started in `Load()` alongside the other timers,
  ticking `RefreshClockAndBattery()`; also call `RefreshClockAndBattery()` once synchronously in
  `Load()` so the label isn't blank for up to 60s after opening a book.
- `RefreshClockAndBattery()`: sets `CurrentTimeLabel = DateTime.Now.ToString("t")`;
  `BatteryStatusLabel = _batteryStatusService.GetStatus() is { } s ? $"{s.Percentage}%" : null`.
- Stop `_clockTimer` wherever `_overlayAutoHideTimer`/`_autoScrollTimer` already get stopped on
  screen teardown (find and reuse that exact spot — don't invent a new cleanup path).
**Depends on:** Step 5.
**Verify:** new `ReaderScreenViewModelTests` cases - `RefreshClockAndBattery` with a fake
`IBatteryStatusService` returning a sample formats the percentage correctly; returning `null` leaves
`BatteryStatusLabel` null; `dotnet test src/Paperbunkr.App.Tests --filter ReaderScreenViewModelTests`.

## Step 7: Clock/battery in the Actions cluster
**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit)
**What:** two new `TextBlock`s in the Actions cluster (~line 337, alongside the bookmark/drawer/
fullscreen buttons) bound to `CurrentTimeLabel`/`BatteryStatusLabel`; battery `TextBlock`'s
`IsVisible` bound through `StringConverters.IsNotNullOrEmpty` (already used elsewhere in this
codebase per the data-binding subskill's built-in-converters table) against `BatteryStatusLabel`.
Styled to match the cluster's existing small-text convention (same `FontSize`/`Foreground` as
`PageLabel`).
**Depends on:** Step 6.
**Verify:** manual on-screen check (Step 9).

## Step 8: Touch center-zone → chrome toggle
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/PageCanvas.cs` (edit), `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit)
**What:**
- New `[RelayCommand] private void ToggleChrome()` on the VM: if `ShowChrome`, set it `false` and
  stop `_overlayAutoHideTimer`; else call the existing `NotifyCursorActivity()` (shows chrome,
  restarts the idle timer, refreshes shortcut hints - already does exactly the right thing for the
  "show" side).
- New `ToggleChromeCommandProperty` (`StyledProperty<ICommand?>`) on `PageCanvas`, same registration
  shape as `FullscreenToggleCommandProperty`.
- `InvokeTouchZone` (line ~2062) gets an `else` branch on the existing
  `if (PageTurnGestureMath.ResolveZone(...) is { } forward)` — when it's `null` (center column),
  call `TryExecute(ToggleChromeCommand)`.
- Bind `ToggleChromeCommand="{Binding ToggleChromeCommand}"` on the `PageCanvasControl` element in
  `ReaderScreen.axaml`, alongside the existing `FullscreenToggleCommand=` binding (~line 231).
**Depends on:** none (independent of Steps 1-7).
**Verify:** new `ReaderScreenViewModelTests` case for `ToggleChromeCommand` flipping `ShowChrome`
both directions; manual on-screen check on a touch device (Step 9).

## Step 9: Doc corrections
**Files:** `docs/ce-feature-inventory.md` (edit), `docs/Paperbunkr-Roadmap.md` (edit)
**What:** only after Steps 1-8 are implemented and verified -
- `ce-feature-inventory.md`'s "Split-page 'part' navigation..." row: replace "split-page part
  navigation doesn't apply (no Part concept, confirmed absent)" with a corrected note (real CE
  mechanism found in `ImageDisplayControl.cs`, now shipped in Paperbunkr) - leave the
  "type-to-jump-to-page genuinely not built" clause alone, that's still true and out of scope here.
- `Paperbunkr-Roadmap.md`'s Reader-polish "Still genuinely open" line: drop "on-screen overlays
  (scrubber, page/status text, clock/battery)", "split-page part navigation", and "touch gestures
  beyond what's already shipped" (replace with a short "shipped 2026-09-05" note, magnifier stays
  listed as explicitly declined).
**Depends on:** Steps 1-8 verified.
**Verify:** none (docs-only).

## Step 10: Full verification pass
**What:** `dotnet build` on the solution, confirming the XAML weave actually ran (per this
project's own standing build gotcha - a bare "0 Errors" isn't sufficient proof after touching a new
or existing `.axaml` file with new controls; if anything looks off, force `CoreCompile` per
`CLAUDE.md`'s documented recipe). Full `dotnet test` run (`Paperbunkr.App.Tests`,
`Paperbunkr.Data.Tests` unaffected but run for regression safety). Manual on-screen verification
(same standing no-GUI-automation caveat as every prior Reader spec) — flag explicitly to the user
what still needs a human pass: part-stepping behavior when zoomed on a real book (including a
double-page spread), clock ticking, battery percentage matching the OS's own reading, touch
center-tap toggling chrome on a touch-capable device.
