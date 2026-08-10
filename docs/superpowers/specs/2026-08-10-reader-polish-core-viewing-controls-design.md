# Reader Polish — Core Viewing Controls (Fit Modes, Zoom, Rotation)

*Date: 2026-08-10. First slice of the Beta backlog's "Reader polish" item
(docs/alpha-roadmap.md), the largest unsequenced entry there. Extends the paged-mode reader built
in docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md, which explicitly
deferred fit-mode presets ("zoom here is simple continuous zoom only, no mode picker") to a later
pass — this is that pass.*

## 1. Scope

Ships:
- **Fit modes**: Original, Fit (All), Fit Width, Fit Height, Best Fit.
- **Zoom**: the existing continuous zoom/pan gesture support, now layered on top of whichever fit
  mode is active, plus a toolbar zoom control with presets (100/125/150/200/400%).
- **Rotation**: a manual rotate button (90° steps) and an auto-rotate-landscape-pages toggle.

Explicitly deferred, not part of this pass:
- **Double-page spread layout** (single/double/adaptive) — a separate Reader-polish sub-item, needs
  its own spread-pairing design (aspect ratio + reading mode + not-a-cover-page heuristics, per
  onboarding.md §15's already-designed-but-unbuilt notes).
- **CE's anamorphic-tolerance non-uniform X/Y scaling** and **`FitWidthAdaptive`'s portrait-doubling
  heuristic** — both real behaviors in CE's `ImageDisplayControl.GetScale`, both skipped as a
  deliberate, named deviation: porting the tolerance behavior would turn `ZoomPanMath`'s scale from
  a single `double` into an `(X, Y)` pair everywhere it's used (including the Novels PDF reader,
  which shares this exact math), for a visual difference that's imperceptible on real comic pages.
  `FitWidthAdaptive` isn't offered as a selectable mode at all in this pass — not a fallback,
  simply not present in the enum (§2) or the picker (§5) — i.e. this pass ships 5 fit modes
  (Original/Fit/FitWidth/FitHeight/BestFit), not CE's 6.
- **Per-page persisted rotation override** — a metadata-editing feature (`docs/ce-feature-inventory.md`
  Section A), distinct from this pass's session rotation. Not touched here.
- **A Preferences → Reader tab** — the global fit-mode/auto-rotate defaults ship as fixed code
  constants (see §3), not a user-editable setting, since no Reader Preferences surface exists yet.
- **Page-transition animations, fullscreen/chrome mode, on-screen overlays, magnifier, image
  adjustment sliders, background/texture/margins, continuous/webtoon scroll, split-page navigation,
  touch gestures beyond what's already shipped, remappable shortcuts, auto-scroll** — all separate
  Reader-polish sub-items (onboarding.md §8), not this slice.

**CE-verification note (per the standing CE-parity rule):** every behavior below was checked
against `_reference/ComicRackCE` source before being scoped, not guessed at —
`ComicRack.Engine/Display/ImageFitMode.cs` (the fit-mode enum), `ComicRack.Engine.Display.Forms/
ImageDisplayControl.cs`'s `GetScale` (the actual per-mode scale formulas and the anamorphic-tolerance
behavior being skipped), `ComicRack/Config/BookPageLayout.cs`/`DisplayWorkspace.cs` (confirming fit
mode/zoom/rotation are CE's own *workspace*-level state, not per-book — informing the persistence
design in §3, which deliberately deviates from that), and `ComicRack.Engine.Display.Forms/
ImageDisplayControl.cs` line ~1367 for the auto-rotate trigger rule (`ImageAutoRotate && width >
height` → rotate −90°, composed with manual rotation rather than replacing it).

## 2. Fit-mode math

`ZoomPanMath.ComputeBaseScale` currently always computes `min(widthRatio, heightRatio)` — that's
CE's `Fit` mode, and it's the only behavior Paperbunkr's reader has today. It grows two new
parameters: `ImageFitMode fitMode` and `bool fitOnlyIfOversized`.

```csharp
public enum ImageFitMode { Original, Fit, FitWidth, FitHeight, BestFit }
```

(Lives in `Paperbunkr.Data.Entities`, matching where `ReadingMode`/`BookFormat` already live, since
`Issue.PageFitModeOverride` below needs to persist it the same way.)

Per-mode scale, given `widthRatio = canvasWidth / imageWidth` and `heightRatio = canvasHeight /
imageHeight`:
- `Original` → `1.0` always (matches CE: no fit-scale component at all in this mode).
- `Fit` → `min(widthRatio, heightRatio)` — today's only behavior, unchanged when this mode is active.
- `FitWidth` → `widthRatio`.
- `FitHeight` → `heightRatio`.
- `BestFit` → `max(widthRatio, heightRatio)` — CE's `BestFit` fills/overflows rather than contains;
  confirmed from source, not the "fit width if portrait else fit height" behavior the name might
  suggest.

