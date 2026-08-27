# Vertical Paged Reading Mode

**Status:** Implemented 2026-08-27 (plan:
`2026-08-27-vertical-paged-reading-mode-plan.md`). Solution build clean (forced `CoreCompile` +
XAML weave verified by a crash-free launch); `Paperbunkr.App.Tests` 904/904 and
`Paperbunkr.Data.Tests` 447/447 green, including new `PageTurnGestureMathTests`, the
`PageTransitionMath` Up/Down cases, `ReadingModeIconConverter` `TopToBottom`, and two
`ReaderScreenViewModelTests` cases. On-screen verification done 2026-08-27 — user confirmed the
flyout entry, Up/Down keys, wheel, top/bottom click-and-tap zones, vertical flick, vertical Slide
animation (incl. with a double-page spread), zoom-in pan behaviour, and mode persistence all work.

Adds a paged top-to-bottom reading mode to the reader — one page fills the viewport (same fit /
zoom / rotation behaviour as `LeftToRight`), but page-turns advance vertically instead of
horizontally.

## Background

The reader has six `ReadingMode` values (`src/Paperbunkr.Data/Entities/ReadingMode.cs`):

- **Paged** (one bitmap at a time, discrete turn): `LeftToRight`, `RightToLeft`
- **Continuous / scroll**: `VerticalContinuous`, `HorizontalContinuous`,
  `HorizontalContinuousRightToLeft`, `Webtoon`

There is no *paged vertical* mode — turn one page at a time, advancing downward. ComicRack CE has
no vertical mode of any kind (its model is `Single`/`Double`/`DoubleAdaptive` page layout plus a
`RightToLeftReading` bool), so this is a deliberate Paperbunkr addition with no parity constraint.

`PageCanvas` (`src/Paperbunkr.App/Views/PageCanvas.cs`) already splits its render and input paths on
`IsContinuous`. Paged navigation runs through `LeftCommand`/`RightCommand` (bound to the
ViewModel's `GoLeftCommand`/`GoRightCommand`), driven by:

- `LeftKey`/`RightKey` gestures (remappable, default physical Left/Right arrows)
- mouse wheel — `Delta.Y < 0` → `RightCommand` (next), `Delta.Y > 0` → `LeftCommand` (prev)
- click zones (`InvokeZoneCommand`, left/right half) and touch tap zones (`InvokeTouchZone`,
  left/right thirds)
- horizontal touch flick (`OnPointerReleased`)

`GoLeft`/`GoRight` in the ViewModel map spatial → logical via `_isRightToLeft`. Page-turn
animation is `PageTransitionStyle` (`None`/`Slide`/`Crossfade`) with `PageTransitionDirection`
(`Left`/`Right` only); `ReaderPageVisualHandler` translates the Slide along X.

## Scope

**In scope:** a new `ReadingMode.TopToBottom` value, vertical page-turn gestures in `PageCanvas`,
vertical Slide animation, the reader flyout entry + icon, and the two Detail-screen label switches
that currently fall to a wrong default for an unknown mode.

**Out of scope:**

- An RTL vertical variant (no `TopToBottomRightToLeft`) — vertical reading has no
  left/right-origin ambiguity the way horizontal does.
- "Pan to the page edge, then the next turn gesture advances" (PDF-style page-down). Today's paged
  modes don't do this in *any* direction (a zoomed-in wide page under `LeftToRight` pans on
  Right-arrow, never turns at the edge); adding it is a separate change across all paged modes, not
  this one.
- Any change to the continuous vertical modes (`VerticalContinuous`, `Webtoon`).
- A per-mode default fit mode. `TopToBottom` inherits the series/issue/global fit mode like every
  other paged mode.

## 1. The `ReadingMode` value

Add `TopToBottom` to the enum, positioned after `RightToLeft` (keeps the two paged modes together,
ahead of the continuous block):

```csharp
public enum ReadingMode
{
    LeftToRight,
    RightToLeft,
    TopToBottom,
    VerticalContinuous,
    HorizontalContinuous,
    HorizontalContinuousRightToLeft,
    Webtoon
}
```

