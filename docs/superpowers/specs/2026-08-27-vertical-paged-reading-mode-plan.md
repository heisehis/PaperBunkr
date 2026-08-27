# Vertical Paged Reading Mode — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-27-vertical-paged-reading-mode-design.md*

**Done 2026-08-27** — all 9 steps landed. Solution build clean, `Paperbunkr.App.Tests` 904/904 +
`Paperbunkr.Data.Tests` 447/447 green. On-screen gesture/animation verification done — user
confirmed everything works.

Surveyed against current source before writing:
- `ReadingMode` enum: 6 values, `HasConversion<string>()` everywhere, no per-value migration.
- `PageTransitionDirection` (`ReaderPageVisualHandler.cs:51`): `internal enum { Left, Right }`.
- `PageTransitionMath.SlideOffset` (`PageTransitionMath.cs:26`): `sign = direction == Right ? 1 : -1`,
  returns `(OutgoingOffset, IncomingOffset)` along one axis; caller applies it as X.
- `ReaderPageVisualHandler`: `RenderTransition` (line 524) → `DrawTransitionSide` (559, param
  `double offsetX`) → `RenderSpread` (484, param `double offsetX`); both translate by
  `new Vector(offsetX, 0)`. Single-page path also does `CenterX = plan.CenterX + offsetX`.
- `PageCanvas`: `IsContinuous` (860), `ExecuteDirectional(ICommand?, PageTransitionDirection)` (1944,
  just sets `_pendingTransitionDirection` + `TryExecute`). Turn gestures: `OnKeyDown` paged tail
  (1685–1701), `OnPointerWheelChanged` paged branch (1533–1549), `InvokeZoneCommand(double x)` (1860,
  one caller at 1418), `InvokeTouchZone(Point)` (1846, one caller at 1414), `OnPointerReleased`
  single-finger flick (1471–1481), `OnPinchEnded` two-finger-drag flick (1785–1802).
- `ReaderScreenViewModel.ApplyReadingMode` (~959): sets `_isRightToLeft` (checks `== RightToLeft`
  only), `IsContinuousMode` (963–964), `EffectiveReadingMode`, `ReadingModeLabel` switch (966–974,
  `_ =>` default "Left to Right ▾").
- Other `ReadingMode` switches with a wrong default for an unknown value:
  `DetailTabsViewModel.SetReadingModeLabel` (827), `MangaDetailScreenViewModel.ReadingModeBadge`
  (220). `SeriesBulkFieldDescriptor` (59–61) uses `Enum.GetNames` — auto-picks-up, no change.
- Tests: `PageTransitionMathTests`, `ReaderScreenViewModelTests` (`[Collection(AvaloniaTestCollection)]`,
  temp-DB via `DatabasePathOverride`), `ReadingModeIconConverterTests` (plain, no collection).

## Step 1: Add `TopToBottom` to the `ReadingMode` enum

**Files:** `src/Paperbunkr.Data/Entities/ReadingMode.cs` (edit)
**What:** Insert `TopToBottom` after `RightToLeft`. Extend the XML doc comment: paged (not continuous)
top-to-bottom mode, page-turns advance downward, stored via the same `HasConversion<string>()` as the
rest so no migration; no RTL variant.
**Depends on:** none
**Verify:** `dotnet build src/Paperbunkr.Data/Paperbunkr.Data.csproj`; `dotnet test
src/Paperbunkr.Data.Tests` still green (no enum-arity assertions expected — confirm).

## Step 2: `PageTransitionDirection` + `PageTransitionMath` vertical support

**Files:** `src/Paperbunkr.App/Views/ReaderPageVisualHandler.cs` (edit — enum only),
`src/Paperbunkr.App/Views/PageTransitionMath.cs` (edit)
**What:**
- `internal enum PageTransitionDirection { Left, Right, Up, Down }`.
- `SlideOffset`: `double sign = direction is PageTransitionDirection.Right or PageTransitionDirection.Down ? 1 : -1;`
  Return shape unchanged. Update the method + class doc comments to say the offset is along "the turn's
  main axis — viewport width for Left/Right, height for Up/Down".
**Depends on:** none
**Verify:** `dotnet build`; new `PageTransitionMathTests` cases land in Step 8.

## Step 3: Thread a `Vector` offset through the transition render path

