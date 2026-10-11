# Insights gaps — goal outcomes + chart colour semantics — design

Date: 2026-10-04. Status: **draft, awaiting user review.** Follows the grilling pass of 2026-10-04 (all eleven
recommended answers accepted). Builds on `2026-09-23-insights-reading-goals-design.md` and
`2026-09-23-insights-redesign-design.md`; nothing in those is reversed, only extended.

**CE parity:** ComicRack CE has no goals and no Insights/Stats charts of this kind — deliberate deviation, nothing to match.

**Avalonia notes** (to be checked against `avalonia-pro-max/review-checklist` before calling the UI done): every colour
below is a skin resource (`Pb*Brush`/`Pb*Color`), never a hex literal, so it reacts to the skin system. The donut and
ring are `OnRender` controls, so they resolve brushes with `TryFindResource` as they do today.

## Part 1 — Goal outcomes

### Problem
`GoalResolver` has two outcomes (`IsComplete` or not) and only recognises an *ended* goal for `PeriodKind.Custom`. A missed
"This year" goal keeps reading "N behind pace" indefinitely, the Today hero (`GoalsViewModel.HeroGoal`, nearest `PeriodEnd`) can
pick an expired goal, and no alert marks a miss.

### Model (derived, no migration)
```csharp
public enum GoalOutcome { Active, Completed, Missed }
public sealed record GoalProgress(ReadingGoal Goal, long CurrentValue, bool IsComplete, GoalPaceState PaceState,
    long BehindAmount, GoalOutcome Outcome, DateOnly? CompletedOn);
```
- `Completed` — `CurrentValue >= Target` (unchanged rule).
- `Missed` — not complete **and** `today > PeriodEnd`, for every `PeriodKind`.
- `Active` — otherwise; pace stays `OnTrack`/`Behind` as today, `NotApplicable` for Custom.
- `CompletedOn` — local date of the matched `Finished` event on which the running total (items count, or cumulative
  `PagesRead`) first reached `Target`, ordered by `TimestampUtc`. Derived from `ReadingEvent`, which is append-only and never pruned,
  so it cannot drift. Null unless Completed.
- Nothing is persisted: `ReadingGoal` is untouched.

