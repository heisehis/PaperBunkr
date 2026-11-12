# Series structure and discovery — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md*

Paths are relative to `src/`. Verify with targeted test filters only (CLAUDE.md "Running the App tests"); never two `dotnet` processes at
once; kill build servers afterwards. No commits. Provider calls in tests use canned JSON through fake handlers, never the live APIs.

## Completion record — 2026-11-10

Resumed Claude's partial D1.5 implementation and completed Step 0, D1.1–D1.10, D2, D3, D4, and the Library groupers in the working tree.
No commits were made. Base HEAD is `28e60a1`; unrelated existing edits were preserved. The user's instruction to finish the whole spec
supersedes the original per-slice pause for user inspection at the end of this plan.

| Area | Implemented |
|---|---|
| Step 0 | Queue anchor through reading order, reader and preloader; captured worker snapshot |
| D1 foundations | Four-index scan/edit sync and versioned background rebuild, profile migration, provider parsing, cache/completion/sweep |
| D1 pages | Asynchronous entity page, continuity switch, all applicable cards, custom/reset images, ordered reading/list saving |
| D1 integration | Detail/reader/credit-chip navigation, cached chip images, continuity Overview strips and selected-continuity navigation |
| D2 | Creator-run detector, useful-only grouping, shared progress/actions header, creator-page Runs |
| D3 | Explicit and comic title-derived arcs, manga explicit arcs, existing layout migration, shared progress/actions header |
| D4 | Shared recommendation resolver, capped candidate scoring, ten signals and multi-reason explanations, trimmed rail text |
| CE parity | Full-string Writer and Main character/team Library groupers; separate column-browser gap documented |

Authoritative Metron server serializers resolve the profile/credit-ID question; see design §9. Tests use isolated databases and fake
provider responses, never live provider accounts. Expanded selections passed 103/103 Data and 394/394 App cases; final visual/lint
revalidation passed 49/49 cases after the last theme-token correction. Limits are recorded in design §9 and the roadmap tracker.
Native Narrator, OS high-contrast/DPI and cross-platform manual checks are not claimed by headless renders.

## Survey corrections to the spec (record in the spec's §9 when building)

1. **Scans never update the entity indexes.** `LibraryFolderScanner`, `IssueFileRescanService` and the resync path call no
   `CharacterResolver`/`TeamResolver`/`LocationResolver`/`CreatorResolver` sync; only edits, scrapes and merges do. The one-time character
   backfill (`Services/PaperbunkrDb.cs:79-87`) runs only when the Character table is empty. So every page built on the indexes would miss
   newly scanned issues. Step D1.1 fixes all four indexes, not just creators, and replaces the "table empty" guard with a version flag
   (`AppSettings.EntityIndexVersion`, a column the spec did not list).
2. **The issue editors sync characters only** (`IssuePropertiesScreenViewModel.cs:970`, `BulkIssuePropertiesScreenViewModel.cs:363`). They
   switch to the four-index sync.
3. **Detail's Story Arc grouping is the shared Library group field** (`DetailGrouping.ForField(IssueListGroupField.StoryArc)` through
   `DetailLayoutRendering.Grouping`). The detector-driven groupings are new Detail-only constants (`DetailGrouping.Arcs`,
   `DetailGrouping.CreatorRuns`); Detail's options list shows "Story arcs" in place of the "Story Arc" field entry. The Library keeps its plain
   Story Arc grouping.
4. **Library grouping takes one key per row** (`GroupStrategies` returns a single `Func<IssueListRow, string>`). Verified against CE during
   this continuation: `ComicBookGroupWriter.GetGroup` passes the **entire Writer string** to `SingleComicGrouper.GetNameGroup`;
   `SingleGrouper.IsMultiGroup` is false. `ComicBookStringGrouper.CreateMatcher` using `ListContains` is a search operation, not multiple
   grouping keys. Use the full Writer field for parity. CE's separate `SearchBrowserControl` is the multi-value browser.
5. **Pills and chips carry names, not ids** (`DetailBandViewModel` builds pills from strings). Entry points resolve name → entity id
   (case-insensitive, per kind) before navigating; a name with no entity row falls back to the old Library search.
6. **The current entity page loads synchronously on the UI thread** (`MetadataEntityDetailScreenViewModel.Load`). The new one loads off it.
7. **`StoryEventDetailsCompletion` stamps `DetailsCheckedAt` even when the fetch fails.** The new profile completion deliberately does not
   (spec §3.3: a failure retries on the next open).
