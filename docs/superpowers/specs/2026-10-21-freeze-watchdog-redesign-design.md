# Freeze watchdog redesign

**Status:** Draft for review
**Date:** 2026-10-21
**Supersedes:** §3 of [2026-08-23-app-chrome-crash-reporter-and-tray-design.md](2026-08-23-app-chrome-crash-reporter-and-tray-design.md)

## Problem

`FreezeWatchdogService` pings the UI thread every second and, after a stall, shows a raw
`MessageBoxW` (`NativeMessageBox.ShowNotResponding`). Its problems:

1. **The threshold was too short.** 10 s fired on long-but-recovering work (big scans, plugin
   warm-up). Already raised to 30 s on 2026-10-21 (`DefaultFreezeThreshold`, pinned by
   `Tick_WithDefaultThreshold_WaitsThirtySeconds`). This spec builds on that.
2. **Confusing buttons.** `MessageBoxW` only offers stock labels, so "Retry" means *keep waiting*
   and "Cancel" means *force quit*.
3. **The dialog never notices recovery.** If the UI thread comes back while the box is up, it stays
   up until clicked.
4. **Re-fire bug.** `Tick` resets `_lastResponse` *before* the (blocking) dialog. If the box sat
   open longer than the threshold, the first tick after "Retry" fires again immediately.
5. **Short stalls leave no trace** now that the threshold is 30 s.
6. **A paused debugger trips it.**
7. **No record after the fact.** Once the app recovers there is nothing in the UI saying it froze.

## Goals

- Two thresholds: a silent **stall** at 10 s (log + managed stack), a user-facing **freeze** at 30 s.
- A Windows TaskDialog with plain buttons (**Keep waiting** / **Close Paperbunkr**), a live
  "Not responding for m:ss" counter, an **Open log folder** link, and a warning that closing may
  lose changes in progress.
- The dialog closes itself when the UI thread answers again.
- After **Keep waiting**, a full 30 s from the click before asking again.
- Log the duration of every stall ≥ 10 s when it ends; after a freeze that showed the dialog, also
  raise an Activity Center alert.
- Silent while a debugger is attached, and across a machine sleep.

## Non-goals

- A user setting for the thresholds (CE has none either).
- Any attempt to unblock the UI thread (Avalonia has no safe equivalent to CE's
  `ThreadUtility.BreakForegroundLock`; unchanged from the 2026-08-23 decision).
- A new forced-quit marker or startup check. Every launch already runs `PRAGMA integrity_check`
  (`DatabaseIntegrityService.CheckIntegrity`, `App.RunStartupSequenceAsync`) and routes a failure to
  `DatabaseRecoveryWindow`, which covers a database damaged by **Close Paperbunkr**.
- An expandable "details" section in the dialog: while the UI thread is wedged there is nothing
  reliable to show beyond the elapsed time.
- Non-Windows platforms (same Windows-first scope as `NativeMessageBox`).

## CE reference check

`_reference/ComicRackCE/cYo.Common/Runtime/CrashWatchDog.cs` polls every 1 s
(`WatcherTimeSpanMS`) with a field default of 10 s (`lockTestTime`). But
`ComicRack/Program.cs:741` starts it as `Diagnostic.StartWatchDog(CrashDialog.OnBark)` with the
default `lockTestTimeSeconds = 0`, and `LockWatcher`'s `now - d > LockTestTime` guard then resets
`LastTimeRunning` on every poll, so **shipped CE never shows a lock dialog**. It also only counts
a lock while the UI thread is in `WaitSleepJoin`, not a busy loop. Two CE ideas are kept:

- `inBark`: no re-fire while the dialog is up (fixes problem 4).
- `now - d > LockTestTime`: if the watcher's own loop gap exceeds the threshold (the machine
  slept, the process was suspended), reset instead of firing.

Everything else (two thresholds, TaskDialog, auto-dismiss, recovery alert) is a deliberate
Paperbunkr addition.

## Approaches

**A. Watchdog keeps ticking; the dialog runs on its own thread (chosen).** The watchdog loop never
blocks on UI. While a freeze is open it keeps pinging; when the UI answers it tells the dialog to
close. The dialog's TDN_TIMER callback only reads shared state (elapsed time, close request), so it
never blocks its own buttons.

**B. Dialog on the watchdog thread, pinging from the TaskDialog timer callback.** Fewer threads,
but each ping waits up to 1 s inside the dialog's message loop, so the buttons lag, and the
ping logic splits between two places.

