# Detail screens — streaming-style redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-28-detail-screens-streaming-redesign-design.md*

Three phases (P1 shared chrome + comic, P2 manga, P3 book). Each phase builds green and is
separately shippable. Re-run `git status` before each phase — the tree carries concurrent
Phase-4d/e/f/g WIP in `MainViewModel.cs`, `MainWindow.axaml`, `EventsScreen.axaml`,
`IssuePropertiesScreen.axaml`; park our WIP to scratchpad, never `git stash`.

Test commands: `dotnet test src/Paperbunkr.App.Tests`, `dotnet test src/Paperbunkr.Data.Tests`.
UiTests are not run (flaky in this env). New `.axaml` views are added **with** their code-behind
`.cs` in the same step (CLAUDE.md AVLN2000 gotcha).

---

## PHASE 1 — Shared chrome + comic `DetailScreen`

### Step 1.1: `IDetailHeaderSource` + supporting records
**Files:** `src/Paperbunkr.App/ViewModels/IDetailHeaderSource.cs` (new),
`src/Paperbunkr.App/Models/DetailHeroAction.cs` (new),
`src/Paperbunkr.App/Models/DetailHeroProgress.cs` (new)
**What:** the interface from the design (§Architecture) — `CoverBrush`, `CoverImage`,
`BackdropImage`, `Title`, `SecondaryTitle`, `MetaLine`, `IReadOnlyList<DetailHeroAction> Actions`,
`DetailHeroProgress? TrackerProgress`, extends `INotifyPropertyChanged`. `DetailHeroAction` =
record `{ string Label, ICommand Command, bool IsPrimary, bool IsEnabled }`. `DetailHeroProgress`
= record `{ int Current, int Total, string Label }`.
**Depends on:** none
**Verify:** compiles; no test yet.

### Step 1.2: `DetailHero` control
**Files:** `src/Paperbunkr.App/Views/DetailHero.axaml` + `.axaml.cs` (new)
**What:** full-bleed `UserControl`, `x:DataType="vm:IDetailHeaderSource"`. `Grid` — layer 1
`Image Source="{Binding BackdropImage}"` UniformToFill; layer 2 a `Border` with a
`LinearGradientBrush` from `PbHeroGradientStartColor`→`PbHeroGradientEndColor` top→bottom; layer 3
bottom-left `StackPanel` — cover thumb (~90×132, `posterScrim`), `Title` (Bebas ~30),
`SecondaryTitle` (visible when non-null), `MetaLine`, then an `ItemsControl` over `Actions`
rendering `Button.detailAction` (`.primary` when `IsPrimary`, else `.ghost`), `IsEnabled` bound,
`Command` bound. Layer 4 right-edge tracker ring — a small control/Path arc bound to
`TrackerProgress` (`IsVisible` on non-null), `Current`/`Total` label. Height 340, `ClipToBounds`.
Move `Button.detailAction*` styles here or into `Styles/DetailChrome.axaml` (Step 1.4).
**Depends on:** 1.1
**Verify:** renders in isolation via a design-time stub; real verification in 1.8.

### Step 1.3: `PosterRail` control
**Files:** `src/Paperbunkr.App/Views/PosterRail.axaml` + `.axaml.cs` (new),
`src/Paperbunkr.App/Models/PosterRailItem.cs` (new)
**What:** `UserControl` with `StyledProperty`s: `string Title`, `string? ContextLabel`,
`IEnumerable ItemsSource`, `bool ShowAddCard`, `ICommand? AddCommand`, `ICommand? RemoveCommand`.
Layout: header row (`Title` + right-aligned `ContextLabel`), then a horizontal
`ScrollViewer`/`ItemsControl` of cover cards (~74 wide: cover + name + optional sub-label);
`RemoveCommand` → per-card ✕ on `:pointerover` when non-null; trailing dashed "+ Add" card when
`ShowAddCard`. `PosterRailItem` = `{ int Id, string Name, string? SubLabel, IBrush CoverBrush,
Bitmap? CoverImage }`.
**Depends on:** none
**Verify:** design-time stub; real verification in 1.7.

