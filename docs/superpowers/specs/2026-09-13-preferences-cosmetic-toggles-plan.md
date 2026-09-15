# Cosmetic Preferences micro-toggles — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-13-preferences-cosmetic-toggles-design.md*

## Architecture correction found during survey

The design doc says `DogEarThumbnails` should "reuse `IPageImageDecoder.GetThumbnail(1)`". Its only
real implementation is `ReaderImagePipeline` — a heavyweight, stateful, memory-budgeted pipeline
with its own background consumer thread and shared process-wide raw-byte cache, built for one
long-lived Reader session. Spinning one up per tile-hover in a Library grid would be a real
perf/resource regression, not a light reuse.

The actual right tool, found this session: `PageDecodeCore.DecodeSinglePage(string filePath, int
pageIndex = 0)` (`src/Paperbunkr.App/Services/PageDecodeCore.cs:26`) — its own doc comment says
exactly this: "for callers that need a single page and nothing else... without standing up the full
`ReaderImagePipeline` (which spins a background consumer thread per instance)". Already used by
`CoverThumbnailService` for page-0 cover generation. Step 3 below uses this instead — same design
intent ("no new decode path"), correct concrete mechanism.

## Step 1: AppSettings fields + migration

**Files:**
- `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit)
- `src/Paperbunkr.Data/Migrations/<timestamp>_AddCosmeticThumbnailToggles.cs` (new, via `dotnet ef migrations add`)

**What:** Add 5 bool properties near the other Behavior/Advanced settings, matching
`RestoreSessionOnStartup`'s exact shape (auto-property with inline default where non-false):

```csharp
public bool FadeInThumbnails { get; set; } = true;
public bool DogEarThumbnails { get; set; } = true;
public bool ShowToolTips { get; set; }
public bool NumericRatingThumbnails { get; set; } = true;
public bool ExportedListsContainFilenames { get; set; }
```

Each gets a doc comment citing its CE `Settings.cs` source line and default (mirror the existing
`WriteMetadataToFiles`-style comment block: what it's for, CE name/default, cross-reference to this
design doc).

Generate the migration via `dotnet ef migrations add AddCosmeticThumbnailToggles --project
src/Paperbunkr.Data --startup-project src/Paperbunkr.App` (or the project's own established
invocation — check a recent migration's own generation note if one exists) rather than hand-writing
it, then verify the generated `Up`/`Down` — `Down` must be a **real `DropColumn` per column**,
matching `20260912122257_AddIssueAlternateCount.cs`'s current convention (the no-op-Down pattern in
older migrations like `AddBehaviorSettingsBatch2` predates the 2026-09-06 rollback-chain-bug fix and
is no longer this project's convention).

**Depends on:** none
**Verify:** New migration round-trip test (`Paperbunkr.Data.Tests`) — matches the up/down/up pattern
this project's other recent migration tests use, avoiding the up-down-**up** antipattern flagged in
project history (assert schema/model state after a full up→down→up cycle, not just up→down).

## Step 2: FadeInThumbnails

**Files:** `src/Paperbunkr.App/Views/AsyncCoverImage.cs` (edit)

**What:** In `Apply` (the decode-miss completion path, currently `image.Source =
CoverImageCache.StoreIfAbsent(stem, decoded);` with no opacity treatment): when
`settings.FadeInThumbnails` is on, set `image.Opacity = 0` before assigning `Source`, ensure the
`Image` has a `DoubleTransition` on `Opacity` (~120ms — matches `CheckBox.tileSelect`'s existing
`0:0:0.12` fade at `LibraryScreen.axaml`), then set `Opacity = 1` (on the next dispatcher tick, or
immediately if `Transitions` are already attached — a same-tick 0→1 flip needs the transition
already present on the object *before* the value changes, so attach `Transitions` once, in
`OnSourceIdChanged` or a static initializer, not conditionally at fade time). When the setting is
off, set `Opacity = 1` directly with no `Transitions` collection attached — today's exact behavior,
unchanged. The cache-hit path in `OnSourceIdChanged` (`image.Source = cached;`) stays untouched
either way — CE only fades genuine loads, never repaints.

Reading the setting: `AsyncCoverImage.Apply`/`OnSourceIdChanged` run per-tile-realization during
scroll — a `PaperbunkrDb.CreateContext()` round-trip per call would be a real perf regression on a
virtualized grid. Check whether this codebase already has a cached/pushed-settings pattern for
exactly this kind of hot path (e.g. does `LibraryScreenViewModel` or a shared settings-cache service
already exist that other attached-property helpers read from?) before adding a new one — if nothing
suitable exists, add a small static field on `AsyncCoverImage` refreshed once when `AppSettings`
changes (mirror however `AppSettings` change-notification already works elsewhere, e.g. how
`LibraryScreenViewModel` itself picks up a settings change) rather than querying the DB per call.

**Depends on:** Step 1
**Verify:** `AsyncCoverImage`'s existing `internal static void Apply(...)` test seam
(`src/Paperbunkr.App.Tests` — find its current test file) — extend with cases asserting `Opacity`
starts at 0 with a transition present when the setting is on, and `Opacity` is set to 1 directly
with no transition when off.

## Step 3: DogEarThumbnails

**Files:**
- `src/Paperbunkr.App/Services/DogEarThumbnailCache.cs` (new — mirrors `CoverImageCache.cs`'s shape:
  an `LruCache<string, Bitmap>` keyed by cover-stem, `TryGetCached`/decode-and-store, no disposal of
  evicted bitmaps per that file's own established reasoning)
- `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — Poster/Panorama tile templates)
- `src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit, if a hover/selected state needs
  code-behind wiring beyond what XAML pseudoclasses already give for free)

**What:** Gate condition (mirrors CE's `CoverViewItem.cs:556` exactly, using this codebase's real
equivalents found this session):
`issue.PageCount is > 1 && !issue.FileIsMissing && !CustomCoverPaths.Exists(issue.Id)` (the real
"has custom thumbnail" check — `Issue.CustomThumbnailKey` is a dead/unused column, confirmed no
App-layer reference; `CustomCoverPaths.Exists(int issueId)` in
`src/Paperbunkr.App/Services/Covers/CustomCoverPaths.cs:36` is the actual mechanism the cover-art-
override feature uses), AND `(Border.cover:pointerover OR IsSelected)`, AND Poster/Panorama view
only (`IsPosterGrid || IsPanoramaGrid`).

On that condition becoming true for a realized tile: call
`DogEarThumbnailCache.Get(coverStem, issue.FilePath)`, which internally calls
`PageDecodeCore.DecodeSinglePage(filePath, 1)` on a cache miss (off the UI thread, same
threadpool-decode-then-dispatcher-post shape `AsyncCoverImage.OnSourceIdChanged` already uses) and
caches the result. Render the resulting bitmap in a new `Image` positioned behind/offset from the
tile's existing front-cover `Image` inside its cover `Border`, visible only while the gate condition
holds (bind `IsVisible` the same way `CheckBox.tileSelect`'s hover-reveal already works via style
selectors, not a new VM property per row).

**Depends on:** none (independent of Steps 1/2, but shares the settings-read question from Step 2 —
resolve that once, reuse for this toggle's gate too)
**Verify:** Extract the gate condition (`PageCount > 1 && !FileIsMissing && !CustomCoverPaths.Exists`)
into a small pure static helper so it's unit-testable without a real decode — e.g.
`DogEarEligibility.IsEligible(int? pageCount, bool fileIsMissing, bool hasCustomCover)` — and cover
each branch. `DogEarThumbnailCache`'s decode path itself is exercised the same way
`CoverImageCache`/`CoverThumbnailService` tests already exercise `PageDecodeCore` (a real synthetic
`.cbz` via `CbzFixture`, matching this test suite's established fixture convention).

## Step 4: ShowToolTips

**Files:**
- `src/Paperbunkr.App/Controls/ComicHoverTooltip.cs` (new — attached-property/behavior shape,
  mirrors `AsyncCoverImage`'s "attached property helper, no VM changes needed" pattern) or a
  `UserControl` if a behavior needs its own visual tree beyond what an attached property can host —
  decide based on what's cleaner once the `Popup` hookup is drafted; check
  `avalonia-custom-controls` subskill before writing
- A `.axaml` resource (Styles or inline) providing the `Popup`+`Border`+open-class+`Transitions`
  block, mirroring `StatusBar.axaml:64-81,119-173` almost exactly (same `activityPeekPopover`-style
  fade+slide entrance, same `IsLightDismissEnabled`, but `Placement` anchored to the hovered tile
  rather than a fixed toolbar button)
- `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — wire the new control/attached property onto
  every tile template except the Tiles view mode's, matching CE's own `ItemViewMode.Tile` exclusion)