**C. Keep `MessageBoxW`, fix only the behaviour.** Auto-dismiss is possible (find the window,
`WM_CLOSE`), but the Retry/Cancel labels and the missing timer and link remain. Rejected in round 1
(Q1).

## Design

### 1. States (`FreezeWatchdogService`)

A small state machine driven by `Tick()`, still fully injectable (clock, ping, dialog, sinks):

| State | Enters when | On entry |
|---|---|---|
| **Healthy** | ping answered | if leaving Stalled/Frozen: log `FreezeWatchdog: UI thread recovered after {n}s`; if a dialog was shown this episode, raise the recovery alert (§4) and close any open dialog |
| **Stalled** | no answer for > 10 s (`DefaultStallThreshold`) | `LogMilestone("FreezeWatchdog: UI thread stalled (10s)")`; capture managed stack (§5) |
| **Frozen** | no answer for > 30 s (`DefaultFreezeThreshold`) | `LogCrash("FreezeWatchdog", …)` (as today); open the dialog (§2) |
| **Waiting** | user clicked **Keep waiting** | re-arm: `_frozenSince = clickTime`; return to Frozen only after another full 30 s without an answer |

Rules:
- While a dialog is open (`_dialogOpen`, CE's `inBark`), `Tick` never opens another.
- **Debugger guard:** `Debugger.IsAttached` (injectable `Func<bool>`) counts as an answered ping.
- **Sleep guard:** if the gap since the previous `Tick` is greater than the stall threshold, treat
  it as answered (reset the episode) instead of escalating.
- An *episode* runs from the first missed ping to the next answered ping; the elapsed counter and
  the logged duration are measured from the last answered ping.

### 2. The dialog (`FreezeDialog`, replaces `NativeMessageBox.ShowNotResponding`)

`TaskDialogIndirect` from comctl32 v6, shown on a new short-lived background thread
(`Paperbunkr Freeze Dialog`) so the watchdog keeps ticking.

- Window title: `Paperbunkr`; main icon: warning; topmost (`TDF_POSITION_RELATIVE_TO_WINDOW` not
  used: the owner window is the frozen one).
- Main instruction: **Paperbunkr isn't responding**
- Content: `Not responding for 0:42. It may still finish what it's doing.` The counter updates
  from `TDN_TIMER` (`TDF_CALLBACK_TIMER`, ~200 ms) via `TDM_SET_ELEMENT_TEXT`.
- Buttons (custom, `TDF_USE_COMMAND_LINKS` off): **Keep waiting** (default) and
  **Close Paperbunkr**.
- Footer (warning icon): `Closing now may lose changes in progress. <a>Open log folder</a>`
  (`TDF_ENABLE_HYPERLINKS`; `TDN_HYPERLINK_CLICKED` shell-opens `DiagnosticsService.LogDirectory`
  and leaves the dialog open).
- Auto-close: the timer callback checks a `volatile bool _closeRequested` set by the watchdog on
  recovery and sends `TDM_CLICK_BUTTON` with a private `Recovered` id.

Result enum `FreezeDialogResult { KeepWaiting, Close, Recovered }`, returned to the watchdog
through a callback. **Close** logs `FreezeWatchdog: user chose Close Paperbunkr` then calls
`Environment.Exit(1)` (as today).

The watchdog depends on an `IFreezeDialog` interface (`Show(Func<TimeSpan> elapsed,
Action<FreezeDialogResult> onClosed)` + `RequestClose()`) so tests use a fake.

**Manifest.** `TaskDialogIndirect` is only exported by comctl32 v6, which needs a
`Microsoft.Windows.Common-Controls` 6.0.0.0 `<dependency>` in `src/Paperbunkr.App/app.manifest`.
It affects only native Win32 common controls (Avalonia draws its own; file pickers go through
`IFileDialog`); the other `MessageBoxW` uses get themed buttons, nothing else changes.
**Fallback:** if `TaskDialogIndirect` throws `EntryPointNotFoundException` (manifest stripped,
unusual host), fall back to today's `MessageBoxW` with the same meaning; auto-close and the timer
are then unavailable.

**Design language.** This is a native Windows dialog drawn by the OS while the Avalonia UI thread
is wedged, so the `paperbunkr-design-language` tokens and `DialogCard` cannot apply. The wording
follows the app's tone; the look is Windows' own.

### 3. Close Paperbunkr

Unchanged mechanism (`Environment.Exit(1)`); the dialog now warns about lost changes. A damaged
database on the next launch is caught by the existing startup `PRAGMA integrity_check` →
`DatabaseRecoveryWindow` path.

### 4. Recovery alert

Raised only when the episode reached Frozen (the dialog was shown), on recovery, through an
injected `Action<TimeSpan>` that `App` wires to `mainViewModel.Activity.RaiseAlert` (thread-safe;
`ActivityService` dispatches internally):

- Severity: Warning
- Title: `Paperbunkr stopped responding for 47 s` (`1 min 12 s` past 60 s)
- Detail: `It recovered on its own. A log of what it was doing was saved in case it happens again.`
- Action: **Open logs** → `ActivityLinkKind.ExternalUrl` with `DiagnosticsService.LogDirectory`
  as payload (its handler shell-opens with `UseShellExecute = true`, which opens a folder in
  Explorer; update the enum's doc comment to say "URL or local path").
- `DedupeKey = "freeze-watchdog-recovered"`, so repeated freezes refresh one alert.

Episodes that stop at Stalled only log; no alert.

### 5. Managed stack capture

`TryCaptureManagedStack` (out-of-process `dotnet-stack report`) moves from Frozen to Stalled, so
the trace shows what the UI thread was doing at 10 s. It runs on its own background task (it can
take up to 30 s) so it never delays reaching Frozen. Limit: once per episode, at most 3 per
session (today: once per session).

### 6. Wiring

`App.RunStartupSequenceAsync` replaces `new FreezeWatchdogService().Start()` with a construction
that passes `onRecoveredAfterFreeze: d => mainViewModel.Activity.RaiseAlert(...)`. Everything else
keeps its defaults. `NativeMessageBox.ShowNotResponding` is kept only as the fallback in §2.

### 7. Manual trigger for on-screen checks

`#if DEBUG`: if the environment variable `PAPERBUNKR_SIMULATE_FREEZE` holds a number of seconds,
`App` posts one `Thread.Sleep(n * 1000)` to the UI thread 5 s after startup completes. Not in
release builds and not a user feature.

## Testing

`FreezeWatchdogServiceTests` (fake clock, ping, dialog, sinks; no real threads):

- Stall at > 10 s: milestone logged once, stack capture requested once, no dialog.
- Freeze at > 30 s: dialog shown once; no second dialog while it is open, however long.
- Recovery while open: `RequestClose` called, recovery alert raised with the right duration.
- Keep waiting at t: no dialog before t + 30 s; dialog again just after.
- Recovery after Stalled only: duration logged, no alert.
- Debugger attached: never stalls or freezes.
- Tick gap > 10 s (sleep): episode reset, no stall.
- Stack capture: once per episode, never more than 3 per session.
- Existing tests adapted to the new constructor (threshold semantics, exception swallowing),
  except `Tick_AfterFiring_WaitsAFullThresholdBeforeRefiring`, whose "re-fire a full threshold
  after firing" rule is replaced by the Keep-waiting test above.

Duration formatting (`47 s`, `1 min 12 s`) gets a small pure-function test.

The TaskDialog P/Invoke itself is not unit-tested (same "thin native wrapper" precedent as
`NativeMessageBox`); it is checked on screen with §7 in a Debug build:
`PAPERBUNKR_SIMULATE_FREEZE=45` → silent at 10 s (log + stack file), dialog at 30 s with a live
counter, auto-close at 45 s, Activity Center alert with a working **Open logs** button; then once
with **Keep waiting** (`=75`) and once with **Close Paperbunkr**.

## Files

- `src/Paperbunkr.App/Services/FreezeWatchdogService.cs`: state machine, guards, wiring hooks.
- `src/Paperbunkr.App/Services/FreezeDialog.cs` (new): `IFreezeDialog`, `FreezeDialogResult`,
  TaskDialog P/Invoke, `MessageBoxW` fallback.
- `src/Paperbunkr.App/Services/NativeMessageBox.cs`: kept as the fallback.
- `src/Paperbunkr.App/app.manifest`: Common-Controls v6 dependency.
- `src/Paperbunkr.App/App.axaml.cs`: construction with the recovery-alert callback; Debug-only §7.
- `src/Paperbunkr.App/Models/ActivityLinkKind.cs`: doc comment for `ExternalUrl`.
- `src/Paperbunkr.App.Tests/FreezeWatchdogServiceTests.cs`: tests above.
- `docs/paperbunkr-todo.md`: record the change once built and checked.
