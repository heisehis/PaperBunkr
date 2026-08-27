# Metadata Model: AniList Tracker Write-Back Sync

**Date:** 2026-08-19
**Status:** Design sketch only, not implemented - sixth item from the same architecture-review
roadmap as the five adjacent specs, scoped by the user as "sketch now, build later." This is
meaningfully bigger and riskier than R1-R5 (OAuth against a real user account, write access to the
user's real AniList list, background sync, conflict handling) and should not be built without a
separate go-ahead even after this sketch is reviewed.

## Context: two different things both called "AniList"

This codebase already has two unrelated AniList-shaped things, and this spec is explicitly about the
second one, not an extension of the first:

- **`AniListMetadataProvider`** (`IMetadataProvider`, shipped) - read-only, answers "what is this
  work?" No user data ever leaves the machine through it.
- **`TrackingLink`/`TrackingService`** (schema-only since Phase 5a, `TrackingService.AniList` is one
  of six enum values) - the *tracker* concept: "what has this user done with this work, on AniList?"
  This spec is the first real adapter for this half.

The source architecture review's own §16/§30-43 "tracker vs. scraper" distinction is the load-bearing
idea here: a metadata refresh must never touch user state, and a tracker sync must never touch
canonical metadata. Concretely, this feature must never write to `Series.Name`, `SeriesTitle`,
`ExternalMediaId`, or anything `MetadataLinkResolver` owns - only `Series.ReadingStatus`
(docs/superpowers/specs/2026-08-19-metadata-model-reading-status-design.md, shipped this session)
and issue-derived progress.

## Why this is bigger than R1-R5

- Requires the user to register an AniList API OAuth client (a manual step on the user's own AniList
  account settings page - not something this app or Claude can do on the user's behalf) and paste a
  client id into a new Preferences field.
- Requires secure local credential storage that doesn't exist in this codebase yet.
- Writes to the user's real, external AniList list - unlike every other feature shipped this session,
  a bug here has consequences outside this app's own database.
- Needs a conflict-resolution policy, because "PaperBunkr says chapter 42, AniList says chapter 40"
  is a real, expected steady-state case, not an edge case.

## Design

### Phase A - OAuth connect + manual one-way push (recommended starting scope)

The smallest version that's actually useful: the user connects their AniList account once, then can
manually push local `Series.ReadingStatus`/progress to AniList per-series (a "Sync to AniList"
button, same idiom as the R3 "Link to AniList" button) - no automatic sync, no pull direction, no
conflict handling needed yet because push-only can't conflict.

- **`ITrackerAdapter`** (new, `Paperbunkr.Data/Metadata/` or a new `Paperbunkr.Data/Tracking/`
  folder - open question, see below): mirrors the source review's own §207 shape, trimmed to what
  Phase A needs.
  ```csharp
  public interface ITrackerAdapter
  {
      TrackingService Service { get; }
      Task<TrackerUserEntry?> GetEntryAsync(string externalId, CancellationToken ct);
      Task PushEntryAsync(string externalId, TrackerUserEntry entry, CancellationToken ct);
  }

  public sealed record TrackerUserEntry(string Status, int? Progress, float? Score);
  ```
- **`AniListTrackerAdapter`**: AniList's GraphQL API also serves the list-mutation surface
  (`SaveMediaListEntry` mutation) - same `graphql.anilist.co` endpoint `AniListMetadataProvider`
  already calls, but authenticated. Reuses `AniListHttpClient.Shared` for the transport, a *new*
  authenticated client (or an `Authorization` header added per-request) for the write path - the
  read-only metadata provider must stay usable with zero AniList account connected, so the two must
  not become the same object.
- **Status/score mapping**: `Series.ReadingStatus` (Planned/Reading/Completed/Paused/Dropped/
  ReReading) maps directly to AniList's `MediaListStatus` (`PLANNING`/`CURRENT`/`COMPLETED`/`PAUSED`/
  `DROPPED`/`REPEATING`) - a clean 1:1, no lossy cases either direction. Progress maps from the
  series' highest `Issue.EffectiveNumber()` among issues with `IsInProgress()`/`HasBeenRead()`
  (`IssueMetadataExtensions`, already shipped) - parsed as best-effort integer chapter number, same
  `TextNumberFloat`-based parsing `IssueMetadataExtensions.NumberSortKey` already uses.
- **Credential storage**: Windows DPAPI (`System.Security.Cryptography.ProtectedData`,
  `CurrentUser` scope) to encrypt the OAuth token at rest in a small local file alongside the
  database - not plain SQLite, not plain JSON, per the source review's own §213 invariant. No new
  external dependency; DPAPI is part of the .NET Windows platform APIs this app already targets.
  `ITrackerCredentialStore` abstraction over it, so the storage mechanism isn't hardwired into the
  adapter.
- **UI**: a new "Trackers" section in Preferences (alongside the existing Appearance/Behavior/
  Libraries/Advanced tabs) - "Connect AniList" button opens the system browser to AniList's OAuth
  authorize URL, user pastes back the resulting token (AniList's implicit-grant flow returns it in
  the redirect URL fragment - no local callback server needed, matching how simple desktop apps
  without a registered redirect handler typically do this). A per-series "Sync to AniList" action
  (Library context menu or Detail screen, mirroring the R3 "Link to AniList" placement) calls
  `PushEntryAsync` using the series' existing `TrackingLink` (created here, alongside the metadata
  `ExternalMediaId` link, or independently if the user only wants tracking, not metadata).

### Phase B - bidirectional sync + conflict handling (later, only after Phase A is proven)

- **Sync direction policy**, per field, matching the source review's own §209 recommendation almost
  verbatim: Progress bidirectional (highest-value wins by default), Status bidirectional, Rating
  local-wins-by-default (AniList write only on explicit user action), Notes never synced (local-only,
  privacy).
- **Conflict record**: only if Phase A usage shows real disagreements are common enough to need a
  review queue - starting with a silent deterministic "highest progress wins" policy (no persisted
  `SyncConflict` table, no conflict-review UI) is the boring-version-first choice, matching this
  session's own precedent (`TitleMatchScorer` shipping without the review's fuller multi-signal
  scorer). Add the source review's §211 `SyncConflict` record shape only if that turns out wrong in
  practice.

### Phase C - automatic background sync (later still)

This app has no background job/hosting-service infrastructure at all today (no equivalent of the
review's §214-215 `SyncQueue`/`SyncWorker`). Manual "Sync Now" is the entire scope until that
changes - do not build a background timer against a codebase that has nowhere else that pattern
already lives; that's new infrastructure this feature shouldn't be the one to introduce alone.

## Open questions for implementation time

1. Folder placement - a new `Paperbunkr.Data/Tracking/` alongside `Paperbunkr.Data/Metadata/`, or
   inside `Metadata/` next to `MetadataLinkResolver`? Leans toward a separate folder: the source
   review's own tracker-vs-scraper distinction is architecturally real, and giving trackers their own
   namespace makes "this file touches user state, that one touches canonical metadata" visible from
   the folder alone.
2. Does creating a `TrackingLink` for sync purposes reuse the exact same search/match UI as R3's
   metadata linking (same AniList id either way), or does the user need to separately confirm
   tracking consent even when already metadata-linked? Leans toward requiring a separate explicit
   action - metadata linking has no account-write implications, tracker linking does, and conflating
   them risks writing to a user's AniList list without a clearly separate confirmation step.
3. What happens to `TrackingLink.LastSyncedIssueNumber` (already a schema field, currently unused) -
   this spec's progress-push writes to AniList directly rather than through that field; worth
   revisiting whether that column is still the right shape once Phase A actually ships.