8. **Another session has uncommitted work** in `ContinuityScreen.axaml`, `ContinuityScreenViewModel.cs`, `ContinuityScreenNavigation.cs`,
   `StoryEventCoverCache.cs` and `StoryEventDetailsCompletion.cs` (event smart fill). Build on top of the working tree; never revert or
   reformat those changes. Check `git status`/`git diff` on those files before each continuity step.

---

## Step 0. Reader queue anchor (prerequisite)
**Files/what:** step B1 of `docs/superpowers/specs/2026-11-10-smart-features-2-plan.md` (`ReadingContextKind.Queue`, optional queue anchor
through `ReadingOrderResolver`, `NextIssueStager.EnsureStaged` and `ReaderScreenViewModel.LoadIssue/Load`). Skip if already built.
**Depends on:** none
**Verify:** as in that plan.

---

## Slice D1 — Pages, images and Continuity

### D1.1 Four-index sync and one-time rebuild
**Files:** `Paperbunkr.Data/Metadata/EntityIndexSync.cs` (new), `Paperbunkr.Data/Entities/AppSettings.cs` (edit),
`Paperbunkr.Data/Migrations/<ts>_AddEntityIndexVersion.cs` + Designer (new), `Paperbunkr.App/Services/LibraryFolderScanner.cs` (edit),
`Paperbunkr.App/Services/IssueFileRescanService.cs` (edit), `Paperbunkr.App/ViewModels/IssuePropertiesScreenViewModel.cs` (edit, :970),
`Paperbunkr.App/ViewModels/BulkIssuePropertiesScreenViewModel.cs` (edit, :363), `Paperbunkr.App/Services/PaperbunkrDb.cs` (edit, :58/:79),
tests `Paperbunkr.Data.Tests/EntityIndexSyncTests.cs`, `AddEntityIndexVersionMigrationTests.cs` (new)
**What:** `EntityIndexSync.SyncIssues(context, issueIds)` calls the four resolvers' `SyncFromIssue`; `RebuildAll(context)` calls their four
`RebuildAll`. Scanner: after the batch `SaveChanges` (:625) sync the new issue ids; also after the resync-from-file path (:784-860). Rescan
service: sync the issue after re-reading ComicInfo. Editors: replace the character-only call. Startup: replace `BackfillCharacterIndex` with
a check of `AppSettings.EntityIndexVersion` (int, default 0; current = 1); when lower, start `RebuildAll` as a background Activity Center job
("Indexing characters, teams and creators") and set the version on success. Hand-edit the migration's `Down()` to leave the column.
**Depends on:** none
**Verify:** sync on scan/rescan/edit adds and removes rows for all four kinds; rebuild runs once and not again; migration test.
`--filter "FullyQualifiedName~EntityIndexSyncTests|FullyQualifiedName~AddEntityIndexVersionMigrationTests"` (Data tests) and the touched App
classes' existing tests (`IssuePropertiesScreenViewModelTests`, `BulkIssuePropertiesScreenViewModelTests`, `LibraryFolderScannerTests`).

### D1.2 Image and profile columns
**Files:** `Paperbunkr.Data/Entities/Character.cs`, `Team.cs`, `Creator.cs` (edit), `Paperbunkr.Data/Entities/EntityImageSource.cs` (new
enum None/Metron/ComicVine/Custom), migration `<ts>_AddEntityProfiles` (new), `Paperbunkr.Data.Tests/AddEntityProfilesMigrationTests.cs` (new)
**What:** the six columns from spec §2 on each of the three entities. Check that `RebuildAll`/pruning in the resolvers never deletes a row
that has a custom image (pruning removes entities with no appearances: keep the columns' data by matching on name when a row is recreated,
or skip pruning rows with `CustomImagePath`). Pick the second (skip) and note it.
**Depends on:** none
**Verify:** migration test; resolver prune test keeps a row with a custom image.

