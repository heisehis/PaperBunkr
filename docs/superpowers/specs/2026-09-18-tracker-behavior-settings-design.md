# Tracker behavior settings — Design
*2026-09-18. Follow-up to `2026-09-18-per-tracker-score-and-finish-date-design.md` ("we should also add some stuff into the settings after this"). Modeled on Komikku's Settings → Tracking screen; Komikku semantics verified from `komikku-app/komikku` source (`TrackPreferences.kt`, `SettingsTrackingScreen.kt`, `ReaderViewModel`, `MangaScreenModel`, `SyncChapterProgressWithTrack`), not guessed.*

## 1. What is being built

Five global settings in **Preferences → Connections → "Tracking behavior"** (new group box under the existing Trackers box). All global, no per-series overrides.

| # | Setting | Type | Default | Komikku equivalent |
|---|---|---|---|---|
| 1 | Open the tracker link panel automatically | bool | **on** | "Open track menu on adding to library" (reinterpreted, see §3.1) |
| 2 | Update progress after reading | bool | **on** | `pref_auto_update_manga_sync` |
| 3 | Update progress when marked as read | Always / Ask / Never | **Always** | `pref_auto_update_manga_on_mark_read` (`AutoTrackState`) |
| 4 | Auto sync progress from trackers | bool | **off** | `pref_auto_sync_progress_from_trackers` |
| 5 | Select entries using source metadata | bool | **on** | `pref_resolve_using_source_metadata_key` |

Defaults deviate from Komikku in one place: #4 is **off** (Komikku: on). Reason: a pull rewrites local read state; push is forward-only and already consented to by linking.

**Out of scope (explicit):** failed-push retry queue (deferred to `Paperbunkr-Roadmap.md` backlog; v1 = alert + manual Sync button), scheduled background pull task, book/PDF readers (books are not tracker-linked), per-series overrides, auto-changing reading status, auto-pushing score/finish-date, silent auto-linking.

## 2. Data model

One migration `AddTrackerBehaviorSettings`:
- `AppSettings`: `TrackerAutoOpenLinkPanel` (bool, default true), `TrackerUpdateAfterReading` (bool, true), `TrackerUpdateOnMarkRead` (new enum `TrackerAutoUpdateMode { Always, Ask, Never }`, Always), `TrackerAutoSyncFromTrackers` (bool, false), `TrackerUseSourceMetadata` (bool, true).
- `Series.TrackerPromptShown` (bool, default false) — one-shot flag for setting #1.
- Convention (verified against `AddBehaviorSettingsBatch2`): `HasDefaultValue(...)` in `PaperbunkrDbContext` so the existing singleton row backfills; `AddColumn` migration; `Down` is a deliberate no-op for `AppSettings` columns; migration test in `Paperbunkr.Data.Tests`. Migration tests must not use the up-down-up antipattern.

## 3. Behavior

### 3.1 #1 Auto-open link panel (manga detail screen only)
Fires when a **manga detail** screen loads a series (never comic, never Unknown) and all hold: setting on; `Series.TrackerPromptShown == false`; the series has an `ExternalMediaId` whose provider maps to a `TrackingService`; that service has a **connected account** (both conditions required); the series is not already linked to that service. It picks the first service satisfying all of that, sets `TrackerPromptShown = true` **only after the panel has actually opened and rendered** (fires once per series ever, whether or not the user links) - never on load and never before the conditions are validated, so an exception or an early abort can't burn the series. The pinned candidate needs no network (§3.5), so a search failure still leaves a visible panel and counts as shown. Navigates the Details tab → Linking sub-tab, opens the link panel with that service selected, and runs the search (the source match is pinned, §3.5). If landing on a tab unprompted proves jarring on-screen, fallback is an Activity Center Info alert with a series link — decide at on-screen verification.

### 3.2 #2 Update after reading
Hooked at the comic reader's existing 95% "finished" crossing (`ReaderScreenViewModel.TrackSessionProgress` → `EmitFinishedIfNeeded`, once per session). Runs the push flow (§3.6) for that issue's series if it has connected tracker links. Books/PDF readers untouched.

