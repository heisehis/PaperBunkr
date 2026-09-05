# CBL Manager — External Story-Arc Lookup — Design Spec

*Date: 2026-08-22. Scope: the half of the CBL Manager plugin explicitly deferred from the original
Reading Lists pass (docs/superpowers/specs/2026-08-06-reading-lists-design.md §1) — searching a
story arc by name against six external sources, auto-building a correctly-ordered, matched
`ReadingList` from the local library, and refreshing it later as the library grows.*

**Verification practice (per the standing CE-verification rule):** CE itself has no concept of
external story-arc lookup — reading lists are purely local CBL import/export (already confirmed in
the 2026-08-06 spec). This pass instead verifies against the **CBL Manager plugin**
(`_reference/CBLManager`, `docs/action-plan.md` + `docs/api-notes.md`), a real, already-built,
live-verified ComicRackCE plugin covering exactly this feature. Every adapter below is ported from
its real, working C# source (`src/CBLManager/*Source.cs`, `ComicVineClient.cs`) — endpoint shapes,
regexes, and JSON-LD parsing carried over as-is, not re-derived — since that source already did the
live investigation (including finding and fixing a real Metron endpoint bug, and confirming which
sites are viable vs. Cloudflare-blocked) this pass would otherwise have to redo from scratch.

## 1. Scope

Covers:
- A shared, reusable **credential store** (`ProviderCredential`) — not reading-list-specific, since
  the deferred AniList tracker-sync pass (docs/superpowers/specs/2026-08-19-metadata-model-anilist-
  tracker-sync-design.md) will need OAuth tokens later and this pass needs ComicVine/Metron
  credentials now. One shape for both rather than one-off `AppSettings` columns per provider.
- `IReadingListSource` interface + six adapters: **ComicVine**, **Metron** (Tier 1 — structured
  APIs), **Comic Book Reading Orders**, **ComicArc**, **ReadingOrders.com**, **ReadThingsRight**
  (Tier 2 — HTML/JSON-LD scraping). Ported from CBL Manager's own verified implementations.
- Arc search UI (source picker + query + results), replacing the disabled "Auto-Build from Tracked
  Arc" button.
- Create-list-from-arc and Refresh, both built on the existing `ReadingListMatcher` — no new
  matching logic, just new callers.
- Preferences UI for entering ComicVine/Metron credentials.

Explicitly excluded (confirmed nonviable by CBL Manager's own investigation, not worth
re-verifying):
- **Comic Book Herald** — Cloudflare Bot Management rejects .NET `HttpClient` (TLS/behavioral
  fingerprint block, not fixable by header tweaks).
- **CMRO / marvelreading.com** — Cloudflare JS challenge, even `curl` can't clear it.
- **Grand Comics Database** — no story-arc search endpoint on the real API; the main site is
  Cloudflare-gated; arc data only exists in a login-gated bulk DB dump.
- **League of Comic Geeks** — ToS explicitly prohibits scraping.
- **MyComicList** — the described site doesn't exist live.
- Marvel.com/DC.com official pages, Wikipedia story-arc pages — out of scope per CBL Manager's own
  action plan (thin marketing content / no consistent per-page structure).

Still deferred (unrelated to this pass):
- AniList/MyAnimeList tracker-sync buttons on Reading Lists — a different feature (personal tracker
  reading progress, not story-arc lookup); stays disabled.
- Post-to-dpaste.com sharing (already deferred in the 2026-08-06 spec, unrelated).

## 2. Credential store

```csharp
public enum CredentialKind
{
    ApiKey,
    Username,
    Password,
    OAuthAccessToken,
    OAuthRefreshToken,
}

public class ProviderCredential
{
    public int Id { get; set; }
    public string Provider { get; set; } = string.Empty;   // "ComicVine", "Metron" — free string,
                                                             // not tied to ExternalMetadataProvider
                                                             // (that enum's scope is series-metadata
                                                             // providers, a different concern)
    public CredentialKind Kind { get; set; }
    public string Value { get; set; } = string.Empty;       // plaintext — matches every other
                                                             // setting in this single-user local
                                                             // SQLite DB, no vault anywhere else
    public DateTime UpdatedAt { get; set; }
}
```

A `CredentialStore` service (`Paperbunkr.Data.Credentials`) wraps the table:
`Get(provider, kind)`, `Set(provider, kind, value)`, `Delete(provider, kind)`,
`HasCredentials(provider, params CredentialKind[] required)`. Adapters depend on this service, not
on `AppSettings` — same role `AppSettings` plays for app-wide settings, but shaped to add a new
provider (ComicVine today, an OAuth tracker tomorrow) without a schema migration each time.

