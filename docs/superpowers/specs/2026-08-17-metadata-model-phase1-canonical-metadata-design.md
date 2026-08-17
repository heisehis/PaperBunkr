# Metadata Model — Phase 1: Canonical Metadata

**Date:** 2026-08-17
**Status:** Approved, pending implementation plan
**Source docs:** User-provided `PAPERBUNKR_METADATA_MODEL.md` (79-section architectural spec) and a
short companion "Architectural Rules" note. Neither is committed to this repo (external design
input) — this spec is the implementation-oriented distillation of their Phase 1 ("Canonical
metadata," their §68) for this codebase specifically.

## Context

The source doc proposes a full metadata platform — `MetadataDescriptor`/`MetadataProposal` system,
a `MediaRelation` graph, `StoryEvent`/`Continuity`/`ReadingList` overhaul, external provider
adapters (AniList/MAL/GCD), a recommendation engine, `Volume`/`Chapter`/`Story` entities — spanning
its own explicit 7-phase rollout (its §68). That's a multi-month platform, not a single spec. This
document covers **only Phase 1**, the foundational layer everything else depends on; each later
phase gets its own spec when its turn comes, per the source doc's own "do not implement everything
at once."

The source doc's short companion "Architectural Rules" note states: *"Derived properties (like
`ReadPercentage` or `IsLinked`) must be resolved dynamically via resolvers, not saved as database
columns."* This spec applies that rule consistently to every derived value in Phase 1's scope
(including `NumberSortKey`/`VolumeSortKey`/`NumberType`, which the source doc's own field list
didn't explicitly flag as derived but are exactly the same shape as `ReadPercentage`) — no new
columns for anything computable from an existing stored field.

Verified against real CE source and the current schema before writing this (standing project rule):
- `Issue.Number` is already a `string` in Paperbunkr's schema (no change needed there); `Issue.Volume`
  is currently `int?` and needs the same string+sort-key treatment.
- `Issue.OpenedTime` exists but **is never actually written anywhere in the app today** — confirmed
  by search, not assumed. This phase's `OpenCount` work is net-new open-tracking, not a fix to a
  partially-working feature, and it also fixes a latent bug: Library's "Sort by Last Read" is
  currently a no-op (the field it sorts by is always null).
- CE's real "has been read" threshold is `ComicBook.ReadPercentageAsRead = 95` (a real ported
  constant, `src/Paperbunkr.Engine/ComicBook.cs:73`) — used here instead of the source doc's
  placeholder "suggested default: 90%".
- CE's real `BlackAndWhite` representation is a 3-state `YesNo` (Yes/No/Unknown) — the source doc's
  5-state `ColorMode` (adding `Grayscale`/`Mixed`) has no CE precedent at all. Built anyway per
  explicit user confirmation (deliberate deviation, not parity — noted so it isn't mistaken for a
  CE-sourced fact later).
- A parser for exactly this problem — numeric-with-fallback comic issue numbers, including `½`/`¼`/`¾`
  glyphs, negatives, and decimals — is already ported and unused: `TextNumberFloat`
  (`src/Paperbunkr.Common/Text/TextNumberFloat.cs`). Reused directly for `NumberSortKey`/
  `VolumeSortKey` rather than writing a new parser. `Paperbunkr.Data` already has transitive access
  to it (`Data → Engine → Common` project-reference chain), no new reference needed.
- `BlackAndWhite`/`Series.IsComplete` are both real and live today (Bulk Edit, Issue Properties
  editor, Smart Lists, Detail screen), not dead fields — confirmed via grep before scoping the two
  riskier changes below.

## Scope

### Schema changes (the only real EF migration in this phase)

