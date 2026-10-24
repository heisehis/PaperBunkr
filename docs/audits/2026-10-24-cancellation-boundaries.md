# Cancellation and modal-dismissal audit

Date: 2026-10-24
Checkout: HEAD `1167a91` plus the current uncommitted implementation.
Scope: application cancellation boundaries, especially the organizer profile-picker
report and analogous UI commands. This is an audit; application code was not changed.

## Findings, in priority order

### 1. P1 — Closing the scraper results summary skips final bookkeeping

**Location:** `src/Paperbunkr.App/Scraper/ScrapeCoordinator.cs:218`.

The summary is informational, but closing it with X/Escape/scrim cancels the modal
task. The enclosing `catch (OperationCanceledException)` at line 234 returns
`Scrape cancelled.` even though scraping/applying has finished. That bypasses
deleting the unfinished-job record, updating the review queue, flushing decision
history, saving the final targeted undo record and enqueuing file write-back
(lines 251–265). Metadata already saved to the database remains saved.

**Observed:** an isolated scrape which skipped its match reached the summary.
Pressing OK enqueued one write-back; dismissing the same summary enqueued none and
returned `Scrape cancelled.`. This probe verifies the finalization control-flow
gap; it does not claim that an archive was rewritten or that applied metadata was
rolled back. The same return path surrounds applied outcomes.

**Fix:** finalize the completed result before showing the summary, and treat
dismissing this informational dialog as acknowledgement. Keep genuine in-flight
cancellation separate from closing a completed-run report.

### 2. P1 — Dialog commands can throw outside the coordinator's catch

**Location:** `src/Paperbunkr.App/Scraper/ComicVineMatchReviewDialogViewModel.cs:262`
and `:318`.

The dialog's Search command awaits its delegate with only a `finally`; Show issues
awaits its loader without a catch. These commands have their own async relay-command
tasks. The coordinator is awaiting the modal result, not either child command, so
its outer catch cannot handle their exceptions. Normal search request failures are
usually converted to an empty result by SearchAndRank, but credential failures are
deliberately rethrown. The Show issues loader can also propagate provider failures.

**Observed:** executing the real dialog commands with a credential-failing search
and a cancelled issue loader propagated ComicVineException and TaskCanceledException
respectively. The audit awaited ExecuteAsync to inspect the exceptions safely;
normal ICommand execution uses the toolkit's await-and-rethrow path seen in the
user's original report. No live account rejection was triggered.

**Fix:** handle expected cancellation and provider errors within these commands,
restore their busy state, and show actionable status. Credential rejection should
also settle/stop the batch through an explicit result or error channel.

### 3. P2 — Replaced detail searches let their cancellation escape

