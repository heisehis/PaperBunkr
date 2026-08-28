# Detail screens — streaming-style redesign (UI rework Phase 5)

**Status:** design approved 2026-08-28. Supersedes the ad-hoc structure of `DetailScreen`,
`MangaDetailScreen`, `BookDetailScreen`.

**Context:** Phase 5 of the 7-phase UI rework
(`docs/superpowers/specs/2026-08-24-design-language-foundation-design.md`). Phases 1–4 and 6 are
shipped; this is the last major screen phase (7 = Preferences). The foundation phase already
landed the tokens this needs: `PbHeroGradientStartColor`/`PbHeroGradientEndColor`, `PbGlowRing`,
`Border.posterScrim`/`Border.posterTile`, `PbSurface2`/`PbSurface3`, `PbRadiusSm`, Bebas Neue +
Source Serif 4.

**Goal:** the three detail screens today look like three different eras and bury the content that
matters (the Batman screen pushes the Issues list below ~100 cover-artist names, ~25 locations, and
~40 junk import-ID tags). Rebuild all three on one shared skeleton — a cinematic blurred-cover
hero, an always-visible metadata band, a sticky tab strip — with streaming-style related-media
rails, while keeping every behaviour that already works (selection-driven issue focus, 2D
arrow-key nav, click-to-search chips, weight context menus, cover override, tracker/metadata
linking).

---

## Decomposition — one spec, three implementation phases

| Phase | Scope | Ships |
|---|---|---|
| **P1** | Shared controls (`DetailHero`, `DetailBand`, `PosterRail`, shared tab-strip style) + comic `DetailScreen` + `DetailTabs` rebuilt on them; Issues tab 3 view modes; Related tab as rails; junk-tag filter. | independently |
| **P2** | `MangaDetailScreen` adopts `DetailHero`/`DetailBand`; manga-specific hero content; Chapters tab → release-feed. | independently |
| **P3** | `BookDetailScreen` adopts `DetailHero`/`DetailBand` + stacked sections (no tab strip); book **series mode** → hero + poster grid. | independently |

Each phase builds green and is separately verifiable. `writing-plans` may sub-phase further.

### Architecture

- **New shared UserControls in `src/Paperbunkr.App/Views/`:** `DetailHero.axaml`,
  `DetailBand.axaml`, `PosterRail.axaml`. Plus a shared `Styles/DetailChrome.axaml` for the
  tab-strip (`Button.tab`) and metadata-group styles, promoted out of the per-view `<UserControl.Styles>`.
- **The three ViewModels stay separate.** `DetailScreenViewModel`, `MangaDetailScreenViewModel`,
  `BookDetailScreenViewModel` are wired independently in `MainViewModel` and have distinct data
  shapes (book has no relations/trackers; manga routes content-type reclassification differently).
  A merged VM would be a large `MainViewModel` refactor — and that file has concurrent uncommitted
  Phase-4d/e/f/g edits. Not worth it.
- **`DetailHero` / `DetailBand` bind against a thin interface**, `IDetailHeaderSource`, that all
  three VMs implement:

  ```csharp
  public interface IDetailHeaderSource : INotifyPropertyChanged
  {
      IBrush CoverBrush { get; }
      Bitmap? CoverImage { get; }          // drives the foreground thumbnail
      Bitmap? BackdropImage { get; }       // pre-blurred, from BackdropBlurRenderer
      string Title { get; }
      string? SecondaryTitle { get; }      // manga native+romaji; null elsewhere
      string MetaLine { get; }             // "Image · Ongoing · 66 issues · 12 unread"
      IReadOnlyList<DetailHeroAction> Actions { get; }   // primary + ghosts, per screen
      DetailHeroProgress? TrackerProgress { get; }       // manga ring; null elsewhere
  }
  ```

  `DetailHeroAction` = `{ string Label, ICommand Command, bool IsPrimary, bool IsEnabled }`.
  `DetailHeroProgress` = `{ int Current, int Total, string Label }`.

- **`DetailBandViewModel`** absorbs today's `DetailMetaViewModel` + `DetailPillsViewModel`
  (both deleted along with `DetailMeta.axaml`/`DetailPills.axaml` once P1 lands — grep confirms
  `DetailScreen` is the only consumer). It exposes the inline meta row, the synopsis, and the
  tamed metadata groups. Comic/manga instantiate it; book uses only its synopsis + inline-meta.

