# Migration UX — Design

*Design for docs/onboarding.md §14, the last unchecked item on the Alpha bar (§16). Wires the
already-built-and-tested `CeLibraryMigrator` (`src/Paperbunkr.Data/CeMigration/CeLibraryMigrator.cs`)
into the Paperbunkr.App UI, and extends it where its current scope was deliberately left thin.*

## Scope

Builds all five §14 steps for real — detection, dry-run preview, fuzzy series-conflict check,
commit, and a persistent post-migration "Needs Review" queue. §16 allows the queue to be stubbed
for Alpha; this pass builds it in full instead, since the pieces it needs (SmartList field
queries, Series entity) already exist.

Explicitly out of scope, deferred to Beta per §16 regardless of this pass: the real §7/§9
content-type classification/scraping pipeline. Where §14 step 5 says content-type review "reuses
the exact §7/§9 search-and-confirm flow," that flow doesn't exist yet anywhere in the app
(`ContentType` is currently a read-only label on the Detail screen). This pass adds a plain manual
`ContentType` dropdown to Detail screen instead — real, usable today, swapped out when the actual
classification pipeline lands later.

## A. Backend & data model

### New entity: `SeriesConflict`

Tracks one unresolved "is this the same series?" decision. Lives in `Paperbunkr.Data.Entities`.

```
Id                int
ExistingSeriesId  int?     // FK to Series already in the library before this migration; null if
                            // both sides are newly-created series from this same import
SeriesAId         int?     // FK, set when both candidates are new series from this import
SeriesBId         int?     // FK, set alongside SeriesAId
IncomingName      string   // the CE series name that triggered the match
MatchedName       string   // the name it was matched against
Similarity        double   // 0.0-1.0, from SeriesNameMatcher
Status            enum     // Pending / Merged / KeptSeparate
DetectedAt        DateTime
```

Only rows that stay `Pending` after the migration flow closes represent a real "needs review"
item. Anything the user actively resolves during the flow (merge or explicit keep-separate) is
written as `Merged`/`KeptSeparate` and doesn't surface in the queue — the queue only shows
`Pending`.

### Fuzzy matching: `SeriesNameMatcher`

New static class in `Paperbunkr.Data.CeMigration`. Normalizes names (trim, lowercase, strip
punctuation) and computes a Levenshtein-distance-based similarity ratio (`1 - distance /
max(len_a, len_b)`). Threshold: `0.82` (named constant, tunable). No external dependency.

Two comparison passes, both run during `Preview()`:
1. **Intra-import**: distinct incoming series names (post-exact-dedup) against each other.
2. **Against-existing**: each incoming name against `Series.Name` already in the target
   `PaperbunkrDbContext`, excluding exact matches (those are handled by the idempotent-commit path
   below, not the conflict UI).

Exact case-insensitive matches are never flagged as conflicts — `GroupBySeries`'s existing
`StringComparer.OrdinalIgnoreCase` grouping already treats those as one series.

### `CeLibraryMigrator` changes

- `Preview()` return type gains a `ConflictCandidates` list (from `SeriesNameMatcher`), alongside
  the existing `SeriesCount`/`IssueCount`/`SeriesWithGuessedContentType`.
- `Migrate()` takes a new `MigrationOptions` parameter:
  ```
  MergeIntoExisting     Dictionary<string, int>   // incoming name -> existing Series.Id to merge into
  MergeGroups           List<List<string>>        // sets of incoming names to treat as one series
  ```
  Both default to empty (no decisions made — matches "import fast," nothing forces resolution).
- **Idempotent commit**: for each incoming series-name group, if an existing `Series` with the
  exact same name (case-insensitive) already exists in `context`, no new `Series` row is created.
  Instead, incoming issues are compared against existing issues in that series by
  `(Number, Volume)`; only issues not already present are added. This makes re-running migration
  after adding books in CE a real "sync new issues in" operation rather than a duplicate-creating
  one.
- Incoming series left unresolved by the fuzzy-conflict step (no merge decision supplied) still
  commit as their own new `Series` — safe default, non-destructive — and `Migrate()` writes a
  `Pending` `SeriesConflict` row for each one so it lands in the Needs Review queue.
- `MigrationResult` gains `SeriesMerged` (count) and `ConflictsPending` (count) fields.

### Test additions (`CeLibraryMigratorTests`)

- Idempotent re-run: migrate the same fixture twice, assert second run creates 0 new `Series` and
  adds only genuinely-new issues (extend the fixture with a "re-run" variant that has one extra
  issue in an existing series).
- Fuzzy conflict detection: two similarly-named series (e.g. "Moonlit Blade" / "Moonlit Blade "
  with different punctuation, or a deliberately near-miss pair) produce a `ConflictCandidates`
  entry with the expected similarity.
- Merge-decision commit: `MigrationOptions.MergeGroups`/`MergeIntoExisting` supplied, assert the
  merged series ends up with the combined issue set and no `SeriesConflict` row.
- Unresolved conflict: no options supplied, assert a `Pending` `SeriesConflict` row is written and
  both series still exist independently.

## B. UI flow

### Entry point

New rail-nav button (`Mg`), visually grouped with `Pl` (existing `.rail.plugin` style — separated
from the core-content buttons since this is a utility action, not a persistent content screen).
Always visible; not gated on library state.