Name chosen to mirror `LeftToRight`/`RightToLeft` (paged, direction-named) and stay unambiguous
next to `VerticalContinuous`. The user-facing label everywhere is **"Vertical"**.

Stored via the codebase-wide `HasConversion<string>()` enum mapping — **no EF migration**, exactly
as the `HorizontalContinuousRightToLeft`/`Webtoon` additions needed none. A row written before this
change still round-trips; a row somehow holding `"TopToBottom"` before the enum value exists would
throw on parse, but nothing can write that value until this ships.

## 2. `ReaderScreenViewModel`

`ApplyReadingMode` (the shared method `Load` / `ToggleReadingMode` / `SetReadingMode` all call, so
the label / spatial-flip / continuous-mode switches can't drift):

- **Not** added to the `IsContinuousMode` predicate — `TopToBottom` stays paged, so it keeps the
  `PageImageDecoder` path, the 1×–4× zoom clamp, double-page pairing, etc.
- `_isRightToLeft` stays `false` (no vertical RTL).
- `EffectiveReadingMode = TopToBottom` — this is what `PageCanvas` binds and switches on.
- `ReadingModeLabel` switch gains `ReadingMode.TopToBottom => "Vertical ▾"`.

`GoLeft` / `GoRight` are unchanged: with `_isRightToLeft` false, `GoLeft` → `PreviousPage`,
`GoRight` → `NextPage`. In vertical mode "previous" is up and "next" is down — that mapping lives
in `PageCanvas` at the gesture layer, not here, so the ViewModel's page-stepping logic (including
the double-page pair-aware `PreviousPage` step size) is untouched.

## 3. `PageCanvas` — vertical page-turn gestures

New private predicate:

```csharp
private bool IsPagedVertical => ReadingMode == ReadingMode.TopToBottom;
```

`IsPagedVertical` is **not** part of `IsContinuous`, so the paged render path
(`PushPagedVisualData`, single `Page` bitmap) is used unchanged. Only input routing changes, and
only when not zoomed — every vertical gesture below sits behind the same `CanPan()` check that
already makes horizontal paging yield to panning when a page is zoomed in.

| Handler | Change |
|---|---|
| `OnKeyDown` (paged branch, after the `CanPan()` arrow-pan check) | When `IsPagedVertical`: `Key.Up` → `ExecuteDirectional(LeftCommand, PageTransitionDirection.Up)`, `Key.Down` → `ExecuteDirectional(RightCommand, PageTransitionDirection.Down)`. The existing `LeftKey`/`RightKey` gesture matches still run afterward (unchanged), so a user who remapped them, or just prefers arrows-left/right, keeps that too. |
| `OnPointerWheelChanged` (paged branch) | No change — `Delta.Y < 0` → next / `Delta.Y > 0` → prev is already the vertical convention. Only the `PageTransitionDirection` passed to `ExecuteDirectional` changes: `Down`/`Up` instead of `Right`/`Left` when `IsPagedVertical`. |
| `InvokeZoneCommand(Point p)` (was `double x`) | Split on `p.Y` vs `Bounds.Height / 2` when `IsPagedVertical` (top half → prev/`Up`, bottom half → next/`Down`); `p.X` vs `Bounds.Width / 2` otherwise. Signature widened to take the full point. |
| `InvokeTouchZone(Point p)` | When `IsPagedVertical`: top third of `Bounds.Height` → prev/`Up`, bottom third → next/`Down`, middle → reserved no-op (unchanged intent). Horizontal thirds otherwise. |
| `OnPointerReleased` touch flick | When `IsPagedVertical`: test `dy = end.Y - start.Y` with the same `MinFlickDistance` / `MaxFlickDurationMs` thresholds and the dominant-axis guard (`|dy| > |dx|`). Flick **up** (`dy < 0`, content pushed up) → next/`Down`; flick **down** → prev/`Up`. Horizontal `dx` path otherwise. |