**Files:** `src/Paperbunkr.App/Views/ReaderPageVisualHandler.cs` (edit)
**What:** Behaviour-preserving refactor of the private transition draw path so the slide offset can be
vertical:
- `DrawTransitionSide` and `RenderSpread`: replace the `double offsetX` parameter with `Vector offset`.
  - single-page branch: `plan with { DestRect = plan.DestRect.Translate(offset), CenterX =
    plan.CenterX + offset.X, CenterY = plan.CenterY + offset.Y }`.
  - spread branch: `combinedPlan.DestRect.Translate(offset)`.
- `RenderTransition` Slide branch: `bool vertical = data.Direction is PageTransitionDirection.Up or
  PageTransitionDirection.Down; double extent = vertical ? data.Bounds.Height : data.Bounds.Width;`
  pass `SlideOffset(data.Direction, progress, extent)` results as `vertical ? new Vector(0, o) :
  new Vector(o, 0)`.
- Crossfade branch + the non-transition `RenderSpread` call at line 464: pass `default` (zero vector).
**Depends on:** Step 2
**Verify:** `dotnet build`; `dotnet test --filter PageTransition`; manual — a horizontal Slide turn
must look pixel-identical to before (pure refactor for that path).

## Step 4: `ReaderScreenViewModel` recognises `TopToBottom`

**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit)
**What:** In `ApplyReadingMode`, add `ReadingMode.TopToBottom => "Vertical ▾"` to the
`ReadingModeLabel` switch. Leave the `IsContinuousMode` predicate and the `_isRightToLeft` line
untouched (TopToBottom is paged, LTR-navigation) — `EffectiveReadingMode = effectiveMode` already
carries it to the canvas.
**Depends on:** Step 1
**Verify:** `dotnet build`; new `ReaderScreenViewModelTests` cases in Step 8.

## Step 5: `PageCanvas` vertical navigation gestures

**Files:** `src/Paperbunkr.App/Views/PageTurnGestureMath.cs` (new),
`src/Paperbunkr.App/Views/PageCanvas.cs` (edit),
`src/Paperbunkr.App.Tests/PageTurnGestureMathTests.cs` (new)
**What:**
- **New `PageTurnGestureMath`** (`internal static`, mirrors `ZoomPanMath` — pure, no Avalonia
  rendering types beyond `Point`/`Size`/`Vector`), to de-duplicate the four horizontal/vertical
  branch sites:
  - `ResolveZone(Point p, Size bounds, bool vertical, int divisions)` → `bool forward` (true = next).
    `divisions == 2` for the mouse half-split, `3` for the touch third-split (middle third → returns
    `null`, a no-op). Vertical splits on `p.Y`/`bounds.Height`, horizontal on `p.X`/`bounds.Width`.
  - `ResolveFlick(Vector delta, bool vertical)` → `bool? forward`: dominant-axis guard
    (`|primary| > |secondary|` and `>= MinFlickDistance`); horizontal `dx < 0` → forward (spatial
    convention, unchanged), vertical `dy < 0` → forward (flick content up = advance, scroll
    convention per design §Risks).
- **`PageCanvas`:** add `private bool IsPagedVertical => ReadingMode == ReadingMode.TopToBottom;`
  (not part of `IsContinuous`).
  - `OnKeyDown` paged tail — before the `LeftKey`/`RightKey` matches, after the `CanPan()` arrow-pan
    block: `if (IsPagedVertical)` match bare `Key.Up` → `ExecuteDirectional(LeftCommand,
    PageTransitionDirection.Up)`, `Key.Down` → `ExecuteDirectional(RightCommand,
    PageTransitionDirection.Down)`; `e.Handled` + return on hit. `LeftKey`/`RightKey` matches still
    run afterward unchanged.
  - `OnPointerWheelChanged` paged branch: keep the `Delta.Y`/`Delta.X` trigger tests; when
    `IsPagedVertical` pass `Down`/`Up` to `ExecuteDirectional` instead of `Right`/`Left`.
  - `InvokeZoneCommand`: signature `double x` → `Point p`; body calls
    `PageTurnGestureMath.ResolveZone(p, Bounds.Size, IsPagedVertical, divisions: 2)`, maps
    `forward` → `RightCommand`+`Down`/`LeftCommand`+`Up` when vertical, `…Right`/`…Left` otherwise.
    Update the caller at line 1418 to pass `e.GetPosition(this)`.
  - `InvokeTouchZone`: same, `divisions: 3`, `null` result stays a no-op.
  - `OnPointerReleased` flick + `OnPinchEnded` flick: build a `Vector` from the recorded start/end
    (or pinch origin delta), call `PageTurnGestureMath.ResolveFlick(delta, IsPagedVertical)`, map the
    `bool?` to command + `PageTransitionDirection` the same way.
