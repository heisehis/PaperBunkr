# Tracker write-back sync (four services)

**Date:** 2026-08-23
**Status:** Approved, pending implementation
**Backlog ref:** docs/onboarding.md §7's own "architectural note" ("this pipeline *is* the minimum
viable version of tracker integration"), and the standalone sketch at
`docs/superpowers/specs/2026-08-19-metadata-model-anilist-tracker-sync-design.md` ("R6" - AniList
tracker write-back sync). That sketch's own header flagged this as "meaningfully bigger and riskier
than R1-R5... should not be built without a separate go-ahead" - this spec is that go-ahead, expanded
from AniList-only to four services after research showed the underlying work was more architecturally
interesting (and more differentiated per service) than the original single-service sketch assumed.

## Scope

**In scope, this pass:** AniList, MyAnimeList, Shikimori, Bangumi - each gets real title search
(for linking a `Series` to the right external entry) and one-way local-to-remote push of
`Series.ReadingStatus` + chapter progress.

**Explicitly deferred, separate later decision:** Kitsu (OAuth2 password-grant - the app would
collect the user's real Kitsu username/password directly, a materially more sensitive trust model
than the other four's browser-redirect OAuth) and MangaUpdates (session-token login, same
username/password concern, no real OAuth app-registration model found). Both stay off `TrackingLink`
entirely until a separate spec revisits them.