### Presentation
- **Ring (`GoalRing`)**: new `Outcome`/`IsBehind` inputs replace the bare `IsComplete` bool. Completed = `PbSuccessBrush` ring with a check
  mark; Missed = `PbDangerBrush` ring still showing the reached percentage, with a cross; Active on track = `PbAccentBrush`; Active
  behind pace = `PbBadgeBrush` (the skin's amber — there is no separate warning token).
- **Status text**: "Completed Mar 12 · 52 of 50", "Missed · 38 of 50", otherwise the existing on-track / behind-pace strings.
  `StatusText`'s `Custom`-only "Goal ended" branch is removed in favour of the shared Missed path.
- **Grouping**: `GoalsViewModel` exposes `ActiveCards` and `PastCards` (Completed + Missed, newest `PeriodEnd` first). `HeroGoal` picks
  from `ActiveCards` by nearest `PeriodEnd`; if none is active it falls back to the most recent past goal. `SecondaryCards` stays
  "everything except the hero" among active goals; past goals render in a collapsed **Past goals** expander under them, keeping the
  delete affordance (inline confirm, deferred removal per the CLAUDE.md detach-in-routed-event rule — reuse the existing `DeleteGoal` path).
- **Renew**: past goals carry a **Renew** button that opens `GoalEditorViewModel` pre-filled with the same metric, target, scope and
  period kind, with a freshly resolved period. Manual only — the 09-23 "no auto-renewal" decision stands. Saving inserts a new row; the old
  one stays as history.

### Alerts
- `GoalsViewModel.CheckMilestone` gains a Missed case: one Activity Center alert, "Goal missed", `Severity: Warning`, deduped
  `goal-missed:{goalId}`, raised on the first `Refresh` that sees the goal Missed. Per project feedback, notifications go through the
  Activity Center.
- Known quirk, fixed here: `_previousPercents` starts empty each launch, so a long-completed goal re-evaluates as "just crossed". The
  dedupe key already suppresses a visible repeat; the Missed alert relies on the same key and adds no new behaviour. No further change.
- `goal-pace-check` (scheduled task) skips goals whose Outcome is not `Active`, so it no longer reports "behind pace" for ended goals.

## Part 2 — Chart colour semantics and polish

### Shared colour map — `InsightsChartPalette` (new, App/Services)
One place that answers "what colour is this category?", used by `CategoryDonut`, the legends, the bar rows and (as ScottPlot colours) the bar charts.
```csharp
static class InsightsChartPalette
{
    // Semantic first, positional fallback, neutral for "no information".
    static string BrushKey(ChartCategoryKind kind, string label, int positionalIndex);
}
```
- **Reading state** (`ReadingStatus` labels): Completed → Success, Reading → Accent, Re-reading → Violet, Planned → Blue,
  Paused → Badge (amber), Dropped → Danger, Unknown → neutral.
- **Content rating**: a ramp from the success end (Everyone/Kids/G/PG) through Blue/Violet (Teen, Teen Plus, Everyone 10+) to Badge → Danger
  (Mature 17+, M, MA15+, Adults). Unrecognised ratings take the positional fallback.
- **Media type** and everything else: positional categorical palette, as today.
- **"Unknown" / "Other" / "None" are always neutral** (`PbTextMutedBrush` at reduced opacity, or `PbSurface2`-adjacent), in every chart. This is the
  point of Q7: the large Unknown slice stops reading as the headline.
- All colours still pass through `InsightsChartTheme.Visible` / the existing `MinGraphicContrast` so light and dark skins stay legible.
- The 6-colour cap in `CategoryDonut` (fold tail into "Other") stays, but "Other" now takes the neutral tone rather than a palette slot.

### Legends (all three donuts)
A shared `DonutLegend` row template: colour swatch (squircle chip radius `PbRadiusChip`, not a pill), label, count, percent. The legend is
generated from the same ordered/capped slice list the donut draws — `CategoryDonut` exposes it (or the palette helper builds both) so a slice and its
legend row can never disagree on colour or on the "Other" fold. Content rating's bars take their slice colour instead of the uniform tan.

### Interaction
- Donut hit-testing by angle: hovering a slice or its legend row highlights both (others dim) and shows a tooltip "Completed · 120 · 21.5%".
  Keyboard users are not the audience of a hover; legend rows stay plain text (not focusable), per "don't make a control focusable unless the
  keyboard needs it".
- ScottPlot bar charts get hover through the existing `InsightsChartHover` where they lack it (Score distribution; burn-down keeps its
  deliberate no-hover precedent unless trivial).

### Score distribution
- Fix the tofu: the tick labels are built as `$"{Stars}★"` (`InsightsScreen.axaml.cs` `RenderRatings`) and ScottPlot's font has no `★` glyph.
  Use digits, as the 2026-09-06 fix already did elsewhere.
- Colour bars on a red→green ramp across the five buckets (Danger → Badge → Blue → Success), value label above each bar, and a faint stub for
  zero-count buckets so an empty bucket does not look missing.

### Scope (Q11)
Every chart on the Trends tab (Activity: growth, pace, burn-down; Composition: three donuts, score distribution, content rating, publication year; Top Lists
bars) plus the Today tab's goal ring and donut visuals — all through `InsightsChartPalette`. Heatmap/sparkline controls already follow skin brushes; only checked, not redesigned.

## Testing
- `GoalResolverTests`: Missed for each `PeriodKind` once `today > PeriodEnd`; Completed with correct `CompletedOn` for Items and Pages (running total crossing on the
  right event/day); exact-boundary day (`today == PeriodEnd`) is still Active; a goal completed on its last day is Completed, not Missed.
- `GoalsViewModelTests`: `HeroGoal` skips past goals and falls back correctly; `ActiveCards`/`PastCards` split; one `goal-missed:{id}` alert and no repeat on a second `Refresh`;
  Renew pre-fills the editor.
- `InsightsChartPaletteTests`: each semantic label maps to its key; Unknown/Other are neutral regardless of position; unknown labels fall back positionally; no two kept donut slices share a colour.
- Donut legend/slice agreement test (same ordered list, same colours, "Other" fold identical).
- Score distribution: tick labels contain no `★`; bar count = 5 including zero buckets.
- No pixel tests for `GoalRing`/`CategoryDonut` (same precedent as before); on-screen check by the user for the final look, via a headless PNG render
  first (the About-polish render trick).

## Out of scope
In-place goal editing, auto-renewal, goal streaks/history charts, new alert severities (`Warning` already exists), colour-blind-specific pattern fills (legends carry text labels and counts).

## Open for review
- "Past goals" collapsed by default vs expanded when the screen has no active goals (proposed: expanded in that case).
- Whether Missed should also colour the Today hero tile border, or only the ring (proposed: ring only).
