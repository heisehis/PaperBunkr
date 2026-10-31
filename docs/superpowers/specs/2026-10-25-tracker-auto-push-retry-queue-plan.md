# Tracker auto-push offline and retry queue — Implementation Plan

Implements: [2026-10-25-tracker-auto-push-retry-queue-design.md](2026-10-25-tracker-auto-push-retry-queue-design.md)

## Step 1: Typed lookup and push outcomes

**Files:** `src/Paperbunkr.Data/Tracking/ITrackerAdapter.cs`, `ITrackerDetailedPush.cs`, `TrackerAdapterFactory.cs`, a new typed outcome model under `src/Paperbunkr.Data/Tracking/`, all eight adapters under `src/Paperbunkr.Data/Tracking/Adapters/`, `src/Paperbunkr.App/Services/TrackerAutoSyncService.cs`, `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs`, and affected existing adapter/service test fakes.

**What:** Replace nullable lookup and bool-plus-string push outcomes with typed success, confirmed-missing, retryable, and permanent outcomes, preserving provider error details and later `Retry-After` values. Map timeouts/network errors, 408, 429, and 5xx to retryable; auth/permission/invalid-target responses to permanent; distinguish a confirmed missing remote entry from lookup failure. Keep manual two-way sync's current conflict behavior; the auto-push path must not push after an unverifiable lookup.

**Depends on:** none.

**Verify:** Existing adapter tests and auto-sync tests are the affected compile/behavior surfaces; inspect classification for every supported adapter. No live tracker calls.

## Step 2: Durable per-link queue and target cleanup

**Files:** new `src/Paperbunkr.Data/Entities/TrackerPushRetry.cs`, `src/Paperbunkr.Data/Tracking/TrackerPushRetryStore.cs`, `src/Paperbunkr.Data/Migrations/AddTrackerPushRetryQueue.cs` plus its designer/model snapshot updates, `src/Paperbunkr.Data/PaperbunkrDbContext.cs`, `src/Paperbunkr.Data/Tracking/TrackerLinkResolver.cs`, and focused existing Data migration/queue test fixtures if needed for compilation.

**What:** Persist one unique retry row per `TrackingLink`, including target ID snapshot, desired status/highest chapter, attempt count, next due UTC time, last attempt/error, and pending/exhausted state. Add transactional upsert/coalesce, due lookup, attempt update, success/dismiss deletion, and cascade cleanup. Clear queued work when the link is retargeted; verify its saved target before retrying.

**Depends on:** Step 1's retry outcome model.

**Verify:** Existing migration tests and model build surfaces. No migration round-trip tests will be run during this implementation.

## Step 3: Retry runner and sync-path integration

**Files:** `src/Paperbunkr.App/Services/TrackerAutoSyncService.cs`, `src/Paperbunkr.App/Services/ITrackerAutoSyncService.cs`, `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs`, `src/Paperbunkr.App/ViewModels/MainViewModel.cs`, and `src/Paperbunkr.App/App.axaml.cs`.

**What:** On a transient initial auto-sync lookup/push failure, persist the current target and schedule five retries at 1/2/4/8/16 minutes. Coalesce by tracker link, retain the highest chapter, reset the retry budget only when progress advances, pause while the relevant auto-push setting or tracker connection is off, resume due work at startup/network recovery/periodic check, and keep the existing per-service serial pacing. A manual successful push or pull clears that link's queue; manual failure creates no new queue item and leaves existing queued work unchanged. Retry now bypasses due time without consuming a scheduled attempt. Start/stop the runner with the application lifecycle; perform network calls outside database transactions.

**Depends on:** Steps 1 and 2.

**Verify:** Existing `TrackerAutoSyncServiceTests` and `TrackerAutoSyncWiringTests` identify the current fake seams and lifecycle wiring. The user requested the main Debug build; focused tests will not be run in this implementation turn.

## Step 4: Activity Center retry rows and actions

**Files:** `src/Paperbunkr.App/ViewModels/ActivityCenterViewModel.cs`, a new tracker retry row view model in `src/Paperbunkr.App/ViewModels/`, `src/Paperbunkr.App/Views/ActivityDrawerView.axaml`, relevant shared row template resources if needed, and `src/Paperbunkr.App.Tests/ActivityCenterViewModelTests.cs` only for keeping existing constructors/fakes compiling.

**What:** Project persisted retry rows in the Activity Center, showing series, tracker, latest queued chapter, pending/exhausted state, next due time or last error, and Retry now. Pending items cannot be dismissed; exhausted items can be dismissed to delete their durable queue record. Keep the row list sourced from the queue, not from session-only alerts. Use themed resources and accessible labels/focus order for all controls.

**Depends on:** Step 3's queue read/actions.

**Verify:** Existing Activity Center tests are the affected compile surface. Run the Avalonia review checklist and inspect the actual Debug app screen after implementation; no test suite will be run during this turn.

## Step 5: Roadmap and delivery check

**Files:** `docs/Paperbunkr-Roadmap.md`, `docs/paperbunkr-todo.md`, approved spec and this plan.

**What:** Update roadmap item 2 with shipped scope and actual validation, add an as-built note to the spec and plan, run `git diff --check`, and build `src/Paperbunkr.App/Paperbunkr.App.csproj -c Debug` after the final code change.

**Depends on:** Steps 1–4.

**Verify:** Record the Debug build result and UI review evidence. Do not claim tests or native accessibility coverage unless run.

## As built (2026-10-25)

- Steps 1–5 implemented in the working tree. Main Debug app build succeeded with 0 errors; `git diff --check` had no whitespace errors.
- No test suite was run. Native screen-reader and responsive-size visual checks remain outstanding.
