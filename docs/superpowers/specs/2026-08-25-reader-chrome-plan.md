# Reader Chrome — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-25-reader-chrome-design.md*

Verified directly against current source before writing this plan (not guessed — full detail in the
research pass this plan is built from):
- `ReaderScreenViewModel(Action goBack)` is the only constructor, no injected services today — adding
  `KeyBindingService` here is a new (additive) dependency, first time this VM takes one.
- Fullscreen overlay mechanism is `ShowFullscreenOverlays` (bool) + `OverlayAutoHideDelay = 3s` +
  `RestartOverlayAutoHideTimer()`/`_overlayAutoHideTimer`, gated by `if (!IsFullscreen) return;` inside
  `NotifyCursorActivity`. Generalizing to windowed mode means dropping that guard, not building a
  parallel mechanism.
- `Border.floatingPanel` (`Styles/Primitives.axaml:133-144`) already gives translucent surface + border
  + elevation shadow + `Opacity` `DoubleTransition` on `PbMotionFast`/`PbMotionEase` — the clusters and
  drawer apply this class directly, no new Border style needed.
- `PbGlowRing` (`Primitives.axaml`) is a `:pointerover`/`:focus-visible`-triggered `BoxShadowsTransition`
  — a hover-state trigger, not a "flash once on command" precedent. The bookmark-toggle pulse needs a
  transient `Classes` toggle (add on toggle, remove after `PbMotionFast` elapses), not a direct reuse.
- `Skip_Back.png`/`Skip_Forward.png` are confirmed single-consumer (`ReaderScreen.axaml` lines 496, 508
  only) — safe to convert, no conflict audit needed beyond what's already done.
- `KeyBindingService.GetKey(context, commandId)`'s `catch (ArgumentException) → descriptor.DefaultGesture`
  is the exact fallback pattern import's corrupt-file handling mirrors.
- `ReadingScreenViewModel`'s `ImportCbl`/`ExportCbl` (lines 828-896) + `IFilePickerService`
  (`PickOpenFileAsync`/`PickSaveFileAsync`) is the real precedent for import/export — `PreferencesScreenViewModel`
  already has `_filePicker` injected (used today for "Install Skin"), no new DI wiring needed.
- `PreferencesScreenViewModelTests`'s `CreateViewModel(...)` factory already accepts fakes
  (`IFilePickerService? filePicker = null`, etc.) with real defaults — extend it, don't replace it.

## Step 1: Icon conversions

**Files:** `src/Paperbunkr.App/Styles/Icons.axaml` (edit), `src/Paperbunkr.App/Assets/Icons/icon-mapping.md` (edit)

**What:** Add `PbIconSkipBack`/`PbIconSkipForward` `StreamGeometry` resources (hand-computed, same
stroked-outline style as every other icon in this set — not traced from the PNGs). Add a "Phase 6
(Reader chrome)" table to `icon-mapping.md` mapping both, remove `Skip_Back`/`Skip_Forward` from the
"Still raster" list.

**Depends on:** none
**Verify:** Visual check once wired into Step 5's page-turn cluster.

## Step 2: Generalize idle-fade + add drawer state (`ReaderScreenViewModel`)

**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit)

**What:**
- Rename `ShowFullscreenOverlays` → `ShowChrome` (bool, `[ObservableProperty]`) — same field, same
  `OverlayAutoHideDelay`/`_overlayAutoHideTimer`/`RestartOverlayAutoHideTimer()` mechanism, but no
  longer fullscreen-only. `ToggleFullscreen()` keeps setting `ShowChrome = true` +
  `RestartOverlayAutoHideTimer()` on entry (still relevant — entering fullscreen should show chrome
  immediately, same as today).
- `NotifyCursorActivity()`: drop the `if (!IsFullscreen) return;` guard entirely — cursor activity
  restarts the fade timer in both windowed and fullscreen now. Call this from the root Grid's
  `PointerMoved` in both states (Step 5 removes the old windowed/fullscreen split that gated this).