### Step 1.4: `Styles/DetailChrome.axaml` — shared tab-strip + group styles
**Files:** `src/Paperbunkr.App/Styles/DetailChrome.axaml` (new),
`src/Paperbunkr.App/App.axaml` (edit — merge the dictionary)
**What:** lift `Button.tab` / `Button.tab.active` / `TextBlock.tabCount` verbatim from
`DetailTabs.axaml` (they're duplicated in `MangaDetailScreen.axaml` too). Add
`TextBlock.detailGroupLabel`, `Button.detailGroupMore` (the "+N more" link), and a Bebas
`TextBlock.detailGroupHeader` for Issues-grid group headers (reuse the 4a `gh` treatment — check
`LibraryToolbar.axaml` / poster-grid templates for the exact style).
**Depends on:** none
**Verify:** `dotnet build`; existing screens still render (styles are additive until consumed).

### Step 1.5: `DetailBandViewModel` + `DetailBandGroup`
**Files:** `src/Paperbunkr.App/ViewModels/DetailBandViewModel.cs` (new),
`src/Paperbunkr.App/ViewModels/DetailBandGroupViewModel.cs` (new),
`src/Paperbunkr.App.Tests/DetailBandViewModelTests.cs` (new)
**What:** absorbs `DetailMetaViewModel` + `DetailPillsViewModel`. Exposes: inline-meta strings
(status, publisher, year — content-type stays on the screen VM which owns the picker),
`Summary` + `IsSynopsisExpanded` + `ToggleSynopsisCommand` + `SynopsisToggleLabel` (copy from
`MangaDetailScreenViewModel`), and `ObservableCollection<DetailBandGroupViewModel> Groups`.
`DetailBandGroupViewModel` = `{ string Label, bool IsCreditsGroup, ObservableCollection<TagPillViewModel>
VisibleChips, int HiddenCount, bool IsExpanded, ToggleExpandCommand, string? RevealHint }`, cap
12, expand toggles between capped/full. Credits group: Writer + Artist only + a
`FullCreditsCommand` (Action supplied by ctor → screen VM switches Details tab).
`LoadSeries(series, virtualTags)` / `LoadIssue(issue, virtualTags)` build the groups
(Genres, Teams, Locations, Characters, Tags, Virtual Tags — reuse `DetailPillsViewModel.Fill`
/ `FillTagPills` logic; **Characters** = split `Issue.Characters` CSV like Teams/Locations).
Junk filter: `Regex "^CVDB\d+$", IgnoreCase` applied to the Tags group only, dropped values
counted into `HiddenCount`, `RevealHint` = `"N hidden — import IDs"`, reveal = local toggle.
Empty group → not added to `Groups` at all.
**Depends on:** 1.1 (not really — independent), reuses `TagPillViewModel`
**Verify:** new `DetailBandViewModelTests` — regex matches `CVDB123`/`cvdb9`, not `CVDBX`/
`Absolute Batman`; hidden count; cap-at-12 + expand; empty-group suppression; credits = Writer +
Artist only; inline-meta omission when fields blank.

### Step 1.6: `DetailBand` control
**Files:** `src/Paperbunkr.App/Views/DetailBand.axaml` + `.axaml.cs` (new)
**What:** `UserControl`, `x:DataType="vm:DetailBandViewModel"`. Amber left border
(`BorderThickness="2,0,0,0"`), `PbSurface2`. Content: a `ContentControl`/slot for the inline meta
row (the content-type `ComboBox` is passed in from the host screen via a `ContentPresenter` or
just rebuilt per-screen — simplest: `DetailBand` renders status/publisher/year, host screen
places its own `ComboBox` beside it), synopsis clamp + toggle, then an `ItemsControl` over
`Groups` using a `DataTemplate` that switches on `IsCreditsGroup`. `null`/empty `Groups` →
`ItemsControl` collapses (book-lite mode).
**Depends on:** 1.4, 1.5
**Verify:** 1.8.

### Step 1.7: Rebuild `DetailTabs` — Issues view modes + Related rails
**Files:** `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit, large),
`src/Paperbunkr.App/Views/DetailTabs.axaml.cs` (edit),
`src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit),
`src/Paperbunkr.Data/Entities/DetailIssueViewMode.cs` (new enum),
`src/Paperbunkr.Data/Entities/AppSettings.cs` (edit — add `DetailIssueViewMode` property),
`src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit — `HasConversion<string>().HasMaxLength(32)
.HasDefaultValue(Poster).HasSentinel(Poster)` next to `LibraryViewMode` at line ~632),
new migration `AddDetailIssueViewMode`,
`src/Paperbunkr.Data/Migrations/PaperbunkrDbContextModelSnapshot.cs` (regenerated),
`src/Paperbunkr.App.Tests/DetailTabsViewModelTests.cs` (edit)
**What:**
- **Issues tab:** add `DetailIssueViewMode ViewMode` (persisted via a ctor Action that
  reads/writes `AppSettings`, mirror `BooksScreenViewModel` load/save). Three `DataTemplate`s in
  the tab — Poster (cover + number + arc/title below on `posterTile`/`posterScrim`/`PbGlowRing`,
  read dimmed, in-progress bar, `_continueIssueId` amber ring), List (`Grid` rows), Card (~4/row
  WrapPanel, cover + title + `ISSUE #n` + Read button + inline icon row). A view-mode segmented
  control in the chrome row. Move the `OnIssueTilePointerPressed`/`OnIssueTileKeyDown` handlers so
  they attach to whichever container the active mode uses; `SelectedIssueIds` unchanged. Bebas
  group headers via `detailGroupHeader`.
