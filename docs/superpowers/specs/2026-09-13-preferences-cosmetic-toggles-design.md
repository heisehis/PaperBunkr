# Cosmetic Preferences micro-toggles (Browser + Import & Export CE settings)

**Date:** 2026-09-13
**Status:** Approved, ready for planning
**Scope:** Beta backlog ("Preferences: Behavior / CE-parity toggle remainder" — the "Cosmetic browser
micro-toggles" item, previously "low value; revisit only on request", now requested).

## 1. Background

`docs/Paperbunkr-Roadmap.md`'s Beta backlog named 6 remaining CE `Settings` toggles under this item:
`FadeInThumbnails`, `CoverThumbnailsSameSize`, `DogEarThumbnails`, `ShowToolTips`,
`NumericRatingThumbnails`, `ExportedListsContainFilenames`. Verified against
`_reference/ComicRackCE` (`Config/Settings.cs` + every real call site) rather than assumed — the
roadmap doc's one-line gloss for these undersold three of them: `DogEarThumbnails`, `ShowToolTips`,
and `NumericRatingThumbnails` are each a real rendering feature, not a plain checkbox.

### CE facts (file:line citations, `_reference/ComicRackCE`)

- **`FadeInThumbnails`** — `Settings.cs:1673`/`274`, default `true`. Gate at
  `Controls/ThumbnailViewItem.cs:217`: when a thumbnail bitmap just finished loading, `opacity` is
  set to `0` and ramped to `1` via a timer (`ThumbnailViewItem.cs:190,228`). A genuine fade-in on
  first load, not on every repaint.
- **`CoverThumbnailsSameSize`** — `Settings.cs:1749`, default `false`. Gate at `MainForm.cs:849` sets
  static `CoverViewItem.ThumbnailSizing`, read in `CoverViewItem.cs:392-413`: forces every thumbnail
  to a fixed `2:3` (`ThumbRenderer.DefaultThumbnailAspect`, `ThumbRenderer.cs:32`) width:height box
  instead of the real decoded aspect ratio.
- **`DogEarThumbnails`** — `Settings.cs:1692`/`276`, default `true`. Gate at
  `Controls/CoverViewItem.cs:556`: when hovered-or-selected, not Detail/list view, the comic is
  linked with `PageCount > 1`, not missing, and has no custom thumbnail, CE fetches the comic's
  **real second page** (`GetBackThumbnail()`) and draws it peeking out from behind the cover — a
  "preview the next page" hover affordance, not a folded-corner status badge.
- **`ShowToolTips`** — `Settings.cs:1635`, default `false`. Gates at `Views/ComicBrowserControl.cs`
  `:3250`/`:3298`/`:3253-3283`: pops a custom-drawn tooltip window on hover (any view mode except
  Tile) showing a small live thumbnail plus `ComicTextElements.DefaultComic` fields — caption
  (without title), title, artist info, summary, file size, file format.
- **`NumericRatingThumbnails`** — `Settings.cs:1711`/`278`, default `true`. Gate at
  `CoverViewItem.cs:511` → `ThumbRenderer.DrawRating` (`ThumbRenderer.cs:435-459`) →
  `RatingRenderer.DrawRatingTag` (`RatingRenderer.cs:186+`): a small square badge, bottom-right of
  the thumbnail, with the numeric rating (`"N1"`, e.g. "4.5") printed on it — replacing a star-icon
  strip that only exists when this setting is off (itself gated by a separate CE setting,
  `EngineConfiguration.Default.RatingStarsBelowThumbnails`, that Paperbunkr doesn't have).
- **`ExportedListsContainFilenames`** — `Settings.cs:2345`, default `false`. Gates two `.cbl` export
  call sites (`Views/ComicListLibraryBrowser.cs:757,1307`), flowing into
  `ComicReadingListItem`'s ctor (`ComicRack.Engine/Database/ComicReadingListItem.cs:73-82`):
  `FileName = (withFilename ? cb.FileName : string.Empty)`. Every other field (Series/Number/
  Volume/Year/Format/Id) is always populated regardless. CE itself never consults this setting on
  import — it's export-only.

### Paperbunkr facts (current state, verified this session)

- `AsyncCoverImage.cs` (`src/Paperbunkr.App/Views/AsyncCoverImage.cs`) is the one decode/apply path
  for comic cover thumbnails: a cache hit sets `Image.Source` instantly (no fade), a miss decodes
  off-thread and `Apply`s the bitmap with no opacity treatment today. Bound (`AsyncCoverImage.
  SourceId`) from `LibraryScreen.axaml`, `SmartScreen.axaml`, `ReadingScreen.axaml`,
  `EventsScreen.axaml`, `MigrationOverlay.axaml` — the natural scope boundary for `FadeInThumbnails`.
- `LibraryScreenViewModel.cs`: `PosterGrid` already renders every tile at one fixed
  `PosterCardWidth`/`PosterCardHeight` (`VirtualizingWrapPanel.ItemWidth/Height`, a single VM
  property, not per-item); `PanoramaGrid` already renders variable per-item width from
  `Issue.CoverAspectRatio` via `SeriesCardSample.ComputePanoramaWidth`. This **is** CE's
  `CoverThumbnailsSameSize` true/false split, already live as the view-mode choice — confirms the
  roadmap doc's own note. No new toggle needed.
- PosterGrid/Panorama tile corners are already fully occupied: top-right = unread-count pill
  (`ShowUnreadBadge`), top-left = publisher badge, bottom-left = language badge, bottom-right =
  selection checkbox (`CheckBox.tileSelect`, hover-reveal, force-visible when `HasSelection`).
  `ShowUnreadBadge`/`ShowPublisherBadge`/`ShowLanguageBadge` live in `LibraryToolbar.axaml`'s View &
  Sort popup, not Preferences — the established home for this exact family of toggle.
- `IPageImageDecoder.GetThumbnail(int pageIndex)` (`src/Paperbunkr.App/Services/
  IPageImageDecoder.cs`) already decodes an arbitrary page's thumbnail, independent of the full-res
  page cache — the exact mechanism `DogEarThumbnails` needs for a real second-page peek, no new
  decode path required.
- `StatusBar.axaml`'s Activity Center peek-popover (`docs/superpowers/specs/2026-09-07-chrome-
  content-motion-polish-design.md` item 6) is a live `Popup`/`Border`/open-class/transition pattern
  (`Popup PlacementTarget=... IsOpen="{Binding ...IsPeekOpen}"`) — the established precedent for a
  custom, non-native popover in this codebase, reused for `ShowToolTips`.
- `ComicReadingListItem.FileName` (`src/Paperbunkr.Engine/Database/ComicReadingListItem.cs:55-60`)
  already exists, ported, unused — `CblReadingListIO.Export` never populates it. Another instance of
  this codebase's "ported early, never wired" pattern.
- `Issue` entity has `Writer`, `Penciller`, `Summary`, `Format`, `FileSize` — enough to build
  `ShowToolTips`' metadata card without new schema.
- `CsvReadingListIO`'s CBL-sibling CSV format is a Paperbunkr-only convenience format with no
  filename/path column at all — `ExportedListsContainFilenames` stays CBL-only, matching CE exactly.

## 2. Decisions (settled via `/grilling`, all rounds)

1. **Build all 6 named items**, this batch. `CoverThumbnailsSameSize` needs no code — closed as
   already-covered by the PosterGrid/Panorama view-mode split, documented in the roadmap doc, no
   new `AppSettings` field.
2. **Placement:** `FadeInThumbnails`/`DogEarThumbnails`/`ShowToolTips`/`NumericRatingThumbnails` join
   the Library toolbar's View & Sort popup (same family as the existing badge toggles).
   `ExportedListsContainFilenames` goes to Preferences → Advanced, next to the file-metadata-
   writeback toggles (same "Import & Export"-flavored CE category).
3. **`DogEarThumbnails`: real CE-parity second-page peek** (not a decorative fold glyph) — reuses
   `IPageImageDecoder.GetThumbnail(1)`, gated exactly like CE (hover-or-selected, `PageCount > 1`,
   no custom cover, not missing, Poster/Panorama views only), cached per cover-stem like
   `CoverImageCache`.
4. **`ShowToolTips`: a genuinely custom `Popup` control** (not an enriched native `ToolTip.Tip`),
   mirroring the Activity Center peek-popover's `Popup`+open-class+transition shape. Content: small
   thumbnail (`AsyncCoverImage`-bound) + Title / Writer+Penciller / Summary excerpt (~150 chars) /
   FileSize / Format. Excludes the Tiles view mode (CE's own literal exclusion).
5. **`NumericRatingThumbnails`: hover-reveal badge sharing the bottom-right corner with the
   selection checkbox**, resolved by "selection mode owns the corner" — the rating badge shows on
   hover only when `!HasSelection`; the moment any issue is selected anywhere in the list, the
   checkbox takes that corner exclusively on every tile and the rating badge is hidden entirely.
   No CE star-strip fallback built for the off-state (that's gated behind a separate CE setting
   Paperbunkr doesn't have) — off simply means no rating shown on the thumbnail.
6. **`ExportedListsContainFilenames`: CBL export only.** `CblReadingListIO.Export` populates
   `ComicReadingListItem.FileName = issue.FilePath ?? string.Empty` when on. Import path unchanged
   (CE itself never reads this setting on import). CSV format untouched.
7. **Defaults match CE exactly** for all 5 real toggles: `FadeInThumbnails`=true,
   `DogEarThumbnails`=true, `ShowToolTips`=false, `NumericRatingThumbnails`=true,
   `ExportedListsContainFilenames`=false.

## 3. What ships

### `AppSettings` (new fields, one migration)

`FadeInThumbnails` (bool, default true), `DogEarThumbnails` (bool, default true), `ShowToolTips`
(bool, default false), `NumericRatingThumbnails` (bool, default true),
`ExportedListsContainFilenames` (bool, default false).

### FadeInThumbnails

`AsyncCoverImage.Apply` (the decode-miss completion path) gains a fade: instead of setting
`Image.Source` directly, set it with `Image.Opacity` starting at `0`, driven by a `DoubleTransition`
(~120ms, matching this codebase's existing `CheckBox.tileSelect` fade idiom and the motion skill's
"micro" 100-150ms guidance) back to `1`. Cache-hit path (`OnSourceIdChanged`'s early return) stays
instant, matching CE's own "only on genuine load" condition. Gated on the new setting: when off,
`Image.Opacity` is set directly to `1` with no transition, matching today's behavior exactly.

### DogEarThumbnails

New per-tile hover/selected-triggered peek: on `Border.cover:pointerover` or `IsSelected`, when
`Issue.PageCount > 1`, no custom cover override, and the file isn't missing, open a lightweight
`IPageImageDecoder` for the issue's archive and call `GetThumbnail(1)`, caching the result keyed by
the same cover-stem `CoverFingerprint` used for page-0 covers (new small cache, same eviction
posture as `CoverImageCache`). Render behind/offset from the front cover `Image` inside the tile's
existing cover `Border`, visible only while the hover/selected condition holds. Poster/Panorama
views only (mirrors CE's `DisplayType != Detail` exclusion — Paperbunkr's List/Details/Tiles views
don't get it either).

### ShowToolTips

New `Controls/ComicHoverTooltip` — a `Popup` (mirrors `StatusBar.axaml`'s Activity peek-popover
shape: `Popup` + `Border` with an open-class entrance transition) anchored to the hovered tile,
shown after a ~500ms pointer-enter delay (matches Avalonia's own native `ToolTip.ShowDelay` default,
no existing bespoke hover-tooltip timing precedent in this codebase to match instead) and dismissed
on pointer-leave. Content: `AsyncCoverImage`-bound thumbnail + a compact text block
(Title, Writer + Penciller on one line, a ~150-char `Summary` excerpt, `FileSize` formatted
human-readable, `Format`). Wired wherever library tiles render, excluding `IsTilesView` (CE parity).

### NumericRatingThumbnails

A `Border` badge (`RadiusFull`, small padding, rating text — component-recipe badge shape) in the
tile's bottom-right corner, `Opacity` transition-driven hover-reveal like `CheckBox.tileSelect`.
Visibility: `IsVisible="{Binding ..., pointerover-derived}"` gated additionally on `!HasSelection`
at the list level — once any selection is active, the badge's binding forces `false` regardless of
hover, and the existing `CheckBox.tileSelect.forceVisible` takes the corner as it already does
today.

### ExportedListsContainFilenames

`CblReadingListIO.Export` reads the setting off `context.GetOrCreateAppSettings()` and sets
`FileName = settings.ExportedListsContainFilenames ? (issue.FilePath ?? string.Empty) :
string.Empty` on each `ComicReadingListItem`. New Preferences → Advanced checkbox, next to the
file-metadata-writeback group.

### Post-ship addendum (2026-09-13): Series Granularity was missed entirely

The first implementation pass only wired DogEar/ShowToolTips/NumericRatingThumbnails into the
Issue-granularity templates (`PosterGridIssueTemplate`/`PanoramaGridItemTemplate`). Found via the
user's own on-screen test: Library's default/common view is **Series Granularity**
(`PosterGridSeriesTemplate`/`SeriesPanoramaGridItemTemplate`, one card per series) — none of the 3
hover features existed there at all, so toggling them produced no visible change for a user browsing
that way. `SeriesCardSample.RepresentativeRow` already carries a full `IssueListRow` for the card's
cover-issue (the same issue `CoverKey` is keyed to), so the fix reuses every existing
`IssueListRow`-based property (`DogEarEligible`, `HasRating`, `Rating`, tooltip content) via
`RepresentativeRow.*` bindings rather than a second series-shaped implementation. Code-behind
(`OnCoverPointerEntered`/hover handlers) gained `ResolvePeekRow(object? dataContext)` to extract the
effective `IssueListRow` from either an `IssueListRow` or a `SeriesCardSample` DataContext. Series
corner badge uses `HasSeriesSelection` (the series-granularity selection flag) in place of
`HasSelection`.

## 4. Explicitly out of scope

- `CoverThumbnailsSameSize` as a real toggle — already covered by PosterGrid/Panorama, no new
  `AppSettings` field, no new UI.
- CE's star-strip rating fallback for `NumericRatingThumbnails`'s off state — deliberate deviation,
  off means no rating shown on the thumbnail at all.
- `ShowToolTips` content beyond the 5 fields listed — no artist roles beyond Writer/Penciller, no
  additional CE `ComicTextElements` flags.
- Applying any of these 4 toolbar-popup toggles outside the screens `AsyncCoverImage`/tile templates
  already cover (Books screen uses a separate cover-cache mechanism, untouched here).
- CSV reading-list export gaining a filename/path column — CBL only, matching CE exactly.

## 5. Testing

- New `AppSettings` migration: a plain add/round-trip test, matching every prior settings-migration
  test in this project.
- `CblReadingListIO` export: a targeted test asserting `FileName` is populated/empty per the setting,
  reusing this test file's existing `PaperbunkrDbContext` fixture pattern.
- Corner-precedence logic (`NumericRatingThumbnails` vs `HasSelection`): a `LibraryScreenViewModel`
  -level test asserting the rating-badge visibility binding source flips to hidden once any
  selection exists, independent of hover state.
- `DogEarThumbnails`'s gating conditions (`PageCount > 1`, custom-cover override, missing-file) are
  pure boolean logic — factor into a small testable helper (mirrors this codebase's own "extract
  pure conditions" convention) and unit test each branch, without needing a real decode in the test.
- `AsyncCoverImage`'s fade path: reuse this control's existing `Apply`-level testing seam
  (`internal static void Apply`) to assert `Opacity` starts at 0 and the transition is present when
  the setting is on, and that it's skipped when off.
- On-screen verification (no computer-use this session, standing caveat) is a real gap for all 4
  visual toggles here more than most prior batches — hover timing, peek-popover positioning, and the
  dog-ear peel's actual visual read all need a manual pass before being called done.

## 6. On-screen verification

Standing caveat: no computer-use available this session. All 4 visual toggles (fade timing, dog-ear
peek placement/legibility, tooltip popover positioning/dismiss, rating-badge hover/selection
interplay) need a manual pass before being marked verified — flagged as outstanding, not assumed.