- Add `[ObservableProperty] private bool _isDrawerOpen;` and `ToggleDrawerCommand` (`[RelayCommand]`).
  Opening the drawer does **not** touch `ShowChrome`/the fade timer — the design's own rule that the
  drawer doesn't idle-fade.
- Add `[ObservableProperty] private bool _isViewClusterCollapsed;` — true when hosting window width is
  below the ~720px threshold (Step 5 sets this from the View's `SizeChanged`/`Bounds` on the root
  control, simplest hook without inventing a new responsive-layout system).
- Constructor: add `KeyBindingService keyBindingService` parameter (new, additive dependency — first
  one this VM takes). Store as `_keyBindingService`.
- Add a `GetShortcutHint(string commandId)` helper: `_keyBindingService.GetKey(commandId).ToString()`
  wrapped in parens, e.g. `"(R)"` — used by Step 5's `ToolTip.Tip` bindings. Expose as a method, not a
  cached property, since it needs to reflect live remaps without this VM manually listening for
  `KeyBindingService` changes (call it fresh from an XAML converter or a thin wrapping property per
  command — decide the exact binding mechanism in Step 5 once the cluster/drawer markup is in front of
  you; both are valid, don't over-decide here).
- Bookmark-toggle glow: add `[ObservableProperty] private bool _bookmarkJustToggled;` set `true` in
  `ToggleBookmark`'s existing body, cleared via a short one-shot `DispatcherTimer` (reuse
  `PbMotionFast`'s ~150ms as the C# `TimeSpan`, matching the token's value even though this timer can't
  bind to the XAML resource directly).

**Depends on:** none
**Verify:** New `ReaderScreenViewModelTests` cases: `ShowChrome` starts true and fades to false after
`OverlayAutoHideDelay` in **windowed** mode too (previously only tested/true for fullscreen — confirm
by checking the existing test file for what it covers today before assuming this is net-new coverage
vs. an existing case that just needs its `IsFullscreen` assumption removed). `ToggleDrawerCommand`
flips `IsDrawerOpen` without touching `ShowChrome`. `ToggleBookmark` sets `BookmarkJustToggled` true
then false after the timer elapses (use a test-visible seam or just assert the immediate `true`, per
whatever this test file's existing timer-testing convention is — check it, don't invent a new one).

## Step 3: Keyboard shortcut hints — live binding

**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit, continues Step 2)

**What:** For every command with a real keyboard shortcut that will get a `ToolTip.Tip` in Step 5
(rotate CW/CCW, auto-scroll, fullscreen — the 4 that already have hints today — plus zoom in/out,
fit-mode, page-turn, bookmark-prev/next, which currently have none), expose a hint string sourced from
`_keyBindingService.GetKey(...)`, not a literal. Match each to its `KeyboardCommandRegistry` id from
the research (`ReaderRotateClockwise`, `ReaderRotateCounterClockwise`, `ReaderToggleAutoScroll`,
`ReaderToggleFullscreen`, `ReaderZoomIn`, `ReaderZoomOut`, `ReaderFitOriginal`/`FitAll`/`FitWidth`/
`FitHeight`/`FitBest` as applicable to whichever single fit-mode control gets the hint,
`ReaderPageTurnLeft`/`Right`, `ReaderPreviousBookmark`/`NextBookmark`).

**Depends on:** Step 2 (needs `_keyBindingService`)
**Verify:** New test: changing a binding via `KeyBindingService.SetKey` and re-reading the hint
property reflects the new gesture (proves it's live, not cached at construction).

## Step 4: Keyboard layout import/export

**Files:** `src/Paperbunkr.App/Services/KeyBindingIO.cs` (new), `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit), `src/Paperbunkr.App/Views/PreferencesScreen.axaml` (edit), `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)

