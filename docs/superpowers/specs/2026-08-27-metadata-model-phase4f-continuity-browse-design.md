# Metadata Model — Phase 4f: Continuity Browse View

**Date:** 2026-08-27
**Status:** Approved, pending implementation
**Source doc:** Design session with Ehis (2026-08-27), closing the gap [[phase4a-continuity]]
explicitly deferred ("A dedicated Continuities management/browse screen... revisit if continuity
data grows enough to need one"). Cross-checked against Metron.cloud's real `Universe` resource
schema (via the `mokkari` library's own source, since `Continuity`/`Universe` isn't something CE or
Comic Vine model at all) as an external reference point, not as a source of new fields —
Paperbunkr's `Continuity` entity already covers what Metron's `Universe` covers (`name`/
`description`; `Publisher` already exists on `Continuity` too) and this phase adds no new columns.

## Context

`Continuity`/`SeriesContinuity` ([[phase4a-continuity]]) already fully exist —
`ContinuityResolver.GetSeriesInContinuity` already answers "what's in this continuity" as a query.
What's missing is purely a place to *browse* that query's result as a shelf; today a continuity is
only visible as removable chips on one series' Related tab, one series at a time, with no way to
see "everything in Earth-616" as a single view.

Per the decision to land Events-related UI as new modes on the existing Story Events screen rather
than new nav-rail entries (keeping the nav rail from sprawling further for a cluster of
closely-related concepts), this phase adds Continuity browsing as a second mode there, alongside
the Events mode ([[phase4b-story-events]], [[phase4d-event-relations]], [[phase4e-format-signal-
suggestions]]) and the age-progression mode ([[phase4g-age-progression]]).

## Scope

### Screen structure: mode switcher

`EventsScreenViewModel`'s current shape (sidebar of named collections + detail pane) is reused
as-is for the new mode, not rebuilt — the same reasoning Phase 4b originally used to justify a new
screen ("Reading Lists already proves out the right shape... so this phase adds a new screen built
the same way rather than inventing a different UI pattern") applies again here, one level up: don't
invent a second sidebar+detail shape when the one this screen already has fits.

A small mode switcher (segmented control, matching this codebase's existing tab-strip visual
language) at the top of the screen switches between **Events** and **Continuities** modes.
Switching modes swaps the sidebar's contents (event summaries vs. continuity summaries) and the
detail pane's contents, while both share the same screen chrome/layout. The screen's view model
gains a `ScreenMode` enum (`Events | Continuities`) and the existing `Events`/`Members` collections
are joined by parallel `Continuities`/`ContinuityMembers` collections, populated only when that mode
is active (lazy-loaded on switch, not both loaded eagerly on screen entry).

### `ContinuitySummary`

New model, `src/Paperbunkr.App/Models/ContinuitySummary.cs`, mirroring `StoryEventSummary`'s
existing shape:

```csharp
public sealed record ContinuitySummary(int Id, string Name, string? Publisher, int SeriesCount);
```

Sidebar list populated from `Continuity` rows directly (no resolver method needed beyond a simple
projection — unlike `StoryEventSummary`, which needs `EventMembershipResolver` to compute a count
across a join).

### Continuity detail pane

Selecting a continuity shows: its `Name`/`Description`/`Publisher` (all already on `Continuity`),
and its member series as a poster grid — reusing the existing library grid component
(docs/superpowers/specs/2026-08-27-library-browsing-4a-poster-grid-design.md's poster-grid control,
not a new grid implementation) populated via `ContinuityResolver.GetSeriesInContinuity`. Clicking a
series card navigates to that series' Detail screen, same as every other poster-grid instance in
this app.

A "+ Add Series" action opens the same series-search picker Phase 4a's Related-tab continuity
assignment already uses, calling `ContinuityResolver.AddSeriesToContinuity` — so a continuity can be
built up from this browse view directly, not only from a single series' Detail page as today. A
remove action per card calls `RemoveSeriesFromContinuity`.

### Creating a continuity from this screen

A "+ New Continuity" sidebar action, matching `EventsScreenViewModel`'s existing "create from
sidebar" pattern for events — calls `ContinuityResolver.GetOrCreate` with a user-entered name (same
case-insensitive dedup guardrail already built into `GetOrCreate`), then selects it. This is a
second entry point to the same creation path Phase 4a's combo-box-with-create already exposes on
the Related tab — not a new creation mechanism, just a second door into it.

## Testing

- `EventsScreenViewModelTests` (extended, same test class — this is a mode of the same screen, not
  a new view model): switching to Continuities mode populates the sidebar from real `Continuity`
  rows with correct `SeriesCount`; selecting a continuity populates its member series grid;
  Add/Remove series update both the grid and (when checked from the affected series' Related tab)
  its `SameContinuity` section, confirming this mode writes through the same `ContinuityResolver`
  calls the existing Related-tab UI does, not a parallel path; creating a continuity from the
  sidebar dedupes case-insensitively, matching `ContinuityResolverTests`' existing `GetOrCreate`
  coverage.
- Regression: `Events` mode's existing behavior (from [[phase4b-story-events]]/[[phase4d-event-
  relations]]/[[phase4e-format-signal-suggestions]]) is unaffected by the mode switch being added —
  covered by re-running the existing `EventsScreenViewModelTests` suite against the now-modal
  screen.

## Explicitly out of scope

Any new `Continuity` schema field — this phase is purely a browse/edit UI over data
[[phase4a-continuity]] already modeled correctly. Cross-continuity comparison views (e.g. "series in
both Earth-616 and Ultimate"). Continuity-scoped Smart Lists or reading lists — a plausible
follow-up, not required for this phase's "arrange comics by continuity" goal, which the poster grid
alone satisfies. Issue-level or character-level continuity membership (Metron's model) — already
decided against for now in the design discussion preceding this doc; revisit only if real usage
shows the series-level model can't handle a crossover issue that genuinely spans two continuities.