**What:** Unlike the Activity peek-popover (opened by a button `Command`), this one opens from
pointer-enter with a ~500ms delay (matches Avalonia's own native `ToolTip.ShowDelay` default — no
existing bespoke hover-timing precedent in this codebase to match instead) and closes on
pointer-leave — needs its own `DispatcherTimer`-based delay (mirror this codebase's existing
`DispatcherTimer`-per-feature convention, e.g. `ReaderScreenViewModel`'s several timers, rather than
inventing a new async-delay idiom) rather than a bound `Command`. Content: `AsyncCoverImage`-bound
thumbnail (reuse the same `SourceId`/`CoverKey` binding every tile template already uses) + Title /
"Writer, Penciller" (join non-null, comma-separated) / a `Summary` excerpt truncated to ~150 chars
with a trailing ellipsis if longer / `FileSize` formatted human-readable (check whether this
codebase already has a byte-formatting helper — e.g. used anywhere file sizes are already displayed,
such as the file-metadata-writeback or Library Health screens — reuse it rather than writing a new
one) / `Format`.

**Depends on:** none (visually independent of Steps 1-3, but shares the settings-read question)
**Verify:** No on-screen automation available (standing caveat) — unit-test whatever pure logic
exists (the ~150-char truncation helper, the Writer/Penciller join helper, the byte-size formatter if
newly written) directly. The actual hover/popup mechanics need a manual pass.