## 3. `IReadingListSource` + registry

```csharp
public interface IReadingListSource
{
    string SourceKey { get; }               // stored on ReadingList.Source
    string DisplayName { get; }
    bool RequiresCredentials { get; }

    Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken ct);
    Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken ct);
    Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken ct);
}

public sealed record ArcSearchResult(string Id, string Name, string? Deck, string? Publisher, int IssueCount);
public sealed record ArcIssue(string Series, string Number, int Year, string? CoverImageUrl);
public sealed record ArcOverviewInfo(string? Description, string? CoverImageUrl);
```

No exact-ID match tier (CBL Manager's `CustomFieldKey`/`ArcMatcher` primary tier existed only for
CE's per-book plugin custom fields — e.g. `comicvine_issue`, set by a separate scraper plugin —
which has no Paperbunkr equivalent; `ExternalMediaId` is per-`Series`, not per-`Issue`, and doesn't
cover ComicVine/Metron). Every source falls back to `ReadingListMatcher`'s existing fuzzy
Series+Number(+Year) match — the same tier CBL Manager's own Metron and all four Tier-2 adapters
already relied on exclusively.

`ReadingListSourceRegistry.All` lists the six sources; `Get(sourceKey)` constructs one on demand.
Each adapter owns a shared `HttpClient` instance (same static pattern as `AniListHttpClient`) and a
per-instance minimum request interval (courtesy throttle, matching each source's own value from CBL
Manager: 1s ComicVine, 500ms everything else).

### Adapters (ported from `_reference/CBLManager/src/CBLManager/*.cs`)

- **ComicVineSource** (needs `ApiKey`) — search: `/story_arcs/?filter=name:X` (the generic
  `/search/` endpoint silently returns empty despite a nonzero total-results count — confirmed
  dead end, don't use it). Issues: `/story_arc/4045-{id}/`'s own `issues` field gives id+name stubs
  in the site's curated order; a follow-up batched `/issues/?filter=id:1|2|3` fetch gets real
  `issue_number`/`volume`/`cover_date`/`image`, then results are **re-sorted back into the stub
  order** — the batch endpoint doesn't preserve requested id order. Overview: `/story_arc/{id}/`'s
  `description` (real HTML — strip tags) falling back to `deck`.
- **MetronSource** (needs `Username`+`Password`, HTTP Basic Auth) — search: `/api/arc/?name=X`.
  Issues: `/api/arc/{id}/issue_list/`, a **separate paginated endpoint** from arc detail (the arc
  detail endpoint has no `issues` field at all — this was a real bug in an earlier version of the
  reference plugin, root-caused by reading Metron's actual open-source serializers rather than
  guessing again; already server-ordered by `cover_date, series, number`, pages appended as-is).
  Overview: `/api/arc/{id}/`'s `desc`/`image`.
- **ComicBookReadingOrdersSource** — search: scrape the DC/Marvel `event-timeline` index pages
  (cached per adapter instance); only cross-title "Events" are indexed this way, not single-arc
  pages. Issues: tag-agnostic line-splitting HTML parse (markup style varies between `<p>`-wrapped
  and bare `<span>` issue entries across different arc pages — both handled), blue `color:#0000ff`
  spans dropped as annotation/comment text before parsing.
- **ComicArcSource** — search: enumerate `/sitemap.xml`, then per-URL schema.org JSON-LD
  (`ItemList` for issues, sibling `Article` block for description/cover — real SEO markup, parsed
  with `JsonSerializer` not regex-per-field). Small catalog; cleanest source.
- **ReadingOrdersNetSource** — search: homepage's embedded event list (only entry point found — no
  site-wide search endpoint exists). Issues: regex over Next.js RSC payload `\"title\":\"..\"`
  strings, deliberately not reconstructing the full React Flight wire format (an internal
  serialization detail, not a stable target). No year extraction — the date field's position
  relative to title varies between payload chunks; `ArcMatcher`/`ReadingListMatcher` already treat
  year as optional.
- **ReadThingsRightSource** — search: fetch `hubDicts.js` directly (the hub page's arc titles are
  injected client-side from this ES module at runtime, not present in server-rendered HTML).
  Issues: `<li>Series (Year) #N-M</li>` pattern anchored to the *entire* trimmed line (naturally
  excludes annotation prose that merely mentions a number mid-sentence); ranges expand into one
  `ArcIssue` per number, half-issues (`#1/2`) stay singular.

Each adapter's own request/parse failure surfaces as a single adapter-specific exception type,
caught at the call site and shown as a status message — never crashes the search/create/refresh
flow, matching CBL Manager's own error handling.

## 4. Create-list-from-arc / Refresh

Both build on the existing `ReadingListMatcher.ResolveOrCreatePlaceholder` (§3 of the 2026-08-06
spec) — no new matching logic.

**Create**: pick a source + arc from search results → `GetArcIssuesInOrderAsync` → resolve each
`ArcIssue` in order via the matcher → new `ReadingList` with `Source`/`ArcId`/`ArcName` set from the
selection and `Description`/`CoverImageUrl` from `GetArcOverviewAsync` (best-effort — a failed or
empty overview fetch never blocks list creation, same as CBL Manager) → `ReadingListItem`s in arc
order.

**Refresh** (only offered when `ReadingList.Source` is set): re-fetch the arc's issues, then
reconcile against the list's current items **by match key** (Series+Number, narrowed by Year),
mirroring CBL Manager's `RefreshArcList`:

- An arc issue whose resolved `Issue` already has a `ReadingListItem` in this list (real or still a
  placeholder) → keep the item, just update its `SortOrder` to the arc's current position.
  `Role`/`Notes`/`GroupLabel` are never touched by refresh.
- An arc issue with no existing item → resolve via the matcher (reusing a still-`IsPlaceholder`
  `Issue` matching the same Series+Number from a prior Create/Refresh, rather than creating a
  duplicate) and add a new `ReadingListItem` at the arc's position.
- An arc issue whose *old* item pointed at a placeholder `Issue`, but the matcher now resolves it to
  a **different, real** `Issue` → replace: remove the old `ReadingListItem`, add one pointing at the
  real `Issue`. The orphaned placeholder `Issue` is deleted only if no other `ReadingListItem`
  anywhere still references it — Paperbunkr's `Issue`s are shared across lists (unlike CE's
  per-book-per-list model), so this existence check is a real adaptation of CBL Manager's own
  `RemoveBook`-on-stale-placeholder step, not a direct port.

Refresh reports `"Added {n}, replaced {m} placeholder(s), {x} still missing."` via the screen's
existing `StatusMessage`.

## 5. UI

- **Preferences → Advanced → Sources** (new section): one row per credentialed source (ComicVine
  API key; Metron username + password), backed by `CredentialStore`. A "Get one free at
  comicvine.gamespot.com/api" hint on the ComicVine row, matching CBL Manager's own prompt text.
- **Reading Lists screen**: the disabled "Auto-Build from Tracked Arc" button becomes live, opening
  an **Arc Search** panel — source dropdown (6 names) → query box (Enter or Search button) →
  results list → "Use This Arc", the same flow as CBL Manager's `ArcSearchForm`. A source missing
  required credentials shows an inline prompt to fill them in via Preferences rather than failing
  silently.
- A **Refresh** button appears on an arc-linked list's header (only when `Source` is set).
- No separate overview dialog — the screen's existing Total/Owned/Missing stat cards and item list
  (with `IsPlaceholder`/`FileIsMissing` items already visually marked missing) already cover what
  CBL Manager's `ArcOverviewForm` showed separately; `ArcName`/description populate the existing
  `Subtitle`/list header instead of a new surface.
- AniList/MyAnimeList buttons stay disabled — separate, still-deferred tracker-sync feature.

## 6. Testing

`Paperbunkr.Data.Tests`:
- `CredentialStore`: set/get/delete round-trip, `HasCredentials` with multiple required kinds.
- Each adapter's parsing logic against small captured HTML/JSON fixtures (not live network calls in
  CI) — ComicVine issue-stub-reordering, Metron pagination, ComicArc JSON-LD extraction, ReadThings-
  Right range expansion and half-issue handling, ReadingOrdersNet RSC-string regex, Comic Book
  Reading Orders' dual markup styles (both `<p>`-wrapped and bare-`<span>` issue entries).
- Refresh reconciliation: build a `ReadingList` with a placeholder item, simulate the matcher now
  resolving a real `Issue` for the same Series+Number, assert the placeholder item is replaced and
  the orphaned placeholder `Issue` is deleted only when nothing else references it.
- Create: small in-memory `ArcIssue` list → assert `ReadingList` built in order with correct
  `Source`/`ArcId`/`ArcName`.

Live-network verification (ComicVine/Metron need real credentials; the four scraped sites need
their real current markup) is manual, one pass per adapter against the live site — same practice
CBL Manager's own `api-notes.md` already used, not something to automate into CI.
