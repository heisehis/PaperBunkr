# SmartList Engine v2 — Grouping, Operators, and the AllProperties Split

*Date: 2026-08-28. Scope: closes the gaps found in the post-Phase-4g SmartList/rules-engine
gap analysis against CE's real matcher engine (`_reference/ComicRackCE/ComicRack.Engine/
ComicBookGroupMatcher.cs`, `ComicBookStringMatcher.cs`, `Metadata/ComicBook/Matcher/
ComicBookAllPropertiesMatcher.cs`). Three independent gaps, one spec because all three touch
`SmartListQueryBuilder`/`SmartListCatalog` and ship together sensibly. Does not touch the new
relationship/event/continuity/age data at all — that's `IMetadataGraph` in the companion
Plugin API v3 spec (`docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md`).*

## 1. Goals and non-goals

**Goal:** bring the SmartList engine's matching *capability* to CE parity in the three places
it's currently short, without touching the parts already confirmed correct (Number/Date
operators, the deliberate 3-state→2-state Yes/No collapse, skipping `Week`).

**Non-goals:**
- No changes to `SmartListQueryBuilder`'s in-memory (`.ToList()` then LINQ-to-Objects) execution
  model. The existing doc comment's "thousands not millions" justification still holds; none of
  these three changes are about scale.
- No debounce work on `SmartScreenViewModel.RecomputeMatchCount`. Confirmed in the prior gap
  analysis that this is a deliberate, consistent app-wide pattern (`LibraryScreenViewModel`'s
  search box does the same), not a SmartList-specific weak spot. Out of scope here.
- No exposure of any of this to plugins. That's the companion Plugin API v3 spec.

## 2. Gap 1 — Flat AND-only conditions → nested AND/OR groups + per-condition NOT

**Current state:** `SmartList.Conditions` is a flat `List<SmartListCondition>`, always AND-ed —
confirmed via the entity's own doc comment as a *deliberate* v1 scope decision ("matches the
wireframe, which only shows AND pills"), not an oversight. CE's real
`ComicBookGroupMatcher` supports a `MatcherMode` (And/Or) plus arbitrary nesting, and every
individual matcher (not just groups) carries a `Not` flag.

**Why revisit it now:** Ehis has directly called SmartLists "the Rules manager of the whole
app." It's the single biggest fidelity gap against the app it's explicitly modeled on, and every
other identified SmartList gap is smaller in scope than this one. Worth doing once, properly,
rather than deferring again.

**Design — new entity shape, additive migration:**

```
SmartListConditionGroup
    Id
    SmartListId          // top-level groups only; null for nested groups (see ParentGroupId)
    ParentGroupId         // int?, self-referencing FK — null = top-level group under a SmartList
    Mode                  // SmartListGroupMode: And | Or
    SortOrder

SmartListCondition          (existing entity, extended)
    ...existing fields unchanged...
    GroupId               // NEW, int — replaces the implicit "belongs to SmartList" via SmartListId
    Not                   // NEW, bool, default false — negates this condition's own match result