`ExecuteDirectional` itself is unchanged — it already takes a `PageTransitionDirection` parameter
and sets `_pendingTurnDirection` before invoking the command; it just receives `Up`/`Down` now.

## 4. Vertical Slide animation

### `PageTransitionDirection`

```csharp
internal enum PageTransitionDirection { Left, Right, Up, Down }
```

### `PageTransitionMath.SlideOffset`

Currently: `sign = direction == Right ? 1 : -1`. Extend so forward turns (`Right`, `Down`) share
`sign = +1` and backward turns (`Left`, `Up`) share `sign = -1`:

```csharp
double sign = direction is PageTransitionDirection.Right or PageTransitionDirection.Down ? 1 : -1;
```

Return value is unchanged — an `(OutgoingOffset, IncomingOffset)` pair along "the main axis". The
caller decides whether that axis is X or Y. The `mainAxisExtent` parameter is documented as
"always horizontal"; the doc comment updates to "the turn's main axis — viewport width for
`Left`/`Right`, height for `Up`/`Down`".

### `ReaderPageVisualHandler.RenderTransition`

The Slide branch (currently `SlideOffset(data.Direction, progress, data.Bounds.Width)` then
`DrawTransitionSide(..., offsetX: outgoingOffset, ...)`):

- axis from direction: `bool vertical = data.Direction is PageTransitionDirection.Up or PageTransitionDirection.Down;`
- `extent = vertical ? data.Bounds.Height : data.Bounds.Width`
- `DrawTransitionSide` / `RenderSpread` currently take a `double offsetX` and translate by
  `new Vector(offsetX, 0)` (both the single-page `offsetPlan` and the spread's combined
  `shiftedDestRect`). Replace the `offsetX` parameter with a `Vector offset`; the Slide branch
  passes `new Vector(o, 0)` for horizontal or `new Vector(0, o)` for vertical. Every existing
  call site that passes `offsetX: 0` passes `default` (zero vector) instead — behaviour identical.

`Crossfade` and `None` never read `Direction` and are untouched. The pre-existing
`isRightToLeft` spread-placement logic (`primaryLeftFraction`) is orthogonal — it decides which
half of a *horizontal* pair is the primary page and is unaffected by a vertical turn.

## 5. Double-page spread — unchanged

`DoublePagePairingActive` gates on `_decoder is not null && EffectivePageLayoutMode ==
PageLayoutMode.Double && !IsContinuousMode`. `TopToBottom` is not continuous, so a user with
double-page enabled keeps a side-by-side pair. The vertical Slide translates the whole combined
spread unit along Y (via the `Vector` offset in §4); the two pages' left/right placement *within*
the spread is untouched. Advancing still steps by the pair-aware amount `NextPage`/`PreviousPage`
already compute. No code change in the pairing path.

## 6. UI and pickers

