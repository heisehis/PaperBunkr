# Paperbunkr: how the Comic and Manga detail screens work (context brief)

Paste this into another assistant to brainstorm a fix. Verified against the code on 2026-10-14. The two screens are a split of one design, so most of what's below applies to both.

**Stack:** Avalonia (.NET), MVVM with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), EF Core + SQLite. A from-scratch rewrite of ComicRack CE. The screens are not wired by DI; `MainViewModel` constructs them.

## 1. Two screens, one shared kit

| | Comic ("Series Detail") | Manga/Manhua/Manhwa |
|---|---|---|
| View | `Views/DetailScreen.axaml` | `Views/MangaDetailScreen.axaml` |
| ViewModel | `ViewModels/DetailScreenViewModel.cs` | `ViewModels/MangaDetailScreenViewModel.cs` |
| Main tab | **Issues**: cover tiles (Poster / List / Card view modes), plus a **Specials** tab | **Chapters**: a release-feed list grouped by volume, with filter and sort pills |
| Other tabs | Related, Details, Activity | Related, Details, Activity (same content as comic) |
| Selection model | Multi-select tiles; exactly one selected tile "focuses" the screen on that issue | None. A chapter row click opens the reader directly |

Both are routed by `MainViewModel.LoadDetailSeries(seriesId)` (`MainViewModel.cs:~2091`). It looks up the series' `ContentType`; if `IsMangaFamily(...)` is true it calls `MangaDetail.LoadSeries` and sets `CurrentScreen = "mangaDetail"`, otherwise `Detail.LoadSeries` and `"detail"`. **The screen choice is a pure function of `Series.ContentType`.**

**Reclassifying** (the Content Type pill in the band) calls `SeriesContentTypeEditor.SetManual`, saves, enqueues a ComicInfo write-back for every issue, then re-invokes `_goDetailForSeries(seriesId)`. That reroutes to the other screen if the type crossed the Comic/Manga line. There is also an "Undo" for automatic classification by the tracker classifier (`ContentTypeSource.Provider`, `PreviousContentType`).

Navigation back/forward goes through `NavigateToDetailCore` (no history push) and `GoDetailForSeries` (pushes history, with a shared-element cover transition keyed `series-cover:{id}`). After an issue-properties edit, `ReloadDetailAfterIssueEdit` reloads the same series in place.

## 2. Vertical anatomy (identical top two thirds on both screens)

Inside a `ScrollViewer` > `StackPanel`:

1. **Back link.** "Back to Library".
2. **`DetailHero`** (`Views/DetailHero.axaml`, fixed ~360 px). It is bound to `IDetailHeaderSource`, not to the concrete VM, which keeps it reusable (Book detail and the Home spotlight use it too). Contents:
   - Full-bleed blurred backdrop. This is a *pre-rendered bitmap* from `BackdropBlurRenderer.Render(cover, 1600x680)`, not a live Avalonia effect.
   - A vignette gradient, the 142x206 foreground cover, and `HeaderTitle` in Bebas.
   - `SecondaryTitle` (manga native/romanized title; null for comic).
   - A meta-badge row: `DetailMetaBadgeGroup`, capped at 3 visible with a "+N more" toggle. Priority is Publisher, Status, Format. Year, Language, Age Rating and the rest overflow.
   - `IssueSummaryLine`, plain text such as "66 issues · 12 unread".
   - A reading-status picker.
   - The `Actions` row, each a `DetailHeroAction`:
     - Comic: primary **Continue/Re-read — Issue #N**, or **Read — Issue #N** when exactly one tile is selected; **Edit / Edit Issue / Edit N Issues**; **Change Cover** (flyout `CoverPickerViewModel`).
     - Manga: **Continue — N**, **Edit**, **Change Cover**.
     - Change Cover is hidden for remote-library series.
