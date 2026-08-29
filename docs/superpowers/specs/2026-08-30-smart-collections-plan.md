# Smart Collections — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-30-smart-collections-design.md*

Two implementation-level refinements to the design doc, decided while surveying the exact code
shape (functionally identical to the design doc's intent, just cheaper to build):

- **Shared leaf operator evaluators.** `SmartListQueryBuilder.EvaluateText/Number/Toggle/Date`
  (plus `ListContains`/`RegexMatches`/`SplitValues`/`ParseFloat`) already take primitive values, not
  `Issue` — they're kind-agnostic today in everything but visibility. Extracted into a shared
  internal `SmartListLeafEvaluator` and called from all three builders, instead of copy-pasting
  ~120 lines of operator logic twice. Only the per-kind `EvaluateGroup`/`EvaluateCondition`
  (selector extraction) are duplicated per builder, matching the design doc's "copied, not shared"
  call for the parts that actually differ per kind.
- **Per-kind catalog classes instead of a `TargetKind` tag on `SmartListFieldDefinition`.** Tagging
  the existing record would mean touching all ~50 existing `new(...)` call sites for a 4th
  constructor arg. Three separate small catalogs (`SmartListCatalog` unchanged,
  `SeriesSmartListCatalog` new, `NovelSmartListCatalog` new) achieve the same field-scoping outcome
  with zero churn to the existing Issue catalog.

Found during survey (both real, both need fixing as part of this work, not just as documented
"comes for free"):
- `CollectionSummary.Count` and `LibraryScreenViewModel`'s `_activeCollectionHasNonSeriesMembers`
  currently read raw `CollectionItem` rows, not `CollectionResolver.GetMembers` — the design doc
  assumed they already flowed through `GetMembers`. They don't yet; Step 10 fixes both.
- `CollectionServiceTests.cs` and `CollectionResolverTests.cs` don't exist yet — new files, not
  extensions of existing suites.

## Step 1: Entities — TargetKind, new fields, Collection rule slots
**Files:**
- `src/Paperbunkr.Data/Entities/SmartListTargetKind.cs` (new)
- `src/Paperbunkr.Data/Entities/SmartList.cs` (edit — add `TargetKind` property, default `Issue`)
- `src/Paperbunkr.Data/Entities/SmartListField.cs` (edit — append `SeriesStatus`, `SeriesSortName`,
  `NovelTitle`, `NovelAuthor`, `NovelSeries`, `NovelFormat`, `NovelSummary`, `NovelFinished`,
  `NovelChapterCount`, `NovelAdded`, `NovelOpened`, `NovelPublished`; append-only, enum is
  string-converted so no reordering risk)
- `src/Paperbunkr.Data/Entities/Collection.cs` (edit — add `IssueSmartListId`/`IssueSmartList`,
  `SeriesSmartListId`/`SeriesSmartList`, `NovelSmartListId`/`NovelSmartList` (all nullable),
  `[NotMapped] bool IsSmart => IssueSmartListId is not null || SeriesSmartListId is not null || NovelSmartListId is not null`)

**Depends on:** none
**Verify:** `dotnet build` on `Paperbunkr.Data`.

## Step 2: EF model config + migration
**Files:**
- `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit `OnModelCreating`) —
  `SmartList.TargetKind` gets `.HasConversion<string>().HasMaxLength(16)` (same pattern as
  `SmartListConditionGroup.Mode`); `Collection`'s three new FKs configured `HasOne(...).WithMany().OnDelete(DeleteBehavior.SetNull)`
  (no inverse nav collection needed on `SmartList` — one-directional, matching the design doc's "FK
  lives on `Collection`" note).
- New migration via `dotnet ef migrations add AddSmartCollections --project src/Paperbunkr.Data --startup-project src/Paperbunkr.App`
  (adds `SmartList.TargetKind` column default `'Issue'`, three nullable FK columns on `Collection`).

**Depends on:** Step 1
**Verify:** migration applies cleanly against a scratch DB; `Paperbunkr.Data.Tests` still green
(existing `SmartList`/`Collection` tests unaffected by additive columns).

## Step 3: Extract shared leaf evaluators (pure refactor)
**Files:** `src/Paperbunkr.Data/SmartLists/SmartListLeafEvaluator.cs` (new — `internal static class`,
houses `EvaluateText/EvaluateNumber/EvaluateToggle/EvaluateDate/ListContains/RegexMatches/SplitValues/ParseFloat`
moved verbatim from `SmartListQueryBuilder`), `src/Paperbunkr.Data/SmartLists/SmartListQueryBuilder.cs`
(edit — delete the moved methods, call `SmartListLeafEvaluator.X(...)` at each former call site).
**What:** No behavior change — same method bodies, new home.
**Depends on:** none (independent of Steps 1-2, can run any time)
**Verify:** `Paperbunkr.Data.Tests` → `SmartListQueryBuilderTests` all still green, byte-for-byte
same results.

## Step 4: Series query engine
**Files:**
- `src/Paperbunkr.Data/SmartLists/SeriesSmartListCatalog.cs` (new) — `Definitions` dictionary for
  the 8 reused fields (`SeriesName`, `Genre`, `Publisher`, `ContentType`, `ReadingMode`,
  `SeriesComplete`, `ReadingStatus`, `Continuity`) plus `SeriesStatus`/`SeriesSortName`;
  `TextSelectors`/`ToggleSelectors` keyed `Func<Series, ...>`. Per the approved design: `Genre`/
  `Publisher` read `series.Genre`/`series.Publisher` directly (not aggregated from issues) —
  deliberately different from the Issue-kind catalog's `i.JoinedGenre()`/`i.Publisher`. No
  `NumberSelectors`/`DateSelectors` — none of these 10 fields are numeric/date.
- `src/Paperbunkr.Data/SmartLists/SeriesSmartListQueryBuilder.cs` (new) — mirrors
  `SmartListQueryBuilder`'s shape: `SeriesSnapshot { IReadOnlyList<Series> SeriesList }`,
  `LoadSnapshot(ctx, conditions)` (`ctx.Series.Include(s => s.ContinuityMemberships).ThenInclude(m => m.Continuity).AsSplitQuery()`,
  gated on whether any condition touches `Continuity`, mirroring the existing Issue builder's
  gating), `Evaluate(snapshot, list)`, `Build(ctx, list)`, `MatchCount(ctx, list)`, private
  `EvaluateGroup(Series, SmartListConditionGroup, ...)` / `EvaluateCondition(Series, SmartListCondition)`
  dispatching to `SeriesSmartListCatalog` selectors and `SmartListLeafEvaluator`.

**Depends on:** Steps 1, 3
**Verify:** new `SeriesSmartListQueryBuilderTests.cs` (see Step 12).

## Step 5: Novel query engine
**Files:**
- `src/Paperbunkr.Data/SmartLists/NovelSmartListCatalog.cs` (new) — `Definitions` +
  `TextSelectors`/`ToggleSelectors`/`DateSelectors` keyed `Func<Book, ...>` for the 10 `Novel*`
  fields: `NovelTitle`→`Title`, `NovelAuthor`→`Author ?? ""`, `NovelSeries`→`BookSeries?.Name ?? ""`,
  `NovelFormat`→`Format.ToString()`, `NovelSummary`→`Summary ?? ""`, `NovelFinished`→`Finished`
  (toggle), `NovelChapterCount`→`ChapterCount` (number), `NovelAdded`→`AddedTime` (date),
  `NovelOpened`→`LastOpenedTime` (date), `NovelPublished`→`PublishedDate` (date).
- `src/Paperbunkr.Data/SmartLists/NovelSmartListQueryBuilder.cs` (new) — same shape as Step 4's
  builder: `NovelSnapshot { IReadOnlyList<Book> Books }`, `LoadSnapshot` (`ctx.Books.Include(b => b.BookSeries).AsSplitQuery()`),
  `Evaluate`/`Build`/`MatchCount`, private `EvaluateGroup(Book, ...)`/`EvaluateCondition(Book, ...)`.

**Depends on:** Steps 1, 3
**Verify:** new `NovelSmartListQueryBuilderTests.cs` (see Step 12).

## Step 6: Kind dispatch helper
**Files:** `src/Paperbunkr.Data/SmartLists/SmartListEvaluation.cs` (new) — single
`public static int MatchCount(PaperbunkrDbContext ctx, SmartList list)` switching on
`list.TargetKind` to the right builder's `MatchCount`. Used only where the caller doesn't know the
kind ahead of time (the Smart Lists screen's sidebar counts, Step 11) — every other caller
(`CollectionResolver`) knows the kind statically from which FK slot it's reading and calls the
specific builder directly.
**Depends on:** Steps 4, 5
**Verify:** builds; exercised indirectly by Step 11's UI tests.

## Step 7: `CollectionService` rule-slot methods
**Files:** `src/Paperbunkr.Data/Collections/CollectionService.cs` (edit) — add
`SetIssueSmartList(ctx, collectionId, smartListId)`, `SetSeriesSmartList(...)`,
`SetNovelSmartList(...)` (each: loads the `SmartList`, guards `list.TargetKind` matches the slot —
mismatch is a logged no-op per the design doc's error-handling section — then sets the FK and
saves), and `ClearIssueSmartList(ctx, collectionId)` / `ClearSeriesSmartList` / `ClearNovelSmartList`
(null the FK).
**Depends on:** Steps 1, 2
**Verify:** new `CollectionServiceTests.cs` covers these plus the existing untested CRUD surface
(see Step 12 — this file doesn't exist yet at all).

## Step 8: `CollectionResolver` hybrid union
**Files:** `src/Paperbunkr.Data/Collections/CollectionResolver.cs` (edit)
**What:**
- `CollectionMember.CollectionItemId`: `int` → `int?` (null = rule-matched, no backing row).
- `GetMembers(context, collectionId)`: load the `Collection` (need its three FK fields, not just
  its items) alongside its `CollectionItem`s. Build the manual-member list as today (real
  `CollectionItemId`, real `SortOrder`). Then, per non-null slot: run the matching builder's
  `Build(ctx, smartList)`, skip any result whose target id already has a manual member of that kind
  (dedup), and append the rest as `CollectionMember`s with `CollectionItemId = null` and
  `SortOrder = int.MaxValue` (a sentinel — display ordering for rule-matched rows uses each kind's
  own default comparer, applied by the caller after `GetMembers` returns, not `SortOrder`; see
  `LibraryScreenViewModel`'s consumption in Step 10 for where that comparer lives) grouped by kind
  in enum declaration order (Series, Issue, Book) so mixed rule-matched output is at least stable.
- `GetOtherSeriesSharingCollection`/`GetCoverHint`: no signature change — they already call
  `GetMembers` and only touch `.Series`/`.Issue`/`.Book`/`.Kind`, never `.CollectionItemId`,
  confirmed during the survey.

**Depends on:** Steps 4, 5, 7
**Verify:** new `CollectionResolverTests.cs` (Step 12) — union, dedup, ordering, and the
`ResolveCover`/`GetOtherSeriesSharingCollection` "free" cases explicitly smoke-tested per the design
doc.

## Step 9: App layer — Collection editor ripple from the `CollectionItemId` type change
**Files:**
- `src/Paperbunkr.App/ViewModels/CollectionMemberRowViewModel.cs` (edit) — `CollectionItemId`
  becomes `int?`; add `bool IsRuleMatched => CollectionItemId is null`; `RemoveCommand` (and any
  move-up/move-down command it exposes) disabled when `IsRuleMatched`.
- `src/Paperbunkr.App/ViewModels/CollectionPropertiesScreenViewModel.cs` (edit) — `Save()`'s
  `ReorderItems` call filters to `Members.Where(m => m.CollectionItemId is not null)` before
  building the ordered-id list (rule-matched rows aren't part of the manual order at all). Add three
  rule-slot sections' worth of bindable state: for each kind, an `ObservableCollection<SmartListSummary>`
  of that kind's existing lists (loaded on `Load()`), a selected-list property, `SetXRuleCommand`,
  `ClearXRuleCommand`, and a `NewXRuleCommand` that raises a callback into `MainViewModel` (mirroring
  `OpenCollectionProperties`'s existing callback-into-parent pattern) to open the Smart Lists screen
  pre-scoped to that kind; on return, re-run `Load()` so the new list shows up selected.
- `src/Paperbunkr.App/Views/CollectionPropertiesOverlay.axaml` (edit) — three new collapsed-by-default
  `Border Classes="groupBox"` sections ("Issues rule"/"Series rule"/"Novels rule"), each: a
  `ComboBox` over that kind's list, "New rule…" button, "Clear" button — same header/content pattern
  as the existing "Appearance" section. Existing member-row template gets a disabled/grayed style
  trigger on `IsRuleMatched` with the "Matches this collection's rule..." tooltip.

**Depends on:** Step 8
**Verify:** existing `CollectionPropertiesScreenViewModelTests.cs` updated for the `int?` change;
new cases for rule-slot set/clear and disabled reorder on a rule-matched row.

## Step 10: `LibraryScreenViewModel` + `CollectionSummary` — route counts/filters through `GetMembers`
**Files:**
- `src/Paperbunkr.App/Models/CollectionSummary.cs` (edit) — add `bool IsSmart`.
- `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit):
  - `RebuildView`'s `CollectionSummary` construction: `Count` changes from `collection.Items.Count`
    to `CollectionResolver.GetMembers(context, collection.Id).Count` (this is the real fix for the
    discrepancy the survey found — the design doc assumed this already worked). Set the new
    `IsSmart` from `collection.IsSmart`.
  - The `_activeCollectionHasNonSeriesMembers` computation (currently
    `activeCollection.Items.Any(i => i.IssueId is not null || i.BookId is not null)`) switches to
    checking `_activeCollectionMembers` (already resolved via `GetMembers` a few lines earlier in
    the same method) instead of raw `Items` — so a smart collection whose *only* non-series members
    are rule-matched still renders the mixed grid.
  - Line 783's series-grid filter (`s.CollectionItems.Any(ci => ci.CollectionId == collectionId)`,
    used when the active collection has no non-series members and the plain series grid renders)
    needs to also include series matched by that collection's `SeriesSmartListId` rule when set —
    simplest correct fix: when `_activeCollectionId` is set, replace this line's raw-`CollectionItems`
    filter with `_activeCollectionMembers.Select(m => m.TargetId).Contains(s.Id)` given
    `_activeCollectionMembers` is already resolved for the active collection and already contains
    the full manual+rule-matched union for the Series kind — removes the need for a second,
    separate rule check here entirely.
- Ordering for the mixed grid (`CollectionTiles` from `_activeCollectionMembers` via
  `LibraryTile.FromMember`): apply the per-kind default sort (Series by name, Issue by
  series/number, Book by title) to the rule-matched tail of `_activeCollectionMembers` before
  building tiles, per Step 8's note that `SortOrder` alone isn't a meaningful ordering key for those
  rows. Manual members (real `SortOrder`) keep rendering first, unchanged.

**Depends on:** Step 8
**Verify:** existing `LibraryScreenViewModelTests` collection-selection cases still pass; new cases
for a smart collection's sidebar count, mixed-grid inclusion, and series-grid inclusion.

## Step 11: Smart Lists screen — 3 kinds
**Files:**
- `src/Paperbunkr.App/Models/SmartListSummary.cs` (edit) — add `SmartListTargetKind TargetKind`.
- `src/Paperbunkr.App/ViewModels/SmartListConditionViewModel.cs` (edit) — `AllFieldOptions` static
  list becomes a `static IReadOnlyList<SmartListFieldDefinition> FieldOptionsFor(SmartListTargetKind kind)`
  returning `SmartListCatalog.Definitions.Values` (+ `AllProperties`/`CustomValue`/`Duplicate`/
  `VirtualTag`, Issue-only) for `Issue`, `SeriesSmartListCatalog.Definitions.Values` for `Series`,
  `NovelSmartListCatalog.Definitions.Values` for `Novel`. Every construction site threads the
  owning list's `TargetKind` through (from `SmartListGroupViewModel`, which itself takes it as a new
  constructor parameter passed down from `SmartScreenViewModel`).
- `src/Paperbunkr.App/ViewModels/SmartScreenViewModel.cs` (edit) — sidebar summaries split by
  `TargetKind` first, then the existing built-in/custom/maintenance split within `Issue` only
  (Series/Novel lists are always user-created, no built-ins to seed). `CreateNew` takes a
  `SmartListTargetKind` parameter (from a new 3-way picker in the "New" UI). `RecomputeMatchCount`
  dispatches via `SmartListEvaluation.MatchCount` instead of calling the Issue builder directly.
  Results rendering: keep `Results: ObservableCollection<IssueCardSample>` for `Issue`-kind lists
  unchanged; add `SeriesResults: ObservableCollection<SeriesCardSample>` and
  `NovelResults: ObservableCollection<BookCardSample>` (reusing those screens' existing card-sample
  factory methods, not `LibraryTile` — this is a single-kind list, not a mixed collection), each
  populated only when the selected list's kind matches, with the view binding whichever one is
  non-empty via the selected list's kind.
- `src/Paperbunkr.App/Views/MainWindow.axaml` (edit, lines ~403-499) — the existing flat
  built-in/custom/maintenance sidebar block is wrapped in three collapsible kind sections ("Issues" /
  "Series" / "Novels"), reusing the existing `Border.smRow`/`sideItemButton` styles; only "Issues"
  keeps the built-in/maintenance sub-groups, "Series"/"Novels" are flat custom-lists-only like
  today's "CUSTOM" sub-list. Results panel binds to whichever of the three `Results` collections is
  active.

**Depends on:** Steps 4, 5, 6
**Verify:** existing `SmartScreenViewModelTests.cs` updated for the `TargetKind`-scoped field list
and the new kind-split sidebar; new cases creating/editing/deleting a Series- and a Novel-kind list.

## Step 12: Tests
**Files (new unless noted):**
- `src/Paperbunkr.Data.Tests/SeriesSmartListQueryBuilderTests.cs` — one test per Series field's
  operator coverage + nested AND/OR/NOT, mirroring `SmartListQueryBuilderTests`'s existing shape.
- `src/Paperbunkr.Data.Tests/NovelSmartListQueryBuilderTests.cs` — same, for the 10 Novel fields.
- `src/Paperbunkr.Data.Tests/CollectionServiceTests.cs` — brand new (didn't exist): create/rename/
  delete/reorder/`AddItems` idempotency/toggle-off removal/exactly-one-FK guard/cascade-on-delete
  (covering the *existing*, currently-untested `CollectionService` surface) plus the new
  `SetXSmartList`/`ClearXSmartList` methods and their kind-mismatch guard.
- `src/Paperbunkr.Data.Tests/CollectionResolverTests.cs` — brand new: `GetMembers` union/dedup/
  ordering per kind; `ResolveCover`/`GetOtherSeriesSharingCollection` pick up rule-matched members.
- `src/Paperbunkr.Data.Tests/SmartListQueryBuilderTests.cs` (edit) — no new cases required (Step 3
  is a pure refactor) but re-run in full to confirm zero regression.
- `src/Paperbunkr.App.Tests/CollectionPropertiesScreenViewModelTests.cs` (edit) — update for
  `CollectionItemId` → `int?`; add rule-slot set/clear/disabled-row cases.
- `src/Paperbunkr.App.Tests/SmartScreenViewModelTests.cs` (edit) — kind-split sidebar; field-list
  scoping; create/edit/delete for Series and Novel kinds.
- `src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs` (edit) — smart-collection sidebar count,
  mixed-grid inclusion, series-grid inclusion cases per Step 10.
- Migration round-trip test (wherever the existing `AddCollections` migration test lives) — extend
  or sibling-add a case for `AddSmartCollections`: new columns/defaults preserve existing data.

**Depends on:** all prior steps (each test file depends on its corresponding implementation step)
**Verify:** full solution test run — `Paperbunkr.Data.Tests`, `Paperbunkr.App.Tests`,
`Paperbunkr.Plugins.Tests` — all green; app smoke-launch per CLAUDE.md's Avalonia-build gotcha
(new `.axaml` edits only, no new `x:Class` files, so the AVLN2000 risk doesn't apply here, but a
smoke-launch is still the only real check that the new sidebar sections render).

## Roadmap update (after everything above is green)
Update `docs/alpha-todo.md` and add a line to
`docs/superpowers/specs/2026-08-27-collections-design.md`'s "Deferred" list marking the smart
collections item done, referencing this plan and design doc.