```

`SmartList.Conditions` (`List<SmartListCondition>`) is replaced by `SmartList.RootGroup`
(`SmartListConditionGroup`, `Mode = And` by default) whose `Conditions` and `ChildGroups`
collections carry the tree. **Migration path, zero data loss:** every existing `SmartList` gets
one new `SmartListConditionGroup` row (`Mode = And`, `ParentGroupId = null`) as its `RootGroup`,
and every existing flat `SmartListCondition` row gets `GroupId` set to that new group's Id,
`Not = false`. This is exactly the flat-AND semantics every existing smart list already has, so
no smart list changes behavior on upgrade.

`SmartListQueryBuilder.Build` becomes recursive: evaluate a group by evaluating its own
`Conditions` (each XOR'd with its `Not` flag) and its `ChildGroups` (recursively), then combine
all of them with the group's `Mode` (And = all true, Or = any true). Leaf-condition evaluation
logic (`EvaluateText`/`EvaluateNumber`/etc.) is unchanged — only the combination logic above it
changes.

**UI — SmartScreenViewModel / the smart-list editor:** replace the flat pill list with a nested
group control: each group renders as a bordered card with an And/Or toggle at its top and an
ordered list of rows, where each row is either a condition (existing pill UI, now with a NOT
toggle prefix) or a nested group card (same control, recursively). "+ Add condition" and
"+ Add group" actions append to the current group. A flat, single-group smart list (the common
case, and every pre-migration list) renders identically to today's flat pill UI — the nesting
control degrades to today's UI exactly when there's only one group and no nesting, so this isn't
a regression for the common case, only an added capability for the uncommon one.

## 3. Gap 2 — Text operator gaps vs. CE

**Current state:** `SmartListOperator` covers 6 of CE's 8 `ComicBookStringMatcher` operators
(Is/IsNot/Contains/ContainsAny/ContainsAll/StartsWith/EndsWith), missing:
- **List contains** — CE's exact delimited-list-item match (regex word-boundary against a
  comma/semicolon-delimited field like `Writer` or `Characters`), distinct from substring
  `Contains` (e.g. matching the writer "Lee" exactly, not matching "Lee" inside "Leeroy").
- **Regular expression** — raw regex against the field value.

Additionally, `SmartListQueryBuilder.EvaluateText` hardcodes `StringComparison.OrdinalIgnoreCase`
always. CE's `ComicBookStringMatcher` has a per-condition `IgnoreCase` bool, default true, user
configurable.

**Design:**
- Add `SmartListOperator.ListContains` and `SmartListOperator.RegularExpression`.
- `EvaluateText`'s `ListContains` case splits the field value on the same delimiter convention
  already used elsewhere in the codebase for list-shaped text fields (reuse whatever
  `JoinedTags()`/`JoinedGenre()`-style helpers already assume — verify the actual delimiter
  against those helpers rather than assuming comma, per the project's own "verify against source"
  rule) and does an exact (case-sensitivity-aware) match against any one item.
- `RegularExpression`'s value is compiled via `System.Text.RegularExpressions.Regex` with a
  timeout (`TimeSpan.FromMilliseconds(250)` — bounds a pathological user-supplied regex against
  the "thousands not millions" per-condition cost budget) and any `RegexParseException`/
  `RegexMatchTimeoutException` treated as "condition doesn't match" rather than surfaced as an
  app error, so a malformed regex silently filters everything out instead of crashing the
  Smart Lists screen — same "never let one bad input crash the host" spirit as the plugin
  engine's own error handling.
- Add `SmartListCondition.IgnoreCase` (bool, default true — matches CE's own default and every
  existing condition's current de-facto behavior, so this is purely additive: no existing smart
  list changes results on upgrade). `EvaluateText` passes
  `IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal` instead of the
  hardcoded constant.
- UI: the condition-row editor gets a small "Aa" case-sensitivity toggle (default on = ignore
  case, matching the bool's default) next to the operator dropdown, shown only for Text-type
  fields; List Contains/Regular Expression join the existing operator dropdown for Text fields.

Number/Date operator sets are confirmed already at CE parity — no changes in this spec.

## 4. Gap 3 — AllProperties / Library-search duplication

**Current state:** two independent, disconnected implementations of "search across a curated
field bundle": CE's `ComicBookAllPropertiesMatcher` (deliberately deferred from SmartLists'
original design) has an unofficial cousin already shipped as `SearchMode` (`Entities/
SearchMode.cs`) + `LibraryScreenViewModel.MatchesSearch` (a hardcoded switch expression,
Series-scoped, unaware of `SmartListCatalog`).

**Design — extract one shared catalog, both call sites use it:**

```
SearchFieldBundleCatalog   (new, Paperbunkr.Data)
    IReadOnlyDictionary<SearchMode, Func<Issue, IEnumerable<string?>>> IssueFieldSelectors