### 3.3 #3 Update when marked as read
Hooked at every manual mark-as-**read** site (never mark-unread): Detail (`DetailTabsViewModel.MarkIssuesReadState`), Manga detail, Library bulk, Reading list — the five `IssueReadStateResolver.MarkAsRead` call sites, after their existing save. `TrackerSyncResolver.ApplyRemote` (tracker pull) must **not** trigger it (would loop).
- **Always** → push flow. **Never** → nothing.
- **Ask** → persistent actionable toast "Update trackers to chapter N?" (single series) or "Update trackers for M series?" naming up to three of them ("Series A, Series B, +1 more") so the bulk prompt isn't opaque (bulk), actions *Update trackers* / *Dismiss*; *Update* runs the push flow and closes the toast.
- Bulk actions spanning several series = **one** job and **one** toast per action.

### 3.4 #4 Auto sync from trackers
On Detail screen open for a series with connected links, if on: run the pull flow (§3.7), throttled to once per link per 10 minutes, **persisted** in the existing `TrackingLink.LastSyncedAt` column (declared today, never written; no migration needed) so app restarts keep the window. Written after every successful remote read in the pull flow. Detail-open only; no background task.

### 3.5 #5 Source metadata pinning
In `SearchTrackerAsync`, when on and the series has an `ExternalMediaId` for the selected service's provider, a synthetic candidate is pinned first (label "From linked metadata", tier Auto, confidence 1.0; a search hit with the same id is merged into it). Needs no network, so it appears even if search fails. It still uses the existing `TwoStepConfirm` ("writes to your account?") — **no silent link**. `TrackerMatchSample` gains `IsFromLinkedMetadata`. `ExternalMetadataProvider` ↔ `TrackingService` map by enum name for the 7 shared providers (`TrackerProviderMap` helper; Bangumi/Metron/ComicVine have none).

### 3.6 Push flow (used by #2, #3)
Per affected series, for each **connected** link: `GetEntryAsync`; if remote is at/ahead (`TrackerSyncResolver.RemoteWins`) → skip (**no pull here**); else `PushEntryDetailedAsync` with `Status = series.ReadingStatus`, `ChapterProgress = TrackerProgressCalculator.ComputeChapterProgress`, and `UpdateScore`/`UpdateFinishDate` **false** — auto-push can never clobber a rating/date. Same-series pushes are coalesced: if one is in flight, mark dirty and re-run once after it finishes.

**Pacing (protects against 429s on bulk actions):** all automatic push/pull requests run through one in-process serial executor inside `TrackerAutoSyncService` with a per-service minimum spacing between requests (constant table, e.g. AniList ~700 ms to stay under its 90 req/min limit; conservative default ~1 s for the rest). A bulk mark across M series therefore runs sequentially, reporting progress through the job (`Report(done, total)`), not in parallel. A 429/transient error is a normal per-service failure (alert + toast). This is **in-memory pacing only** - the persisted retry queue stays deferred to the backlog (Roadmap).

### 3.7 Pull flow (used by #4)
For each connected link: `GetEntryAsync`; if `RemoteWins` → `ApplyRemote` (marks issues read), record `SeriesActivityLog.TrackerSynced`, and return the newly-read issues so `DetailTabsViewModel` refreshes tiles exactly as the manual Sync does (`SwapReadStateTile`, `_onSelectionChanged`). Never pushes.

## 4. Activity Center (standing rule — verified API, not assumed)

| Event | Job (status-bar indicator + history) | Toast | Alert |
|---|---|---|---|
| Push/pull running | `StartJob(ActivityJobKind.TrackerFetch, …)` (kind exists, currently unused), one job per action across all its series and trackers | — | — |
| Push landed | `Succeed("Synced AniList, MangaBaka to chapter N")` | Success toast, same text | — |
| Pull changed something | `Succeed("Pulled from AniList up to chapter N")` | Success toast | — |
| Nothing to do (remote already ahead / nothing new) | settles "Already up to date" | **none** | — |
| Failure (per service) | `Fail(summary, ex)`; job succeeds if ≥1 tracker succeeded, summary lists failures | Error toast | Warning alert, `DedupeKey = tracker-sync:{seriesId}:{service}`, `ActivityLink(SeriesDetail, seriesId)`, real error from `PushEntryDetailedAsync` in `Detail` |
| Ask prompt | — | actionable toast (§3.3) | — |