- **`MainViewModel.cs` / `MainWindow.axaml` change only** where a new callback is genuinely
  needed (e.g. a "go to Details tab" jump for the "full credits ›" link — can be an internal
  `DetailTabsViewModel` method with no `MainViewModel` involvement). Target: zero `MainWindow`
  edits, ≤1 `MainViewModel` line per phase. Re-check `git status` before starting each phase;
  park WIP to scratchpad, never `git stash` (shared working tree — see
  `project_paperbunkr_concurrent_sessions`).

---

## The skeleton (all three screens)

```
← Back link                        (page background, above the hero)
┌───────────────────────────────────────────────────────────┐
│  DetailHero — full-bleed, blurred cover backdrop + vignette │  ~340px
│    [cover]  Title (Bebas)                        (ring)     │
│            meta line                                        │
│            [Continue #55] [Edit] [Change Cover] [Reveal]    │
├───────────────────────────────────────────────────────────┤
│  DetailBand — amber left-accent, PbSurface2, ALWAYS visible │
│    content-type ▾ · status · publisher · year               │
│    synopsis (3 lines + "more")                              │
│    Credits: Writer, Artist  · full credits ›                │
│    Genres  [chip][chip][chip]  +4 more                      │
│    Teams   [chip][chip]  +5 more                            │
│    Locations [chip][chip]  +24 more                         │
│    Tags    [chip][chip]  +1 more   (41 hidden — import IDs) │
├───────────────────────────────────────────────────────────┤
│  Tab strip:  Issues · Related · Details · Activity          │  sticky
├───────────────────────────────────────────────────────────┤
│  tab content                                               │
└───────────────────────────────────────────────────────────┘
```

Book replaces the tab strip + tab content with stacked `sectionCard`s.

---

## P1 — Shared chrome + comic `DetailScreen`

### `DetailHero`

- **Backdrop:** the series/issue cover, blur-rendered once via the existing
  `Services/BackdropBlurRenderer` (manga already uses it — extend its call sites to comic + book;
  it currently only runs for `MangaDetailScreenViewModel`). `PbHeroGradientStart→End` linear
  vignette overlaid top→bottom so title text is legible over any art. Fixed height ~340px,
  `ClipToBounds`, full-bleed (no side margin — the hero is the first child of the screen's root,
  outside the content `Margin`).
- **Foreground** (bottom-left, `z=1` over the backdrop): cover thumbnail ~90×132 with
  `posterScrim`; `Title` in Bebas ~30px; `SecondaryTitle` line (manga only); `MetaLine`; then
  the action row from `Actions` — primary = `Button.detailAction primary`, rest =
  `Button.detailAction ghost`.
- **Tracker ring** (`TrackerProgress != null`, manga only): a thin circular progress at the
  hero's right edge, `Current`/`Total` label inside.
- Comic `MetaLine`: `{Publisher} · {Ongoing|Complete} · {N} issues · {N} unread`.
- Comic `Actions`: `Continue — Issue #n` / `Re-read — Issue #n` / `No Issues` (existing
  `ContinueLabel` logic), `Edit` (selection-aware label + enablement, existing), `Change Cover`,
  `Reveal in Explorer`.