```

One dictionary, keyed by the existing `SearchMode` enum (All/Series/Writer/Artists/Descriptive/
File/Catalog — unchanged, no new modes), each value a selector returning the exact per-*Issue*
field list `LibraryScreenViewModel.MatchesSearch`'s `s.Issues.Any(i => ...)` clauses already
check today for that mode (Writer mode → `Writer`; Artists mode → the 7-field art-credits list;
Descriptive → Notes/Summary/Review/Tags/MainCharacterOrTeam/Teams/Locations/ScanInformation;
File → FilePath; Catalog → BookAge/BookCollectionStatus/BookNotes/BookOwner/BookStore/
BookLocation/ISBN; All → the full ~30-field union). Transcribed field-for-field from the current
switch expression — no field-list changes, this is a pure extraction, not a behavior change.

- `LibraryScreenViewModel.MatchesSearch(Series s, string query)` keeps its **Series-level**
  checks as-is (`s.Name`/`ContainsAnyTitle`/`s.Publisher`/`s.Genre` for the "All"/"Series" cases
  — those have no per-Issue equivalent and stay hand-written), but every `s.Issues.Any(i => ...)`
  clause is replaced with `s.Issues.Any(i => SearchFieldBundleCatalog.IssueFieldSelectors[SearchMode](i).Any(v => Contains(v, query)))`.
  Confirmed behavior-identical to today (same fields, same `Contains` helper) — pure refactor.
- `SmartListCatalog` gains `SmartListField.AllProperties` (Text-typed), paired with a new
  `SmartListCondition.SearchMode` column (`SearchMode?`, null = CE's "All" default) — same
  "only populated for this one field" convention `CustomValueName`/`VirtualTagId` already use.
  `SmartListQueryBuilder.EvaluateText` special-cases `AllProperties` the same way it already
  special-cases `CustomValue`/`VirtualTag`: pull the selector for `condition.SearchMode ?? SearchMode.All`
  from `SearchFieldBundleCatalog`, and match if *any* selected field value satisfies the
  condition's operator (reusing the same operator/case-sensitivity logic as every other Text
  condition — List Contains and Regular Expression both apply here too, for free, since it's the
  same `EvaluateText` codepath).
- UI: the field picker's dropdown gets a new "All Properties" entry; selecting it reveals a
  secondary "search in" dropdown (All/Series/Writer/Artists/Descriptive/File/Catalog) right next
  to the operator dropdown, matching CE's own two-dropdown `ComicBookAllPropertiesMatcher` editor
  shape.

This closes the duplication rather than just documenting it: one field-bundle definition, two
call sites, both `SearchMode`-driven, both provably matching CE's real intent — a real Smart
List condition can now express exactly what the Library search box's dropdown already lets a
user do informally, with rule persistence and count/preview on top.

## 5. Testing

- Unit tests: recursive group evaluation (And/Or at multiple nesting depths, mixed with `Not`
  on individual conditions); the pre-existing flat-AND fixture data continues producing identical
  results post-migration (regression guard for the migration's zero-data-loss claim).
- `ListContains` vs `Contains` distinguishing test (exact item match vs. substring false-positive
  case from the design rationale above); `RegularExpression` happy path + malformed-pattern
  silently-no-match path; `IgnoreCase = false` case-sensitive match test.
- `SearchFieldBundleCatalog` parity test: for each `SearchMode`, assert the selector's field list
  is character-for-character the same set `LibraryScreenViewModel.MatchesSearch` already checks
  (a golden-list comparison, so a future edit to one call site can't silently drift from the
  other without a test failure).
- Migration test: run the up-migration against a fixture DB seeded with pre-v2 flat smart lists,
  assert `RootGroup`/`GroupId`/`Not` land exactly as specified with no result-set change.
- UI automation: nested group add/remove, AllProperties field's secondary dropdown appearing
  only when that field is selected.