- **Reader flyout** (`ReaderScreen.axaml`, the reading-mode `clusterPill`'s `Flyout`): new row
  between "Right to Left" and "Vertical (Continuous)":
  `<Button Classes="drawerRow" Content="Vertical" Command="{Binding SetReadingModeCommand}" CommandParameter="{x:Static entities:ReadingMode.TopToBottom}" />`
  The flyout's `StackPanel` width may need a small bump if "Vertical" plus the others reflow — an
  implementation-time check, not a design decision.
- **`ReadingModeIconConverter.KeyFor`** (`src/Paperbunkr.App/Views/ReadingModeIconConverter.cs`):
  add `ReadingMode.TopToBottom => "PbIconArrowDown"`. The `PbIconArrowDown` geometry already
  exists (added in the reader-chrome icon pass). `VerticalContinuous`/`Webtoon` already map to the
  same glyph — three modes sharing the "down" direction indicator is correct.
- **`DetailTabsViewModel.SetReadingModeLabel`**: add `ReadingMode.TopToBottom => "Vertical ▾"`.
  Today an unknown mode falls to the `_ => "Left to Right ▾"` default — wrong for this value.
  (`ToggleReadingMode` is an LTR⇄RTL flip only and needs no change.)
- **`MangaDetailScreenViewModel.ReadingModeBadge`**: add `ReadingMode.TopToBottom => "Vertical"`
  to the switch (same default-fallthrough problem).
- **`SeriesBulkFieldDescriptor`** (line ~59): uses `Enum.GetNames<ReadingMode>()` for its options
  and `.ToString()` for display, so `TopToBottom` appears automatically as the raw name
  `"TopToBottom"` — consistent with how `HorizontalContinuousRightToLeft` etc. already render in
  that bulk-edit dropdown. No change; noted so the plan doesn't "fix" it.

## 7. Fit mode

`TopToBottom` takes whatever fit mode resolves from the issue/series/global chain, same as the
horizontal paged modes. Known interaction, not a bug: a tall page under `FitWidth` overflows the
viewport, so `CanPan()` is true and Down-arrow pans within the page rather than turning — the user
turns with the wheel or a bottom tap-zone instead. This exactly mirrors `LeftToRight` with a wide
zoomed page and Right-arrow. See the out-of-scope note about PDF-style pan-then-turn.

## 8. Testing

**Automated:**

- `PageTransitionMathTests` — `SlideOffset(Down, …)` produces the same offsets as
  `SlideOffset(Right, …)`; `SlideOffset(Up, …)` matches `SlideOffset(Left, …)`. Existing
  `Left`/`Right` cases unchanged.
- `ReaderScreenViewModelTests` — setting `TopToBottom` (via `SetReadingModeCommand` and via a
  seeded `Series.ReadingMode`): `IsContinuousMode` is `false`, `ReadingModeLabel` is `"Vertical ▾"`,
  `EffectiveReadingMode` is `TopToBottom`; `SetReadingModeCommand` persists `Series.ReadingMode`.
- `ReadingModeIconConverterTests` — `KeyFor(TopToBottom) == "PbIconArrowDown"`; the existing
  `KeyFor_CoversEveryReadingModeValue` theory already fails loudly if a new enum value is missed.
- If `InvokeZoneCommand` / flick direction resolution is extracted to a pure helper (the
  `GridKeyboardNavigation` / `ZoomPanMath` precedent — a `static (bool forward, PageTransitionDirection dir) ResolveZone(Point, Size, bool vertical)` or similar), unit-test the top/bottom vs left/right split and the flick dominant-axis guard. If it stays inline in `PageCanvas`, flag it for manual coverage rather than forcing an extraction that doesn't pay for itself.

**Manual on-screen** (standing GUI-automation caveat — no unattended desktop automation for this
project):

- Switch a series to "Vertical" from the reader flyout; confirm the pill shows a ↓ icon and
  "Vertical".
- Down / Up arrows, mouse wheel, click top/bottom half, touch tap top/bottom third, and vertical
  touch flick each turn the page in the right direction.
- With `PageTransitionStyle = Slide`, the turn animates vertically (new page enters from the
  bottom on a forward turn, top on a backward turn); `Crossfade` and `None` behave as before.
- With double-page spread on, the pair still renders side-by-side and the turn animates the pair
  as one unit vertically.
- Zoom in past fit: Down-arrow pans within the page; wheel-down still turns.
- Reopen the series later — the `TopToBottom` mode persisted.

## Risks / notes

- **`offsetX` → `Vector` parameter change in `ReaderPageVisualHandler`** touches several private
  method signatures (`DrawTransitionSide`, `RenderSpread`, and their call sites). All internal to
  one file; every current caller passes `0` on the X axis, so the mechanical translation to
  `default`/`new Vector(x, 0)` is behaviour-preserving. Called out so the plan sequences it as one
  atomic step with its own build/test checkpoint.
- **`InvokeZoneCommand` signature widening** (`double x` → `Point`) has one caller
  (`OnPointerPressed`), which already has the full point.
- Vertical flick direction deliberately follows *scroll* convention (flick content up = go
  forward), matching the wheel (`Delta.Y < 0` = forward) and the continuous modes, not the
  horizontal paged flick's "flick left = forward" spatial convention. A vertical page-turn is
  conceptually a scroll-forward, so this is the consistent choice.