### D1.3 Provider profile fetch
**Files:** `Paperbunkr.Data/ComicVine/EntityProfile.cs` (new record `EntityProfile(string? ImageUrl, string? Description, IReadOnlyList<string> Aliases)`
and interface `IEntityProfileSource`), `MetronClient.cs` (edit: `GetEntityProfileAsync(kind, id)` over `character/{id}/`, `team/{id}/`,
`creator/{id}/`, same shape as `GetUniverseAsync` :358), `ComicVineClient.cs` (edit: `character/4005-{id}/`, `team/4060-{id}/`,
`person/4040-{id}/` with `field_list=image,deck,aliases`), `MetronClient.cs` credits (:628, verify), tests `Paperbunkr.Data.Tests/EntityProfileFetchTests.cs` (new, canned JSON)
**What:** parse `image` (Metron: string URL; ComicVine: `image.original_url`/`medium_url`), description (Metron `desc`; ComicVine `deck`,
HTML stripped with the existing description cleaner used for events), aliases (Metron `alias` list; ComicVine `aliases` newline text). 404 →
null. **Verify against the real APIs once by hand** (not in tests): record in spec §9 which fields Metron actually returns and whether its
credit objects carry a creator id; if they do, store it through `ComicMetadataExternalIdSync` (Kind Creator).
**Depends on:** none
**Verify:** parser tests for both providers, null on 404, aliases split.

