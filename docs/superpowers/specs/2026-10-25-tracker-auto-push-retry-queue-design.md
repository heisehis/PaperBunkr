# Tracker auto-push offline and retry queue

Date: 2026-10-25. Status: approved design; written spec awaiting user review.

Roadmap item 2: persist failed automatic tracker progress pushes and retry safely while PaperBunkr is running. The existing behavior (Activity Center warning plus manual Sync) remains the fallback for failures that are not safe to retry.

## 1. Scope and CE check

The queue covers automatic progress synchronization triggered by finishing a comic in the reader or marking issues as read, including a push that cannot first verify remote progress because the lookup failed transiently. Manual “Sync with Trackers” failures do not create queue entries.

The ComicRack CE reference has no tracker sync or tracker retry queue behavior. This is a PaperBunkr-specific feature; no CE retry behavior needs to be preserved.

In scope:

- Durable, coalesced retry state for one linked tracker entry at a time.
- Retrying transient tracker lookup and push failures while the app is open.
- Activity Center status and controls for queued retries.
- Clearing queue state after successful manual sync, unlink, or a change to the linked remote target.

Out of scope: retries for user-initiated manual sync failures, OS background execution while PaperBunkr is closed, scheduled pull retries, score/finish-date pushes, and changes to two-way sync conflict policy.

## 2. Existing behavior and constraints

- `TrackerAutoSyncService` currently serializes auto-sync calls, paces requests per service, coalesces concurrent calls by series in memory, reads remote state before pushing, and reports failures through the Activity Center and a toast.
- Its same-series `_inFlight`/`_dirty` state does not survive process exit and does not coalesce durable work by tracker link.
- `ITrackerAdapter.GetEntryAsync` returns `null` for both an absent remote entry and some lookup failures. The current auto-push path treats this as unverifiable and does not push, which is safe but cannot distinguish offline lookup failure from an absent entry.
- `ITrackerDetailedPush` returns a success flag and error text. Adapters already receive HTTP responses and catch network errors, so they can report typed transient/permanent outcomes without parsing user-facing error strings in the queue.
- `TrackingLink.LastSyncedAt` is used for pull throttling, while `LastSyncedIssueNumber` is unused. Retry state belongs in a separate record rather than repurposing these sync markers.
- Activity Center alerts are in-memory and have only a typed navigation action. Durable queue status therefore needs a dedicated Activity Center projection/action path rather than relying on alerts as the source of truth.
- Tracker links are removed through Unlink and can be retargeted through the explicit link flow, which upserts by series/service. A retry must never follow a changed `ExternalId` to a different remote entry.

## 3. User-approved behavior

### 3.1 Durable queue and coalescing

- Add a dedicated `TrackerPushRetry` record with a unique foreign key to `TrackingLink` and cascade cleanup when the link is deleted.
- Persist the target `ExternalId` snapshot, desired `ReadingStatus`, highest chapter progress, scheduled retry attempts used, next due UTC time, last attempt/error, and pending/exhausted state.
- Before every retry, confirm the link still exists and still targets the saved remote `ExternalId`. Delete stale queue state when a link is removed or retargeted.
- Keep one queue item per link. When a new automatic event advances the desired chapter, update the target and reset the five-retry budget. Never lower the queued chapter. Same-target updates keep the existing due time and retry count while updating the latest status.
- A successful automatic push removes the queued item. A permanent failure stops automatic retries and leaves the item exhausted for user action.

### 3.2 Retry policy and lifecycle

- Make the initial automatic synchronization attempt immediately. If a transient lookup or push fails, schedule five additional attempts with exponential delays of 1, 2, 4, 8, and 16 minutes. The initial attempt does not count against the five retries.
- Retry known transient network/timeouts, HTTP 429, and HTTP 5xx results. Respect a provider `Retry-After` value when it is later than the scheduled retry time.
- Do not automatically retry confirmed missing remote entries, auth/permission failures, invalid IDs/requests, or other permanent outcomes. Keep those in the existing failure feedback.
- Replace nullable/boolean-only lookup and push outcomes with typed results sufficient to distinguish success, confirmed absence, transient failure, and permanent failure. Retry transient lookup failures as well as push failures; do not blindly push when remote progress cannot be verified.
- Pause due work without consuming attempts while the relevant automatic progress setting is disabled or the tracker is disconnected. Resume after re-enabling/reconnecting.
- Persist absolute due times. If PaperBunkr is closed when a retry becomes due, elapsed time counts; resume overdue work on next launch when enabled and connected. While the app is open, wake the runner on network recovery and periodically check for due records. No OS background worker is added.
- After the fifth scheduled retry fails, retain an exhausted record and its last error until it is successfully retried or dismissed.

