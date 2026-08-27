# MangaBaka Tracker Adapter — Design

*Follow-on from `2026-08-23-mangabaka-metadata-provider-design.md`, which concluded MangaBaka
"cannot be a tracker" — corrected same day after a user-supplied OpenAPI 3.1 spec showed a real
authenticated personal-library API exists. This spec covers building the actual push-tracker
adapter that conclusion said wasn't possible. See `docs/mangabaka-metadata-ui-research.md`
findings 12/18/19 for the full research trail.*

## What the real API supports (confirmed via the OpenAPI spec + the user's own account settings
## screenshot — not guessed)

- `PUT`/`PATCH https://api.mangabaka.org/v1/my/library/{series_id}` — `series_id` is MangaBaka's
  own series id (the same one `/v1/series/{id}` and this session's `MangaBakaMetadataProvider`
  already resolve via search — e.g. `708` for Kagurabachi). Body (all fields optional, partial
  update): `state` (enum `considering`/`completed`/`dropped`/`paused`/`plan_to_read`/`reading`/
  `rereading`), `progress_chapter` (0–10000), `progress_volume` (0–10000), `rating` (0–100), `note`,
  `read_link`, `start_date`/`finish_date`, `priority`, `is_private`, `number_of_rereads`.
  Paperbunkr's `TrackerPushPayload` only carries `Status`/`ChapterProgress` (per
  `ITrackerAdapter.cs`'s own "chapter progress only, no volume progress, this pass" note) — this
  adapter sends only `state` + `progress_chapter`, same scope as every other tracker adapter today.
- Auth: `x-api-key: mb-...` header (Personal Access Token) or OAuth. PAT it is — same choice
  `BangumiTrackerAdapter` already made for the same reason (no OAuth flow to build/maintain). The
  user located the real PAT page live: **mangabaka.org → My profile → Settings → "API and Apps" tab
  → "Personal Access Tokens (PAT) for API" → "New token"** button. The page itself warns "The token
  has full access to everything in your account - do NOT share with any 3rd party you don't fully
  trust!" — worth echoing in Paperbunkr's own field description, matching how Bangumi's field
  already links to `bgm.tv/dev/app`.
- Search: unchanged — the same `/v1/series/search` `MangaBakaMetadataProvider.SearchAsync` already
  calls serves tracker-linking search too, no new HTTP logic needed.