**Location:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs:1676` and `:2079`.

Both search methods cancel their previous CTS and then catch exceptions only when
`!cts.IsCancellationRequested`. That filter excludes precisely the cancellation
they initiated. A provider honoring the cancelled token can therefore cancel the
UI command rather than end it normally. Tracker searches also have an automatic
entry point at line 641, so not every invocation is serialized by the same command.
Both unconditional finally blocks can clear a newer search's busy flag.

**Observed:** a fake metadata provider cancelled the actual stored search CTS and
returned a cancelled task. SearchMetadataCommand.ExecuteAsync propagated
TaskCanceledException. The tracker branch has the same filter by source inspection;
its live provider was not exercised. Re-entry through a disabled UI button was not
assumed or used in the probe.

**Fix:** explicitly consume OperationCanceledException when that search's own token
was cancelled. Retain ordinary error reporting for timeouts/provider failures and
only reset busy state if that CTS is still the active search. Dispose obsolete CTSs.

### 4. P2 — Cancelling a batch does not release its pending dialog wait

**Location:** `src/Paperbunkr.App/Scraper/ScrapeCoordinator.cs:188` and `:197`;
`src/Paperbunkr.App/Scraper/OrganizeCoordinator.cs:166` and `:183`;
`src/Paperbunkr.App/ViewModels/NativePluginModalHostViewModel.cs:118`.

Scraper review delegates receive the batch token but drop it when showing the modal.
Organizer previews and collision dialogs similarly do not observe their operation's
token. ShowAsync has no token parameter/registration. Cancelling the Activity Center
job (or the scraper header's Cancel button) while it is waiting for a choice does
not itself complete that modal task. The job and heavy-job slot can stay held until
the user also dismisses or answers the dialog.

**Observed:** with a real scraper match review pending, cancelling its supplied token
left the run incomplete and the host open. Dismissing the host subsequently ended
the run. The organizer variant was traced in source, not exercised with file moves.

**Fix:** make the owned modal wait observe operation cancellation, remove/close the
specific cancelled request on the UI dispatcher, and settle it exactly once. Simply
adding Task.WaitAsync(token) would abandon an open/queued modal and is insufficient.

### 5. P2 — Scheduled coordinator cancellation can be recorded as success

**Location:** `src/Paperbunkr.App/Scraper/ScrapeCoordinator.cs:234`;
`src/Paperbunkr.App/Scraper/OrganizeCoordinator.cs:212`;
`src/Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs:263` and `:336`;
`src/Paperbunkr.App/Services/Scheduling/SchedulerService.cs:266`.

The coordinators return a cancellation string after catching OperationCanceledException,
including when an existing scheduler-owned handle was supplied. Their catalog wrappers
then call handle.Succeed(summary), and the scheduler stamps the task as Succeeded.
ActivityService.JobHandle.Succeed does not reinterpret a cancelled token. This can
produce a success record/toast for an interrupted operation and advance its last-run
timestamp instead of preserving cancellation semantics.

**Evidence:** traced across the coordinator, catalog, scheduler and job-handle source;
not exercised as a timed scheduler integration probe.

**Fix:** preserve cancellation as a distinct outcome across coordinator/scheduler
boundaries. Interactive commands may translate it to a friendly message; a borrowed
scheduler handle must let the scheduler take its cancellation branch. Apply the same
principle to credential failures currently returned as ordinary strings.

## Coverage and paths already protected

- Enumerated the application's eight direct shared-native-modal call sites, its
  plugin bridge/settings wrappers, cancellation-task producers, 32 Cancel call
  occurrences and 18 async-void declarations. Counts include control confirmations
  and infrastructure, not 32 independently cancellable background jobs.
- Organizer profile-picker dismissal is caught at OrganizeCoordinator:71; its run
  catches cancellation at :212. This covers the original reported picker path.
- Core confirmation dialogs resolve dismissal to -1 rather than cancel their task;
  the inspected callers check the returned answer.
- Both native plugin-settings modal paths explicitly consume expected cancellation.
- Manual scan/cover/health operations in Preferences, GCD installation, Metron sync,
  acquisition connection tests and the scheduler have explicit cancellation catches.
- Weekly release navigation already catches cancellation and guards its busy reset
  with ReferenceEquals on the active CTS; use that pattern for detail searches.
- ComicVine/Metron and reading-list HTTP adapters distinguish request timeouts from
  cancellation in the inspected request paths. A global rule ignoring every
  TaskCanceledException would hide useful timeout failures.
- Review-queue opening awaits an uncaught modal cancellation, but MainViewModel
  discards that task. A cancelled discarded task is not, by itself, the UI-thread
  await-and-rethrow crash. Observe that task and handle dismissal at its owner as
  follow-up hardening; do not count it as a reproduced instance of the original crash.

## Verification and limits

Ran temporary, isolated xUnit probes against the real coordinator/dialog/search
methods with fake providers, temporary SQLite databases and no live network or file
moves:

`dotnet test src/Paperbunkr.App.Tests --no-restore --filter FullyQualifiedName~CancellationAuditProbeTests -p:WarningLevel=0 --logger "console;verbosity=normal"`

All four observation probes passed: they confirm the current problematic behavior,
not that fixes are present. One probe compares summary OK with dismissal. The
temporary test source was removed from the project after the run; its source and
log are retained under the user's Temp directory as
`paperbunkr-cancellation-audit-probes.cs` and
`paperbunkr-cancellation-audit-probes.log`.

This audit did not click every native dialog, provoke OS drag/drop/WebView2 shutdown
races, cancel every scheduler task, or run the full App suite. Uncaught awaits around
native drag/drop and WebView script calls remain source-level follow-up candidates;
their backend cancellation behavior was not proven here and is not presented as a
confirmed finding.

Recommended repair order: completed-run finalization; child-dialog command errors;
token-aware modal lifecycle; search cancellation/state ownership; scheduler outcome
propagation. Add permanent regression tests asserting the corrected behavior at each
boundary rather than swallowing cancellation globally.
