# Statistics Page — Design

**Status:** Design phase, not yet planned/implemented.
**Date:** 2026-08-29.
**Builds on:** the completed Design Language Foundation (`2026-08-24-design-language-foundation-design.md`) and its six follow-on UI-rework phases (nav shell, Home, Library, Detail, Reader chrome, Preferences/remaining editing surfaces) — all seven are implemented and verified as of this writing. This is a **new initiative**, not an eighth phase of that rework: it's the first genuinely new screen added after the rework's own scope closed, and it consumes that phase's tokens/primitives (`statCard`, `PosterTile`, `pbChip`, `Pb*` tokens) rather than inventing new visual language.

## Background

Grew out of a design-language review against the "Elegance Formula" UI-rules sheet, which flagged two gaps: no gamification anywhere in the app, and (once gamification was scoped down to something tasteful for a personal library rather than a habit-app) no single place to see reading history at all. A competitive check against Kavita and Omnibus (`hankscafe/omnibus`, the self-hosted comic/manga manager — not the unrelated commercial Android app of the same name) confirmed both split "personal recap" from "collection health," and both use per-sitting reading-session data as the foundation for anything time-based. Kavita's own history is the cautionary example here: their stats launched built entirely on reading-session events, and when users who just clicked "mark as read" turned out not to generate any session data, they had to retroactively synthesize fake sessions from average reading-time estimates to backfill it. That is the mistake this design avoids by adding real session logging as part of this phase rather than after the fact (see [New data: reading-session log](#new-data-reading-session-log)).