- **Selection focus:** when exactly one Issues tile is selected, `CoverImage`/`BackdropImage`/
  `MetaLine`/synopsis switch to that issue (today's `RefreshForSelection`); `Title`/status stay
  series-level. Unchanged behaviour, new binding surface.

### `DetailBand`

Amber left-border (`BorderThickness="2,0,0,0"`, `PbBadgeBrush`), `PbSurface2` background,
full-bleed, sits directly under the hero.

1. **Inline meta row:** the real editable content-type `ComboBox` (`ContentTypeOptions` /
   `SelectedContentType`, `contentTypePicker` style — reclassification routing unchanged) ·
   `StatusLabel` · publisher · year. Dots as separators. Empty segments omitted.
2. **Synopsis:** `Summary`, `MaxLines="3"` + `TextTrimming="CharacterEllipsis"` with a
   `More`/`Less` toggle (same idiom as `MangaDetailScreenViewModel.ToggleSynopsisCommand`).
3. **Tamed metadata groups** — a repeated sub-control, `DetailBandGroup`, one per group:
   - Header: group label + right-aligned `+N more` link (visible only when the group overflows
     one row's worth — cap at a fixed count, **12**, not a measured row).
   - Body: `WrapPanel` of chips, capped to 12 unless expanded; expansion is per-group local
     state (no persistence).
   - **Credits** group is special: shows Writer + Artist name-chips only, then a
     `full credits ›` link that switches the tab strip to **Details** and scrolls to the Credits
     section there. No `+N more`.
   - Groups: `Credits`, `Genres & Concepts`, `Teams`, `Locations`, `Characters` (if the field
     is populated — CE `Characters`), `Tags`, `Virtual Tags`. **A group with zero items renders
     nothing** — no bare header (fixes the empty "Genres & Concepts" in the screenshot).
   - Chip behaviour unchanged: `weightedTagPill` for `IssueTag`-backed groups (Genres/Tags —
     click-to-search + weight `ContextMenu` when `CanReweight`), `plainTagPill` for
     Teams/Locations/Characters (click-to-search only), `tagPill highlight` non-interactive for
     Virtual Tags.
4. **Junk-tag filter:** `DetailBandViewModel` drops any Tags-group value matching
   `^CVDB\d+$` (`RegexOptions.IgnoreCase`) from the visible list and counts them; the Tags header
   shows `… (N hidden — import IDs)` with a click to reveal (local state, not persisted).
   Purely display-layer — import and DB untouched. A permanent scrub is deferred (see below).

### Tab strip

Promote `Button.tab` / `Button.tab.active` / `TextBlock.tabCount` to `Styles/DetailChrome.axaml`
(currently duplicated verbatim in `DetailTabs.axaml` and `MangaDetailScreen.axaml`). Tabs:
`Issues` (count) · `Related` (count) · `Details` · `Activity`. Sticky: the strip stays pinned at
the top of the scroll viewport when the content scrolls under it (`DetailScreen`'s `ScrollViewer`
gets the hero+band as non-sticky header content and the strip in a pinned container — Avalonia
has no native sticky; implement as the strip living outside the `ScrollViewer` with the tab
*content* in its own inner `ScrollViewer`). Hero + band scroll away; strip + content don't.

### Issues tab — 3 view modes

- **Chrome row:** existing sort (`Number`/`Date`/`Rating` + ↑↓) · filter (`All`/`Unread`/…) ·
  group (`None`/`Story Arc`/`Year`) · **view-mode segment** (`Poster` / `List` / `Card`),
  right-aligned. New `AppSettings.DetailIssueViewMode` (enum `DetailIssueViewMode { Poster,
  List, Card }`, default `Poster`) — same `HasDefaultValue`/`HasSentinel` EF treatment as
  `LibraryViewMode` (see `AppSettings.cs` — the enum-column sentinel gotcha bit us before, per
  `project_paperbunkr_preferences_reader_tab`). Migration: one column add, `AddDetailIssueViewMode`.
- **Poster:** cover + issue number + arc/title as two text lines below, on `Border.posterTile` /
  `Border.posterScrim` / `PbGlowRing`. Read = dimmed (`Opacity 0.45`), in-progress = thin amber
  bar on the cover bottom, `_continueIssueId` = persistent amber ring.
- **List:** `Grid` rows — 24px (thumb on current/selected row only) · 34px number · `*` title ·
  130px arc · 70px cover date · 46px rating. `Button.chapterRow`-style hover. Read rows muted.
- **Card:** ~4/row `WrapPanel`. Per card: cover thumb ~52×78 · full title (2 lines) · `ISSUE #n`
  + progress · primary `Read`/`Continue` button · inline icon row (mark-read `PbIconCheck`,
  edit `PbIconPencil`, reveal `PbIconExternal`). Current/selected card = amber border.
- **All modes:** Bebas group headers (the 4a `gh` style). Selection-driven Detail focus and the
  2D arrow-key nav (`OnIssueTilePointerPressed` / `OnIssueTileKeyDown` in `DetailTabs.axaml.cs`)
  work identically — the key handlers move to whichever item container each mode uses; the
  `SelectedIssueIds` model is unchanged. Context menu unchanged (Edit Properties, Show in
  Explorer, Mark Read/Unread, Quick Rate, Set/Reset Cover).

### Related tab — rails

Replace the current single h-scroll + chip sections with stacked `PosterRail`s. `PosterRail` =
title + right-aligned context label + horizontal cover strip (`ScrollViewer`
`HorizontalScrollBarVisibility="Auto"`), optional trailing dashed `+ Add` card, optional
per-card ✕-on-hover.

- **Related Series** — editable. Context label `N · manage`. Trailing `+ Add` card opens the
  existing type-scoped picker (`ToggleAddRelationCommand` / `RelationTypeOptions` /
  `RelationSearchResults` / `AddRelationCommand`). Per-card ✕ = `RemoveRelationCommand`. Per-card
  sub-label = relation note (`RelatedSeriesSample.Note`).
- **Same Continuity** — read-only, `SameContinuity`, context label = continuity name.
- **Same Event** — read-only, `SameEvent`, context label = event name.
- **More Like This** — read-only, from `RecommendationResolver` (exists —
  `src/Paperbunkr.Data/Metadata/RecommendationResolver.cs`, built for Home, never surfaced;
  `project_paperbunkr_metadata_model_phase6a`). New `DetailTabsViewModel` call; cap ~15;
  context label `from your library`; hidden entirely if empty or resolver returns nothing.
- **Continuity membership** — the add/create chip row (`ContinuityChips` / `ToggleAddContinuity`
  / `AddContinuityCommand` / `RemoveContinuityCommand`) moves to a compact row directly under the
  Related-tab header, above the rails.
- Empty-state: if every rail is empty, one "No related series yet." line + the `+ Add` affordance.

### Details tab

Same content as today, restyled to `DetailChrome` / `sectionCard`: a **Credits** section (all
roles — Writer, Artist, Cover Artist, Colorist, Letterer, Inker, Editor as label + name-chip
rows, the old `DetailMeta` grid), Publisher / imprint / dates, reading-mode toggle, External
Metadata linking (`ToggleSearchMetadata` flow), Trackers (`ToggleLinkTracker` / `SyncToTrackers`
flow). The `full credits ›` band link deep-links here.

### Activity tab

Unchanged — real empty state ("No recent activity."). No activity-log feature exists yet.

---

## P2 — `MangaDetailScreen`

- Adopts `DetailHero` + `DetailBand` + the shared tab strip. Delete `MangaDetailScreen.axaml`'s
  local `backLink` / `contentTypePicker` / `headerPill` / `metaRow` / `tab` / `tabCount` styles
  (now shared or in the band). Keep its chapter-list styles.
- **`MangaDetailScreenViewModel` implements `IDetailHeaderSource`** with manga extras:
  - `SecondaryTitle` = native title + romaji (`ヴィンランド・サガ · Vinrando Saga`) — from the
    series' external metadata snapshot when linked, else romaji only, else null.
  - `MetaLine` carries demographic (seinen/shounen/…) · serialization magazine · reading
    direction. An RTL badge renders in the hero when `ReadingMode` is RTL
    (`ReadingModeBadge` today).
  - `TrackerProgress` populated from the linked tracker (AniList/MAL/MU) when present —
    `Current` = chapters read, `Total` = total chapters, `Label` = service name.
- **Chapters tab → release-feed:**
  - One vertical list, **volume section headers** (Bebas, from `ChapterRowSample` volume grouping
    — the VM already tracks `VolumeCountBadge`; group by parsed volume, "No volume" bucket last).
  - Row: chapter number (`DisplayNumber`) · title · scanlation/official group (`ScanInformation`)
    · relative date (`Date`) · **NEW badge** when unread *and* `Date` within the last 14 days ·
    bookmark marker (`HasBookmark`) · missing marker (`IsMissing`). In-progress `ProgressBar`
    unchanged.
  - Existing filter pills (`All`/`Unread`/`Bookmarked`/`Missing`) and sort pills
    (`By Number`/`By Date` + direction) unchanged.
  - No covers on chapter rows (manga chapters have no variant covers — established in
    `2026-08-23-manga-detail-screen-design.md`).
- Related / Details tabs = the shared `DetailTabs` content, as today (`views:DetailTabs
  DataContext="{Binding Tabs}"`).
- Manga tab strip: `Chapters` (count) · `Related` (count) · `Details` · `Activity`.

---

## P3 — `BookDetailScreen`

- **Book mode:** `DetailHero` (backdrop from the book cover — extend `BackdropBlurRenderer` to
  EPUB/PDF-extracted covers) + a reduced `DetailBand` (inline meta row =
  `Author · Format · FINISHED?`; synopsis 3-line + more; **no** metadata groups — books carry
  none). **No tab strip.** Then stacked `sectionCard`s, as today's structure but under the hero:
  - **Reading progress** — `ProgressLabel` + bar + `Mark finished`/`Mark unread` toggle. Lives
    just below the band.
  - **Chapters** — EPUB TOC rows (`BookChapterSummary`), `listRow` style, current chapter marked.
    Omitted for PDF / when `ChaptersUnavailable`.
  - **Bookmarks** — `BookBookmarkSummary` rows with excerpt + chapter + date, delete via context
    menu.
  - **Delete Book** — the existing flyout-confirmed destructive action at the bottom.
- `Actions` = `Continue · N%` (or `Start reading`), `Edit`, `Reveal in Explorer`.
- **Series mode:** `DetailHero` with backdrop from a representative member cover, `Title` =
  series name, `MetaLine` = `{Author} · {N} books`, `Actions` = `Edit series` / `Edit all books`.
  Then a poster grid of member books on the **Issues-tab Poster tile** (`Border.card` /
  `bookCover` / `posterScrim` / `PbGlowRing` — already present in `BookDetailScreen.axaml`,
  keep). Context menu `Edit…` unchanged.
- Book mode keeps no `IDetailHeaderSource` metadata groups, so `DetailBand` needs a "lite" mode
  (a bool or a null groups list) — cleanest as `DetailBand` simply rendering nothing for a null
  `Groups`.

---

## Testing

ViewModel-level (this codebase does not test XAML). New/updated:

- **`DetailBandViewModelTests`** — CVDB filter regex (matches `CVDB123`, `cvdb9`; not
  `CVDBX`, `Absolute Batman`); hidden count; group cap at 12 + expand toggle; empty-group
  suppression; credit collapse to Writer+Artist; inline-meta segment omission.
- **`DetailScreenViewModelTests`** (extend) — `DetailIssueViewMode` persistence round-trip
  (load → change → new context → reload); selection-focus still switches cover/summary in every
  view mode; `full credits ›` sets the Details tab active.
- **`DetailTabsViewModelTests`** (extend) — `PosterRail` add/remove relation; `+ Add` picker
  scoping unchanged; `MoreLikeThis` populated from a stub `RecommendationResolver`, hidden when
  empty; continuity-membership row still writes through `ContinuityResolver`.
- **`MangaDetailScreenViewModelTests`** (extend) — `IDetailHeaderSource` surface
  (`SecondaryTitle`, RTL badge flag, `TrackerProgress` from a linked tracker vs null when
  unlinked); release-feed volume grouping + "No volume" bucket ordering; NEW-badge cutoff
  (unread + within 14d = badge; unread + older = none; read + recent = none).
- **`BookDetailScreenViewModelTests`** (extend) — book-mode band has null `Groups`; PDF hides
  Chapters section; series-mode projection + poster grid; hero backdrop requested for
  EPUB and PDF.
- **Regression:** all existing detail-screen VM tests updated in place, not deleted. Full
  `Paperbunkr.App.Tests` + `Paperbunkr.Data.Tests` green per phase.
- **`Paperbunkr.App.UiTests`:** not run — flaky in this environment (standing note across many
  specs). On-screen GUI verification flagged outstanding at each phase's end.
- **Build gotcha:** `DetailHero`/`DetailBand`/`PosterRail` are brand-new `x:Class` views — add
  each `.axaml` **with** its code-behind `.cs` in the same commit (CLAUDE.md "adding a new
  Avalonia View" — `AVLN2000` otherwise).

## Risks

- **Concurrent Phase-4d/e/f/g WIP** touches `MainViewModel.cs`, `MainWindow.axaml`,
  `EventsScreen.axaml`, `IssuePropertiesScreen.axaml` and may touch `DetailTabsViewModel`. P1
  rewrites `DetailTabs.axaml` heavily. Mitigation: re-check `git status` before each phase; keep
  edits carveable; park to scratchpad not `git stash`; if `DetailTabsViewModel` has landed
  concurrent edits, rebase our changes onto them rather than the reverse.
- **`BackdropBlurRenderer`** is manga-only today. Extending to comic covers (from
  `CoverImageCache`) and book covers (EPUB/PDF-extracted) may surface a decode-path gap —
  verify with a real comic and a real EPUB before wiring the hero.
- **`DetailMeta`/`DetailPills` deletion** — grep for every consumer first (expected: only
  `DetailScreen.axaml`).
- **Sticky tab strip** — Avalonia has no native sticky positioning; the "strip outside the outer
  ScrollViewer, content in an inner one" approach needs the hero+band to be a fixed-height or
  naturally-sized header that the page scroll reveals. If this fights the layout, fall back to a
  non-sticky strip (acceptable — not a blocker).

## Deferred (future ideas, not dropped)

- **Manga volume-forward view** — a tankōbon cover shelf with chapters nested under the selected
  volume, as the default Chapters presentation. Needs per-volume cover art (a schema addition or
  a spine-placeholder treatment). Its own spec when picked up.
- **Permanent `CVDB…` tag scrub** — a one-time DB migration + an import-time filter so the IDs
  never land. Separate small task; the display filter here makes it non-urgent.
- **Activity tab real content** — needs an activity-log feature to exist first.