| Change | Type | Notes |
|---|---|---|
| `Issue.OpenCount` | new `int`, default 0 | Real counter — CE's own `OpenedCount` only ever flips 0→1; this one actually increments. |
| `Issue.Volume` | `int?` → `string?` | Same treatment as `Number` (already a string) — preserves display value, no destructive reformatting. |
| `Issue.ColorMode` | new enum, replaces `bool BlackAndWhite` | `Color \| BlackAndWhite \| Grayscale \| Mixed \| Unknown`. Migration: `true→BlackAndWhite`, `false→Color`. |
| `Issue.IsFinalIssue` | new `bool` | Replaces the never-ported per-issue `SeriesComplete` concept. Default `false`; no migration backfill (nothing to backfill from — the flag never existed in Paperbunkr's schema). |
| `Series.Status` | new enum | `Ongoing \| Completed \| Cancelled \| Hiatus \| Unknown`. Migration backfill: existing `IsComplete == true` → `Completed`, else → `Unknown` (CE never told us anything stronger than "not marked complete", so `Ongoing` would overclaim). |
| `Series.IsComplete` | stored `bool` → computed property | `get => Status == SeriesStatus.Completed`. No column; every current reader (Smart Lists' `Func<Issue,bool>` selector, Detail screen) keeps working unchanged since they call it as a normal C# property over an already-loaded entity, not a SQL-translated expression. |
| New `IssueBookmark` entity | `Id`, `IssueId`, `PageNumber` (int), `Label` (string?), `Note` (string?), `CreatedTime` (DateTime) | Mirrors `BookBookmark`'s existing shape/conventions (`src/Paperbunkr.Data/Entities/BookBookmark.cs`), simplified to page-number instead of `BookBookmark`'s character-offset (Issues are page-paginated, not reflowable text like `Book`). `BookmarkCount` is a computed `issue.Bookmarks.Count`, never stored. |

`AlternateSeries`/`AlternateNumber`/`AlternateCount` are untouched — they already exist
(`AlternateSeries`/`AlternateNumber` are real fields) or are explicitly deferred to the source doc's
later `IssueEdition` relationship phase (§35 in the source doc), not this one.

### Computed/derived layer (no schema — resolvers only)

Two new extension-method classes, no new entity properties:

- **`src/Paperbunkr.Data/Metadata/IssueMetadataExtensions.cs`**: `ReadPercentage` (`LastPageRead /
  PageCount`, clamped 0–100, 0 when `PageCount` is 0/null), `HasBeenRead` (`ReadPercentage >= 95`,
  the CE-verified constant, exposed as `IssueMetadataExtensions.ReadThresholdPercent` for later
  promotion to an `AppSettings` field — starts as a constant, matching this project's established
  pattern of shipping a fixed default before a Preferences surface exists), `IsUnread`
  (`ReadPercentage == 0`), `IsInProgress` (`0 < ReadPercentage < threshold`), `NumberSortKey`/
  `NumberType`, `VolumeSortKey`, `PublishedDate`/`PublicationDatePrecision` (combining
  `Year`/`Month`/`Day`, precision reflects which of the three are actually set), `FileName`/
  `FileDirectory`/`FileFormat` (parsed from `FilePath`), `IsLinked` (`FilePath` non-empty).
- **`src/Paperbunkr.Data/Metadata/SeriesMetadataExtensions.cs`**: `IsComplete` (see table above).
- **`ActualFileFormat`** (container-sniffed, requires opening the file) is deliberately **not** in
  the same always-cheap resolver class — it's a separate on-demand method
  (`IssueMetadataExtensions.SniffActualFileFormatAsync(Issue)` or similar), called only where a
  caller explicitly wants it (e.g. a future "verify library" tool), never from a hot path like
  Library grid rendering.

#### Number/Volume normalization rule

`NumberSortKey`/`VolumeSortKey`: `TextNumberFloat(value).IsNumber ? (float?)TextNumberFloat(value).Number : null` —
null means "not purely numeric, fall back to natural string sort," matching the source doc's
"use `NumberSortKey` when available, fall back to natural string sorting when not" (§10).

`NumberType` (`Numeric | Fraction | Annual | Special | AlphaNumeric | Text | Unknown`), derived from
the raw string, checked in this order: null/empty → `Unknown`; contains a `½`/`¼`/`¾` glyph →
`Fraction`; case-insensitive contains "annual" → `Annual`; case-insensitive contains "special" →
`Special`; `TextNumberFloat.IsNumber` true → `Numeric`; matches `^\d+[A-Za-z]+$` (e.g. `1A`, `1B`) →
`AlphaNumeric`; otherwise → `Text`. Never invented from nothing — every branch matches an example
literally listed in the source doc's own test-case list (§71: `1, 2, 10, 10.5, ½, 0, -1, Annual 1,
Special, 1A, 1B`).

## Data flow / wiring

- **`OpenCount`/`OpenedTime`**: both written together in `ReaderScreenViewModel.Load()`
  (`src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs:617`, the confirmed real issue-open path)
  — `issue.OpenCount++; issue.OpenedTime = DateTime.UtcNow; context.SaveChanges();`. This is the
  first time either field is actually populated by the running app.
- **`ColorMode`**: the EF migration backfills existing Paperbunkr rows from the outgoing
  `bool BlackAndWhite` column (`true→BlackAndWhite`, `false→Color`). Separately — confirmed via grep,
  not assumed — `CeLibraryMigrator` does **not** currently set `BlackAndWhite` from CE source data at
  all; that's a pre-existing gap unrelated to this phase, not something this migration needs to keep
  in sync with. Populating `ColorMode` from real CE libraries during CE migration (CE's own field is
  `YesNo BlackAndWhite` on `ComicInfo`) is a small, separate, natural follow-up but not required for
  this phase to be complete — out of scope here, flagged for whoever picks up CE-migration parity
  work next. Issue Properties editor's existing checkbox becomes a picker (same pattern as the
  existing Content Type picker); Bulk Edit's `BulkFieldDescriptor` entry changes from a bool field to
  `FieldKind.Enum` (the kind Manga/ContentType classification already added for exactly this shape).
- **`Series.Status`**: not migration-backfilled from CE (CE has no such concept at all) beyond the
  `IsComplete`-derived default above; left editable nowhere in the UI this phase (schema-present,
  dormant — same accepted precedent `PageLayoutModeOverride` already has in this codebase). A
  Status-editing UI is a follow-up, not blocking this phase.
- **`IsFinalIssue`**: schema-present, dormant this phase too — no source (CE or otherwise) to
  backfill it from, and no UI yet. Available for the eventual `MetadataDescriptor`/sort-group work
  in a later phase.
- **`IssueBookmark`**: schema only this phase — no reader UI to create one yet (that's follow-on
  work once the reader's page-turn surface has a bookmark affordance). Shipping the entity now means
  `BookmarkCount`-driven sort/group strategies in the later metadata-descriptor phase have real data
  to read from day one instead of waiting on a second migration.

## Testing

- `IssueMetadataExtensionsTests`/`SeriesMetadataExtensionsTests` (new, `Paperbunkr.Data.Tests`):
  every derived value above, including the full `NumberType` example set from the source doc's own
  §71 test-case list verbatim.
- Extend `CeLibraryMigratorTests`: `ColorMode` backfill from both `true`/`false` source values,
  `Series.Status` backfill from both `IsComplete` states.
- New EF migration: verified via the same `dotnet ef migrations add` + review pattern used all
  session (e.g. this session's `AddLibraryListLayoutSettings` migration) — enum-as-string columns
  get the same `HasConversion<string>()` + `HasSentinel`-where-needed treatment already established
  in `PaperbunkrDbContext.OnModelCreating` for every other enum column.
- `ReaderScreenViewModel` test coverage: opening an issue increments `OpenCount` and sets
  `OpenedTime`, confirmed via a fresh read from the database (not just the in-memory object), matching
  this project's existing pattern for that class's other `AppSettings`-touching behavior.
- Update `BulkFieldRegistryTests`/`IssuePropertiesScreenViewModelTests` for the `ColorMode` field-kind
  change.

## Explicitly out of scope (later phases, per the source doc's own §68 rollout)

`MetadataProposal`/`MetadataResolutionPolicy`/`MetadataDescriptor` system (Phase 2 — this is also
where the sort/group strategy work paused earlier this session properly belongs, superseding the
plain `ISortStrategy` idea discussed before these docs arrived). `MediaRelation`/`RelationEvidence`
(Phase 3). `StoryEvent`/`EventMembership`/`ReadingList` overhaul/`Continuity` (Phase 4). External
provider adapters — `ExternalMediaId`/`ExternalMetadataSnapshot`/`ExternalRating` (Phase 5).
`Recommendation`/`RecommendationReason` (Phase 6). `Volume`/`Chapter`/`Story`/`Collection` entities
(Phase 7). `IssueEdition` (replacing flat `Alternate*` fields, part of Phase 3's relationship work).
A `Series.Status`-editing UI and a reader bookmark-creation UI (both schema-ready, deliberately
UI-less this phase, same precedent as existing dormant override fields in this codebase).