An audit of the actual schema (`Series.cs`, `Issue.cs`) found real material to build on: `Issue.Rating` ("My Rating," CE-parity, drives the Favorites smart list), `Issue.OpenCount`/`OpenedTime`/`LastPageRead`, `Series.ReadingStatus` (Planned/Reading/Completed/Paused/Dropped/**ReReading** — re-read tracking already exists as a first-class state), the weighted/categorized `IssueTag` model (Genre vs. Tags), and the Continuity/StoryEvent metadata platform (`ContinuityMembership`, `EventMembership`) that is unique to Paperbunkr among comic-library tools — no competitor surveyed has an equivalent, so a Continuity/Events stats cluster is a genuine differentiator, not a parity feature.

## Scope

**In scope** (per decision: ship all seven content clusters together rather than a smaller slice):

1. Overview
2. Time & Pace
3. Composition
4. Creators
5. Ratings & Taste
6. Continuity & Events
7. Collection Health

Each is detailed in [Page content](#page-content) below, with the exact fields it reads.

**Out of scope (deferred, deliberately not bundled here):**

- Actual gamification mechanics — badges, streaks, trophies, milestone toasts. This page is the data foundation those would ride on (same session log, same completion/rating aggregates), but shipping them together would conflate "can you see your reading history" with "should the app try to motivate you," which is its own decision (see the earlier gamification discussion — most of those mechanics were judged tasteful only if opt-in and non-punitive, which deserves its own spec once this data exists to build them on).
- A shareable recap export (PNG of the year-in-review card). Natural follow-on once the Overview cluster's numbers are real; not needed for v1.
- Any Book Collection–derived stats (collection value, "total spent"). `Issue.BookPrice`/`BookAge`/`BookCollectionStatus`/`BookOwner` exist in the schema but are explicitly dormant — "no editor UI yet" per their own doc comments — so there's no reliable data to aggregate. Blocked on a future Book Collection panel, not on anything in this spec.
- Multi-device/server-wide stats (Kavita's admin dashboard has no Paperbunkr equivalent — this is a single-user desktop app, not a multi-tenant server).

## New data: reading-session log

Per-sitting reading history does not exist today — `Issue` only carries point-in-time state (`LastPageRead`, a single `OpenedTime`, `OpenCount` as a running total). That's sufficient for state-based stats (completion %, ratings, format breakdown) but not for anything that needs to know *when* reading happened: the activity heatmap and pace-over-months chart in Time & Pace, specifically.

New entity, following this codebase's existing join/log-entity conventions (compare `EventMembership`, `ContinuityMembership`):

```csharp
public class ReadingSession
{
    public int Id { get; set; }
    public int IssueId { get; set; }
    public Issue? Issue { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }     // null = session never cleanly closed (crash/kill)

    public int StartPage { get; set; }
    public int? EndPage { get; set; }          // null alongside EndedAt
}
```

No CE precedent — ComicRack CE has no reading-session concept at all, so per the standing rule (`CLAUDE.md`: verify against CE before adding a new field/behavior) this is flagged explicitly as a deliberate new feature, the same footing already established for `Series.ReadingStatus` and the Continuity/StoryEvent entities.

Written from `ReaderScreenViewModel` — a session starts when an issue opens for reading (same moment `OpenCount`/`OpenedTime` already update, so this is additive to an existing write path, not a new one) and closes when the reader screen is left (navigate away, app close) or after an idle timeout. `EndedAt`/`EndPage` staying null is the expected shape for a crash or kill mid-read, not an error state — Time & Pace stats should treat an open session as "ended at last known page" rather than discard it.

Migration: new table only, no changes to `Issue`/`Series` — naming would follow the existing `{timestamp}_PascalCaseDescription` convention seen in `Migrations/` (e.g. `AddReadingSessionLog`), paired `.cs`/`.Designer.cs`, registered as `DbSet<ReadingSession> ReadingSessions` in `PaperbunkrDbContext.cs` alongside the other DbSets.

## Page content

### 1. Overview

The headline row — `statCard` instances (the shared class from `Styles/ScreenChrome.axaml`, currently only consumed by `MigrationOverlay.axaml`; this is its second real use site): total issues/series read all-time (`OpenCount`/`ReadingStatus == Completed`), a this-year-vs-last-year comparison once `ReadingSession` has a year of data to compare, and a series-status breakdown (Planned / Reading / Completed / Paused / Dropped / ReReading counts) reusing the `Series.ReadingStatus` enum directly — no new aggregation logic beyond a GROUP BY.

### 2. Time & Pace

Depends entirely on the new `ReadingSession` table:

- Activity heatmap (GitHub-contributions style) — `ReadingSession.StartedAt` bucketed by day.
- Pace trend — issues finished per month, from sessions whose issue reached `LastPageRead == PageCount` (or `ReadingStatus` flips to `Completed`) within that session's date range.
- Busiest month / day-of-week — same bucketing, different grouping.
- Average time-to-finish a series — first `ReadingSession.StartedAt` for the series' first issue to the session in which its last issue completes.

### 3. Composition

Proportional breakdowns, each a straightforward GROUP BY over existing fields:

- By `Series.ContentType` (Comic/Manga/Manhua/Manhwa).
- By `Issue.Format` — this is the "dormant Format field" flagged in earlier metadata-model work; this page becomes its first real consumer. Series with no `Format` set fall into an explicit "Unset" bucket rather than being silently dropped, so the chart also functions as a nudge toward filling the field in.
- By Genre — `IssueTag` rows where `Field == IssueTagField.Genre`.
- By Publisher — `Issue.Publisher` (the per-issue field is the source of truth per its own doc comment, not `Series.Publisher`, which is migration-time-only).

### 4. Creators

Top Writers/Pencillers/Inkers/etc. by issues read. Worth flagging plainly: unlike `Character` (a real entity with a `CharacterAppearance` join table), `Writer`/`Penciller`/`Inker`/`Colorist`/`Letterer`/`CoverArtist`/`Editor`/`Translator` on `Issue` are still flat ComicInfo.xml-style strings, some of which are comma-separated multi-value fields. Aggregating "top writer" means parsing those strings consistently (matching whatever split/trim convention, if any, the Detail screen's credit display already uses — `DetailBand.axaml`/`DetailTabs.axaml` render these today, so that convention should be reused rather than reinvented), not a clean SQL GROUP BY. This is the one cluster most likely to need real implementation-time care.

### 5. Ratings & Taste

`Issue.Rating` ("My Rating") distribution as a histogram, average rating given, and a "most re-read" callout (using `Series.ReadingStatus == ReReading` plus `OpenCount`). A distinguishing angle over Composition/Creators: `ExternalRating` already stores provider scores (AniList/MAL/etc., normalized via its own `Scale`) alongside `Issue.CommunityRating` — a "where you disagree with the crowd" comparison (your rating vs. community/external rating, sorted by largest delta) is genuinely differentiated content no generic reading tracker surfaces, since it needs both a personal rating and fetched external ratings in the same schema.

### 6. Continuity & Events

The one cluster with no equivalent in Kavita or Omnibus, so it's worth leading with rather than burying last:

- Per-continuity completion — `ContinuityMembership` → `Series` → issues, `ReadingStatus`/`OpenCount` aggregated up to "you've read N% of the [X] Continuity."
- Story Events "witnessed" — `EventMembership` → `Issue`, counting how many of a `StoryEvent`'s member issues have been read, surfaced as "N of M cross-series events witnessed."

### 7. Collection Health

- Backlog size — issues with `ReadingStatus` in {Unknown, Planned} and `OpenCount == 0`.
- Oldest unread — same set, sorted by `Issue.AddedTime` ascending, surfaced as "in your library since [date]."
- Library growth over time — `Issue.AddedTime` bucketed by month, a simple acquisitions-over-time line.

(Collection *value* — total spent, most expensive series — is explicitly out of scope per above; it needs `BookPrice` populated, which needs an editor that doesn't exist yet.)

## Visual design — reusing existing primitives, not inventing new ones

Every visual element here already exists in the design system from the completed UI rework:

- Headline numbers: `statCard`/`statNumber`/`statLabel` (`Styles/ScreenChrome.axaml`), `PbDisplayFontFamily` (Bebas Neue) for the large figures, matching the existing type scale.
- Completion/progress bars: `PosterTile`'s existing `ShowProgress`/`ProgressFraction` slot, same one Home's Continue Reading rail already uses — Continuity completion and per-series progress reuse it rather than a new progress-bar control.
- Category/tag chips (Genre, Format buckets): `pbChip` (`Styles/Primitives.axaml`).
- Charts (heatmap, bar/line breakdowns): new territory — nothing in the current primitive set covers this. This is the one genuinely new visual component the phase needs, and it should still be built on the existing token set (`PbSurface2Brush` backgrounds, `PbAccentBrush`/`PbAccentTextBrush` for the data itself so it reads as "the app's one accent color," `PbBorderBrush`, `PbMotionFast`/`PbMotionSlow` for load-in transitions) rather than a generic charting-library default theme.
- Color: single-accent restraint carries over — charts should use `PbAccentColor` as the primary data color rather than introducing a new categorical palette, consistent with the existing "one amber accent used deliberately" identity. A multi-series chart (e.g. Composition's breakdowns) is the one place a small extension to the palette may be genuinely needed — an open question below, not decided here.

## Navigation placement

The current nav rail (`MainWindow.axaml`) runs Home → Library → Books → Smart Lists → Reading Lists → Continuity → Undo/Redo → Preferences. Proposed: a new rail item between Reading Lists/Continuity and Preferences — grouped with the other "reflective/meta" surfaces rather than the primary browsing surfaces (Library/Books), since Statistics is something you check in on, not something you browse from. Not a hard requirement of this design — genuinely open, see below.

## Testing / verification

- `ReadingSession` write path: unit tests confirming a session is created on issue open and closed (or left open with `EndedAt == null`) on the paths described above — same rigor as existing `OpenCount`/`OpenedTime` coverage.
- Each stat cluster's aggregation logic gets unit tests against seeded data, independent of the UI — matching how `SmartListCatalog`'s selectors are tested today, not just eyeballed on-screen.
- Visual verification: build clean, full test suite green, crash-free launch — same bar as every prior UI-rework phase.
- Live visual/interactive confirmation is the known computer-use gap in this environment (noted in the Home Screen design doc too) — this phase specifically benefits from direct testing on the actual app once implemented, especially the heatmap/chart rendering, which can't be meaningfully verified from source alone.

## Open questions / deferred

- Exact session-boundary heuristic (idle timeout duration, what counts as "cleanly closed" vs. "abandoned") is an implementation-time decision, not pre-specified here.
- Nav rail placement (proposed above) is a suggestion, not a decision — could equally live as a card/link off Home or Preferences rather than its own rail item.
- Whether Composition's multi-series breakdowns need a small extension to the color palette (currently single-accent) or should stay monochrome (one bar color, differentiated by label/size alone) is undecided — the latter is more consistent with the existing restraint, the former is more legible for a 5+ category breakdown. Worth a direct call before implementation, not guessed here.
- Whether "Time & Pace" ships in the same pass as the other six clusters or trails them (since it alone is blocked on `ReadingSession` accumulating real data before it says anything meaningful — a chart with three days of history isn't useful) is a sequencing question, not addressed by this document.
- The exact Writer/Penciller/etc. string-splitting convention to reuse from the Detail screen (see Creators above) needs to be confirmed by reading `DetailBand.axaml`/`DetailTabs.axaml`'s current credit-rendering code at implementation time, not assumed here.