**What:**
- `KeyBindingIO` static class, mirroring `CblReadingListIO`'s shape: `Export(PaperbunkrDbContext
  context, string path)` (serializes `KeyBindingService.GetAllBindings()` — command id + gesture string
  pairs — to a simple format, e.g. one `CommandId=GestureString` line per binding or a small JSON
  object; pick whichever this codebase's nearest text-export precedent uses, don't invent a third
  format) and `Import(PaperbunkrDbContext context, string path)` (parses, calls
  `KeyBindingService.SetKey` per line/entry, **skipping** any line that fails `KeyGesture.Parse` rather
  than aborting the whole import — same per-entry fallback philosophy as `GetKey`'s own
  `catch (ArgumentException)`, applied per-line instead of per-read).
- `PreferencesScreenViewModel`: `ImportKeyBindingsCommand`/`ExportKeyBindingsCommand` (`[RelayCommand]`
  async, mirroring `ImportCbl`/`ExportCbl`'s exact structure — `_filePicker.PickOpenFileAsync`/
  `PickSaveFileAsync`, then `KeyBindingIO.Import`/`Export`, then `RefreshKeyBindings()` to reflect the
  new state, then a `StatusMessage` update).
- `PreferencesScreen.axaml`: two buttons (Import/Export) added to the Keyboard Shortcuts section's
  existing header area, above or alongside the three group boxes — exact placement is a small layout
  call, not a design decision needing pre-approval.

**Depends on:** none (independent of Steps 1-3)
**Verify:** New `PreferencesScreenViewModelTests` cases using the existing `CreateViewModel(...)` fake
seam: export-then-import round-trips a remapped binding correctly; importing a file with one corrupt
line still imports every valid line in it (not an all-or-nothing failure) and doesn't throw.

## Step 5: Corner clusters + drawer (`ReaderScreen.axaml`)

**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit)

**What:** The largest step — replaces the top-toolbar `Border`/12-column `Grid` (current lines ~91-286)
and the two fullscreen-only overlay `Border`s (current lines ~453-478) with 4 `Border Classes="floatingPanel"`
clusters, each `Panel.ZIndex`-stacked over the canvas `Grid` (not laid out in the row-based `Grid` at
all — these become siblings of `PageCanvas` inside the body's column-1 `Grid`, `HorizontalAlignment`/
`VerticalAlignment` positioned per corner):

- **Navigate** (top-left): back button, `PageLabel` text — content moved from the old toolbar's
  column 0/7.
- **View** (bottom-left): reading-mode picker, fit-mode picker (`IsVisible="{Binding !IsContinuousMode}"`,
  unchanged rule), zoom controls — content moved from old columns 1/2/3. `IsVisible` on the fit/zoom
  portion additionally gated by `!IsViewClusterCollapsed` (Step 2); when collapsed, only the
  reading-mode picker shows here and fit/zoom relocate into the drawer's existing Page section (add
  them there, `IsVisible="{Binding IsViewClusterCollapsed}"` on that drawer copy so they don't appear
  in both places at once).
- **Page-turn** (bottom-center): prev-chapter, prev-page (now `Path Classes="pbIcon" Data="{StaticResource
  PbIconSkipBack}"` per Step 1), progress bar, next-page (`PbIconSkipForward`), next-chapter — content
  moved from the old bottom bar (current lines 483-518), which is deleted entirely (row 2 of the root
  `Grid` goes away; root `Grid.RowDefinitions` becomes just the body, no more `Auto,*,Auto`).
- **Actions** (top-right): bookmark quick-toggle (`Classes.justToggled="{Binding BookmarkJustToggled}"`
  driving a glow via a new `Border.actionIcon.justToggled` style using `PbGlowRing` — the transient-class
  technique noted in Step 2), overflow `⋮` (`Command="{Binding ToggleDrawerCommand}"`), fullscreen
  toggle — moved from old columns 10 (bookmark part only)/11.

All four clusters bind `IsVisible`/`Opacity` to `ShowChrome` with the `floatingPanel` style's own
`Opacity` `DoubleTransition` doing the fade (no separate fade mechanism needed — `IsVisible` for fully
gone, or just rely on `Opacity` alone and skip `IsVisible` if click-through-when-faded isn't desired;
confirm which during implementation by checking whether `IsHitTestVisible` needs to independently track
`ShowChrome` too, since a translucent-but-still-hit-testable cluster could intercept clicks meant for
the canvas underneath it — a real interaction detail, not pre-decided here).

