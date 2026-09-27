# Comic reader — Panels & zoom (slice D) — Implementation Plan
*Implements: `2026-09-25-comic-reader-panels-and-zoom-design.md`.*

**Status 2026-09-26:** all six steps built and unit tested (see the spec's "Implementation notes" for deviations); **on-screen checks by the user outstanding**. Uncommitted, shared working tree (slices A/B/F/G and other sessions): edit narrowly, `git diff` shared files before
and after, never revert another hunk. Test runs: check `Get-Process testhost` first, redirect `dotnet test` output to a log, build to a scratch folder (`-o`) when the app is running from the normal
output. Each new migration's `Up()` is read before it is kept. Avalonia UI steps read the matching `avalonia-pro-max` subskill from disk and run its review checklist before close-out.

1. **Zoom range.** `ZoomPanMath` (`MinZoom 0.25`, `FitZoom 1.0`, `MaxZoom 4.0`, pure log-slider and step helpers), view-model setter and pan zeroing, `PageCanvas` clamps and every "fit state" call site,
   multiplicative wheel and keys, slider proxy, preset removal, palette reset. Tests: `ZoomPanMathTests`, reader VM zoom tests.
2. **`PanelDetector`** (pure; synthetic pages): gutter model, recursive cut, order (LTR/RTL), fallback. Tests: `PanelDetectorTests`.
3. **`PanelViewMath`, `ZoomPanTween`, `PanelStepper`** (pure). Tests for each.
4. **Guided view:** `PagePanelService`, view-model (`CurrentPagePanels`, `GuidedView`, `G` key, spread toast, palette, drawer button), canvas (`TryStepPanel`, landing, tween driver, label), settings
   `GuidedViewOnOpen`, migration `AddGuidedViewSettings`, Preferences group, profile field.
5. **Smart double-click:** canvas paged double-click, restore, guided toggle, `SmartDoubleClickZoom` setting.
6. **Tuning overlay** (`PanelDebugOverlay`, palette "Show detected panels"), docs, wiki, memory, review checklist.

## Checklist
- [x] 1 Zoom range  - [x] 2 Detector  - [x] 3 Math and tween  - [x] 4 Guided view  - [x] 5 Smart double-click  - [x] 6 Overlay and docs
