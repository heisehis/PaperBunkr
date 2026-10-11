# Metron account sync — design

**Status:** built 2026-10-05, uncommitted, not seen on screen, never run against a real Metron account. "As built" at the
end lists where the code departs from this document.
**Sibling:** [2026-10-05-metron-api-efficiency-and-matching-design.md](2026-10-05-metron-api-efficiency-and-matching-design.md)
(its `cv_id` lookup is what resolves Metron ids here).

## Problem

The user wants the Metron API used fully. What's left after scraping, the weekly list, the change sweep and exact
lookups is the part tied to a Metron *account*: pull list, collection, read tracking, wish list and reading lists. All
of it sends or reads personal data, so all of it is opt-in.

## Facts this design is grounded on (Metron `api/README.md`, read 2026-10-05)

- **Pull list:** `GET pull_list/series/`, `POST pull_list/series/add` (`{series_id}`; 201 new, 200 already there),
  `DELETE pull_list/series/{series_id}/remove`.
- **Collection:** `GET collection/` (filterable, `modified_gt`), `POST collection/add/` (`issue_id`, `book_format`
  PRINT/DIGITAL/BOTH, grade, purchase and storage fields; idempotent, never updates an existing item),
  `DELETE collection/{item_id}/`, `PATCH collection/{item_id}/` accepting **only** `rating` (1–5 or null).
- **Scrobble:** `POST collection/scrobble/` (`issue_id`, optional `date_read`, optional `rating` 1–5). Adds a read date
  every call (re-reads kept) and creates the collection item, as DIGITAL, if missing. Read state is writable only here.
- **Wish list:** `GET wish_list/items/`, `POST wish_list/items/add` (`issue_id`; idempotent),
  `POST wish_list/items/{item_id}/acquire` (also creates a collection item), `DELETE wish_list/items/{item_id}/remove`.
- **Reading lists:** read-only. `GET reading_list/?name=` (own + public; `username`, `list_type`, `attribution_source`
  filters), `GET reading_list/{id}/`, `GET reading_list/{id}/items/` (ordered, 50 a page, with `issue_type`).
- Everything is keyed by Metron issue or series id. Limits are 20 requests a minute and a per-account daily cap.
- **Local facts:**
  - followed series are `WatchedSeries` (per provider); wants are `WantedIssue` (status `Imported` once the file is in);
  - a finished read is a `ReadingEvent` of kind `Finished` (written by the reader; **"Mark as read" does not write one**);
  - the personal rating is `Issue.Rating` (0–5 float);
  - issue ids live in `ComicMetadataExternalId`;
  - app-side recurring work runs through `ScheduledTaskCatalog` and shows in the Activity Center on its own;
  - reading-list sources implement `IReadingListSource` (a `MetronSource` for *arcs* already exists).
- Follows, wants, reads and ratings change in a dozen view models **and** in the separate daemon process. Hooking each
  would be fragile; the database already holds the full desired state.

## Decisions

| # | Decision |
|---|---|
| D1 | One master switch, "Sync with my Metron account", **off by default**, with a toggle per area. Uses the Metron login under Connections. Nothing is sent until it is on. |
| D2 | **Pull list:** additions go both ways (followed Metron series → pull list; pull-list series → followed here). Unfollowing here removes it there. A removal made on Metron never unfollows here, and is not pushed back. |
| D3 | **Reading:** each finished read is scrobbled with its date and the issue's rating if set; a re-read sends another. Existing history goes only when the user presses "Send my reading history". |
| D4 | **Collection:** each library issue is added as a DIGITAL copy; rating changes are sent. Nothing is ever deleted on Metron. A manual "Import from Metron" fills local read state and ratings that are empty and never overwrites. |
| D5 | **Wish list:** push only. A want is added, a removed want is removed, an imported want is marked acquired. |
| D6 | **Reading lists:** import only — a "Metron reading lists" source in the new-reading-list flow (own and public lists). |
| D7 | A book known only by a Comic Vine id gets its Metron id from one `cv_id` lookup, saved as a link. A book with neither id doesn't sync. |
| D8 | Nothing is lost when Metron is unreachable or the limit is hit: sync is a **reconciliation** against saved state, run at background priority within a per-run request budget, and reported through the Activity Center. |
| D9 | Metron's `collection/missing_series` and `missing_issues` are not used (we compute gaps ourselves). |