Mechanics forced by the verified API:
- Job toast policy is fixed at `StartJob`, but "quiet when nothing changed" is only known afterwards → jobs start with `ActivityToastPolicy.Never` and the service raises its own toasts.
- `MainViewModel.ShowToast/CloseToast` are private and screen VMs only get a title/message callback → add a small public `IToastHost` (wraps show/close, **marshals to the UI thread** — toasts aren't marshalled today; jobs/alerts already are). Modeled on the update-ready toast (`MainViewModel.cs:2036-2048`, capture the `ToastRequest` for its own close command).
- Activity Center is plumbed by constructor (no DI/static): `MainViewModel` builds one `TrackerAutoSyncService(IActivityService, IToastHost, adapter factory, connection check)` and passes it to the reader, Detail/MangaDetail, Library, Reading VMs. The app has no DI container (constructor injection is the convention), so none is introduced. Parameter is optional but defaults to a `NoOpTrackerAutoSyncService` **null object**, not `null` - a required parameter would touch ~380 existing test construction sites. To keep missing wiring from hiding, one wiring test asserts `MainViewModel` hands the real service to every screen VM that needs it. The service is behind an `ITrackerAutoSyncService` interface for that purpose. `IToastHost` **always** dispatches onto `Dispatcher.UIThread` (toasts aren't marshalled today).
- `SeriesDetail` alert links are supported but nothing creates one yet — verify on-screen.

## 5. Structure

- `src/Paperbunkr.App/Services/TrackerAutoSyncService.cs` — §3.2–3.7 orchestration + Activity Center calls. Takes `Func<TrackingService, ITrackerAdapter>` and a connected-check delegate so tests can **inject fake adapters** (closing the standing "no seam to inject a fake tracker adapter" gap).
- Extract the duplicated per-service switches (`TrackerServiceOptions`, `GetTrackerAdapter`, `GetTrackerSearchProvider`, the `isConnected` switch, `PushTrackerDetailedAsync`) from `DetailTabsViewModel` into one `TrackerAdapterFactory` in `Paperbunkr.Data/Tracking` (or App/Services). A missing case in any of the four today silently skips a tracker; the new automatic paths must not inherit that. `DetailTabsViewModel` switches to it.
- `TrackerProviderMap`, `TrackerAutoUpdateMode` (Data).
- `PreferencesScreenViewModel`: five properties following the verified `PromptReviewOnFinish` template (`[ObservableProperty]` → load inside the `_suppressBehaviorApply` block → `partial void OnXChanged` → `PersistBehaviorSetting`); enum exposed as `TrackerUpdateOnMarkReadText` + `TrackerUpdateModeNames` for a strict `SuggestBox` (never `ComboBox`, per the freeze bug), like `ScheduledTaskNotificationLevelText`.
- `ConnectionsSection.axaml`: new `groupBox` (`Tag="connections.trackingBehavior"`) of `pref:SettingsRow` + `ToggleSwitch` (OnContent/OffContent `x:Null`); `PreferenceIndex` entries so "auto sync", "update progress", "track" find it. Consumers read `AppSettings` fresh per event so toggles apply without restart.
- Avalonia guidance: existing idioms only (SettingsRow, ToggleSwitch, strict SuggestBox); no new styling. Run `avalonia-pro-max/review-checklist` (read from disk per CLAUDE.md) before calling the XAML done.

## 6. Testing
- Migration test (defaults backfill, new columns round-trip).
- `PreferencesScreenViewModelTests`: load/persist each setting (template: existing `PromptReviewOnFinish` tests).
- `TrackerAutoSyncServiceTests` with fake adapters + real `ActivityService(dispatch: a => a())` + fake `IToastHost`: push lands / skipped because remote ahead / partial failure (alert dedupe key, toast, job summary) / never sends score or date / coalescing / pull applies and never pushes / throttle.
- Hook tests: mark-read sites call the service only for mark-read (not unread) and never from `ApplyRemote`; Ask shows the actionable toast and Update runs the push; reader 95% crossing fires once.
- Pacing: a bulk push over several series runs sequentially with per-service spacing (fake clock); persisted throttle: second Detail open within 10 minutes (including after a simulated restart) makes no remote call.
- Auto-open: only manga detail, both conditions, `TrackerPromptShown` set once and only after the panel opened. Pinning: pinned candidate present without network, merged with a same-id hit, still requires confirm.
- On-screen verification against real trackers is pending (computer-use/UI-automation need per-session permission).

## 7. Notes / not-in-scope observations
- Observed while researching (unverified, not part of this work): `MetadataWriteBackQueue` is constructed before `MainViewModel.Activity` is assigned (write-back jobs may not reach the status bar) and it calls `_showToast` off the UI thread.
- The retry queue's backlog entry is in `docs/Paperbunkr-Roadmap.md`.