- **Related tab:** replace the h-scroll + chip sections with stacked `PosterRail`s — Related
  Series (`ShowAddCard`, `AddCommand`=`ToggleAddRelationCommand` flow, `RemoveCommand`=
  `RemoveRelationCommand`), Same Continuity, Same Event (read-only), **More Like This** (new —
  Step 1.9). Continuity-membership chip row moves directly under the tab header.
- `DetailTabsViewModel`: add `MoreLikeThis` `ObservableCollection<PosterRailItem>` +
  `ShowMoreLikeThis`; add `IssueViewMode` observable + persistence Action param (default-null so
  existing tests/callers compile); map `Related`/`SameContinuity`/`SameEvent` samples →
  `PosterRailItem`.
**Depends on:** 1.3, 1.9 (More Like This resolver call)
**Verify:** `DetailTabsViewModelTests` — view-mode persistence round-trip; add/remove relation
still works; MoreLikeThis populated from stub resolver, hidden when empty; continuity membership
still writes through `ContinuityResolver`. `Data.Tests` — migration test (apply to a real
pre-migration SQLite db, assert column + default) following
`LibraryDetailsColumnsMigrationTests` shape.

### Step 1.8: Rebuild `DetailScreen` on the shared controls
**Files:** `src/Paperbunkr.App/Views/DetailScreen.axaml` (edit, large),
`src/Paperbunkr.App/Views/DetailScreen.axaml.cs` (edit),
`src/Paperbunkr.App/ViewModels/DetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/DetailMeta.axaml` + `.axaml.cs` (**delete**),
`src/Paperbunkr.App/Views/DetailPills.axaml` + `.axaml.cs` (**delete**),
`src/Paperbunkr.App/ViewModels/DetailMetaViewModel.cs` (**delete**),
`src/Paperbunkr.App/ViewModels/DetailPillsViewModel.cs` (**delete** — after manga also migrated?
see note), `src/Paperbunkr.App.Tests/DetailScreenViewModelTests.cs` (edit)
**What:** `DetailScreen.axaml` = `Grid` root: `DetailHero` (full-bleed, row 0), `DetailBand`
(row 1), sticky tab strip (row 2, outside the inner `ScrollViewer`), tab content in an inner
`ScrollViewer` (row 3). Back link floats above row 0. `DetailScreenViewModel` implements
`IDetailHeaderSource` (Title=`SeriesTitle`, MetaLine built from publisher/status/counts,
Actions list, BackdropImage via `BackdropBlurRenderer.Render` on the series cover — add to
`LoadSeries`/`RefreshForSelection`), owns a `DetailBandViewModel Band` replacing `Meta`+`Pills`,
keeps `SelectedContentType`/picker, wires `Band`'s FullCredits Action to
`Tabs.GoDetailsCommand`. `RefreshForSelection` updates `Band.LoadIssue`/`LoadSeries` +
`BackdropImage`.
**Note on `DetailPillsViewModel` deletion:** manga still uses it until P2. Keep the class through
P1, delete it in P2 Step 2.x. `DetailMeta*` is comic-only → delete now.
**Depends on:** 1.2, 1.6, 1.7
**Verify:** `DetailScreenViewModelTests` updated (Band replaces Meta/Pills assertions;
`IDetailHeaderSource` surface; full-credits switches tab). `dotnet build` then **launch the app**
(new views → confirm the XAML weave ran, per CLAUDE.md — verify by launching the exe, not just
"0 Errors"). On-screen: open a comic series detail, check hero/band/tabs/three view modes.