## Step 5: NumericRatingThumbnails

**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — Poster/Panorama tile templates)

**What:** New `Border` badge (`CornerRadius="{DynamicResource PbRadiusFull}"` or this codebase's
equivalent full-radius resource — check `avalonia-pro-max/design-system` tokens already in use
elsewhere in this file — small padding, rating text bound to `Rating` formatted `"N1"`) in the same
bottom-right corner as `CheckBox.tileSelect`, using the **same hover-reveal `Opacity` transition
style** that control already has (mirror `Style Selector="CheckBox.tileSelect"`/`Border.cover:
pointerover CheckBox.tileSelect` at `LibraryScreen.axaml:164-177` for the badge's own selector
pair). Additional visibility rule: bind the badge's own `IsVisible` (or a wrapping `Border`'s) to
`!$parent[UserControl].((vm:LibraryScreenViewModel)DataContext).HasSelection` — so once any
selection exists anywhere in the list, the badge's binding forces `false` regardless of hover state,
and the existing `CheckBox.tileSelect.forceVisible` class (already driven by `HasSelection`) takes
the corner exactly as it does today. No new `HasSelection`-adjacent VM code needed — the existing
property is reused as-is.

**Depends on:** none
**Verify:** A `LibraryScreenViewModel`-level test isn't really possible for pure XAML visibility
binding — instead, if the badge's own visibility ends up driven by a small VM-exposed bool (e.g. a
per-row `ShowRatingBadge` computed property) rather than a pure XAML multi-binding, that computed
property is what gets unit-tested (asserting it's `false` whenever `HasSelection` is true,
regardless of a simulated hover flag). Decide the exact binding shape while implementing, whichever
is cleaner given `IssueListRow`'s existing shape — a VM-computed property is the more testable and
more consistent option if `IssueListRow` already exposes similar per-row derived flags elsewhere.