3. **`DetailBand`** (`Views/DetailBand.axaml` + `DetailBandViewModel`).
   - An inline meta row with the Content Type `SuggestBox` injected through a `LeadingContent` slot.
   - A 3-line synopsis with a more/less toggle.
   - Metadata chip groups (`DetailBandGroupViewModel`): capped at 12 with "+N more". Credits collapse to Writer/Artist plus a "full credits" link that jumps to the Details tab.
   - Virtual tags, special marks, and format/age-rating marks. The `^CVDB\d+$` ComicVine junk tags are hidden, display-only.
4. **Tab strip + content.**
   - Comic: `DetailTabs` draws its own strip (Issues / Specials / Related / Details / Activity). The strip is not sticky, because Avalonia has no native sticky.
   - Manga: the screen draws its own unified strip (Chapters / Related / Details / Activity), and the embedded `DetailTabs` has `ShowTabStrip=false`.

### The shared `DetailTabs` / `DetailTabsViewModel`

`DetailTabsViewModel` is **~2,900 lines** and serves both screens. Manga constructs it with `ShowIssuesTab=false, ShowTabStrip=false, IsMangaDetailHost=true`. Its `LoadSeries(Series)` does the following:

- Builds issue tiles (`IssueCardSample`). Specials (`IsSpecial()`) are pulled out of the numbered run. The rest are grouped by volume into `IssueRunGroup`s, with a run header shown only when there is more than one volume.
- Computes `Publisher`, `ReadingMode`, and per-series fit/auto-rotate/profile reader defaults.
- Builds `CreditRoles` and `AdditionalDetails` from all issues.
- Fetches the plugin scraper panel (`ComicScraperDetailView`), or falls back to the default External Metadata UI.
- Refreshes Related, Continuity, Collections, Same Event, More Like This, ExternalLinks, TrackerLinks and Activity, all through one `_contextFactory()` DB context.
- Resets `ActiveTab` (Issues for comic, Related for manga) and `ActiveDetailsSubTab="info"`.
- Runs the automatic tracker behaviors. Opening the tracker link panel automatically is manga-only (`IsMangaDetailHost`).

Tab content:
- **Related**: Continuity chips, Collection chips, an add-relation expander, then stacked `PosterRail`s (Related, Same Continuity, Same Collection, Same Event, More Like This). Each is wrapped in a card at the call site.
- **Details**: an inner **Info / Linking** sub-tab strip. Info holds Publisher & Reading Mode, Credits and Additional Details. Linking holds the plugin scraper view *or* the External Metadata search/link/apply block, plus Trackers (AniList / MangaBaka / etc.).
- **Activity**: a log list.

## 3. How data flows (important for any bug)