**Explicitly Phase A only** (matching the original sketch's own phasing, still sound at 4x scope):
manual one-way push, triggered per-series by the user. No bidirectional sync, no pull-from-remote,
no conflict handling (push-only can't conflict), no automatic background sync (this app has no
background job/hosting-service infrastructure at all - manual "Sync to Trackers" is the entire scope
until that changes elsewhere first).

## Why the four services aren't architecturally uniform

Confirmed via direct research against each service's real API docs (not assumed) - this shapes
several decisions below:

| | Auth flow | Write endpoint | Status states (manga) |
|---|---|---|---|
| AniList | OAuth2 implicit grant, token in redirect URL fragment | `SaveMediaListEntry` GraphQL mutation | 6, clean 1:1 match to `ReadingStatus` |
| MyAnimeList | OAuth2 **with mandatory PKCE**, no scope parameter at all | `PUT /manga/{id}/my_list_status` | 5 status strings + a separate `is_rereading` boolean - re-reading is not its own state |
| Shikimori | OAuth2 (Doorkeeper), confidential client (needs a client secret), supports `urn:ietf:wg:oauth:2.0:oob` redirect | `POST`/`PUT /api/v2/user_rates` | 6, clean 1:1 match |
| Bangumi | **No OAuth for this pass** - Personal Access Token instead (see below) | `POST`/`PATCH /v0/users/-/collections/{subject_id}` | Only 5 states total, shared across every subject type - no re-reading concept exists on this service at all |

**Bangumi's auth model is deliberately different from the other three.** Its OAuth token-exchange
endpoint is documented in community reports as returning HTTP 500 roughly 10-20% of the time, and its
app-registration/redirect requirements were not confirmed even after direct research. Rather than
build against an unreliable, under-documented flow, Bangumi uses its **Personal Access Token**
feature instead: the user generates a token directly on bgm.tv's own site and pastes it into
Paperbunkr - no OAuth dance, no token-exchange call, sidesteps the flaky endpoint entirely. This is a
real, deliberate asymmetry (three "Connect" buttons, one "paste your token" field), not an oversight.

## Data model

No new entities. Reuses two entities already schema-complete but previously unused for this purpose:

- **`TrackingLink`** (`SeriesId`, `Service` enum, `ExternalId`, `LastSyncedIssueNumber`,
  `LastSyncedAt`) - populated by this feature's search-and-link flow, one row per series-per-service.
- **`ProviderCredential`**/**`CredentialStore`** (shipped 2026-08-22 for the CBL Manager arc-lookup
  work; its own doc comment already anticipated this exact feature: "since the deferred AniList
  tracker-sync pass will need OAuth tokens later"). Reused as-is, plaintext, single-user trust model
  - matches every other setting in this app's local SQLite database, and specifically *not* the
  original R6 sketch's now-superseded proposal to add Windows DPAPI encryption, since the shipped
  reality already diverged from that sketch.

**`CredentialKind` gains two new values**, appended at the end (confirmed the `Kind` column is a
plain `INTEGER`, not a `HasConversion<string>()` mapping like most other enums in this codebase - so
this needs no migration, but new values must always be appended, never inserted/reordered, since
existing rows would silently reinterpret their stored ordinal):

```csharp
public enum CredentialKind
{
    ApiKey,
    Username,
    Password,
    OAuthAccessToken,
    OAuthRefreshToken,
    OAuthClientId,      // new
    OAuthClientSecret,  // new
}
```

`Provider` key per service is its `TrackingService` name (`"AniList"`, `"MyAnimeList"`,
`"Shikimori"`, `"Bangumi"`) - same convention `ReadingListSourceRegistry` already uses for
`"ComicVine"`/`"Metron"`.

## Shared infrastructure (`Paperbunkr.Data/Tracking/`, new folder)

A new folder, not inside `Metadata/` - the source architecture review's own tracker-vs-scraper
distinction is architecturally real (a metadata refresh must never touch user state; a tracker sync
must never touch canonical metadata), and giving trackers their own namespace makes that boundary
visible from folder structure alone, not just a comment.

### `ITrackerSearchProvider`

```csharp
public interface ITrackerSearchProvider
{
    TrackingService Service { get; }
    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
}
```

Reuses `MetadataSearchResult` (`ExternalId`/`Title`/`Url`) from `Paperbunkr.Data.Metadata` - the
shape is identical to metadata search, and `AniListMetadataProvider` already implements this exact
signature, so it additionally implements `ITrackerSearchProvider` directly (one thin interface
addition, zero new AniList code). MyAnimeList/Shikimori/Bangumi each get a **new** search
implementation (see per-service section below) - genuinely new work, not a reuse, since this
codebase has no existing integration with any of the three.

### `ITrackerAdapter`

```csharp
public interface ITrackerAdapter
{
    TrackingService Service { get; }
    Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken);
}

public sealed record TrackerPushPayload(ReadingStatus Status, int? ChapterProgress);
```

Deliberately trimmed versus the original R6 sketch's `ITrackerAdapter` (which included a
`GetEntryAsync` for a future pull direction) - nothing in Phase A ever reads a remote entry, so it
isn't part of the interface yet. Add it back only when a Phase B pull/bidirectional spec actually
needs it.

Each of the four services implements both interfaces on one class (`AniListTrackerAdapter`,
`MyAnimeListTrackerAdapter`, `ShikimoriTrackerAdapter`, `BangumiTrackerAdapter`) - matching
`AniListMetadataProvider`'s own precedent of one class handling both search and per-item operations
for a single provider, rather than splitting search and push into separate types per service.

### `TrackerLinkResolver` (new, mirrors `MetadataLinkResolver`'s shape but touches only `TrackingLink`)

```csharp
public static class TrackerLinkResolver
{
    public static Task<IReadOnlyList<ScoredMetadataMatch>> SearchAsync(
        ITrackerSearchProvider provider, PaperbunkrDbContext context, int seriesId, string query, CancellationToken cancellationToken);

    public static void Link(PaperbunkrDbContext context, int seriesId, TrackingService service, string externalId);

    public static void Unlink(PaperbunkrDbContext context, int seriesId, TrackingService service);
}
```

`SearchAsync` reuses `TitleMatchScorer`/`ScoredMetadataMatch` exactly as `MetadataLinkResolver` does
(same known-titles-vs-search-results scoring). `Link` differs deliberately from
`MetadataLinkResolver.LinkAsync`: it only upserts the `TrackingLink` row (`SeriesId`+`Service`+
`ExternalId`) - **no** `ExternalMediaId`, no `ExternalMetadataSnapshot`, no `SeriesTitle` writes,
because a search result already carries everything a `TrackingLink` needs (its `ExternalId`), and
because writing canonical metadata from a tracker-linking action would blur the same tracker-vs-
scraper boundary the folder split above exists to keep visible. No network fetch needed for `Link`
itself, so it isn't `async`.

## Per-service adapters

### AniList (`AniListTrackerAdapter`)

Search: delegates to the existing `AniListMetadataProvider.SearchAsync` (already implements
`ITrackerSearchProvider`'s shape). Push: new `SaveMediaListEntry` GraphQL mutation call, authenticated
with the stored `OAuthAccessToken`. Connect: "Connect AniList" opens the system browser to AniList's
OAuth authorize URL (implicit grant, using a Client ID the user registered on their own AniList
account settings and pasted into Preferences - same "user registers their own app" model the
original sketch established, not a Paperbunkr-wide embedded client); user copies the token out of the
redirect URL's fragment and pastes it back into Paperbunkr.

Status mapping: `Planned`→`PLANNING`, `Reading`→`CURRENT`, `Completed`→`COMPLETED`, `Paused`→`PAUSED`,
`Dropped`→`DROPPED`, `ReReading`→`REPEATING` - clean 1:1, no lossy case.

### MyAnimeList (`MyAnimeListTrackerAdapter`)

Search: new `GET https://api.myanimelist.net/v2/manga?q={query}&fields=id,title` call - this is a
"public" MAL v2 endpoint, authenticated with just an `X-MAL-CLIENT-ID` header (the user's pasted
Client ID), not a full OAuth token - so search works as soon as a Client ID is entered, before
"Connect" (the OAuth exchange, needed only for push) is completed.

Push: `PUT /manga/{id}/my_list_status`, authenticated with the OAuth access token. Connect: PKCE
flow - Paperbunkr generates a code verifier/challenge pair, opens the browser to MAL's authorize URL
with `redirect_uri=http://localhost:<port>` (matching MAL's documented native-app redirect patterns;
no local server actually listens on that port). The browser shows a "can't reach this page" failure,
but the address bar still carries the `code` query parameter - the user copies it from there and
pastes it into Paperbunkr, which then performs the token exchange itself (`POST
https://myanimelist.net/v1/oauth2/token` with the `code` + `code_verifier`). MAL registration
requires manual one-time app approval by MAL after the user submits their app at
`myanimelist.net/apiconfig/create` - a real, unavoidable manual step documented in the Preferences UI
copy, not something this app or Claude can complete on the user's behalf.

Status mapping: `Planned`→`plan_to_read`, `Reading`→`reading`, `Completed`→`completed`,
`Paused`→`on_hold`, `Dropped`→`dropped`. **`ReReading`→`reading` + `is_rereading: true`** - the one
documented lossy case for this service (MAL has no separate re-reading status string).

### Shikimori (`ShikimoriTrackerAdapter`)

Search: new `GET https://shikimori.one/api/mangas?search={query}` call - Shikimori's basic content
API is public, no auth needed for search at all.

Push: `POST`/`PUT /api/v2/user_rates` (create if the series has no existing rate record, update by
id otherwise - the adapter checks via a preceding lookup since `TrackingLink` doesn't itself store
the `user_rate` id, only the manga's own `ExternalId`), authenticated with the OAuth access token,
body shape `{ user_rate: { target_id, target_type: "Manga", status, chapters } }`. Connect: OAuth2
via Doorkeeper, `redirect_uri=urn:ietf:wg:oauth:2.0:oob` and `scope=user_rates` - Shikimori's own
authorize page then displays a code directly (no failed-redirect trick needed, cleanest UX of the
four), which the user copies and pastes back; Paperbunkr exchanges it using the user's pasted Client
ID **and Client Secret** (Doorkeeper defaults to a confidential-client model, unlike AniList's
implicit grant or MAL's PKCE-only public client).

Status mapping: `Planned`→`planned`, `Reading`→`watching`, `Completed`→`completed`,
`Paused`→`on_hold`, `Dropped`→`dropped`, `ReReading`→`rewatching` - clean 1:1, no lossy case (the one
other service besides AniList with a genuine 6-state match).

### Bangumi (`BangumiTrackerAdapter`)

Search: new `POST https://api.bgm.tv/v0/search/subjects` call, body `{ keyword, filter: { type: [1] } }`
(`type: 1` = book/manga subjects) - public endpoint, but Bangumi requires a descriptive `User-Agent`
header identifying the calling application per its own API guidelines; Paperbunkr sends one
identifying itself and a contact/repo URL.

Push: `POST`/`PATCH /v0/users/-/collections/{subject_id}`, body
`{ type, vol_status: <ignored this pass>, ep_status: <chapter progress> }`, authenticated with the
pasted Personal Access Token as a plain Bearer token (no OAuth exchange at all - see the auth-model
section above). Connect UI: no "Connect" button: a "Paste Personal Access Token" field with a link to
Bangumi's own token-generation page, stored via `CredentialStore.Set(..., CredentialKind.ApiKey, ...)`.

Status mapping: `Planned`→`1` (想看/Wish), `Reading`→`3` (在看/Doing), `Completed`→`2` (看过/Done),
`Paused`→`4` (搁置/OnHold), `Dropped`→`5` (抛弃/Dropped). **`ReReading`→`3` (Doing), same value as
`Reading`** - the one other documented lossy case: Bangumi has no re-reading concept on any subject
type, so this is an accepted, permanent loss of that one distinction for this service specifically.

Progress: `ep_status` only (chapter-equivalent); `vol_status` left unset, matching this pass's
"chapter progress only, no volume push" scope for every service.

## UI

### Preferences → new "Trackers" section

One row per service, alongside the existing Appearance/Behavior/Libraries/Advanced tabs pattern:
- **AniList / MyAnimeList / Shikimori**: a Client ID field (+ Client Secret field for Shikimori
  only, since AniList/MAL are public clients), a "Connect" button (disabled until the Client ID
  field, and Secret where required, are filled in) that opens the system browser per that service's
  flow above, and a paste-back field for the resulting code/token. Each row shows connected/
  disconnected state (via `CredentialStore.HasCredentials`).
- **Bangumi**: a single "Personal Access Token" paste field plus a link to Bangumi's token page - no
  Client ID/Secret, no browser round-trip.

### Tracker linking - deliberately separate from R3's metadata linking

The existing AniList search-and-link flow in `DetailTabsViewModel` (`MetadataSearchResults`/
`SearchMetadataAsync`/`LinkMetadataAsync`, calling `MetadataLinkResolver`) stays exactly as-is for
read-only metadata linking (`ExternalMediaId`). This feature adds a **new, separate** "Link for
Tracking" action and search UI (same search-and-confirm interaction shape, a new provider dropdown
since there are now four tracker services to search against, backed by `TrackerLinkResolver` instead
of `MetadataLinkResolver`) with its own explicit "this will let Paperbunkr write your reading
progress to this account" confirmation copy before the link is created - per your direction, linking
for tracking carries real account-write consequences that linking for metadata does not, and the two
actions must stay visibly distinct even when they'd resolve to the same external id.

### "Sync to Trackers" - one action, all connected links

A single per-series action (Library context menu / Detail screen, same placement precedent as the
existing "Link to AniList" button) that iterates every `TrackingLink` the series has whose service is
currently connected (`CredentialStore.HasCredentials` for the required kinds), calling
`PushEntryAsync` on each via a small per-service adapter registry (`Dictionary<TrackingService,
ITrackerAdapter>`), and reports a summary toast ("Synced to AniList, MyAnimeList" / "Shikimori sync
failed, try again later") rather than one toast per service. A series with zero connected tracker
links doesn't show the action at all.

## Error handling

Every push/search call degrades the same way `AniListMetadataProvider` already does: non-2xx
responses, timeouts, and malformed JSON are treated as "unavailable right now," never an exception -
the library must keep working when any one tracker is down. Bangumi's collection endpoints
specifically get one retry-with-backoff on the initial call (not just token exchange, since PAT
avoids that specific endpoint, but the underlying API's documented flakiness isn't limited to the
OAuth layer) before surfacing failure.

## Testing

- **Pure mapping functions** (`ReadingStatus` → each service's status representation and progress
  field) get direct `Theory`-based unit tests per service, mirroring
  `CeLibraryMigratorTests.MapMangaField_MatchesDocsSection6Table`'s and this session's own
  `LanguageIsoClassifierTests`' pattern - explicitly covering each documented lossy case
  (MyAnimeList's `is_rereading` flag, Bangumi's `ReReading`→`Doing` collapse).
- **Search and push HTTP behavior** per adapter, against a fake `HttpMessageHandler`
  (`AniListMetadataProviderTests`' existing `StubHandler`/`JsonResponse` pattern, reused directly) -
  no live network calls in the test suite, consistent with the existing AniList precedent and every
  external service's own terms against automated hammering.
- **`TrackerLinkResolver`** tests mirroring `MetadataLinkResolverTests`' existing shape, confirming
  `Link` only ever touches `TrackingLink` and never `ExternalMediaId`/`SeriesTitle`/
  `ExternalMetadataSnapshot`.

## Explicitly out of scope

- Kitsu, MangaUpdates (separate later decision, see Scope section above).
- Phase B (bidirectional sync, conflict resolution) and Phase C (automatic background sync) from the
  original R6 sketch - both still apply at 4x scope, deferred for the same reasons.
- Volume-progress push (`num_volumes_read`/`volumes`/`vol_status`) - chapter progress only, every
  service, this pass.
- Pulling/reading remote tracker state at all - `ITrackerAdapter` has no `GetEntryAsync` this pass.
