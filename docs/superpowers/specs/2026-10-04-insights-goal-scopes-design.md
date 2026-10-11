# Insights goals — more scopes, finish goals, combined scopes — design

Date: 2026-10-04. Status: **approved in a grilling pass (Q1–Q9, all recommended; Q6 answered "yes, combine scopes"), building.**
Extends `2026-09-23-insights-reading-goals-design.md` and `2026-10-04-insights-goal-outcomes-and-chart-colour-design.md`.

**CE parity:** none — ComicRack CE has no goals (deliberate deviation).

## What changes
1. **New scopes**, beyond Series / Publisher / Genre: **Reading list, Collection, Story event, Continuity, Creator, Media type** (Comic / Manga / Manhwa / … / Novel).
   *Reading status was dropped from Q1's list:* a finish goal scoped to "Planned" items shrinks as you read them (finishing changes the status), so
   progress would never converge. A count goal could use it, but a scope that is only valid for one goal kind is a trap — left out.
   Smart lists remain out (rule evaluation is its own piece of work).
2. **Two goal kinds.** *Count* (today's behaviour: N items / pages in a period) and *Finish* ("finish this reading list"): the target is the number of items in the
   scope, read live, and the deadline is optional.
3. **Combined scopes.** A goal has any number of scope filters, all of which must match (AND) — e.g. Publisher = Marvel AND Reading list = Civil War.
4. **Count each once** toggle on a count goal (off by default: a re-read counts again, as today).
5. **Reading-list chip + quick-create**: a "Goal: 12 of 30" chip on a reading list's page when a finish goal points at it, and a "Set a goal" action on a reading
   list and a collection that opens the editor pre-filled (the Renew path).

## Data model
```csharp
ReadingGoal  + GoalKind Kind (Count=0, Finish=1)  + bool DistinctOnly  + ICollection<ReadingGoalScope> Scopes
ReadingGoalScope { int Id; int ReadingGoalId (FK, cascade); GoalScopeKind Kind; string? Value; string? Label }
GoalScopeKind  + ReadingList=4, Collection=5, StoryEvent=6, Continuity=7, Creator=8, MediaType=9   (existing 0-3 unchanged)
GoalPeriodKind + NoDeadline=3
```
- `Value` is the entity id as a string (list/collection/event/continuity/creator), the publisher/genre text, or the media-type name. `Label` is the name frozen at
  creation so a goal whose target was deleted can still say what it was.
- **No deadline** = `PeriodKind.NoDeadline`, `PeriodStart` = creation day, `PeriodEnd` = `DateOnly.MaxValue`. A sentinel rather than a nullable column: making
  `PeriodEnd` nullable forces a SQLite table rebuild, which this project's orphan-column rule forbids (see `AddNavRailHoverExpandEnabled.Down`).
- The legacy single `ScopeKind`/`ScopeValue` columns stay (never dropped). The migration copies a non-Library legacy scope into a `ReadingGoalScopes` row; the
  resolver reads the rows, and falls back to the legacy pair only when a goal has none (so un-migrated rows and older tests keep working). New goals write rows only.
- Migration `AddGoalScopes`: new table + two new columns on `ReadingGoals` + the backfill. `Down()` drops the table only (no `DropColumn`). Scaffolded with the real
  `dotnet ef migrations add`, not hand-written.

## Resolution (`GoalResolver`)
Each filter becomes either an **event predicate** (Series / Publisher / Genre: the frozen columns on `ReadingEvent`, so they survive item deletion) or a
**membership set** of item ids:
- Reading list → `ReadingListItem.IssueId`; Story event → `EventMembership.IssueId`; Creator → `CreatorCredit.IssueId` for that creator.
- Collection → its `CollectionItem` rows: `IssueId` directly, `SeriesId` expanded to that series' issues, `BookId` directly.
- Continuity → its `ContinuityMembership` series, expanded to their issues.
- Media type → predicate: Novel events by `ItemType`, comic events by the event series' `ContentType`.
A goal's *universe* is the intersection of its membership sets (a comic-issue id set and a book id set). An event matches when it satisfies every predicate and every set.
- **Count goal:** as today inside `[PeriodStart, PeriodEnd]`; with `DistinctOnly`, distinct `(ItemType, ItemId)` instead of events. Pages metric unchanged.
- **Finish goal:** `CurrentValue` = distinct universe items finished **at any time** (earlier reading counts; re-reads don't double); `EffectiveTarget` = universe size,
  live. Metric is always Items. No deadline → never Missed; with a deadline the Completed/Missed rules are unchanged (Missed = deadline passed, unfinished).
- `GoalProgress` gains `EffectiveTarget` (every display uses it instead of `Goal.Target`) and `ScopeMissing` — set when a scope's list/collection/event/continuity/creator
  no longer exists. Such a goal counts nothing, never reads Completed (a zero-item universe is not "done"), and its card says "<Label> was deleted".
- Membership is looked up per `Build` (a handful of small queries per goal; goals are few). No caching.

## Editor
`GoalEditorOverlay` is rebuilt:
- **Goal type** first: *Count* / *Finish*. Finish hides Metric and the Pages option, defaults Period to *No deadline* (Custom range and the presets stay available), and requires
  at least one membership scope (list, collection, story event, continuity); Count keeps today's fields plus **Count each issue once**.
- **Scopes** is a list of rows (kind dropdown + picker) with *Add scope* / remove — replacing the four-chip row. Kind dropdown and pickers use `SuggestBox` (the app's rule: no
  `ComboBox`/`AutoCompleteBox`). Pickers search existing series / publishers / genres / reading lists / collections / story events / continuities / creators / media types.
- Title auto-suggests ("Finish Civil War", "Read 50 Marvel issues from Civil War this year") and stays editable.
- Saving writes the goal and its `ReadingGoalScope` rows in one `SaveChanges`.
- Renew copies kind, metric, target, distinct flag and every scope row; a finish goal keeps its "no deadline" shape.

## Cards, alerts, chip
- Status text for a finish goal: "12 of 30 · finished" is "Completed Mar 12 · 30 of 30"; in progress "12 of 30 · 18 to go"; Missed as before. A count goal is unchanged.
- Pace (on track / behind) applies only to count goals with a preset period, as now.
- Milestone / Missed alerts work unchanged (they key on percent and outcome).
- **Chip:** the reading list page shows "Goal: 12 of 30" for the nearest-deadline active finish goal whose scopes are exactly that list; clicking goes to Insights.
  Read from `GoalResolver`, refreshed with the list.
- **Quick-create:** "Set a goal" in the reading list and collection context menus opens the editor via the shell's goal dialog with a Finish goal and that scope pre-filled.

## Testing
- `GoalResolverTests` (Data.Tests): each new scope's membership; combined scopes intersect; finish goal counts distinct items finished before the period too and ignores re-reads;
  live target (adding a list item raises `EffectiveTarget`); no-deadline finish never Missed; deleted-scope goal -> `ScopeMissing`, not Completed; `DistinctOnly`; legacy-scope fallback;
  Series expansion for collections/continuities; media-type predicate.
- Migration test: backfill moves a legacy Publisher scope into a row; Down drops the table without touching columns (per this project's migration-test conventions).
- `GoalsViewModelTests` / `GoalEditorViewModelTests`: finish-goal status text; create writes rows; Renew copies scopes; Finish requires a membership scope.
- On-screen check of the editor and the chip by the user.

## Built as (differences from the above, 2026-10-04)
- **Finish goals take membership scopes only** (reading list, collection, story event, continuity, creator; several intersect). Series / publisher / genre / media type are count-goal filters: for a finish goal
  the target is the size of the scope's item set, and those four describe *events* (frozen columns), not items, so a target for "Marvel AND this list" cannot be counted without per-item lookups. The editor
  only offers finish goals the membership kinds, and switching a goal to Finish drops any other rows.
- The chip shows a finish goal whose scope is *exactly* this list (one filter); a goal narrowed by a second scope is about something smaller than the list.
- A deleted scope is flagged per goal (`GoalProgress.ScopeMissing`); the goal counts nothing instead of widening to the whole library.

## Out of scope
Smart-list scopes; Reading-status scope (see above); OR between scopes; editing a goal in place; goal templates; chip on collections (only reading lists get one).