Root `Grid`'s `PointerMoved="OnReaderPointerMoved"` stays wired the same way (calls
`NotifyCursorActivity`, per Step 2 now ungated).

Drawer: a new right-anchored `Border Classes="floatingPanel"` (~230px, per the design doc), `IsVisible=
"{Binding IsDrawerOpen}"`, containing the 4 labeled sections (Page/Adjust/Transition/Bookmarks) with
content moved from the old toolbar's columns 4/5/6 (rotate, auto-rotate, double-page-or-autoscroll),
8 (Adjust), 9 (transition), 10 (bookmark list portion only — the quick-toggle stays in Actions per
above).

**Depends on:** Steps 1, 2, 3 (needs the icon resources, `ShowChrome`/`IsDrawerOpen`/
`IsViewClusterCollapsed`/`BookmarkJustToggled`, and the live shortcut-hint properties all in place
before the markup can bind to them)
**Verify:** `dotnet build` with the XAML weave confirmed to have actually run (delete
`obj/Debug/net8.0/Paperbunkr.App.dll`/`.pdb` first if the build only touched `.axaml`, per this repo's
own `CLAUDE.md` gotcha); manual on-screen check — this is almost entirely a layout/interaction phase,
manual verification is the real test, flagged explicitly rather than assumed covered by unit tests.

## Step 6: Restyle thumbnail rail + on-canvas overlays

**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit, continues Step 5)

**What:**
- Thumbnail rail (current lines ~289-341, unchanged position/behavior per the design's explicit
  "stays persistent" call): swap `Border.thumb`'s hardcoded colors for `Pb*` tokens (`PbSurface2Brush`
  background, `PbBorderBrush` default border, `PbAccentBrush` for `.selected` — already using
  `PbAccentBrush`, so just the non-selected/background/badge colors need converting), same for the
  bookmark-ribbon/page-type-badge colors (`#E0B814`→`PbBadgeBrush` or similar, `#3A6EA5`→a real token,
  pick the closest existing one rather than adding a new color for a single small badge).
- Error card and the two chapter-transition cards (current lines ~411-451): swap `ReaderChromeBrush`/
  `ReaderBorderBrush`/`ReaderTextBrush`/etc. for their `Pb*` equivalents, replace the local
  `DoubleTransition Duration="0:0:0.2"` with `PbMotionFast`/`PbMotionEase`.
- Remove the now-fully-unused `UserControl.Resources` block (all 8 `ReaderXxxBrush` entries) and the
  now-fully-unused local styles (`Button.toolPill`/`.active`, `Button.toolIcon`, `Button.flyoutRow`) —
  grep the file after Step 5+6 land to confirm zero remaining references before deleting, same
  no-dangling-references discipline as every prior phase.

**Depends on:** Step 5 (confirms what's actually still referenced before deleting the old resources)
**Verify:** `dotnet build` clean; grep confirms no `ReaderXxxBrush`/`toolPill`/`toolIcon`/`flyoutRow`
references remain anywhere in the file.

## Step 7: Final pass

**Files:** none new — verification only

**What:** Full `dotnet build` + `dotnet test`. Re-grep for any leftover references to removed
elements/properties (`ShowFullscreenOverlays` renamed in Step 2 — confirm no stale references survived
anywhere, including `ReaderScreen.axaml.cs`'s code-behind). Confirm the design doc's every section has
a corresponding implemented piece: 4 clusters, drawer, idle-fade in both window states, thumbnail rail
untouched-behaviorally, on-canvas overlays restyled, 3 keyboard-shortcut gaps closed, ~720px collapse
behavior. Manual on-screen checklist (flagged, not assumed): fade timing feels right, drawer open/close
feels right, cluster positions hold up when resizing the window across the 720px threshold, shortcut
hints actually update after a remap in Preferences, bookmark-toggle glow fires, import/export
round-trips for real through the actual file picker (not just the test's fake).

**Depends on:** Steps 1-6
**Verify:** `dotnet build` clean, `dotnet test` green, grep confirms no dangling references, manual
checklist complete or explicitly handed to the user the same way every prior phase's on-screen
verification gap was — stated honestly, not assumed away.
