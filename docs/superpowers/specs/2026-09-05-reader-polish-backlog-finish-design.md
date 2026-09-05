# Reader polish: closing the backlog (split-page part navigation, clock/battery, touch center-zone)

*Date: 2026-09-05. Closes the last three items on the Reader polish Beta backlog
(`docs/Paperbunkr-Roadmap.md`'s "Still genuinely open" list, `docs/alpha-todo.md`'s "Bonus, ahead
of schedule" section). Magnifier remains explicitly out of scope per prior user direction ("we have
a zoom slider").*

## 0. Correcting a stale doc claim

`docs/ce-feature-inventory.md`'s "Split-page 'part' navigation" row currently says CE has "no Part
concept, confirmed absent." That's wrong — verified directly against CE source this session:
`ComicRack.Engine.Display.Forms/ImageDisplayControl.cs` (`ImagePartInfo`, `PartPageToDisplay`,
`DisplayOutput.PartCount`/`GetPartGridSize`/`GetPartRectangle`) and `ComicDisplay.cs`
(`DisplayNextPageOrPart`, `MovePart`, `DisplayPart`) implement a real, working feature: when a page
is zoomed past what the viewport shows, it's split into a grid of viewport-sized tiles ("parts"),
and Next/Previous-page steps through those tiles in reading order before actually turning the page.
This doc's row will be corrected alongside this work (see §5).

## 1. Split-page part navigation

### Current gap
`ReaderScreenViewModel.NextPage()`/`PreviousPage()` (lines ~1946-1975) advance/retreat the page
index unconditionally. Zoom and pan (`PanOffsetX`/`PanOffsetY`) are session-only and never reset on
a page turn by default (`ResetZoomOnPageChange` off) — so today, turning the page while zoomed in
just jumps to the next page's default pan position, discarding whatever part of the current
(zoomed) page hadn't been seen yet.

### New pure-math helper: `PagePartMath.cs`
Same directory and shape as `ZoomPanMath`/`SpreadLayoutMath` (plain value types, no Avalonia
context, `internal static class`):

- `(int Cols, int Rows) ComputePartGrid(Size viewport, PixelSize content, double zoom, ImageFitMode fitMode, bool fitOnlyIfOversized)`
  — same ceil-division grid CE's `GetPartGridSize` uses, built on the effective scale from
  `ZoomPanMath.ComputeBaseScale(viewport, content, fitMode, fitOnlyIfOversized) * zoom`. Returns
  `(1, 1)` when the content fits inside the viewport (no overflow) — matching CE's `GetPartGridSize`
  early-return.
- `int FindNearestPart((int Cols, int Rows) grid, Size viewport, PixelSize content, double scale, double panX, double panY, bool rightToLeft)`
  — given the *current* pan offset, the grid cell index (row-major reading order; RTL reverses
  column order per row) nearest to it. Mirrors CE's `GetBestPartFit`: deriving "current part" from
  pan offset (rather than tracking separate mutable state) means a free mouse-drag pan between
  page-turns is still respected, and there's no new state to keep in sync with existing pan/zoom
  code.
- `(double X, double Y) PanForPart(int partIndex, (int Cols, int Rows) grid, Size viewport, PixelSize content, double scale, bool rightToLeft)`
  — the pan offset that centers a given grid cell, clamped via the existing
  `ZoomPanMath.ClampPan`.

Reading order: row-major, left-to-right, top-to-bottom for LTR; row-major, right-to-left per row
for RTL (mirrors CE's `rightToLeftReading` column-order flip, simplified since Paperbunkr's RTL
model is a single `ReadingMode.RightToLeft` rather than CE's dual Mirror/FlipParts modes).

### Double-page spread extension
Per approved scope, part navigation also applies to double-page spreads. When
`PageCanvas.SecondaryPage` is set, the grid is computed over
`SpreadLayoutMath.ComputeCombinedSize(primaryPixelSize, secondaryPixelSize).Combined` instead of
the solo page's `PixelSize` — reusing the exact "one wider virtual page" trick the spread feature
already established (`SpreadLayoutMath.cs`'s own doc comment), not a new layout model.

### Wiring: `PageCanvas.ExecuteTurn(bool forward)`
Every page-turn input (wheel, arrow/PageUp/PageDown keys, click zones, flick, toolbar buttons)
already funnels through this one method (confirmed: `ExecuteTurn` is called from
`OnPointerWheelChanged`, `OnKeyDown`, and the click-zone/flick handlers). It becomes:

```csharp
private bool ExecuteTurn(bool forward)
{
    if (!IsContinuous && TryStepPart(forward))
    {
        return true; // stayed on the same page, moved to the next/previous part
    }

    var (command, direction) = forward ? ForwardTurn : BackwardTurn;
    return ExecuteDirectional(command, direction);
}
```

`TryStepPart(forward)`: computes the current grid via `PagePartMath`, finds the nearest current
part, and if there's a next/previous cell in that direction, pans to it and returns `true` -
*without* invoking `LeftCommand`/`RightCommand`, so `ReaderScreenViewModel`'s page index never
changes. Returns `false` when already on the grid's last/first cell (or grid is 1×1), letting
`ExecuteTurn` fall through to the real page-turn exactly as today.

**Landing position on a genuine page turn caused by exhausting parts:** matches CE's convention
(`part = (oldPage >= newPage) ? (PartCount - 1) : 0`, `NavigationOverlay.cs`) - explicitly set pan
to part 0 (forward turn) or the new page's last part (backward turn) rather than leaving the stale
pan value from the old page, which could land at a nonsensical spot once `ClampPan` clamps it
against the new page's (possibly differently-shaped) dimensions.

**Scope cut:** continuous/webtoon modes (`IsContinuous`) are unchanged - no parts concept there,
matching CE (which has no continuous mode at all to have one in).

### "Part X/Y" label
Two new read-only-from-XAML `PageCanvas` properties, `CurrentPart`/`PartCount` (plain
`StyledProperty<int>`, following the exact pattern `PanOffsetXProperty` already uses), recomputed
in the control's existing pan/zoom/page property-changed dispatch (the list at line ~445 that
already reacts to `PageProperty`/`SecondaryPageProperty`/`ZoomLevelProperty`/`PanOffsetXProperty`/
`PanOffsetYProperty`). `ReaderScreen.axaml`'s Navigate cluster (top-left) gets a new `TextBlock`
next to the existing `PageLabel`, bound via `{Binding #PageCanvasControl.CurrentPart}` /
`{Binding #PageCanvasControl.PartCount}` (the control already has `x:Name="PageCanvasControl"`,
so this needs no new View-to-ViewModel plumbing). `IsVisible` bound to `PartCount > 1` via a small
`FuncValueConverter` (or a computed bool property on `PageCanvas` itself, matching the codebase's
existing preference for computed properties over inline converters where reasonable) so it's
invisible whenever the page fits the viewport.

## 2. Clock + battery status

New elements in the existing Actions cluster (`ReaderScreen.axaml`, top-right, alongside the
bookmark/drawer/fullscreen buttons) - rides the cluster's existing idle-fade/capsule chrome
unchanged, no new UI mechanism:

- **Clock:** a `TextBlock` bound to a new `ReaderScreenViewModel.CurrentTimeLabel` string property,
  refreshed once a minute by a `DispatcherTimer` (started in `Load()`, stopped in the VM's existing
  cleanup path) - CE repaints its clock on every frame (`timeLabel.Drawing`), but a Reader that
  isn't continuously repainting has no equivalent hook, and a clock only needs minute resolution
  anyway.
- **Battery:** a `TextBlock` (or icon + percentage) bound to a new `BatteryStatusLabel` string,
  populated via a new `BatteryStatusService` wrapping a P/Invoke of Win32 `GetSystemPowerStatus`
  (Windows-only, matching this project's existing Windows-only scope - `ShellRegister.cs` already
  P/Invokes raw Win32 registry APIs directly, same pattern). `BatteryStatusLabel` is `null`/empty
  and the element hidden (`IsVisible` bound to a null-check) when the API reports no battery present
  (`BATTERY_FLAG_NO_BATTERY`) - matches CE's own guard
  (`SystemInformation.PowerStatus.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery`).
  Refreshed on the same once-a-minute timer as the clock (battery percentage doesn't need
  sub-minute polling).

## 3. Touch center-zone → chrome toggle

`docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md` §4 left the 3×3
touch tap grid's center column (all three rows) as a documented no-op: "reserved... the natural
target is 'toggle chrome/menu,' but Paperbunkr has no minimal-chrome/fullscreen mode yet." Chrome
(`ReaderScreenViewModel.ShowChrome`, driven by `NotifyCursorActivity`/idle-fade) shipped later
(2026-08-11 fullscreen, 2026-08-25 chrome redesign). This item is just wiring that already-reserved
zone to toggle chrome visibility - no new gesture geometry, no change to the left/right columns or
double-tap/flick behavior.

Concretely: verified in `PageCanvas.InvokeTouchZone` (line ~2062) - `PageTurnGestureMath.ResolveZone(p,
Bounds.Size, IsPagedVertical, divisions: 3)` returns `bool?`, `null` for the center third, and
`InvokeTouchZone` currently does nothing when it's `null` (no `else` branch). This adds that `else`:
calls a new `ToggleChromeCommand`-equivalent that flips `ShowChrome` and resets the idle-fade timer
accordingly - if chrome is currently showing, hide it immediately (timer treated as already
elapsed); if hidden, show it and restart the normal 3-second idle countdown. This matches the "tap
center to toggle UI" convention the original spec's own reasoning anticipated.

## 4. Testing

- `PagePartMathTests` (new, pure-function unit tests, no Avalonia context needed - matching
  `ZoomPanMathTests`/existing convention): grid computation at various zoom levels/fit modes
  (1×1 when content fits, 2×1/1×2/2×2/etc. when it doesn't), `FindNearestPart` against known pan
  offsets, `PanForPart` round-trips back through `FindNearestPart`, RTL column-order reversal,
  double-page spread combined-size grid computation.
- `ReaderScreenViewModelTests`/`PageCanvas`-adjacent tests (wherever the existing zoom/pan tests
  for `PageCanvas` behavior actually live - confirmed during planning, not guessed here): stepping
  through parts via `NextPage`/`PreviousPage`-equivalent triggers doesn't change the page index
  until the last part; landing position (part 0 vs. last part) on the page turn that follows;
  1×1 grid (unzoomed) turns the page immediately as today (regression guard - this is the most
  important case to not break).
- `BatteryStatusServiceTests`: mock/fake the P/Invoke boundary (an interface wrapping the raw call,
  matching how other Win32-touching services in this codebase are tested without hitting the real
  API) - no-battery-present hides the label, a real percentage formats correctly.
- Manual on-screen verification (same standing no-GUI-automation caveat as every prior Reader
  spec): actual part-stepping behavior when zoomed on a real book, clock ticking, battery reading
  matching the OS's own indicator, touch center-tap toggling chrome on a touch-capable device.

## 5. Doc corrections bundled with this work

- `docs/ce-feature-inventory.md`'s "Split-page 'part' navigation... doesn't apply (no Part concept,
  confirmed absent)" row corrected to reflect the real CE mechanism found this session, and marked
  shipped once this lands.
- `docs/Paperbunkr-Roadmap.md`'s "Still genuinely open" line (Reader polish section) updated to
  drop all three items once shipped and verified.

## Explicitly not in this pass

Magnifier (already declined by the user - "we have a zoom slider"). CE's `MovePart`/small-nudge
sub-pixel scroll commands - Paperbunkr's existing continuous free-pan (drag/wheel/arrow when
zoomed) already covers fine-grained movement; only the *discrete* Next/Previous-page part-stepping
behavior is being ported. Any change to continuous/webtoon scroll mechanics. Any change to the
left/right tap-zone or double-tap/flick touch behavior beyond the center column.