### 3.3 Activity Center and manual sync

- Show one dedicated Activity Center retry row per tracker link, backed directly by the durable queue. Display the series, tracker, latest queued chapter, pending/exhausted state, next retry or last error, and **Retry now**.
- **Retry now** bypasses the due time but does not consume a scheduled attempt. On success, clear the row. A transient failure preserves the scheduled retry count and schedule; if already exhausted, it stays exhausted. A permanent failure stops automatic retry and leaves an exhausted, actionable row.
- Show **Dismiss** after scheduled retries are exhausted. Dismissing removes that link's durable queue row.
- A successful manual “Sync with Trackers” operation, whether it pushes or pulls remote progress, clears any older queued work for that link. Manual failures keep any existing queued item unchanged and do not create new retry work.
- Each scheduled retry attempt is recorded through the existing Activity Center job/history mechanism.

## 4. Recommended implementation shape

Use a dedicated queue entity and keep retry execution in the existing `TrackerAutoSyncService`. This reuses the established serial executor and service pacing while keeping queue persistence separate from `TrackingLink`'s pull state. A separate retry service would isolate lifecycle more but require extra coordination with the current auto and manual sync paths; retry columns on `TrackingLink` would couple queue lifetime and semantics to connection metadata.

`TrackerAutoSyncService` will own start/stop, due-item scans, network-availability wakeups, and per-link retry execution. `App.axaml.cs` will start it once after `MainViewModel` construction and stop it on desktop exit, following other app-owned background services. The existing two-way manual sync path will notify the queue service per link after successful push/pull.

The Activity Center will project queue records as dedicated rows so pending/exhausted state survives restarts independently from session-only alerts. Its row commands will use the queue service's retry and dismiss operations. Pending rows cannot be dismissed; exhausted rows can.

## 5. Failure handling and safety

- A network/HTTP transient failure during either remote lookup or push schedules a retry; remote progress is always checked again before the next push.
- A confirmed missing remote entry or permanent failure is reported and never converted into a blind push.
- Disabled preferences and disconnected tracker credentials pause the retry counter. Connectivity recovery or application startup resumes due work; retries never run before their persisted due time (except explicit Retry now).
- Unlinking or changing the remote target discards old-target retry state. A retry never uses a queued progress value against a new remote ID.
- Queue upsert, coalescing, attempt advancement, success deletion, and dismissal are persisted transactionally. Network calls happen outside database transactions.
- If a scheduled retry is interrupted after the request reaches the provider but before local success is recorded, the persisted retry row may cause the same progress/status to be sent again on restart. Tracker progress writes are idempotent assignments; every retry re-reads remote state and skips if the remote is already ahead.
- Activity Center queue rows are derived from persisted records; dismissing an exhausted row deletes its record. Existing transient alerts and Activity job history retain their current behavior.

## 6. Verification

Data and App verification should cover:

- Migration creation and round-trip persistence of queue rows, unique link ownership, cascade cleanup, pending/exhausted status, retry timestamps, target ID, and latest progress/status.
- Typed lookup/push classification for network/timeouts, 429/5xx, auth/permission/invalid-target responses, confirmed missing entries, and successful responses across supported tracker adapters.
- Initial automatic failures, scheduled delays and five-retry exhaustion, startup recovery for overdue records, network wakeup, and no attempt before due time.
- Link-key coalescing, higher-progress reset, no progress regression, latest status updates, and same-target events preserving the retry schedule.
- Pause/resume for settings and connection changes; cleanup on unlink/retarget; remote-ahead recheck before push.
- Manual success clearing only the corresponding queue item; manual failures creating no new item; Retry now not consuming scheduled attempts; exhausted dismissal removing durable state.
- Activity Center rows/actions, including persisted state projection after restart and keyboard/accessibility behavior.

Finish with focused Data/App verification, the main `Paperbunkr.App` Debug build, and `git diff --check`. Run the UI/Avalonia review checklist for Activity Center changes and verify the actual screen before calling the UI complete.

## 7. Self-review

- Scope is limited to roadmap item 2; tracker behavior settings, pull synchronization, and score/finish-date behavior are unchanged.
- Confirmed-missing and permanent lookup errors are distinct from transient lookup failures, resolving the existing nullable lookup contract's offline ambiguity.
- Retry schedule counts mean five retries after one initial automatic attempt; a manual Retry now does not consume that budget.
- Persisted queue records, not transient `ActivityAlert` objects, are the source of truth for Activity Center retry status.