Clicking it opens `MigrationOverlay`, a new `Views/MigrationOverlay.axaml` rendered as a modal-style
overlay on top of whatever screen is currently showing — driven by a new `bool
IsMigrationOverlayOpen` on `MainViewModel` (not a `CurrentScreen` value, since it needs to float
over any screen and closing it returns to what was underneath, unlike the existing
rail-nav-driven screens).

### First-run auto-offer

In `App.axaml.cs`, after `PaperbunkrDb.EnsureCreatedAndSeeded()`: if the library has zero `Series`
*and* the default CE path exists on disk, construct `MainWindow` with the overlay pre-opened and
the path field pre-filled (still requires the user to click "Scan" — never auto-imports).

Default path: `%AppData%\cYo\ComicRack Community Edition\ComicDb.xml`, built from the existing
`ApplicationInfo.CompanyName`/`ApplicationInfo.ProductName` constants
(`src/Paperbunkr.Common/Runtime/ApplicationInfo.cs`) plus
`Environment.GetFolderPath(SpecialFolder.ApplicationData)`, rather than a new hardcoded string.

### Overlay stages

One `MigrationViewModel`, internal `Stage` enum (`Locate`, `Preview`, `Conflicts`, `Commit`,
`Results`) selects which panel renders — matching the existing pattern of one ViewModel driving
visibility flags (`IsLibrary`/`IsDetail`/… on `MainViewModel`) rather than separate routed screens.

1. **Locate** — shows the default path with a found/not-found indicator, or a "Browse…" button
   (`IFilePickerService.PickOpenFileAsync`, filtered to `.xml`) for manual selection. "Scan"
   advances to Preview.
2. **Preview** — calls `CeLibraryMigrator.LoadFromXml` then `Preview()`. Shows series count, issue
   count, guessed-content-type count. Read-only summary, no per-series gate — "import fast."
3. **Conflicts** — shown only if `Preview().ConflictCandidates` is non-empty; skips straight to
   Commit otherwise. Each candidate pair shows both names, similarity, and **Merge** / **Keep
   Separate** actions. Leaving pairs untouched is allowed — "Continue" is always enabled,
   unresolved pairs flow through to Commit as pending conflicts.
4. **Commit** — calls `Migrate()` with a `MigrationOptions` built from the Conflicts stage's
   decisions. Progress bar driven by the existing `Action<int>` progress callback on
   `LoadFromXml`. Source CE XML is never written to.
5. **Results** — final counts (series created, series merged, issues added, conflicts still
   pending). "View Needs Review" button (visible only if `ConflictsPending > 0` or any
   `ContentType == Unknown` exists) closes the overlay and reopens it directly on the Needs Review
   panel.

Reopening the `Mg` overlay after a completed migration (i.e. no in-progress Locate/Preview/Commit
state) shows the Needs Review panel by default instead of restarting at Locate — the overlay is a
dual-purpose entry point: first-run/re-run import, and ongoing review.

## C. Needs Review queue

`NeedsReviewViewModel`, three `ObservableCollection`s, refreshed the same way `RefreshSidebar()`
works on `SmartScreenViewModel`/`ReadingScreenViewModel`. All three sections are real, current
data — no derived "flag" fields beyond `SeriesConflict` itself.

- **Content Type** — live query: `Series` where `ContentType == Unknown`, via the existing
  `SmartListField.ContentType`/`SmartListQueryBuilder` machinery (same mechanism the "Missing
  Files" system smart list already uses for its field). Clicking an item navigates to that
  series' Detail screen. Detail screen gains a real editable `ContentType` dropdown
  (`DetailPillsViewModel` or `DetailMetaViewModel`, wherever the existing read-only
  `ContentTypeLabel` lives) replacing today's plain label. Item drops off the queue automatically
  once resolved, since it's a live query, not a stored status.
- **Missing Files** — reuses the existing "Missing Files" system smart list unchanged.
- **Series Conflicts** — `SeriesConflict` rows where `Status == Pending`. Same Merge/Keep-Separate
  UI as the in-flow Conflicts stage, sharing a `SeriesConflictRowViewModel` and its commands
  rather than duplicating the logic. Merge here performs the actual issue-move and deletes the
  redundant `Series` row (same code path `Migrate()`'s merge handling uses); Keep Separate just
  flips `Status`.

## Testing

- `CeLibraryMigratorTests` additions per section A.
- New `Paperbunkr.Data.Tests/SeriesNameMatcherTests.cs` — similarity scoring on known pairs
  (exact, near-miss, clearly-different), threshold boundary behavior.
- `MigrationViewModel`/`NeedsReviewViewModel` are plain ViewModels over `PaperbunkrDb.CreateContext()`
  like existing screens — no Avalonia UI dependency, so these can be covered by
  `Paperbunkr.Data.Tests`-style unit tests without needing the `Paperbunkr.App.Tests` headless-Skia
  setup that `PageImageDecoderTests` required.
- Manual pass: run against the checked-in `Paperbunkr.Data.Tests/TestData/SampleComicDb.xml`
  fixture through the real app (first-run auto-offer, manual re-run, conflict resolution, Needs
  Review queue) before calling this done.