## Step 6: ExportedListsContainFilenames

**Files:**
- `src/Paperbunkr.Data/ReadingLists/CblReadingListIO.cs` (edit)
- `src/Paperbunkr.App/Views/Preferences/AdvancedSection.axaml` (edit)

**What:** In `Export`, read `context.GetOrCreateAppSettings()` once before the loop, then per item:

```csharp
FileName = settings.ExportedListsContainFilenames ? (issue.FilePath ?? string.Empty) : string.Empty,
```

added to the existing `ComicReadingListItem` object initializer (currently populates
Series/Number/Volume/Year/Format only). Import path (`CblReadingListIO.Import`) stays untouched —
CE itself never reads this setting on import either.

New Preferences → Advanced control: a `ToggleSwitch` (matching `WriteMetadataToFiles`'s exact shape
at `AdvancedSection.axaml:83`, `OnContent="{x:Null}" OffContent="{x:Null}"`), placed near the
existing "Comic File Metadata" group (same "Import & Export"-flavored CE category), bound to
`ExportedListsContainFilenames` on `PreferencesScreenViewModel` (check its current property-exposure
pattern for the sibling `WriteMetadataToFiles`/etc. bools and mirror it exactly — likely a plain
pass-through property that reads/writes `AppSettings` via the same context the screen already holds).

**Depends on:** Step 1
**Verify:** A `CblReadingListIO`-level test (new or extended file) asserting `FileName` is populated
when the setting is on and empty when off, reusing this test area's existing `PaperbunkrDbContext`
temp-DB fixture pattern. A `PreferencesScreenViewModel` test for the new property's read/write
round-trip, mirroring the existing `WriteMetadataToFiles`-adjacent test if one exists.

## Step 7: Toolbar placement for the other 4

**Files:** `src/Paperbunkr.App/Views/LibraryToolbar.axaml` (edit — the View & Sort popup's "Overlay"
group, `LibraryScreen.axaml.cs`/`LibraryScreenViewModel.cs` if new bindable properties are needed)

**What:** Add 4 new `Grid`+`CheckBox` rows mirroring the existing `ShowUnreadBadge`/
`ShowPublisherBadge`/`ShowLanguageBadge` block exactly (`LibraryToolbar.axaml:438-458` — same
`ColumnDefinitions="*,Auto"`, same label/checkbox shape), bound to `FadeInThumbnails`,
`DogEarThumbnails`, `ShowToolTips`, `NumericRatingThumbnails` on `LibraryScreenViewModel` (check how
the existing 3 overlay-toggle properties round-trip through `AppSettings` — likely immediate
no-debounce write-on-change, matching this ViewModel's own established convention per the Saved List
Layouts work — and mirror that exact pattern for the 4 new properties rather than inventing a
different persistence timing).

**Depends on:** Step 1
**Verify:** Extend whatever existing `LibraryScreenViewModelTests` cover the 3 existing overlay
toggles' load-reflects-settings/write-back round-trip, adding the same 2 assertions (load, write-back)
for each of the 4 new properties.

## Not covered here (per design doc §4)

- `CoverThumbnailsSameSize` — no code, already covered by PosterGrid/Panorama.
- CE's star-strip rating fallback for `NumericRatingThumbnails`'s off state.
- CSV reading-list export — untouched, CBL only.

## On-screen verification

Standing caveat, carries more weight than usual for this batch: fade timing, dog-ear peek placement/
legibility, tooltip popover positioning/dismiss timing, and the rating-badge hover/selection
interplay are all real visual/interaction behavior no unit test can confirm reads correctly — flagged
as outstanding, needs a manual pass before this batch is called done.