### Step 1.9: "More Like This" — surface `RecommendationResolver`
**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/DetailTabsViewModelTests.cs` (edit)
**What:** in `DetailTabsViewModel.LoadSeries`, call `RecommendationResolver` (read
`src/Paperbunkr.Data/Metadata/RecommendationResolver.cs` for its exact entry point + return
shape) for the loaded series, map top ~15 to `PosterRailItem` (cover via `CoverImageCache`),
populate `MoreLikeThis`. `ShowMoreLikeThis` false when empty.
**Depends on:** none (merge into 1.7 if smaller than expected)
**Verify:** covered in 1.7's test list.

### Step 1.10: P1 regression + on-screen pass
**Verify:** full `Paperbunkr.App.Tests` + `Paperbunkr.Data.Tests` green. Launch app: comic
detail — hero backdrop renders from a real comic cover, band groups cap/expand, CVDB tags
hidden with reveal, all three Issues view modes, Related rails + More Like This, selection focus
+ arrow-key nav, Continue/Edit/Cover/Reveal actions. Update `docs/alpha-todo.md` /
`alpha-roadmap.md` P5 status.

---

## PHASE 2 — `MangaDetailScreen`

### Step 2.1: `MangaDetailScreenViewModel` implements `IDetailHeaderSource`
**Files:** `src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/MangaDetailScreenViewModelTests.cs` (edit)
**What:** implement the interface. `SecondaryTitle` = native + romaji from the series'
`ExternalMetadataSnapshot` when linked (check `ExternalMetadataResolver` for the accessor),
else romaji/alt-title, else null. `MetaLine` = demographic · serialization magazine · reading
direction (demographic + magazine may need reading from snapshot or a Series field — inspect;
if unavailable, fall back to `StatusLabel · SourceLabel` and note the gap). `TrackerProgress`
from a linked tracker (`ExternalMetadataResolver.GetExternalIds` already loaded → find a
tracker-type provider; Current/Total from snapshot chapter counts + read count). RTL badge flag
= existing `ReadingModeBadge == "RTL"`. Add a `DetailBandViewModel Band` replacing `Pills`.
**Depends on:** P1 (1.1, 1.5)
**Verify:** `MangaDetailScreenViewModelTests` — `SecondaryTitle` populated/null cases;
`TrackerProgress` non-null when a tracker is linked, null otherwise; RTL badge flag.

### Step 2.2: Chapters tab → release-feed
**Files:** `src/Paperbunkr.App/Views/MangaDetailScreen.axaml` (edit, large),
`src/Paperbunkr.App/Models/ChapterRowSample.cs` (edit — add `Volume`, `IsNew`),
`src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs` (edit — `RenderChapters` groups
by volume; `IsNew` = `!IsRead && Date >= now-14d`),
`src/Paperbunkr.App.Tests/MangaDetailScreenViewModelTests.cs` (edit)
**What:** replace the flat `ItemsControl` with a grouped one — Bebas volume headers
(`i.EffectiveVolume()`, "No volume" bucket last), rows: number · title · `ScanInformation` ·
relative date · NEW badge (`IsNew`) · bookmark/missing markers. Keep filter/sort pills.
Convert `Chapters` to a grouped projection (`ObservableCollection<ChapterVolumeGroup>` or use
`ItemsControl` with a `CollectionView` group) — simplest: a `List<ChapterVolumeGroup>` rebuilt
in `RenderChapters`.
**Depends on:** 2.1
**Verify:** volume grouping + "No volume" ordering; NEW-badge cutoff (unread+recent=badge,
unread+old=none, read+recent=none).

### Step 2.3: Rebuild `MangaDetailScreen.axaml` on shared controls + delete `DetailPillsViewModel`
**Files:** `src/Paperbunkr.App/Views/MangaDetailScreen.axaml` (edit),
`src/Paperbunkr.App/Views/MangaDetailScreen.axaml.cs` (edit),
`src/Paperbunkr.App/ViewModels/DetailPillsViewModel.cs` (**delete**),
`src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs` (edit)
**What:** `DetailHero` + `DetailBand` + shared tab strip (`Chapters`/`Related`/`Details`/
`Activity`), release-feed as the Chapters content, `views:DetailTabs` for Related/Details as
today. Delete manga's local `backLink`/`contentTypePicker`/`headerPill`/`metaRow`/`tab`/
`tabCount` styles. Now that comic + manga both use `DetailBand`, delete `DetailPillsViewModel`.
**Depends on:** 2.1, 2.2
**Verify:** `dotnet build` + launch app; open a manga series — hero with native title/RTL/ring,
band, release-feed. Full `App.Tests` green.

### Step 2.4: P2 regression + on-screen pass
**Verify:** full test suites green; on-screen manga detail check; roadmap docs updated.

---

## PHASE 3 — `BookDetailScreen`

### Step 3.1: `BookDetailScreenViewModel` implements `IDetailHeaderSource` (both modes)
**Files:** `src/Paperbunkr.App/ViewModels/BookDetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/BookDetailScreenViewModelTests.cs` (edit)
**What:** implement the interface for book mode (Title=book title, SecondaryTitle=null,
MetaLine=`Author · Format · FINISHED?`, Actions=Continue/Edit/Reveal, TrackerProgress=null,
BackdropImage via `BackdropBlurRenderer` on the book cover — extend
`BookCoverImageCache`/`BookCoverThumbnailService` retrieval as needed) and series mode
(Title=series name, MetaLine=`Author · N books`, Actions=Edit series/Edit all books,
BackdropImage from a representative member cover). Add a `DetailBandViewModel Band` used in
**lite** mode — inline meta + synopsis only, `Groups` left empty/null.
**Depends on:** P1 (1.1, 1.5)
**Verify:** `BookDetailScreenViewModelTests` — band `Groups` empty in book mode; hero fields for
both modes; backdrop requested for EPUB and PDF.

### Step 3.2: Rebuild `BookDetailScreen.axaml` — hero + band + stacked sections
**Files:** `src/Paperbunkr.App/Views/BookDetailScreen.axaml` (edit, large),
`src/Paperbunkr.App/Views/BookDetailScreen.axaml.cs` (edit)
**What:** Book mode: `DetailHero` + `DetailBand` (lite) + Reading-progress section + `Chapters`
`sectionCard` (hidden for PDF / `ChaptersUnavailable`) + `Bookmarks` `sectionCard` + Delete-Book
flyout, all under the hero, no tab strip. Series mode: `DetailHero` + edit actions + poster grid
of member books (keep the existing `Border.card`/`bookCover` treatment). Delete the local
`backLink`/`metaPill`/`detailAction` styles now covered by shared styles (keep `sectionCard`/
`listRow`/`card` — book-specific).
**Depends on:** 3.1
**Verify:** `dotnet build` + launch app; open a book (EPUB + PDF) and a book series — hero,
band, sections, series poster grid. Full `App.Tests` green.

### Step 3.3: P3 regression + final pass
**Verify:** full `Paperbunkr.App.Tests` + `Paperbunkr.Data.Tests` + `Paperbunkr.Plugins.Tests`
green. On-screen pass across all three screen types. Update `docs/alpha-todo.md` +
`docs/alpha-roadmap.md` — mark UI rework Phase 5 done with what was verified. Update memory
`project_paperbunkr_ui_rework`.

---

## Test strategy summary

- **ViewModel unit tests** (xUnit, `PaperbunkrDb` in-memory/SQLite fixtures as existing tests
  use) for every new/changed VM behavior — listed per step.
- **Migration test** (`Data.Tests`, real pre-migration SQLite db + `context.Database.Migrate()`
  then assert schema) for `AddDetailIssueViewMode`, mirroring `LibraryDetailsColumnsMigrationTests`.
- **Regression:** existing `DetailScreenViewModelTests`, `MangaDetailScreenViewModelTests`,
  `BookDetailScreenViewModelTests`, `DetailTabsViewModelTests` updated in place.
- **Build verification:** after each phase touching new `.axaml`, launch the exe (not just
  `dotnet build`) to confirm the Avalonia XAML weave ran.
- **On-screen:** manual, once per phase — no unattended GUI automation (standing caveat).
- **UiTests:** not run.
