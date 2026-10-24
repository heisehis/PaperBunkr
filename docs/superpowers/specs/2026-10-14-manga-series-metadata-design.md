# Manga series-centric metadata — Slice 1 design (Edit Series, inheritance, chapter overrides)

Date: 2026-10-14
Status: Draft for user review
Source: ChatGPT's `PaperBunkr-Unified-Manga-Metadata-Implementation-Spec.md` (Downloads), audited against the repo and cut down through a grilling round (decisions D1–D13 below are the user's answers). Facts in this doc were checked against the code on 2026-10-14.

## 1. Goal

Make Manga/Manhwa/Manhua detail **series-centric**: edit publisher, credits, genres/tags once on the series, and every chapter follows immediately, with deliberate per-chapter exceptions. Western Comics stay issue-centric and untouched.

## 2. What the repo actually looks like (why this differs from the ChatGPT spec)

| ChatGPT spec assumed | Repo reality | Consequence |
|---|---|---|
| `Series` owns publisher/creators/genres | `Issue` owns them: `Issue.Publisher`, per-role credit strings (`Writer`, `Penciller`, `Inker`, `Colorist`, `Letterer`, `CoverArtist`, `Editor`), tags as `IssueTag` rows. `Series.Publisher/Genre/Creator` are legacy/provider strings | Inheritance must work *through* the issue columns (D2) |
| A read-time resolver is cheap | ~48 non-test files read the issue columns (Smart Lists, Library search/sort, write-back mappers, reader info, Continuity, …) | Resolver-on-read rejected: blast radius too large |
| Credits are structured | `CreatorCredit` is a *derived index* rebuilt from the issue's text by `CreatorResolver` | Series credits fan out into the text columns; the index re-derives |
| Write-back queue is persistent and fingerprinted | `MetadataWriteBackQueue` is in-memory, 300 ms debounce, coalesced by issue id; formats CBZ/CB7/CBT only | Durability is Slice 2 (D4) |
| Write-back default on | `WriteMetadataToFiles` and `WriteMetadataAutomatically` default **false** | Kept as-is (D6). Deliberate deviation from the ChatGPT spec |
| New provenance + identity tables | `MetadataProposal`, `ExternalMetadataSnapshot`, `ExternalMediaId`, `TrackingLink`, `ExternalMetadataResolver` already exist | Reuse in Slice 3; do not build a parallel stack |
| `HeaderTitle` must notify | Already a notifying property on both VMs | Nothing to do |
| Edit is tied to `_continueIssueId` | Confirmed (`MangaDetailScreenViewModel.Edit`) | Replaced (D11) |

## 3. Decisions

