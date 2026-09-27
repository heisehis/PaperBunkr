# Insights pitch, slice 4 — Reading goals & challenges (#2) — design

Date: 2026-09-23. Source: "Insights pitch" item #2 in `docs/Paperbunkr-Roadmap.md:2228` (10-item idea
capture, added 2026-09-21), fourth in the 5-item queue decomposed during slice 1's brainstorm
(`docs/superpowers/specs/2026-09-22-insights-period-over-period-deltas-design.md`'s Scope section). Status:
~~**approved, not yet implemented.**~~ **Built, uncommitted** — confirmed 2026-09-26 via source:
`GoalsViewModel`/`GoalEditorViewModel`, `ReadingGoal` entity, `GoalResolver` all present and untracked.
On-screen check by the user still outstanding.

**CE parity:** ComicRack CE has no reading-goal/challenge concept of any kind — deliberate deviation, no CE
behavior to match (confirmed via a source search; no relevant hits in `_reference/ComicRackCE`).

## Scope

Slice 4 of 5. User-created, user-managed reading goals ("50 issues this year", "3,000 pages a month",
optionally scoped to a series/publisher/genre), a progress ring + pace status on the Overview tab, and
Activity Center nudges (a 50%/100% milestone the moment it's crossed, a daily behind-pace check). Slices 1-3
(period-over-period deltas, nightly snapshot + backlog burn-down, year-in-review recap) are already shipped
and untouched by this work. Item #9 (recommendations surface) remains queued as the last slice.

Unlike slices 1-3, this slice needs **real persisted state** — a goal is user-defined, not derived purely
from existing data — so it's the first Insights-family slice with its own entity/table and CRUD, alongside
the usual pure read-side resolver.

## Data model — `ReadingGoal` entity + migration

```csharp
public class ReadingGoal
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;       // short user-facing label, auto-suggested, editable
    public GoalMetric Metric { get; set; }                   // Items | Pages
    public long Target { get; set; }
    public GoalPeriodKind PeriodKind { get; set; }           // ThisYear | ThisMonth | Custom
    public DateOnly PeriodStart { get; set; }                // resolved once at creation, even for presets
    public DateOnly PeriodEnd { get; set; }
    public GoalScopeKind ScopeKind { get; set; }             // Library | Series | Publisher | Genre
    public string? ScopeValue { get; set; }                  // series id (as string) / publisher name / genre tag; null for Library
    public DateTime CreatedUtc { get; set; }
}

public enum GoalMetric { Items, Pages }
public enum GoalPeriodKind { ThisYear, ThisMonth, Custom }
public enum GoalScopeKind { Library, Series, Publisher, Genre }
```

- **Presets resolve their window once, at creation time**, into concrete `PeriodStart`/`PeriodEnd` — a
  "This year" goal created in September still spans Jan 1–Dec 31 of the creation year, not "the next 12
  months." This keeps `GoalResolver` (below) working off one uniform `[PeriodStart, PeriodEnd]` shape
  regardless of `PeriodKind`, with no special-casing at query time.
- **No auto-renewal.** Once `PeriodEnd` passes, the row simply stops being "current" — it stays visible in
  the goal list as history (complete or not), and the user creates a fresh goal for the next period
  themselves. Nothing auto-spawns a successor row. (Confirmed explicitly during this brainstorm — the
  alternative of silently auto-creating a new goal at rollover was rejected as unwanted magic.)
- **No in-place editing for v1** — only create and delete. Editing an existing goal's period/target/scope
  mid-flight raises questions (does progress-to-date get reinterpreted under the new scope?) that the
  original pitch never asked for; out of scope here, deletion + recreate covers the same need.
- Migration: adds the `ReadingGoals` table only. No existing table changes. Scaffolded via the real
  `dotnet ef migrations add AddReadingGoals` CLI (per this project's established convention — see
  `AddLibrarySnapshots` for the most recent precedent of the same "plain new table" shape), not hand-written.

## Read side — `GoalResolver`

New static class, same pure-function shape as `StatsResolver`/`RecapResolver`:

```csharp
public static class GoalResolver
{
    public static IReadOnlyList<GoalProgress> Build(PaperbunkrDbContext context, DateTime nowUtc);
}

public sealed record GoalProgress(ReadingGoal Goal, long CurrentValue, bool IsComplete, GoalPaceState PaceState, long BehindAmount);

public enum GoalPaceState { OnTrack, Behind, NotApplicable }
```

For each `ReadingGoal`:
- Filters `context.ReadingEvents` to `Kind == Finished` and `TimestampUtc` (converted to local, matching
  every other Insights-family date bucketing) falling within `[PeriodStart, PeriodEnd]`.
- Applies the scope filter directly against `ReadingEvent`'s own denormalized columns — `SeriesId` for
  `Series` scope, `Publisher` for `Publisher` scope, `PrimaryGenre` for `Genre` scope — no join needed; this
  is exactly what those columns exist for, per `ReadingEvent`'s own class doc ("frozen ... so the
  pace/composition queries stay flat and self-sufficient").
- `CurrentValue`: event count for `Metric.Items`, sum of `PagesRead ?? 0` for `Metric.Pages`.
- `IsComplete`: `CurrentValue >= Target`.
- **Pace** (skipped — `NotApplicable` — once `IsComplete`, and for `PeriodKind.Custom` goals, where there's
  no assumed daily cadence to project against beyond the goal's own start/end): `elapsedFraction =
  clamp((nowUtc - PeriodStart) / (PeriodEnd - PeriodStart), 0, 1)`; `expected = Target * elapsedFraction`;
  `Behind` (with `BehindAmount = ceil(expected - CurrentValue)`) when `CurrentValue < expected`, else
  `OnTrack`.
- A `Custom`-range goal whose `PeriodEnd` has passed without completing isn't a special resolver case — it
  naturally reads as `PaceState.NotApplicable`, `IsComplete: false`; the UI renders that as "Goal ended · X
  of Y" (see UI section).
- **`Publisher`/`Genre` scope is comics-only, by construction, not by extra filtering logic.**
  `ReadingEvent.Publisher`/`PrimaryGenre` are both explicitly null for novels (per the entity's own doc
  comments), so a novel's `Finished` event simply never matches a `Publisher`- or `Genre`-scoped goal's
  filter — no special-casing needed, but worth stating plainly: a "publisher" or "genre" goal is implicitly
  comics-only, while `Library`/`Series` scope (and `Series` covers `Book.BookSeriesId` too, since
  `ReadingEvent.SeriesId` is frozen from either) can include novels.

## UI — Overview tab goal cards + creation overlay

- **Goal cards** (`InsightsScreen.axaml`): a new `WrapPanel` above the existing "READING" section, one
  `Border Classes="card attnCard"` per `GoalProgress` — same visual family as the existing Continue/Almost
  Done/Dive In cards (confirmed via the visual companion during this brainstorm), so a variable number of
  goals wraps exactly like those already do. Each card: a new small ring control, the goal's `Title`, and a
  status line — "36 of 50 · on track" / "1,050 of 3,000 · 400 behind pace" / "Complete!" / "Goal ended · 38
  of 50". A trailing "+ Add goal" tile in the same wrap opens the creation overlay. Each card carries a
  delete affordance using this app's existing inline-delete-with-confirm idiom.
- **Ring control** (new, `Views/Stats/GoalRing.cs` or similar — same shape as `TrendSparkline`/
  `ActivityHeatmap`: a `Control` subclass, `StyledProperty<double> Percent` + `StyledProperty<bool>
  IsComplete`, `AffectsRender`, drawn via `DrawingContext.DrawGeometry`/arc math in `Render`, brush resolved
  via `TryFindResource` against the app's skin resources with a hardcoded fallback, same `ResolveBrush`
  idiom as the other two controls). Complete goals render a solid ring in the success brush.
- **Empty state**: zero goals → just the "+ Add goal" tile, no placeholder empty-state card (matches
  `ReadingAllClear`'s existing "positive default over empty placeholder" precedent on this same tab).
- **`GoalsViewModel`** (new; composed into `InsightsScreenViewModel` exactly like `Stats`/`Recap`): holds
  `IReadOnlyList<GoalProgress>`, refreshed on the same `ReadingEventRecorded` hook `Stats`/`Recap` already
  subscribe to. This is also where the **live milestone check** happens: on each refresh, compare each
  goal's new `CurrentValue`/`Target` ratio against its previously-cached ratio; crossing 50% or 100% for the
  first time raises an `ActivityAlert` immediately (`Severity: Info` for 50%, `Success`-flavored copy for
  100% — this codebase's `ActivityAlertSeverity` only has Info/Warning/Error, so "goal complete" uses `Info`
  with celebratory copy rather than inventing a new severity), deduped by `goal-50:{goalId}` /
  `goal-complete:{goalId}` so it never re-fires for the same goal.
- **`GoalEditorViewModel`** (new overlay, same create-dialog shape as `NewEventOrContinuityViewModel`: a
  `Reset()` for create mode, `onSaved`/`onCancel` callbacks). Fields: `Metric` radio (Items/Pages), `Target`
  numeric input, `PeriodKind` selector (This year / This month / Custom, the latter revealing two date
  pickers), `ScopeKind` selector (Library / Series [reusing the existing series picker control] / Publisher
  [text/autocomplete over existing publisher values] / Genre [text/autocomplete over existing genre tags]),
  and an auto-suggested `Title` ("Read 50 issues this year") that live-updates as the other fields change
  but stays directly editable. Saving resolves `PeriodStart`/`PeriodEnd` from `PeriodKind` (today's date for
  preset start, actual local year/month bounds for the end) and inserts the new `ReadingGoal` row.
- **Deletion**: a plain `context.ReadingGoals.Remove(...)` behind the existing confirm idiom — no soft
  delete, no archive table.

## Notifications + scheduling

- **Milestone nudges (50%/100%)**: live, in `GoalsViewModel`, as described above — no scheduled task
  involved, so the feedback is immediate when a `ReadingEvent` pushes a goal across a threshold.
- **Behind-pace nudges**: a new `ScheduledTaskCatalog` entry, `goal-pace-check`, `ScheduleMode.DailyAt`,
  `Priority: 15` (next free slot after `LibrarySnapshot`'s 14), `ActivityJobKind.Other`, `DefaultEnabled:
  true` (same reasoning as `LibrarySnapshot` — cheap, safe, no visible side effect). **Corrected against the
  real code, not the original assumption**: a scheduled task's `RunAsync` body has no `IActivityService` of
  its own to call `RaiseAlert` on (no DI container in this app — confirmed via `ScheduledTaskCatalog.cs`'s
  own comment on the `StoryEventAutodetect` entry), so a persistent, per-goal-deduped `ActivityAlert` isn't
  reachable from inside the task the way originally specced. Every existing task in this catalog instead
  reports its outcome through the job's own completion summary (`return summary` from the task delegate,
  which `SchedulerService` turns into `handle.Succeed(summary)` and, per the user's own notification-level
  preference, a completion toast) — `goal-pace-check` follows that same shape: `GoalResolver.Build` once
  daily, and the task returns `"No goals behind pace"` or `"{N} goals behind pace"` as its summary, with no
  per-goal `ActivityAlert`/dedupe mechanism. A goal that's behind on consecutive days simply produces the
  same kind of daily summary each time (subject to the user's own "every run / failures only / never" toast
  preference, exactly like every other routine task here) rather than one persistently-updating alert card.

## Error handling

- **Completed/expired goals never error or disappear** — `IsComplete` goals stop being eligible for pace
  checks (nothing to be behind on) and keep rendering "Complete!" until manually deleted; expired `Custom`
  goals read as "Goal ended · X of Y", same "stays as history" treatment as a completed goal, just without
  the celebratory framing.
- **Scope value pointing at deleted data** (e.g. a `Series`-scoped goal whose series was later deleted from
  the library): `GoalResolver` still counts matching `ReadingEvent` rows by `SeriesId` regardless of whether
  the `Series` row itself still exists — same accepted "reading events survive item deletion" tolerance
  established by `ReadingEvent`'s own design and already relied on by `StatsResolver`/`RecapResolver`. The
  goal card's `Title` (a static string set at creation) still displays fine even if the underlying series
  name would no longer resolve.

## Testing

- **`GoalResolverTests`** (new, `Paperbunkr.Data.Tests`, reusing `InsightsResolverTests`'s seed helpers):
  one case per `Metric` (`Items`/`Pages`) × `ScopeKind` (`Library`/`Series`/`Publisher`/`Genre`) combination;
  a pace-behind case and a pace-on-track case at a known `elapsedFraction`; a completed case
  (`PaceState.NotApplicable` once `IsComplete`); a `Custom`-range case past its `PeriodEnd` without
  completing (`IsComplete: false`, `PaceState.NotApplicable`); a mid-period elapsed-fraction edge case
  verifying the pace math directly.
- **`GoalsViewModelTests`** (new, `Paperbunkr.App.Tests`): crossing 50%/100% raises exactly one alert per
  threshold per goal (a second `Refresh()` after crossing doesn't re-raise); deleting a goal removes it from
  the exposed list and its own future pace checks.
- A new EF migration test, matching this project's per-migration convention
  (`AddLibrarySnapshotsMigrationTests.cs` is the closest recent precedent).
- **No automated test for the ring control's rendered pixels** — same accepted precedent as
  `TrendSparkline`'s own test coverage (construction/property-level assertions only).

## Out of scope for this slice

Item #9 (recommendations surface) remains queued as the final slice. In-place goal editing, auto-renewing
presets, an Activity-Center-driven "renew?" prompt at period rollover, and genre-scoping's interaction with
multi-genre issues beyond the existing `PrimaryGenre` column are all explicitly deferred — none were asked
for beyond what's specified above.
