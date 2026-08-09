# Reader — Right-to-Left (Manga) Navigation

*Date: 2026-08-07. First real Reader-capability build since the reader canvas landed
(docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md). Scoped after a CE-source triage
of the Preferences Reader tab (`Settings.cs`'s `[Category("Right to Left")]` group —
`TrueRightToLeftReading`/`RightToLeftReadingMode`/`LeftRightMovementReversed`) concluded Reader was
a zero-real-surface dead end on its own — every other CE Reader setting gates a capability
(zoom, scroll, full-screen, overlays, color adjustment, keyboard remapping) that doesn't exist at
all in Paperbunkr yet. RTL/manga page order is the one exception: `ReadingMode.RightToLeft` already
exists on `Series`/`Issue` (docs/superpowers/specs §6 of onboarding.md), but `NextPage`/`PreviousPage`
never read it. This spec wires it up and, in doing so, gives the Reader tab its first real content.*

## 1. Scope decision, vs. CE

CE's actual RTL model has two independent knobs: `RightToLeftReadingMode` (`FlipPages` vs.
`FlipParts` — the latter does true pixel/part-level mirroring of a two-page spread) and
`LeftRightMovementReversed` (only takes effect when the mode is `FlipParts`, per
`ComicDisplayControl.IsMovementFlipped`). Paperbunkr has no double-page-spread/two-page-navigation
concept at all (`ReadingMode.cs`'s own doc comment already calls that out as explicitly excluded),
so `FlipParts`-style part mirroring isn't a fit. The only meaningful, buildable piece is the
navigation-direction flip itself — the same UX every mainstream manga reader defaults to: tapping/
pressing the *physical* left side continues forward through the book when it's a right-to-left
title. That's what this spec builds, as a direct simplification of CE's two-knob system into one.

## 2. Data model

`AppSettings` gains one column:

- `ReverseRtlNavigation` (`bool`, default **`true`**) — deliberately diverging from CE's
  `LeftRightMovementReversed` default of `false`: CE's default is safe specifically because its
  default RTL mode (`FlipPages`) already does visual page mirroring, so navigation direction not
  flipping still reads correctly. Paperbunkr has no such mirroring — without this flag on,
  `ReadingMode.RightToLeft` would do nothing observable at all, which isn't a real feature. On by
  default matches what a manga reader is expected to do out of the box.

## 3. Navigation flip mechanism

`ReaderScreenViewModel.Load()` already computes `readingMode = issue.ReadingModeOverride ??
series.ReadingMode`. It now also stores:

```
_isRightToLeft = readingMode == ReadingMode.RightToLeft && context.GetOrCreateAppSettings().ReverseRtlNavigation;
```

Two new commands sit alongside the existing `PreviousPage()`/`NextPage()` (which keep their current
page-index-stepping behavior unchanged — every existing test that calls
`vm.NextPageCommand`/`vm.PreviousPageCommand` directly keeps passing as-is):

```
GoLeft()  → _isRightToLeft ? NextPage() : PreviousPage()
GoRight() → _isRightToLeft ? PreviousPage() : NextPage()
```

`PageCanvas`'s bound command properties are renamed from `PreviousPageCommand`/`NextPageCommand` to
`LeftCommand`/`RightCommand` — they were always spatial (Left key / left-half click →
one property, Right key / right-half click → the other), just named for the LTR case that used to
be the only case; the rename makes the actual contract explicit and removes the "`NextPageCommand`
fires on a Left click" mismatch RTL would otherwise create. `ReaderScreen.axaml`'s `PageCanvas`
element and the bottom-scrubber ◀/▶ buttons all rebind to `GoLeftCommand`/`GoRightCommand`. Nothing
else about page rendering, numbering, the progress bar, or thumbnail order changes — RTL only ever
changes which physical direction is "forward."

## 4. Preferences → Reader tab

`PreferencesScreenViewModel` gains a third real tab (`"reader"`, `IsReaderTab`), same
`ActiveTab`-string + computed-flag pattern as Appearance/Behavior/Libraries/Advanced. One checkbox,
applying immediately (no Save step, matching every other tab):

- "Reverse left/right page-turn direction for right-to-left books" → `ReverseRtlNavigation`

Persists straight to `AppSettings` via the existing `PersistBehaviorSetting` helper — no new
service.

## 5. Detail screen — setting a series' reading mode

Today `Series.ReadingMode` is populated only by CE library migration; there is no UI to set it,
which would make RTL navigation unreachable for anyone building a library natively in Paperbunkr.
`DetailTabsViewModel`'s Details sub-tab shows `ReadingModeLabel` as a plain read-only `TextBlock`
today. It becomes a click-to-toggle field:

- Label gains the same "▾" chevron affordance `ReaderScreenViewModel.ReadingModeLabel` already
  uses for its (display-only) reading-mode chip, signaling it's interactive here.
- New `ToggleReadingModeCommand`: binary flip between `LeftToRight` and `RightToLeft` only.
  `VerticalContinuous`/`HorizontalContinuous` (unreachable via any UI today, only via CE migration
  data, and with no scroll-paging implementation behind them — see docs/superpowers/specs/
  2026-08-07-preferences-behavior-tab-design.md §1) collapse to `RightToLeft` if toggled from that
  state. This keeps the affordance a simple two-state click rather than growing a mode picker for
  states nothing else in the app produces or consumes yet.
- `DetailTabsViewModel` gains a `Func<PaperbunkrDbContext>` constructor parameter (default
  `PaperbunkrDb.CreateContext`, same test-injection seam as every other DB-touching ViewModel —
  `DetailTabsViewModel` currently has none, and currently has no test coverage at all) and captures
  `_seriesId` in `LoadSeries`. The toggle writes `Series.ReadingMode` directly and re-derives
  `ReadingModeLabel` locally — no round-trip through `DetailScreenViewModel`, matching how
  `DetailScreenViewModel.OnSelectedContentTypeChanged` already persists its own field directly
  rather than delegating.
- This only changes `Series.ReadingMode` (the default), never `Issue.ReadingModeOverride` — per-
  issue override stays a data-only field with no edit UI, consistent with the rest of the app
  having no per-issue metadata editing at all yet.

## Testing

- `AppSettingsTests`: `ReverseRtlNavigation` defaults to `true`, round-trips via
  `GetOrCreateAppSettings`. EF migration must set `.HasDefaultValue(true)` explicitly in
  `OnModelCreating` (the Behavior-tab gotcha — a bare `= true` initializer alone would silently
  backfill existing rows to `false` on migrate).
- `ReaderScreenViewModelTests`: `GoLeft`/`GoRight` step forward/backward correctly for LTR issues;
  flip correctly for RTL issues; flip is suppressed when `ReverseRtlNavigation` is off even for an
  RTL issue; issue-boundary crossing (existing `AutoNavigateComics` behavior) still fires correctly
  through the flipped commands.
- `PreferencesScreenViewModelTests`: `IsReaderTab` tab-switch flag; toggling the checkbox persists
  to `AppSettings`.
- `DetailTabsViewModelTests` (new file — no existing coverage of this ViewModel): `LoadSeries`
  populates `ReadingModeLabel`; `ToggleReadingMode` flips `LeftToRight ↔ RightToLeft`, persists to
  the database, and collapses `VerticalContinuous`/`HorizontalContinuous` to `RightToLeft`.
- Manual verification: same no-GUI-automation approach as prior specs — build + run real tests,
  then ask the user to toggle a series to Right to Left on the Detail screen, open it in the
  Reader, and confirm the left/right zones and arrow keys actually advance in the flipped
  direction; then flip the new Preferences checkbox off and confirm the same issue now navigates
  normally again.