`fitOnlyIfOversized` (CE's `FitOnlyIfOversized`, default `true`, shipped as a fixed value — see §3
on why it isn't user-configurable yet): when true, a mode that would *upscale* an already-smaller-
than-canvas image instead returns `1.0` (native size), matching CE's early-return behavior exactly.
The per-mode check isn't uniform, confirmed from source rather than assumed: `Fit` skips fitting
only when the canvas is already bigger than the image in **both** dimensions (`&&`); `BestFit` skips
it if **either** dimension already fits (`||`) — consistent with `BestFit`'s fill/cover intent making
it more eager to avoid upscaling. `FitWidth`/`FitHeight` check only their own single dimension.

This is a pure function, extending the existing `ZoomPanMathTests` coverage directly (§5).

## 3. Persistence

**Fit mode and auto-rotate persist; zoom level and manual rotation angle stay session-only.**

This is a deliberate deviation from CE, which persists none of these per-book (all four live on
CE's `BookPageLayout`, itself part of a `DisplayWorkspace` — global/workspace state, not per-comic).
Fit mode and auto-rotate get a per-book override here because page dimensions and scan quality
genuinely vary issue-to-issue in ways reading direction (Series-scoped, per the existing
`ReadingMode`/`ReadingModeOverride` design) doesn't. Zoom level and manual rotation stay
session-only because neither CE nor Paperbunkr's existing zoom behavior has ever persisted an exact
value long-term, and remembering "143% zoom on page 12" has no real user value.

- **New**: `Issue.PageFitModeOverride` (`ImageFitMode?`, nullable) and `Issue.AutoRotateOverride`
  (`bool?`, nullable) — same nullable-override shape as the existing (currently dormant)
  `Issue.ReadingModeOverride`. One new EF migration.
- **Global default**: a fixed code constant, `ImageFitMode.FitWidth` (matching CE's own
  `BookPageLayout` constructor default) and `AutoRotate = false` — *not* a new `AppSettings` column,
  since nothing can edit it without a Reader Preferences tab, which doesn't exist yet (§1). Trivial
  to upgrade later: swap the constant read for an `AppSettings` read once that tab lands.
- **Write path**: changing fit mode or toggling auto-rotate from the reader toolbar writes straight
  to `Issue.PageFitModeOverride`/`AutoRotateOverride` for the currently-open book, immediately — same
  "open a fresh context, write, `SaveChanges`" shape as `ReaderScreenViewModel.GoToPage`'s
  `Issue.LastPageRead` persistence and the Novels reader's `PersistPosition`.
- **Read path on `Load`**: `effectiveFitMode = issue.PageFitModeOverride ?? ImageFitMode.FitWidth`;
  `effectiveAutoRotate = issue.AutoRotateOverride ?? false`.
- **Zoom** (`ReaderScreenViewModel.ZoomLevel`) keeps its exact current behavior, unchanged: resets to
  `1.0` on `Load`, persists across page turns within a session. This design only adds the fit-mode
  base-scale layer underneath it — doesn't touch when zoom itself resets.
- **Manual rotation** is a new session-only field (`0`/`90`/`180`/`270`, four steps matching CE's
  `ImageRotation`), same reset-on-`Load`/persists-across-page-turns shape as zoom.

## 4. Rendering

`ReaderPageDrawOperation`/`PageCanvas` already thread `zoom`/`panOffsetX`/`panOffsetY` through to
`ComputeBaseScale` on every draw — `fitMode` and a rotation angle join that same parameter list.

**Auto-rotate trigger**, ported faithfully from CE (`ImageDisplayControl` line ~1367): per page, if
the toggle is on and that page's image is landscape (`pixelWidth > pixelHeight`), rotate −90°,
*composed* with whatever manual rotation is currently active rather than replacing it — matches
CE's `(AutoRotate && width > height) ? RotateLeft() : ImageRotation` composition exactly.

**Rotation and fit-scale interact**: a 90°/270°-rotated page needs its *effective* (post-rotation)
pixel size — width/height swapped — fed into `ComputeBaseScale`, not its raw bitmap size, so the fit
calculation accounts for the rotated shape rather than fitting the wrong orientation. The draw call
itself then applies a rotation transform around the render bounds' center. The exact Avalonia
transform API for `ImmediateDrawingContext` is an implementation detail to resolve while writing the
code, not pinned here.

## 5. UI

Toolbar controls join the reader's existing top chrome bar (same "extend existing chrome" pattern
the Novels reader used when it added its 🔍/🔖 icons, rather than a new panel):
- **Fit-mode picker** — five options (Original/Fit/Fit Width/Fit Height/Best Fit).
- **Zoom control** — `−`/percentage/`+` plus CE's preset list (100/125/150/200/400%).
- **Rotate button** — one press = +90°, wraps at 360°.
- **Auto-rotate toggle**.

## 6. Testing

- `ZoomPanMathTests` gains coverage for every `(fitMode, fitOnlyIfOversized)` combination — pure
  function, no UI dependency, matching the file's existing shape.
- A new pure-function test for the auto-rotate trigger rule (width > height + toggle on → −90°,
  composed with existing manual rotation).
- `ReaderScreenViewModel` tests: toolbar change writes `PageFitModeOverride`/`AutoRotateOverride`
  for the open book; a fresh `Load` reads it back correctly; a book with no override falls back to
  the fixed defaults.
- New EF migration gets the usual round-trip test (existing rows come back with both new columns
  `null`).
- **Manual-only, same caveat as every prior reader spec:** actual on-screen fit-mode switching, zoom
  presets, and rotation rendering need eyes-on verification — no unattended desktop GUI automation
  available for this project.