- **D1 Slicing.** Slice 1 (this doc): series editor, immediate inheritance, chapter overrides, Edit Series button. Slice 2: durable sync queue. Slice 3: provider comparison, provenance/locks, migration review UI.
- **D2 Inheritance = cached fan-out.** The series value is the authority. Saving it writes the value into every non-overridden issue in one transaction (set-based update, not per-row entity tracking). Issue columns are a rebuildable cache, so Smart Lists, search, sort and ComicInfo export need no change. This knowingly breaks the ChatGPT spec's "no duplicate strings" rule; the override table plus the snapshot (D8) make the cache rebuildable.
- **D3 Credits.** New `SeriesCredit` rows (series, role, name, order). Fan-out renders them into the per-role issue strings, then `CreatorResolver` re-derives `CreatorCredit` for affected issues.
- **D4 Sync durability** (Slice 2): no job table. A `LastEmbeddedFingerprint` column on `Issue` plus a startup sweep that re-queues any issue whose effective export differs. Covers crash recovery, "already current", and coalescing.
- **D5 Provenance** (Slice 3): reuse `MetadataProposal` (add `Publisher` and credit roles to `MetadataProposalField`) plus one user-locked-fields set on the series.
- **D6 Settings.** Keep the existing two write-back settings. In-app inheritance is instant regardless; files change only when the user has opted in.
- **D7 Migration conflicts.** No review UI in Slice 1. A series whose chapters disagree shows the existing most-common value with a Needs Review flag; nothing is rewritten until the user confirms in Edit Series.
- **D8 Reclassification safety.** Before the first fan-out for a series, snapshot each affected issue's old values (one row per issue, written once). Manga → Comic restores from the snapshot; `SeriesContentTypeEditor` Undo keeps working.
- **D9 First-save exceptions.** If chapters already differ from the new value, Save shows "N chapters have a different value": **Keep as exceptions** (default, marks them overridden) or **Replace all**. After the first save only explicit chapter overrides count.
- **D10 Fan-out fields.** Publisher, Imprint, the seven credit roles, Genre and Tags (`IssueTag`), AgeRating. **Summary does not fan out** (chapters keep their own synopsis; export falls back to the series summary when blank). Never touched: number, title, volume, translator, scan group, language, release date, file info, reading state.
- **D11 Edit Series replaces Edit.** The manga hero's Edit becomes **Edit Series**, rooted in the series id, and opens a large borderless overlay (same pattern as the Issue Properties overlay, `2026-08-23-issue-editor-borderless-overlay-design.md`). It no longer opens issue properties for the continue chapter. Continue logic is untouched. Chapters are edited via the chapter context menu (D13). Comic's Edit / Edit Issue / Edit N Issues is unchanged.
- **D12 Working location.** The main checkout on `master`, as usual — no new worktree. The tree has ~214 uncommitted files from other work: touch only this feature's files, never stage or revert others'. The migration is verified with tests against throwaway databases only; the on-screen check happens when the user chooses, since running the app applies the migration to the real library.
- **D13 Chapter edit.** Reuse the Issue Properties editor. Inherited fields show a "from series" marker; editing one creates an override; "Reset to series" removes it and re-applies the series value. No new chapter editor.

## 4. Data model (one migration)

```text
SeriesCredit        Id, SeriesId (FK, cascade), Role, Name, SortOrder
IssueFieldOverride  Id, IssueId (FK, cascade), FieldKey          unique (IssueId, FieldKey)
IssueMetadataSnapshot  IssueId (PK/FK, cascade), FieldsJson, TakenUtc       written once, never updated
```

- `Series.Publisher` is promoted to the canonical manga publisher. `Imprint` and `AgeRating` need a home: add nullable `Series.Imprint` and `Series.AgeRating`. Genre and tags have no series home today, so series-level tag rows are stored as `SeriesTag` (series, field, value, category, weight), mirroring `IssueTag`; fan-out copies them into `IssueTag`.
- `FieldKey` comes from a single typed registry (`MangaInheritedFields`) listing each inheritable field: where the series value lives, how to write it onto an `Issue`, whether it is an issue string or an `IssueTag` set. The editor, the fan-out and the "from series" markers all read this one registry.
- No `MangaSeriesProfile` yet. Native/romanized/localized titles use `SeriesTitle`. Dates, expected counts, platform and country are deferred. No revision counter: Slice 2's fingerprint replaces it.

## 5. Behaviour

**Save (Edit Series).** Validate → in one SQLite transaction: update `Series` fields, replace `SeriesCredit` rows, replace `SeriesTag` rows, take the snapshot for issues that don't have one, run the fan-out for each changed field over issues without an override for that field, re-derive `CreatorCredit` for changed issues, and commit. Then enqueue write-back for changed issues through the existing `MetadataWriteBackQueue.Enqueue` (the queue applies the user's two settings). Unchanged save = no writes, no enqueue. DB failure = no success shown, nothing enqueued. UI reloads from the DB immediately and does not wait on files.

**New chapter.** The scanner assigns a new chapter in a manga-family series the series' current values for the D10 fields (no override created). Re-scan/re-import of an existing chapter's ComicInfo must not overwrite a non-overridden inherited field. Both are the only changes to scanner behaviour and are manga-family-only.