- Rate limit: 30 req/min on search (already fixed this session), 180/min on `/my/*`-adjacent
  defaults but `/my/*` itself is **never cached** per the docs — the existing provider's rate
  limiter already covers this adapter's search calls; push calls are user-initiated one-at-a-time
  (matching every other adapter's `SyncToTrackersAsync` loop), never a batch, so no additional
  pacing is needed for pushes specifically.

**`ReadingStatus` → MangaBaka `state` mapping is lossless** — both are 7-value enums that line up
1:1 (`Unknown`→`considering`, `Planned`→`plan_to_read`, `Reading`→`reading`, `Completed`→`completed`,
`Paused`→`paused`, `Dropped`→`dropped`, `ReReading`→`rereading`) — no information-loss collapsing
the way Bangumi's own 5-state service forces (`ReReading` folds into `Reading` there).

**Open question, not resolved by the spec text alone**: whether `PUT`/`PATCH` also *creates* a
library entry on first push (upsert) or requires a separate `POST` first. The docs literally say
"PATCH is currently identical to PUT, since all fields are optional" without clarifying create-vs-
update-only semantics — this adapter uses `PUT` on the assumption it upserts (standard REST
convention for that verb), but this is the one genuinely untested assumption in this spec and
should be the first thing manually verified once a real PAT is available to test against.

## Implementation — mirrors `BangumiTrackerAdapter.cs` almost exactly

1. **`TrackingService.MangaBaka`** — new enum value, appended at the end. Stored via
   `HasConversion<string>()` (confirmed in `PaperbunkrDbContext.OnModelCreating`,
   `builder.Property(t => t.Service).HasConversion<string>()`) — **no EF migration needed**, same
   as `ExternalMetadataProvider.MangaBaka` already required none.
2. **`MangaBakaMetadataProvider` gains `ITrackerSearchProvider`** (one property + interface
   declaration, zero new logic — its existing `SearchAsync` is already the right shape, same as how
   `AniListMetadataProvider` implements both `IMetadataProvider` and `ITrackerSearchProvider` off
   one `SearchAsync`).
3. **New `MangaBakaTrackerAdapter : ITrackerAdapter`** (`src/Paperbunkr.Data/Tracking/Adapters/`) —
   push-only, PAT-authenticated, structurally a near-copy of `BangumiTrackerAdapter`: a
   `CompleteConnect(context, pat)` static helper storing `CredentialKind.ApiKey` under
   `nameof(TrackingService.MangaBaka)`, `PushEntryAsync` reading the stored PAT (returns `false`
   immediately if absent, same as every adapter), sending `PUT /v1/my/library/{link.ExternalId}`
   with `x-api-key` header and a `{ state, progress_chapter }` JSON body, `IsSuccessStatusCode` as
   the success signal (matching every other adapter's error handling — network/timeout/non-2xx all
   collapse to `false`, never an exception). Reuses `MangaBakaHttpClient.Shared` (already exists in
   the metadata provider file, same host) rather than adding a redundant client to
   `TrackerHttpClients`.
4. **`DetailTabsViewModel` wiring** (four small additions, each mirroring an existing
   `TrackingService.Bangumi` branch exactly):
   - `TrackerServiceOptions` array gains `TrackingService.MangaBaka`.
   - `GetTrackerSearchProvider` switch gains `TrackingService.MangaBaka => new MangaBakaMetadataProvider(MangaBakaHttpClient.Shared)`.
   - `GetTrackerAdapter` switch gains `TrackingService.MangaBaka => new MangaBakaTrackerAdapter(MangaBakaHttpClient.Shared)`.
   - `SyncToTrackersAsync`'s `isConnected` switch gains
     `TrackingService.MangaBaka => CredentialStore.HasCredentials(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey)`.
5. **Preferences UI** (`PreferencesScreenViewModel.cs` + `PreferencesScreen.axaml`) — a new block in
   the Trackers section, structurally identical to Bangumi's (`MangaBakaPersonalAccessToken`
   property, `IsMangaBakaConnected` property, `SaveMangaBakaTokenCommand` calling
   `MangaBakaTrackerAdapter.CompleteConnect`), with the instruction text pointing at the real page
   the user found: *"Generate a Personal Access Token at mangabaka.org → My profile → Settings →
   API and Apps, then paste it here."*

## Explicitly out of scope for this pass

- Volume progress, rating, notes, read-link push — `TrackerPushPayload` doesn't carry these fields
  for *any* tracker today; adding them is a cross-cutting change to the shared payload type, not
  MangaBaka-specific, and stays out of scope here.
- OAuth support for MangaBaka — PAT covers the same use case with far less implementation risk,
  same reasoning `BangumiTrackerAdapter`'s own doc comment already gives for skipping Bangumi's
  OAuth.
- Pull/read-back of an existing MangaBaka library entry (the `GET /v1/my/library/{series_id}`
  endpoint) — `ITrackerAdapter` is deliberately push-only per its own doc comment ("Phase A scope
  only... add a `GetEntryAsync` method only when a Phase B pull/bidirectional spec actually needs
  it"), unchanged by this addition.

## Testing

Same pattern as `AniListTrackerAdapterTests`/`BangumiTrackerAdapterTests` — a fake
`HttpMessageHandler`, no real network calls. Planned cases: `PushEntryAsync` with no stored PAT
returns `false` without sending a request; a successful `PUT` returns `true`; a non-2xx response
returns `false`; `ReadingStatus`→`state` mapping covers all 7 values losslessly (a dedicated
assertion per value, given the 1:1 mapping is the one genuinely novel/interesting part relative to
Bangumi's lossy 5-state mapping). Manual on-screen verification against a real PAT (generated by
the user, entered directly into Preferences, never through this session) is the only way to confirm
the open PUT-upsert-vs-separate-POST question above — flagged as the first thing to check once
building starts, not assumed to work from the schema alone.