## Design

### Shape: reconcile, don't hook

`MetronAccountSync` (Data) compares what the database says should be on Metron with a ledger of what has been sent, and
makes the calls that close the gap. It needs no hooks in view models or the daemon, is safe to stop at any point, and a
failed call is simply still a gap next run. This *is* the saved queue D8 asks for.

- **Ledger:** `MetronSyncLink` — `Kind` (PullListSeries / CollectionItem / WishListItem), `LocalId` (WatchedSeries /
  Issue / WantedIssue id), `RemoteId` (Metron series id, collection item id, wish-list item id), `State` (Synced /
  RemovedRemotely / Acquired), `PushedRating` (collection only), `SyncedAt`. Unique on (`Kind`, `LocalId`).
- **Reading watermark:** `AppSettings.MetronSyncReadingEventId` — the highest `ReadingEvent.Id` already scrobbled.
  Turning the reading toggle on sets it to the current maximum (history is not sent); "Send my reading history" sets it
  to 0.
- **Budget:** a run makes at most 120 requests (six minutes of Metron's burst allowance) through a `Low`-priority
  client, and stops early on a rate-limit answer or when `MetronQuota.BackgroundShouldWait`. Order within a run: pull
  list, reading, wish list, collection — the cheap, visible things first; the bulk collection upload uses what's left.
  A 5,000-issue first upload therefore takes many runs, by design.

### Areas

- **Pull list (D2).** One `GET pull_list/series/` (paged). Followed, unpaused Metron `WatchedSeries` with no ledger row →
  `add`, ledger `Synced`. Ledger rows whose `WatchedSeries` is gone → `remove`, drop the row. Remote series with neither
  → `WantedService.TrackVolume(..., watchFutureReleases: true, provider: Metron)` + ledger. A `Synced` row missing from
  the remote list → `RemovedRemotely` (kept so it isn't re-added; the local follow is untouched).
- **Reading (D3).** `Finished` comic `ReadingEvent`s above the watermark, oldest first: resolve the Metron issue id
  (D7); `POST collection/scrobble/` with `date_read` and, when `Issue.Rating` rounds to 1–5, `rating`. The watermark
  advances past each event that was sent or cannot ever be (no id). A scrobble response's collection item id goes into
  the ledger, so the collection step doesn't add it again.
- **Wish list (D5).** Open Metron `WantedIssue`s without a ledger row → `add`. Ledger rows whose want is gone →
  `remove`. Wants now `Imported` → `acquire`, ledger `Acquired` (and a collection ledger row from the response).
- **Collection (D4).** Library issues (not placeholders, file present) with a Metron id and no collection ledger row →
  `POST collection/add/` `{issue_id, book_format: "DIGITAL"}`. Rows whose `PushedRating` differs from the issue's
  rounded rating → `PATCH {rating}`. Never `DELETE`.
- **Import from Metron (D4, manual).** Pages `GET collection/`; for each item whose issue maps to a local issue: set
  `Issue.Rating` if null; if the local issue is unread and the item `is_read`, mark it read
  (`IssueReadStateResolver.MarkAsRead`) and record a `Finished` event at `date_read` (below the watermark, so it is
  not scrobbled back). Fills ledger rows for what it sees.
- **Id resolution (D7).** Metron link → use it. Else Comic Vine link → `IComicIssueLookup.FindIssuesByComicVineIdAsync`,
  exactly one hit → save the Metron link. A miss is remembered for the run only.
- **Reading lists (D6).** `MetronReadingListSource : IReadingListSource` (`SourceKey` "MetronLists"): `SearchAsync` →
  `reading_list/?name=`; `GetArcIssuesInOrderAsync` → `reading_list/{id}/items/`; overview from the detail call.
  Registered beside the existing arc source. Independent of the master switch (it only reads public data and the
  user's own lists, on request).

### Client

`MetronAccountClient` (Data/ComicVine) — the verbs above over the shared rate-limited `MetronHttp.Client`, reusing
`MetronClient`'s error mapping (100 login, 101 not found, 107 rate limit). `MetronClient.GetAsync` is generalised to a
`SendAsync(method, url, json?)` so one code path owns auth, quota observation and retries; writes are **not** retried
on a transport failure that may have reached the server, except the idempotent ones (`add` endpoints, `PATCH`,
`DELETE`). Scrobble is the one non-idempotent call: a timeout there advances nothing, and the event is retried next
run, accepting a possible duplicate read date over a lost one.

### Hosting and UI

- A `ScheduledTaskCatalog` task, "Sync with Metron" (hourly, enabled, a no-op with a clear message when the master
  switch is off or no login is saved). The scheduler gives it an Activity Center job, run history and "Run now".
- Preferences → Connections, under the Metron row (existing `SettingsRow` + `ToggleSwitch` pattern, sub-toggles
  `IsEnabled` on the master): master; Pull list; Reading; Collection; Wish list; buttons "Sync now", "Send my reading
  history", "Import from Metron". Descriptions say plainly what is sent. No new control or style; per project rules the
  `avalonia-pro-max/review-checklist` runs before the section is called done.

### Out of scope

Deleting anything on Metron beyond pull-list and wish-list entries the user removed here; physical-copy fields
(grade, purchase, storage) — we send DIGITAL only; pulling wish-list items into wants; two-way rating conflict
resolution (ours wins on push, theirs only fills blanks on the manual import); Bearer-token login; Books (no Metron data).

## Testing

- `MetronAccountClientTests` (fake handler): each verb's URL, method and body; paging; error mapping.
- `MetronAccountSyncTests` (real SQLite, scripted client): every area's add / remove / skip paths, `RemovedRemotely`
  not re-added, watermark behaviour incl. "history" reset, rating PATCH only on change, never-delete, id resolution and
  its saved link, the request budget stopping a run mid-way and the next run finishing it, a rate-limit answer stopping
  cleanly, master/area toggles off = zero requests.
- Import: fills blanks, never overwrites, imported reads are not scrobbled back.
- `MetronReadingListSourceTests`; Preferences persistence; migration round-trip.
- **Cannot be checked here:** any call against the real Metron API (needs the user's account). The write endpoints
  are built from the README alone; the first real run should be watched, with only the Pull list toggle on.

## As built (2026-10-05)

- **Imported reads are marked, not hidden below the watermark.** Event ids only grow, so a read recorded by "Import
  from Metron" would always sit above the watermark. The ledger has a fourth kind, `ImportedRead` (local id = the
  `ReadingEvent`), which reading sync skips once and deletes.
- **A fourth ledger state, `Rejected`.** When Metron answers 400 or 404 for an issue (it doesn't know it), the row is
  kept so the same request isn't repeated every hour.
- **Wish list covers Metron wants only.** A want from a ComicVine series has no local issue to hang a resolved Metron
  id on, so it is not mirrored. A want that was imported before it was ever sent is not added after the fact.
- **A new collection item's rating goes on the following run**, as a PATCH, not with the add.
- **The Preferences buttons run the sync directly** (with their own Activity Center job) rather than asking the
  scheduler to run its task, so "Send my reading history" and "Import" can share the path.
- **Import has its own allowance** (60 pages of the collection) and is not counted against a run's 120 requests.
- **A read of a book with no id is stepped over for good** - if the book is matched later, that earlier read is not sent
  unless the user chooses "Send my reading history".