- **Everything is loaded synchronously on the UI thread** inside `LoadSeries` (EF `Include` with `AsSplitQuery`, then cover decode and backdrop blur). A 2026-10-11 measurement found the earlier single joined query took 1.2-1.5 s for an ordinary series, which is why it was split.
- `LoadSeries` is also the *reload* mechanism. Mark-read, cover change, content-type change and returning from the editor all call `ReloadCurrentSeries()` / `LoadSeries(id)` again. Any "stale data after X" bug is usually a path that skips this, or one that reloads only part of the state.
- **Comic issue-focus mode** (`DetailScreenViewModel.RefreshForSelection`, triggered by `Tabs`' selection-changed callback). Selecting exactly 1 tile swaps the hero cover, backdrop, summary, meta line, band chips and badge Format/AgeRating/Language to *that issue's* values. Publisher, Year and Status stay series-level. Selecting 0 or 2+ restores the series aggregate from cached `_seriesCoverImage` / `_seriesBackdrop` / `_seriesSummary` / `_seriesMetaLine`. A guard, `_isLoadingSeries`, suppresses it mid-load. `RefreshSeriesAggregates` is re-run on selection so unread counts stay correct after an in-place mark-read.
- **Manga has no focus mode.** It is always the series aggregate. `_continueIssueId` (in-progress, else first unread, else first chapter for a re-read) drives the primary button and Edit.
- **Continue-pick priority** is identical on both: an in-progress issue beats a never-opened one, which beats re-reading #1.
- **Manga Chapters pipeline:** `_allChapters` (flat, ordered by number) -> filter (All / Unread / Bookmarked / Missing) -> sort (Number or Date, asc/desc) -> `Chapters` (flat) -> `RebuildChapterGroups` -> `ChapterGroups` (one `ChapterVolumeGroup` per volume, "No volume" last; this is what the XAML binds). `IsNew` means unread and released or added within 14 days. Each group is a `Border Classes="card"` containing a `Button.chapterRow` per chapter: number, title, NEW badge, read check, bookmark, missing warning, scanlation-group `BrandMark`, date and a thin progress bar.
- **`IDetailHeaderSource`** is the contract the hero/band bind to. `HeaderTitle` must be a real notifying property. Binding through an explicit interface impl once caused "every detail screen shows the same title".
- The right-click menus come from `IContextMenuProvider` (`MangaDetailContextMenuBuilder`, `DetailIssueContextMenuBuilder`).
- `MangaDetailScreenViewModel.ActivityCenter` and `Tabs.ActivityCenter` are set by `MainViewModel` after construction.

## 4. Gotchas and project rules that constrain any fix

- **Input handling:** do not add `KeyDown` / `KeyBinding` hotkeys to a screen. Shortcuts go through `IInputService` (`ScreenInput.Attach` with `InputActionIds`). Item-level keys (Enter / Delete / F2 / arrows on a focused row) stay the control's own. The Manga chapter list already has `OnChapterKeyDown`.
- **Don't remove or detach a control from inside the routed event it is raising.** Closing a popup or mutating the collection behind a button's own row from its click throws in `Visual.OnDetachedFromVisualTree`. Defer with `Dispatcher.UIThread.Post(...)`. The code already does this for `UndoContentType`.
- **A `Button` swallows Enter, Space and left-press** before XAML `KeyDown` / `PointerPressed` handlers on it run, so tunnel from an ancestor.
- **Avalonia `ItemsControl` handles arrow keys itself** and can drop focus on virtualizing lists.
- **`BoxShadow` glow works only on `Border`**, not `Button` or `ComboBox`.
- No oval pills: use `PbRadius`, never `CornerRadius=999`.
- **CE parity:** any new field or default must be checked against `_reference/ComicRackCE` first. This is a rewrite aiming for parity plus deliberate deviations.
- Cover override is a deliberate CE deviation: any cover can be replaced, linked file or not.
- Remote (shared-library) series are read-only: `IsRemoteSeries` removes Edit and Change Cover.
- Several "deferred" items per the design docs: manga tracker progress ring and native title from provider payload, manga volume-forward shelf view, DB-persisted Issues view mode (it is in-memory today), a permanent `CVDB...` tag scrub.

## 5. Where to look

- Specs: `docs/superpowers/specs/2026-08-23-manga-detail-screen-design.md`, `2026-08-28-detail-screens-streaming-redesign-design.md`, `2026-09-13-detail-screens-redesign-design.md` (the latest hero / Issues grid / Info-Linking / card restyle), `2026-08-07-detail-screen-issue-focus-design.md`, `2026-08-28-series-detail-specials-tab-design.md`, `2026-09-13-details-tab-credits-and-fields-design.md`.
- Code: `ViewModels/{DetailScreen,MangaDetailScreen,DetailTabs,DetailBand,DetailBandGroup,IDetailHeaderSource}ViewModel.cs`, `Views/{DetailScreen,MangaDetailScreen,DetailHero,DetailBand,DetailTabs}.axaml`, `Models/{IssueCardSample,ChapterRowSample,ChapterVolumeGroup,DetailMetaBadge,DetailMetaBadgeGroup}.cs`, `ViewModels/MainViewModel.cs` (`LoadDetailSeries`).

## 6. The issue to fix (fill in before pasting)

- Which screen: Comic / Manga / both?
- What you see vs. what you expect:
- When it happens (on open, after an action, after an edit, a specific series type):
- Any error text or log:
