# Smart Collections — rule-based collection membership

*Date: 2026-08-30. Closes the deferred item from
`docs/superpowers/specs/2026-08-27-collections-design.md`: "Smart collections — a SmartList
flagged to also appear under COLLECTIONS, reusing that rule engine rather than duplicating it."
Scope grew during brainstorming beyond that one-line note (user chose the larger option at each
fork) — this doc records the final, larger shape, not the original minimal one.*

## Problem

Collections (`docs/superpowers/specs/2026-08-27-collections-design.md`) are 100% manually curated:
a user drags Series/Issue/Book rows into a `Collection` one at a time via `CollectionItem`. Smart
Lists (`docs/superpowers/specs/2026-08-06-smart-lists-design.md`,
`2026-08-28-smartlist-engine-v2-design.md`) already have a mature nested AND/OR rule engine, but it
only ever produces a flat list of `Issue` rows and only shows up on its own screen — it has no way
to become a collection a user browses alongside their manual ones.

This spec lets a `Collection` attach one rule per entity kind (Issues / Series / Novels), so its
membership becomes manual items **plus** whatever currently matches each attached rule, unioned
live. It also generalizes the Smart Lists engine — today entirely Issue/ComicInfo-shaped — to
Series and Novel targets, since those are needed to make a Series- or Novel-flavored smart
collection possible at all.

## Scope

**In scope:**

1. `SmartList.TargetKind` (`Issue` / `Series` / `Novel`) — every existing `SmartList` row defaults
   to `Issue`, unchanged behavior.
2. New `SmartListField` values for `Series` and `Novel` targets, and parallel query-builder paths
   that evaluate a `SmartList`'s existing `RootGroup` tree shape against `Series` or `Book` (novel)
   rows instead of `Issue` rows.
3. The Smart Lists screen generalizes to show and edit all three kinds.
4. `Collection` gains three nullable rule slots (`IssueSmartListId`, `SeriesSmartListId`,
   `NovelSmartListId`), each an FK to a `SmartList` of the matching `TargetKind`.
5. `CollectionResolver.GetMembers` unions manual `CollectionItem` rows with each attached rule's
   live match set, per kind, deduped by target id.
6. UI: rule-slot pickers in `CollectionPropertiesOverlay`, a sidebar badge marking a collection as
   (partly) rule-driven, and correct behavior for the `Add to Collection ▸` toggle and the mixed
   Library grid when some members are rule-matched rather than `CollectionItem` rows.

**Explicitly out of scope (unaffected by this spec):**

- `RecommendationReason.SameCollection` wiring, the Home-feed collections shelf, and typed
  `MediaRelation` edges for collections — still their own deferred follow-ons per the original
  collections spec.
- Any change to how Issue-targeted Smart Lists work today (fields, operators, nesting) — this spec
  only adds two new targets alongside the existing one.
- Extending `SmartListField.Genre`/`Publisher` to aggregate across a series' issues. Series-targeted
  conditions read `Series.Genre`/`Series.Publisher` directly, which are known-stale
  CE-migration-only columns per those fields' own doc comments on `Series`. Worth revisiting
  separately; not blocking here.

## Naming note — avoiding a `Book` collision

`SmartListField` already has a "Book-collection text" group (`BookAge`, `BookCollectionStatus`,
`BookCondition`, `BookLocation`, `BookNotes`, `BookOwner`, `BookStore`, `ISBN`,
`ScanInformation`) — these are CE's physical-book-collector metadata *on a comic issue*, unrelated
to Paperbunkr's `Book` entity (an EPUB/PDF novel). To avoid a second, confusing meaning of "Book"
in the same enum, every new field for the novel target is prefixed `Novel*` (`NovelTitle`,
`NovelAuthor`, ...) and the new `SmartListTargetKind` value is named `Novel`, not `Book`.

## Data model

### `SmartListTargetKind` (new enum)

```csharp
public enum SmartListTargetKind { Issue, Series, Novel }
```

### `SmartList` (extended)

```
SmartList
    ...existing fields unchanged...
    TargetKind   // NEW, SmartListTargetKind, default Issue
```

`TargetKind` is set at creation and immutable afterward — switching it on an existing list would
invalidate every condition referencing a field the new kind doesn't have. The rule editor hides the
kind picker once a list has any conditions.

### `SmartListField` (extended)

New Series-target values (read directly off `Series`, not joined through `Issue`):

```
SeriesStatus         // Series.Status (SeriesStatus enum) — distinct from the existing
                      // ReadingStatus and from the computed SeriesComplete
SeriesSortName       // Series.SortName
```

The existing `SeriesName`, `Genre`, `Publisher`, `ContentType`, `ReadingMode`, `SeriesComplete`,
`ReadingStatus`, and `Continuity` values are reused for Series-target lists — same field, same
label, but evaluated straight against the `Series` row instead of via `issue.Series`. No new field
values needed for those eight.

New Novel-target values (all new — the `Book` entity shares nothing with `Issue`):

```
NovelTitle           // Book.Title
NovelAuthor          // Book.Author
NovelSeries          // Book.BookSeries?.Name
NovelFormat          // Book.Format (BookFormat: Epub/Pdf)
NovelSummary         // Book.Summary
NovelFinished        // Book.Finished (toggle)
NovelChapterCount    // Book.ChapterCount (numeric; meaningless/always-0 for Pdf, same caveat
                      // Book.ChapterCount's own doc comment already states)
NovelAdded           // Book.AddedTime (date)
NovelOpened          // Book.LastOpenedTime (date)
NovelPublished       // Book.PublishedDate (date)
```

`SmartListCatalog.Definitions` grows a `TargetKind` tag per entry (reusing existing entries for the
eight shared Series fields, adding entries for the ten new ones) so the rule editor's "Add
condition" field picker can filter to only the fields valid for the list's own `TargetKind`. The
existing Issue-only fields (Writer, Penciller, PageCount, File, ...) are simply never shown for a
Series- or Novel-target list — no removal, no renumbering.

### `Collection` (extended)

```
Collection
    ...existing fields unchanged...
    IssueSmartListId    // NEW, int?, FK -> SmartList (must have TargetKind == Issue)
    SeriesSmartListId   // NEW, int?, FK -> SmartList (must have TargetKind == Series)
    NovelSmartListId    // NEW, int?, FK -> SmartList (must have TargetKind == Novel)
```

`Collection.IsSmart` (computed, `[NotMapped]`) = any of the three is non-null. A `SmartList`
plugged into one of these slots keeps working as a normal, independently-editable entry on the
Smart Lists screen — nothing about "belonging" to a collection is stored on the `SmartList` side;
the FK lives on `Collection`, so one `SmartList` could even be reused as the rule for more than one
collection (not a scenario this spec builds UI for, but nothing prevents it, and it costs nothing
to leave possible).

No FK-slot deletion cascade to the `Collection` itself: deleting the underlying `SmartList` (from
the Smart Lists screen) sets the corresponding `Collection` FK to `null` (`ON DELETE SET NULL`) —
the collection reverts to manual-only rather than being deleted. The Smart Lists screen's delete
confirmation says so when the list being deleted is in use by a collection ("Also used by
collection 'X' — deleting will make it manual-only").

### Migration `AddSmartCollections`

1. `AddColumn` `SmartList.TargetKind` (int, default `0` = `Issue`).
2. `AddColumn` `Collection.IssueSmartListId`, `SeriesSmartListId`, `NovelSmartListId` (all
   nullable int, FK `ON DELETE SET NULL`).
3. No data migration needed — every existing `SmartList` is correctly `Issue`-targeted already, and
   every existing `Collection` has all three new FKs null (manual-only, unchanged behavior).

## Query engine

### Per-kind snapshot + evaluate

`SmartListQueryBuilder`'s existing shape — `LoadSnapshot` (DB round-trip) then `Evaluate`
(in-memory recursive tree walk) — is duplicated per kind rather than generalized behind an
interface, because the three snapshots load genuinely different data (different `Include`s,
different derived lookups like `DuplicateIds`/`VirtualTags` that only make sense for `Issue`):

```
SmartListQueryBuilder            // existing, unchanged — Issue target
SeriesSmartListQueryBuilder      // NEW — mirrors LoadSnapshot/Evaluate/Build/MatchCount for Series
NovelSmartListQueryBuilder       // NEW — same shape, for Book (novel)
```

Each has its own `EvaluateGroup`/`EvaluateText`/`EvaluateNumber`/etc. leaf evaluators reading the
per-kind field list above, but the group-combination logic (AND/OR/NOT over `Conditions` +
recursive `ChildGroups`) is identical in shape to the existing Issue builder's — copied, not shared
via inheritance, matching this codebase's existing preference for a few duplicated small methods
over a premature shared abstraction (confirmed pattern: `SmartListQueryBuilder` itself is a single
`internal static class`, not a base class with subclasses).

`SmartListDataType`/operators (`Is`, `Contains`, `ListContains`, `RegularExpression`, numeric
comparisons, date ranges, toggle) are shared as-is — they're operator semantics, not Issue-specific.

### Dispatch

Callers that already know a list's kind statically — `CollectionResolver` reading its own
`IssueSmartListId`/`SeriesSmartListId`/`NovelSmartListId` slots — call the matching builder
directly, typed. The one caller that doesn't know the kind ahead of time is the Smart Lists
screen's live match-count display, which iterates whatever list is currently selected regardless of
kind; it gets a single `SmartListEvaluation.MatchCount(ctx, list)` helper that switches on
`list.TargetKind` and returns just the `int` count (never the typed row list) — the smallest
possible change to keep that display working for all three kinds.

## `CollectionResolver` — hybrid union

`GetMembers` becomes:

1. Load `CollectionItem` rows as today (manual members, real `SortOrder`).
2. If `collection.IssueSmartListId` is set, run `SmartListQueryBuilder.Build` and add any matched
   issue **not already present** as a manual member (dedup by `IssueId`).
3. Same for `SeriesSmartListId` → `SeriesSmartListQueryBuilder.Build`, deduped by `SeriesId`.
4. Same for `NovelSmartListId` → `NovelSmartListQueryBuilder.Build`, deduped by `BookId`.

`CollectionMember` needs a way to represent a rule-matched row that has no backing
`CollectionItem`. Rather than overload `CollectionMember.Id` (currently `CollectionItem.Id`) with a
sentinel, it gains an explicit flag:

```
CollectionMember(
    int? CollectionItemId,   // CHANGED from int Id — null for a rule-matched member
    int SortOrder,           // manual members keep their real value; rule-matched members get
                              // int.MaxValue-descending assigned by match order, see below
    CollectionMemberKind Kind,
    int TargetId, string Title, Series?, Issue?, Book?)
```

Every call site that used `CollectionMember.Id` to drive a remove/reorder action (the
`CollectionPropertiesOverlay` member list) checks `CollectionItemId is not null` first — a
rule-matched row's remove/reorder controls are disabled with a tooltip ("Matches this collection's
rule — edit the rule to exclude it") rather than removed from the list entirely, so the user isn't
left wondering why an item they can see isn't in the reorder list.

**Ordering:** manual members keep rendering in their `CollectionItem.SortOrder` first (unchanged
"Collection order" default), with rule-matched members of each kind appended after, ordered by the
same default the Library grid already uses for that kind (Series → name; Issue → series/number;
Book → title). This avoids inventing a merged sort key across two fundamentally different
orderings, and matches how the Library's own sort/group toolbar already treats "Collection order"
as just one of several selectable sorts — picking a different sort for a smart collection's view
ignores manual `SortOrder` for everyone, matching the existing documented behavior for manual
collections ("the other sort fields remain selectable and simply ignore `CollectionItem.SortOrder`").

`GetOtherMembersSharingCollection` (feeds the Detail tab's "Also in this collection" group) is
built on top of `GetMembers`, so it picks up rule-matched membership for free — no changes needed
there.

`ResolveCover` (`IsAutoCover` → first member's cover) also comes for free: "first member" already
means "first entry in `GetMembers`'s result," which now includes rule-matched entries when there
are no manual ones.

## UI

### Smart Lists screen — generalized to 3 kinds

The current WIP layout redesign (`docs/superpowers/specs/2026-08-29-smartlists-screen-layout-
redesign-design.md`, sidebar + editor pane) gains a kind grouping in the sidebar: three collapsible
sections ("Issues", "Series", "Novels"), each listing that kind's `SmartList` rows, mirroring the
existing "▾ Maintenance" section-header pattern already in that sidebar. Creating a new list asks
for its kind up front (a 3-way segmented control, defaulting to Issues) and the field picker in the
editor pane filters to that kind's `SmartListCatalog` entries.

### `CollectionPropertiesOverlay` — rule slots

Three new collapsed-by-default sections below the existing member list, one per kind ("Issues
rule", "Series rule", "Novels rule"). Each is empty by default (manual-only collection, today's
behavior, zero visual change). Expanding one offers:

- A dropdown of existing `SmartList` rows of that kind (empty state: "No Issue smart lists yet").
- A **New rule…** action that opens the Smart Lists screen's editor pre-scoped to that kind; on
  save, control returns to the overlay with the new list auto-selected in the dropdown.
- A **Clear** action that nulls the slot (collection reverts to manual-only for that kind; existing
  manual members of that kind are untouched).

Picking a rule immediately reflects in the member list below (rule-matched rows appear, grayed
remove/reorder controls as above) — this is a live preview, not a separate "preview" step.

### Sidebar `COLLECTIONS` section

`CollectionSummary` gains `IsSmart` (bool, `Collection.IsSmart`). A row with `IsSmart == true` shows
a small rule/wand badge next to its accent dot — same visual family as whatever icon the Smart
Lists sidebar rows already use for "this is a rule, not a manual list," reused rather than
inventing a second icon language for the same concept. `Count` already reads `GetMembers().Count`,
so it picks up the union automatically.

### `Add to Collection ▸` context submenu

Unchanged for the common case (adding/removing a `CollectionItem` row). The one new behavior: if
the clicked target is present in the collection *only* because it matches that collection's rule
(no backing `CollectionItem`), the checkmark still shows (it *is* a member), but clicking it is a
no-op with the same "matches this collection's rule" tooltip as the overlay's disabled row —
consistent messaging across both surfaces rather than two different explanations for the same
state.

### Mixed Library grid (`LibraryScreen`, collection view)

No change to the rendering path itself — it already consumes `CollectionResolver.GetMembers`'s
output as a flat mixed list per
`docs/superpowers/specs/2026-08-27-collections-design.md`'s §D. The only change is that the list it
consumes can now contain rule-matched entries alongside manual ones, which is transparent at this
layer (a `CollectionMember` is a `CollectionMember` regardless of `CollectionItemId`).

## Error handling

- The exactly-one-slot-per-kind invariant (`IssueSmartListId` must reference a `TargetKind.Issue`
  list, etc.) is enforced in `CollectionService` when setting a slot — a mismatched kind is a
  caught, logged no-op, same posture as the existing exactly-one-target `CollectionItem` guard.
- Deleting a `SmartList` that's in use by one or more collections: `ON DELETE SET NULL` at the DB
  level is the backstop; the Smart Lists screen's delete confirmation names the affected
  collection(s) up front so this isn't a silent surprise.
- A `SmartList` slot referencing conditions on a field that's since become invalid (e.g. a disabled
  `VirtualTagDefinition`, already-handled in the existing Issue builder) behaves identically for the
  new Series/Novel builders — same "excluded, not unpicklable" posture ported over.
- Regex conditions keep the existing 250ms timeout guard; the "thousands not millions" in-memory
  execution model is unchanged and now also applies to the (much smaller) Series and Book tables.

## Testing

- **`SmartListTests` (entity/migration)** — `TargetKind` defaults to `Issue` on every pre-migration
  row; new columns round-trip.
- **`SeriesSmartListQueryBuilderTests`** / **`NovelSmartListQueryBuilderTests`** (new) — one test
  per new field's operator coverage (mirroring the existing `SmartListQueryBuilderTests` shape),
  plus nested AND/OR/NOT on each kind.
- **`CollectionServiceTests`** — setting/clearing each rule slot; kind-mismatch guard; `SetNull` on
  underlying `SmartList` delete.
- **`CollectionResolverTests`** — `GetMembers` unions manual + rule-matched per kind; dedup when a
  manually-added item also matches its collection's rule; ordering (manual-first, then rule-matched
  in kind-default order); `ResolveCover`/`GetOtherMembersSharingCollection` pick up rule-matched
  members without their own new tests (documented as free, but a smoke-test each all the same).
- **`CollectionPropertiesScreenViewModelTests`** — rule-slot dropdown population; disabled
  remove/reorder on a rule-matched row; live preview on rule selection.
- **`SmartScreenViewModelTests`** — kind-grouped sidebar sections; field picker scoped to the
  selected list's `TargetKind`; kind picker hidden once a list has conditions.
- **Migration round-trip test** — new columns/defaults preserve existing data, matching the pattern
  from the original collections migration test.

## Roadmap

Update `docs/alpha-todo.md` and note in `docs/superpowers/specs/2026-08-27-collections-design.md`'s
own "Deferred" list that the smart-collections item is done, once landed. This closes out that
deferred item entirely except for the three still-separate follow-ons listed in this spec's Scope
section (recommendations, home-feed shelf, `MediaRelation` edges).