- **`PageTurnGestureMathTests`:** zone splits (2- and 3-way, both axes, middle-third no-op), flick
  dominant-axis guard and direction for both axes, sub-threshold flick returns null.
**Depends on:** Steps 1, 2
**Verify:** `dotnet build`; `dotnet test --filter PageTurnGestureMath`; **manual on-screen** is the
real check for the wiring (arrow/wheel/zone/flick each turn vertically in the right direction).

## Step 6: Reader flyout entry + icon converter

**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit),
`src/Paperbunkr.App/Views/ReadingModeIconConverter.cs` (edit)
**What:**
- Flyout (reading-mode `clusterPill` → `Flyout` → `StackPanel Width="170"`): add
  `<Button Classes="drawerRow" Content="Vertical" Command="{Binding SetReadingModeCommand}"
  CommandParameter="{x:Static entities:ReadingMode.TopToBottom}" />` between the "Right to Left" and
  "Vertical (Continuous)" rows. Eyeball whether the 170px width still fits all seven rows; bump if
  not.
- `ReadingModeIconConverter.KeyFor`: fold `TopToBottom` into the existing down-glyph arm →
  `ReadingMode.VerticalContinuous or ReadingMode.Webtoon or ReadingMode.TopToBottom => "PbIconArrowDown"`.
**Depends on:** Step 1
**Verify:** `dotnet build` with the CLAUDE.md AVLN2000 guard (delete
`obj/Debug/net8.0/Paperbunkr.App.dll`+`.pdb` if the compile skips, confirm the XAML weave ran by
launching); `ReadingModeIconConverterTests` in Step 8.

## Step 7: Detail-screen label switches

**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs` (edit)
**What:** `DetailTabsViewModel.SetReadingModeLabel` → add `ReadingMode.TopToBottom => "Vertical ▾"`.
`MangaDetailScreenViewModel.ReadingModeBadge` → add `ReadingMode.TopToBottom => "Vertical"`. Both
currently fall to a wrong `_ =>` default for this value.
**Depends on:** Step 1
**Verify:** `dotnet build`; `dotnet test --filter "DetailTabsViewModel|MangaDetailScreenViewModel"`;
add an assertion if either test file already covers the label/badge mapping.

## Step 8: Automated tests

**Files:** `src/Paperbunkr.App.Tests/PageTransitionMathTests.cs` (edit),
`src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` (edit),
`src/Paperbunkr.App.Tests/ReadingModeIconConverterTests.cs` (edit)
**What:**
- `PageTransitionMathTests`: `SlideOffset(Down, p, e)` equals `SlideOffset(Right, p, e)` and
  `SlideOffset(Up, …)` equals `SlideOffset(Left, …)` across a couple of progress values; existing
  Left/Right cases unchanged.
- `ReaderScreenViewModelTests`: with `Series.ReadingMode = TopToBottom` seeded, after load —
  `IsContinuousMode` false, `ReadingModeLabel == "Vertical ▾"`, `EffectiveReadingMode ==
  ReadingMode.TopToBottom`; `SetReadingModeCommand.Execute(ReadingMode.TopToBottom)` writes
  `Series.ReadingMode` to the temp DB.
- `ReadingModeIconConverterTests`: add `[InlineData(ReadingMode.TopToBottom, "PbIconArrowDown")]`;
  `KeyFor_CoversEveryReadingModeValue` already guards the enum (its allowed set includes
  `PbIconArrowDown`).
**Depends on:** Steps 1–7
**Verify:** `dotnet test src/Paperbunkr.App.Tests` and `src/Paperbunkr.Data.Tests` fully green.

## Step 9: Full build, manual verification, doc status

**Files:** `docs/superpowers/specs/2026-08-27-vertical-paged-reading-mode-design.md` (edit — status)
**What:** Solution build with the AVLN2000 guard; full `dotnet test`; launch the app and run the
design §8 manual checklist (switch to Vertical, arrow/wheel/zone/flick turn vertically, Slide
animates vertically incl. with a double-page spread, Crossfade/None unaffected, zoom-in pans on Down,
mode persists across reopen). Flip the design doc status to Implemented with what was verified vs.
left to manual.
**Depends on:** Steps 1–8