### D1.4 Image cache and profile completion
**Files:** `Paperbunkr.App/Services/EntityImageCache.cs` (new, `StoryEventCoverCache` pattern: `entity-images/` directory, `SHA256(url).jpg`
for provider images, `{kind}-{id}.jpg` for custom; temp file → decode check → move; `Get`, `DownloadAndCacheAsync`, `TrySetCustom`,
`Invalidate`), `Paperbunkr.App/Services/EntityProfileCompletion.cs` (new), `Paperbunkr.App/Services/EntityImageResolver.cs` (new),
tests `Paperbunkr.App.Tests/EntityProfileCompletionTests.cs`, `EntityImageResolverTests.cs` (new)
**What:** `EntityProfileCompletion.CompleteAsync(context, kind, id, ct)`: if `ImageCheckedAt` is null or older than 30 days, look up the
entity's external ids (`ComicMetadataExternalId` Kind Character/Team/Creator); try Metron (client from `ComicProviderFactory.Create(...,
ComicVineRequestPriority.Low)`), then ComicVine; on a result store the fields, download the image, stamp `ImageCheckedAt`; on "no record"
stamp too; on an exception leave the stamp unset. Respect `MetronClient.BackgroundShouldWait`. `EntityImageResolver.For(entity, firstAppearanceIssueId)`
returns the display source in spec order (custom > cached provider > first-appearance cover cropped 4:5 > initials).
**Depends on:** D1.2, D1.3
**Verify:** order Metron → ComicVine; stamp on miss, not on failure; 30-day recheck; resolver order; custom wins.

### D1.5 Background image fill task
**Files:** `Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs` (edit: new descriptor "Fill character images", `ScheduleMode.Interval`
daily, Low priority, `SchedulerResourceClass` network), `Paperbunkr.App/Services/EntityImageSweep.cs` (new), test `EntityImageSweepTests.cs` (new)
**What:** up to 20 entities per run with an external id and no `ImageCheckedAt`, most appearances first, through `EntityProfileCompletion`;
stops on a provider error. Pattern `ContinuityUniverseSweep`.
**Depends on:** D1.4
**Verify:** batch size, order, stops on error.

### D1.6 Entity page data
**Files:** `Paperbunkr.Data/Metadata/EntityPageResolver.cs` (new), test `Paperbunkr.Data.Tests/EntityPageResolverTests.cs` (new)
**What:** `Build(context, kind, id, continuityId?)` returns `EntityPage { Name, Aliases, Description, AppearanceCount, SeriesCount, ReadCount,
Continuities (id, name, count), Events (EventChronology order, cover key, year, continuity, issue count), FirstAppearance, Series (count,
read), ByYear, Unread (reading order), Roles (creator), ReadingOrder (issue ids by cover date, ties series sort name then NumberSortKey) }`.
Appearances per kind from the appearance/credit tables; continuity attribution through `ContinuityMembership` of each appearance's series;
`continuityId` narrows everything. Publisher keeps its published-series list; location has no images.
**Depends on:** D1.1
**Verify:** each field on a seeded fixture; narrowing by continuity; undated issues last; event order.

### D1.7 Entity page view model and view
**Files:** `Paperbunkr.App/ViewModels/MetadataEntityDetailScreenViewModel.cs` (rewrite), `Paperbunkr.App/Views/MetadataEntityDetailScreenView.axaml`
(+ `.axaml.cs`) (rewrite), `Paperbunkr.App/ViewModels/MainViewModel.cs` (edit: constructor wiring at :271 for reader-with-queue, reading-list
creation, continuity and event navigation), tests `Paperbunkr.App.Tests/MetadataEntityDetailScreenViewModelTests.cs` (new or extend), render test
**What:** load `EntityPageResolver.Build` off the UI thread (generation guard, like other screens), then `EntityProfileCompletion` in the
background and refresh the header when it lands. Header, continuity switch (`segGroup` up to 4 continuities, `SuggestBox` above), buttons,
cards in a two-column grid that drops to one column below ~1000 px. Read in order → `LoadIssue(first, queue: ReadingOrder)`; Save as reading
list → the existing reading-list creation used by "Save as reading list" elsewhere (find the shared helper; slice B's B5 uses one); Change
image → the existing cover picker plus a file picker, then `EntityImageCache.TrySetCustom`; Reset image. Register the screen's shortcuts with
`ScreenInput.Attach` (Refresh). Load the `paperbunkr-design-language` skill and the matching avalonia subskills before writing XAML.
**Depends on:** D1.4, D1.6, Step 0
**Verify:** VM tests (switch narrows, buttons call the right actions, profile refresh); headless render in default/daylight/matrix at 1366 and
1000 px; keyboard reach in the real window (`RealWindowKeyboardTests` pattern).

### D1.8 Entry points and chip images
**Files:** `MainViewModel.cs` (edit: `GoMetadataEntityByName(kind, name)` resolving name → id, falling back to `GoLibraryWithSearch`),
`DetailScreenViewModel.cs` (edit: pass it through), `DetailBandViewModel.cs` (edit: writers/artists → Creator, Teams, Locations, Characters
pills open the entity; genres/tags unchanged), `DetailTabsViewModel.cs` (edit: credit chips :640-692), `ReaderScreen.axaml` + reader info
builder (edit: info chips :320, :1002 become buttons), the Detail band pill template (edit: optional 20 px squircle image from
`EntityImageCache.Get`), tests (extend `DetailBandViewModelTests`, `DetailTabsViewModelTests`; new navigation test)
**What:** wire every entry point; chip images only when already cached (no fetch from a chip).
**Depends on:** D1.7
**Verify:** each entry point opens the right entity; unknown name falls back to search; history Back returns; render with and without images.

### D1.9 Continuity links
**Files:** `Paperbunkr.App/Services/ContinuityScreen/ContinuityOverview.cs` (edit: `KeyCharacters`, `KeyTeams` on `ContinuityOverview` and
input), `ContinuityOverviewLoader.cs` (edit: top 10 by appearances in member series), `Views/ContinuityOverviewView.axaml` (edit: two strips,
72×88 tiles, hidden when empty), `ContinuityScreenNavigation.cs` (edit: `OpenEntity(kind, id, continuityId)`), `DetailTabs.axaml` +
`DetailTabsViewModel.cs` (edit: Related tab Continuity chips open the continuity, :455-472), tests (extend `ContinuityOverviewBuilderTests`,
new strip render test)
**What:** see spec §3.4. Read correction 8 first: these files have another session's uncommitted changes.
**Depends on:** D1.7
**Verify:** strip counts and order; hidden when empty; click opens the entity with the continuity selected; chip opens the continuity.

### D1.10 Slice check
**What:** lint set (`XamlConsistencyLintTests|ThemeHardcodedColorLintTests|SharedControlsStyleTests`), `avalonia-pro-max/review-checklist` on
the new views, spec §9 notes (corrections, the real-API field check), build into `bin`, then the user's on-screen check: open Storm from a
Detail chip, switch continuity, Read in order, change and reset the image, open a continuity's Key characters.
**Depends on:** D1.1–D1.9

---

## Slice D2 — Creator runs

### D2.1 Run detector
**Files:** `Paperbunkr.Data/Metadata/CreatorRunDetector.cs` (new), `Paperbunkr.Data.Tests/CreatorRunDetectorTests.cs` (new)
**What:** spec §4.1 over `CreatorCredit` rows (Writer; Penciller and Artist combined), issues by `NumberSortKey`, placeholders neutral.
**Depends on:** D1.1
**Verify:** the spec §4.3 table.

### D2.2 Detail grouping and creator page card
**Files:** `Paperbunkr.App/Models/DetailListLayoutState.cs` (edit: `DetailGrouping.CreatorRuns`), `Models/IssueRunGroup.cs` (edit: optional
`Subtitle`, `ReadCount`, `TotalCount`, `ReadCommand`, `SaveCommand`), `ViewModels/DetailListOptionsViewModel.cs` (edit: offer "Creator runs"
only when the rule in spec §4.2 holds), `ViewModels/DetailTabsViewModel.cs` (edit: `GroupCards` branch, :251), `Views/DetailTabs.axaml`
(edit: section header with progress and buttons), `MetadataEntityDetailScreenView.axaml` (edit: Runs card), tests (extend
`DetailTabsViewModelTests`, `DetailListOptionsViewModelTests`)
**What:** sections per run plus "Other issues"; Read this run → queue; Save → reading list.
**Depends on:** D2.1, D1.7, Step 0
**Verify:** offer rule; sections and counts; queue order; render.

### D2.3 Slice check
Lint, checklist, build into `bin`, on-screen check (a long comic series with two writers; a manga series shows no option).

---

## Slice D3 — Arcs

### D3.1 Arc detector
**Files:** `Paperbunkr.Data/Metadata/SeriesArcDetector.cs` (new), `Paperbunkr.Data.Tests/SeriesArcDetectorTests.cs` (new)
**What:** spec §5.1; number words up to twenty; manga uses the field only.
**Depends on:** none
**Verify:** the spec §5.3 table.

### D3.2 Detail "Story arcs" grouping
**Files:** `DetailListLayoutState.cs` (edit: `DetailGrouping.Arcs`), `DetailListOptionsViewModel.cs` (edit: "Story arcs" replaces the Story Arc
field entry on Detail only), `DetailTabsViewModel.cs` (edit: `GroupCards` branch), `DetailTabs.axaml` (reuse D2.2's section header; if D2 is
not built yet, D3.2 adds the header), tests (extend the D2.2 tests)
**What:** sections ordered by first issue, source label, progress, Read this arc, Save, "Other issues". A saved layout with the old
`StoryArc` field value maps to `Arcs` on Detail.
**Depends on:** D3.1, Step 0
**Verify:** sources and progress; old layout value migrates; render.

### D3.3 Slice check
Lint, checklist, build into `bin`, on-screen check (a series with a filled Story Arc field; one with "Part N of M" titles).

---

## Slice D4 — More like this

### D4.1 Recommendation engine
**Files:** `Paperbunkr.Data/Metadata/RecommendationResolver.cs` (edit; keep `GetRecommendations(context, seriesId, limit)` and
`RecommendationCandidate`, add a multi-reason explanation), `Paperbunkr.Data.Tests/RecommendationResolverTests.cs` (extend), perf test on a
10k-issue fixture (new, tagged `Speed=Slow` if over 30 s)
**What:** spec §6 candidate pool, pre-rank to 200, weights table, explanation. Uses `CreatorCredit` and `CharacterAppearance` (D1.1 makes them
complete).
**Depends on:** D1.1
**Verify:** spec §6.1.

### D4.2 Callers
**Files:** `DetailTabsViewModel.cs` (edit: More Like This rail :3216-3274 — confirm it computes off the UI thread and fills later; fix if not),
`HomeScreenViewModel.cs` (:792-829) and `InsightsRecommendationResolver.cs` (check only: same signature, explanation text now longer — make
sure their templates trim), `DetailTabs.axaml` (edit if the reason line needs trimming), tests (extend the rail's tests)
**Depends on:** D4.1
**Verify:** rail fills without blocking; Home dismissals still work; render at 1366 px.

### D4.3 Slice check
Lint, checklist, spec §9 (tuned weights), build into `bin`, on-screen check of the rail, Home and Insights.

---

## Library group-by Writer and Main character (spec §7)
**Files:** `Paperbunkr.Data/Entities/IssueListGroupField.cs` (edit: append `Writer`, `MainCharacterOrTeam` at the end — values persist by
name), `Paperbunkr.App/Models/IssueListRow.cs` (edit: expose `Writer`/`MainCharacterOrTeam` if missing), `Models/IssueListFieldCatalog.cs`
(edit: register both, Writer keyed on the complete field per correction 4), tests (extend the catalog tests)
**Depends on:** none
**Verify:** grouping counts on a fixture; layouts saved before the change still load.

---

## Order and parallelism

Step 0 → D1.1 → (D1.2, D1.3 in either order) → D1.4 → D1.5; D1.6 after D1.1; D1.7 after D1.4/D1.6; D1.8, D1.9 after D1.7. D2 and D4 need
D1.1 (D2.2 also D1.7). D3 and the Library groupers are independent and can be built any time. The user’s instruction to finish the whole spec supersedes the original pauses for per-slice user inspection; automated themed screen checks cover the completed slices.