**Overrides.** Created by editing an inherited field in the Issue Properties editor (and by D9's Keep-as-exceptions). A fan-out skips overridden fields. Reset removes the row and writes the series value onto that issue only.

**Reclassify.** Comic → Manga: no automatic fan-out. The first Edit Series Save runs it (with the D9 prompt). Manga → Comic: restore from the snapshot where one exists; issues without one keep current values.

**Remote series.** Edit Series hidden/disabled, same as today's `IsRemoteSeries` handling.

## 6. UI

- **Hero:** Edit → **Edit Series** (series id, always enabled for local series, independent of `_continueIssueId`). Continue/Change Cover unchanged.
- **Overlay sections (Slice 1):** Identity & Titles, Creators, Publication, Classification, Description & Artwork. Sources, provenance and Advanced arrive in Slice 3. Uses `OverlayShell`, existing tokens, squircle `PbRadius`. Avalonia subskills (layout-patterns, components, accessibility) and `avalonia-pro-max/review-checklist` are consulted during planning and implementation per CLAUDE.md.
- **Keyboard/input:** the overlay registers any shortcuts through `AttachedInputRegistration` (no `KeyDown`/`KeyBinding`); Save/Cancel use the existing `Save` action and Escape. Remove-row and close buttons inside it defer collection changes with `Dispatcher.UIThread.Post`.
- **Hero/band/details (manga only):** publisher, credit chips, genres and tags read from the series values. Chapter and unread counts stay derived from local chapters.
- **Issue Properties editor (when opened on a manga chapter):** inherited fields get a "from series" marker plus Override / Reset to series.
- **Context menu:** Edit Series added beside the existing chapter entries; chapter "Edit Chapter Details" opens the existing editor.

## 7. Out of scope (all slices)

`MangaSeriesProfile` (dates, expected counts, platform, country), editions, filename/volume cleanup (`VOLUME -1`), new provider APIs or endpoints, a standalone manga tracker-progress rework, any change to Comic detail, reader, or Home. (User decision 2026-10-14: Slices 2 and 3 are in scope and built together with Slice 1; sections 10 and 11 below.)

## 8. Testing

- **Fan-out:** 100 chapters, change publisher and a writer → all non-overridden issues update in one transaction; overridden ones don't; number/title/volume/translator/scan group/progress unchanged.
- **First-save exceptions:** differing chapters become overrides under Keep, are replaced under Replace all.
- **New chapter / rescan:** inherits series values; rescan doesn't overwrite a non-overridden field.
- **Override reset:** removes the row and restores only that issue.
- **Snapshot / reclassify:** snapshot written once; Manga → Comic restores; Undo still works.
- **Credits:** `SeriesCredit` → role strings → `CreatorCredit` re-derivation, including multi-person roles and ordering.
- **Tags:** `SeriesTag` → `IssueTag` copy keeps issue-level tags on overridden issues.
- **Unchanged save:** no writes, no enqueue. Failed save: no enqueue.
- **Edit Series command:** works with no continue chapter; hidden for remote series. Comic Edit behaviour unchanged (regression).
- **Migration:** fresh DB and populated DB; nothing destructive. `RealWindowKeyboardTests`-style check for the overlay's focus/Escape. Run only the targeted test classes plus `Speed!=Slow`, per CLAUDE.md, and kill build servers afterwards.
- **On-screen verification** (overlay layout, fan-out result, markers) is the user's call after merge, per D12.

## 9. Risks

- Fan-out is a cache: any code path that edits an issue's publisher/credits directly on a manga chapter must create an override (or it will be overwritten by the next series save). The Issue Properties editor and Bulk Issue editor are the two such paths; both are covered in the plan.
- Adding columns to `Series` and a migration on a shared dev DB: tests use throwaway DBs; the real DB migrates on first app launch.
- A large fan-out transaction on 500+ chapters must stay on a background context and be set-based; the UI waits only for the DB commit.

## 10. Slice 2 — durable embedded sync

**Verified facts.** `MetadataFileWriteBackService` writes **CBZ and image folders only** (CB7/CBT/CBR/PDF/DjVu/EPUB return `SkippedUnsupportedFormat`; the queue's own comment saying CB7/CBT is stale). It already writes to a `.pbwrite-*.tmp` sibling and `File.Replace`s it in, suppresses the folder watcher, and refreshes the issue's file stats afterwards. It reads the issue's columns through `IssueToComicInfoMapper`, which is why the fan-out cache (D2) feeds it unchanged. `MetadataWriteBackQueue` is in-memory, debounced 300 ms, coalesced by issue id, gated by `WriteMetadataToFiles` + `WriteMetadataAutomatically` (both default false).

**Design (D4 made concrete): intent lives on the Issue row, not in a job table.** New `Issue` columns:

| Column | Meaning |
|---|---|
| `EmbeddedDirty` (bool) | Durable "this file owes a write". Set **in the same transaction** as the fan-out (Edit Series save, override set/reset, Replace-all). Cleared only after a confirmed write. |
| `EmbeddedFingerprint` (string?) | SHA-256 of the exported field set (the mapper output for the issue plus the sidecar/MetronInfo flags) at the last successful write. |
| `EmbeddedFileStamp` (string?) | `size:modifiedTicks` of the file right after our last write. |
| `EmbeddedSyncState` (enum) | `None, Synced, RetryScheduled, NeedsAttention, SkippedReadOnly, UnsupportedFormat, SkippedMissingFile, ConflictExternalModification`. |
| `EmbeddedSyncMessage` (string?) | Last error/conflict text. |

Plus `Series.EmbeddedSyncEnabled` (bool, default true), the per-series off switch. A disabled series still inherits in-app.

**Behaviour.**
- **Startup sweep:** after migrations and the splash, a low-priority background task enqueues `EmbeddedDirty = true` issues (never "everything"). The first run after upgrading rewrites nothing, because no issue is dirty until the user edits. The queue still applies the two global settings. With write-back off, dirty flags stay set, so turning it on later catches up only the edited chapters.
- **Skip current files:** before writing, compute the fingerprint. If it equals `EmbeddedFingerprint` and the file stamp is unchanged, clear `EmbeddedDirty` and skip the write.
- **Latest-value coalescing:** the writer reads the issue's columns at execution time, so three quick edits export only the final value, and the fingerprint after the write reflects it.
- **External modification:** if `EmbeddedFileStamp` is set and the current file's stamp differs, the file was changed outside Paperbunkr. Automatic writes do not overwrite it. State becomes `ConflictExternalModification` and dirty stays set. A manual **Write anyway** bypasses it. A first-ever write (no stamp) proceeds, matching today's behaviour.
- **Failures:** `Failed` becomes `RetryScheduled` with up to 3 attempts per session (backoff 5 s / 30 s / 5 min), then `NeedsAttention` (dirty stays set, retried next launch). Unsupported, read-only and missing states stay visible and are never shown as "Synced".
- **No lost work:** dirty is written in the edit transaction. A crash mid-sweep leaves dirty set, and the sweep removes leftover `.pbwrite-*.tmp` files.
- **Progress:** each sweep or batch is one `IActivityService` job in the Activity Center ("Embedding metadata: 185 / 500"), with failures as alerts carrying Retry. Chunked, one file at a time (the existing archive-write rule), off the UI thread, with its own `PaperbunkrDbContext` per unit of work.
- **Summary fallback:** export fills `Summary` from the series summary when the chapter's is blank (manga-family only). The fingerprint includes the effective value.
- **Actions** (Edit Series → Advanced, and the chapter context menu): **Retry failed**, **Write anyway** (conflicts), **Verify embedded metadata** (read-only: compares each file's embedded ComicInfo against the expected export and reports mismatches; writes nothing).

## 11. Slice 3 — sources, provenance, locks, migration review

**Verified facts.** Linking a provider today (`MetadataLinkResolver.LinkAsync`) calls `ProposeAndApply`, which writes straight onto `Series.Summary/Genre/Status/Creator` as an already-`Accepted` proposal, and `ExternalTagImportResolver.ApplyToSeries` merges genre/other tags into **every** issue. `ExternalMediaMetadata` carries titles, description, status, chapter/volume counts, a joined `Creator` string, CSV `Genre`, and genre/other tag lists — **no publisher and no per-role credits**. `MetadataProposalField` has no Publisher/Imprint/AgeRating/role entries.

**Design.**
- **Field locks.** `Series.LockedFields` (CSV of registry field keys). Saving Edit Series locks every field the user changed. Edit Series shows a lock marker per locked field with an Unlock action. Locks cover the D10 fields plus Summary, Status and titles.
- **Provider candidates respect locks.** `ProposeAndApply` changes as follows. If the target field is locked, the proposal is stored as `Pending` and is **not applied**. If it is not locked, behaviour is unchanged (auto-accept) for non-manga series. For manga-family series, a provider value that changes a fan-out field goes through the same canonical save path (series value, fan-out, dirty flags). `ExternalTagImportResolver` for a manga-family series merges into `SeriesTag` and fans out to non-overridden issues only. Overridden issues keep their own tags.
- **Sources section** in the Edit Series overlay: lists linked providers (`ExternalMediaId`: provider, id, URL, last fetched); a **Refresh from source** action (re-runs the existing fetch for that identity, with no new endpoints); and a field-by-field comparison of pending provider proposals against the current value, showing provenance ("via MangaBaka"), lock state, and Accept / Reject per row. Accept applies through the canonical save path and records an `Accepted` proposal. Reject records `Rejected`. A **Creator** candidate (the provider only supplies a joined author/artist string) is accepted into `SeriesCredit` under a role the user picks (default Writer), never silently split by guess.
- **`MetadataProposalField` additions** (appended only, so stored integers are unchanged): `Publisher`, `Imprint`, `AgeRating`. Manual Edit Series changes are recorded as `Manual`-source accepted proposals (a provenance trail), and a future provider that supplies a publisher plugs in with no schema change. No provider supplies them today, and nothing is invented.
- **Migration/reconciliation ("Needs Review").** A `SeriesCanonicalSeeder` runs lazily when Edit Series opens (and when a series is first routed to the manga screen). For each D10 field with no canonical series value it reads the chapters' current values. If all agree, the value is proposed as the series value (unconfirmed until Save). If they disagree, both values are kept as candidates in the editor, the field is flagged **Needs Review**, and nothing is written. The detail band shows a "Needs review" chip when any field is flagged, and Edit Series Save is what confirms. `Series.CanonicalReviewVersion` (int) records that reconciliation was resolved so it isn't asked again. Re-import of ComicInfo never overwrites a confirmed series value (the new-chapter/rescan rule in §5).
- **Trackers.** No change to tracker code or credentials. The manga Details tab keeps its existing Linking and Trackers blocks. A provider identity (`ExternalMediaId`) stays separate from a tracker link (`TrackingLink`).

## 12. Testing additions (Slices 2–3)

- **Sync:**
  - Dirty is set in the edit transaction, and the sweep re-queues only dirty issues.
  - The first upgrade rewrites nothing.
  - A fingerprint match skips the write and clears dirty.
  - Three edits export the last value.
  - External modification gives `ConflictExternalModification` with no overwrite, and Write anyway overwrites.
  - Retry/backoff ends in `NeedsAttention`.
  - Unsupported, read-only and missing files are never shown as "Synced".
  - A crash leaves dirty set and tmp leftovers get cleaned.
  - A series with sync disabled is skipped.
  - The Summary fallback is part of the fingerprint.
  - Verify writes nothing.
- **Sources and locks:**
  - A locked field leaves the provider proposal `Pending` and the series unchanged.
  - An unlocked non-manga series keeps the old auto-apply behaviour (regression).
  - Accept and Reject round-trip.
  - Creator accept uses the chosen role.
  - Provider tags fan out only to non-overridden issues.
- **Seeder:** the agree, disagree and empty cases; no writes without Save; `CanonicalReviewVersion` prevents re-asking; a confirmed value survives a rescan.

## 13. As built (2026-10-14)

All three slices are implemented. Where the build differs from the sections above:

- **Fan-out uses tracked entities in one transaction**, not a set-based `ExecuteUpdate`: EF batches the updates, and it keeps tags, overrides and the creator index in the same unit of work. A 500-chapter series is one transaction on a background context.
- **Services use default contexts.** `MangaSeriesMetadataService`, `MangaSourceCandidates`, `EmbeddedSyncActions` and the queue never opt in to another library's rows (the repository's remote opt-in allowlist is a deliberate policy), so a mirrored series is simply not found by them. Edit Series is also disabled for it on the screen.
- **Tags in the editor** keep an existing value's category and weight; a new value gets the default category ("Genre" / "Uncategorized"). Weight is not editable in the overlay.
- **Content type** is still changed from the series header (it already reroutes between screens); it is not duplicated inside the overlay.
- **No "from series" marker on chapter rows.** The Issue Properties editor shows the chapter's exceptions with Reset to series; the band, Details credits and the Needs review chip read the series values.
- **Embedded write-back is still opt-in** (the two existing Preferences settings default off); Edit Series' Advanced tab explains this and has Retry failed, Write anyway and Verify.
- **Provider candidates** exist for Summary, Status, Genre and credits (the joined author/artist list needs a role). Publisher, Imprint and Age rating have proposal fields ready but no provider supplies them today.

## 14. Follow-up: weighted tags, full MangaBaka scrape, reading mode (2026-10-14, built)

Decided in one grilling round (all recommended answers taken). Facts behind it were read from the live public API and the code, not assumed.

**What MangaBaka returns that we did not read:** a `weight` on every tag (`core`, `defining`, `recurrent`, `incidental`, or `unweighted`), including the genre-flagged tags; authors and artists as separate lists; publishers with a `type` (`Original`, `English`, `Other`); `content_rating` (`safe`, `suggestive`, `erotica`, `pornographic`); `original_language`; a 0-100 `rating`; every alternate title with its language; and `merged_with`. The reading-mode side: the reader implements all seven modes, but the Detail screen's toggle only flipped between two and showed Webtoon and the RTL strip as "Left to Right".

**1. Weighted tag editor.** Edit Series Classification tab: Genres and Tags are lists of rows (value, category, weight none / Incidental / Recurrent / Defining / Core, remove), with an add box that takes a pasted comma list. Weights pass to every chapter without an override. Tag comparison now includes category and weight, so changing only a weight is a real edit (it was silently ignored before this fix); a chapter whose tag weight differs from the series' is an exception.

**2. The refresh policy** (`MangaProviderApply`, confirmed local manga series only):

| Tier | Fields | Rule |
|---|---|---|
| Always applied (unless the field is locked) | status, the 0-100 rating (kept as an external rating), alternate titles, tags with their weights | tags are additive: new ones added with the provider's weight and category, an **unset** weight filled, a set weight never replaced, nothing removed; spoiler tags skipped unless "Include spoiler tags" is on |
| Filled only when blank, otherwise a pending candidate (a locked field is always only a candidate) | summary, authors to Writer, artists to Penciller, the `Original`-type publisher, age rating | a candidate already dismissed is not offered again |
| Only ever suggested | reading mode | Korean origin or manhwa suggests Long Strip, Japanese manga suggests Right to Left |

Age rating: safe fills nothing, suggestive is Teen, erotica is Mature 17+, pornographic is X18+. English-edition publishers are not applied (editions are deferred). A series the provider merged into another is followed to the live entry. Everything lands on the series first and then passes to chapters, with the usual one-time snapshot and file-write flag. An unconfirmed manga series and any Comic keep the old behaviour, except that provider weights now fill unset tag weights there too. Sources has an "Accept all" (it skips hand-set fields and the reading-mode suggestion) and the spoiler switch.

**3. Reading mode.** `ReadingModeCatalog` is the single list of the seven modes (names, one-line descriptions, badges) used by the reader's palette and pill, the Detail screen, the manga header badge and Edit Series. The Detail card has a strict picker for all seven. Edit Series has the same field on the Publication tab. A file can only record right-to-left or not, so a manga series' chapters are marked as owing a write only when that part changes; the long-strip modes stay in Paperbunkr.

**Deliberately not done:** the series' start and end dates, the `is_licensed` flag, the provider's links list (no place to store free-form links yet), edition publishers, and a separate preview screen (the existing candidate list is the review step).
