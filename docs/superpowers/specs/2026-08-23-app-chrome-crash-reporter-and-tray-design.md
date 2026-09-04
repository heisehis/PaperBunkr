# App Chrome: Crash Reporter Dialog + Minimize-to-Tray

**Date:** 2026-08-23
**Status:** Approved, pending implementation

## Context

`docs/Paperbunkr-Roadmap.md`'s Beta backlog lists an "App chrome" line: crash reporter dialog,
minimize-to-tray, external "open with" app associations. File association already shipped as part
of Alpha's Advanced tab, so this spec covers the remaining two pieces: a crash reporter dialog and
minimize-to-tray.

Per the project's standing rule (`CLAUDE.md`), both were checked against real ComicRackCE source
before designing:

- `_reference/ComicRackCE/cYo.Common/Runtime/CrashWatchDog.cs` — hooks
  `AppDomain.UnhandledException` + `Application.ThreadException`, plus a background thread that
  pings the UI thread every second and, if it goes unresponsive for 10s (`lockTestTime`), fires a
  "lock detected" event and attempts to force-break the foreground lock (a WinForms/Win32-specific
  trick).
- `_reference/ComicRackCE/ComicRack/Dialogs/CrashDialog.cs` — on any bark (exception or lock), shows
  a report (program info + exception chain + full thread dump) with a collapsible Details section.
  Buttons: Retry (break the lock, freeze case only), OK (`Application.Restart()`), Cancel
  (`Environment.Exit(1)`), Abort (dismiss, keep running — relies on WinForms'
  `ThreadExceptionEventArgs` swallow-and-continue semantics).
- `_reference/ComicRackCE/ComicRack/MainForm.cs` (tray logic, ~line 4063 `MinimizeToTray()`) —
  `notifyIcon.Visible` toggles on minimize when `Settings.MinimizeToTray` is set; double-click
  restores; a first-time balloon tip explains the behavior with a "don't show again" flag
  (`HiddenMessageBoxes`).

Paperbunkr is Avalonia, not WinForms, so this is a port of *behavior*, not code — see "Platform
differences from CE" below for where the two diverge and why.

**Existing infrastructure this builds on:** `Services/DiagnosticsService.cs` already exists and
already hooks `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`,
and `Dispatcher.UIThread.UnhandledException`, writing a CE-shaped report (program info + exception
chain) to `%AppData%\Paperbunkr\logs\crash-*.log` on every one of them. Its own doc comment states
the problem it solves is "a startup failure produces zero durable evidence," explicitly *not* "the
user needs an in-app crash reporter" — this spec is exactly that follow-on. Nothing about the
existing capture/logging changes; this spec adds a UI layer on top of it.

## Platform differences from CE (and why)

1. **No native balloon tips.** Avalonia's `TrayIcon` (checked against
   `avalonia/12.1.1/lib/net8.0/Avalonia.Controls.xml`) exposes only `Icon`, `ToolTipText`, `Menu`,
   `IsVisible`, and a `Clicked` event/`Command` — no balloon/toast API. The first-time
   minimize-to-tray explanation uses an in-app overlay notice instead (see below).

2. **`Dispatcher.UIThread.UnhandledException` *does* support swallow-and-continue.** The existing
   `DiagnosticsService.cs` comment claims "Avalonia has no equivalent to WinForms' swallow and keep
   running" — checked against `Avalonia.Base.xml` and this is incorrect:
   `Avalonia.Threading.DispatcherUnhandledExceptionEventArgs.Handled` exists and, when set `true`,
   suppresses the crash, exactly mirroring the WinForms `ThreadExceptionEventArgs.Handled` that
   CE's Abort button relies on. This spec uses it to offer a real Continue option (see below) and
   corrects the stale comment.

3. **No forced unblock for a frozen UI thread.** CE's lock-breaking is a Win32-specific trick tied
   to WinForms' message pump. Avalonia has no equivalent, and more fundamentally, Avalonia has a
   single Dispatcher/UI thread per process (unlike WPF's multiple-Dispatcher-thread model) — a
   second Avalonia-rendered window cannot be shown from a background thread while the main
   dispatcher is stuck, because it would need that same stuck dispatcher to render. The
   freeze-watchdog therefore only *detects and reports*; the on-screen notice it shows is a native
   `user32.dll` `MessageBoxW` via P/Invoke, which runs independently of Avalonia's dispatcher and
   is a small, self-contained, Windows-only shim consistent with this project's "Windows-first,
   cross-platform later" scope (`docs/onboarding.md` §1).

## 1. Crash reporter — capture (no new capture code)

`DiagnosticsService`'s three existing hooks stay as-is for logging. What's added:

- After `LogCrash` returns for **`AppDomain.UnhandledException`** or
  **`Dispatcher.UIThread.UnhandledException`** (both currently called with `isTerminating: true`),
  show `CrashReportWindow` **synchronously**, blocking the handler until the user responds — the
  CLR terminates the process the instant an `AppDomain.UnhandledException` handler returns, so this
  must be a blocking modal call (`ShowDialog` awaited synchronously via `Dispatcher.UIThread`, same
  modal semantics as CE's `CrashDialog.Show`).
- **`TaskScheduler.UnobservedTaskException`** stays dialog-free. It's already `isTerminating: false`
  — the app keeps running with no user-visible break — so popping a crash dialog for a silently
  swallowed background task would be a new, unwarranted interruption. It keeps just logging via the
  existing `LogCrash` call.

## 2. Crash reporter — dialog (`CrashReportWindow`)

A plain Avalonia `Window`, **not** the borderless-overlay pattern (`MigrationOverlay`,
`ReadingListPropertiesOverlay`, the Issue Properties overlay) used elsewhere in this app. Those
overlays render inside `MainWindow`'s own visual tree — the exact thing that may be broken when a
crash dialog needs to appear. A crash dialog must not depend on the window that just crashed.

- **Content:** scrollable, read-only text showing the same report already written to the crash log
  file (program info + exception chain), a "Copy to clipboard" button, and a "Save As…" button
  (writes the same content to a user-chosen path via the existing file-picker service).
- **Buttons**, mapped from CE's four (Retry/OK/Cancel/Abort) to what's actually valid on this
  platform:
  - **Restart** — `Process.Start` a new instance of the current executable, then
    `Environment.Exit(0)`.
  - **Exit** — `Environment.Exit(1)`.
  - **Continue** — shown **only** when the source is `Dispatcher.UIThread.UnhandledException`.
    Sets `Handled = true` on the original `DispatcherUnhandledExceptionEventArgs` and closes the
    dialog without exiting. **Not shown** for `AppDomain.UnhandledException` — the CLR terminates
    that regardless of any handler, so offering Continue there would misrepresent what's about to
    happen.

## 3. Freeze watchdog (`FreezeWatchdogService`)

Background thread, same shape as CE's `CrashWatchDog.LockWatcher`: every 1 second (matching CE's
`WatcherTimeSpanMS`), post a no-op through `Dispatcher.UIThread.InvokeAsync` and track the
timestamp of the last response. If unanswered for 10 seconds (CE's own `lockTestTime` default):

1. Log it via the existing `DiagnosticsService.LogCrash("FreezeWatchdog", exception: null,
   isTerminating: false)` path, for postmortem diagnosis.
2. Show a native `MessageBoxW` (P/Invoke, called directly from the watchdog thread — no Avalonia
   dispatcher involved) reading "Paperbunkr isn't responding" with two options: **Wait** (dismiss,
   watchdog keeps polling — if the UI thread was just slow rather than truly stuck, the next
   heartbeat succeeds and nothing further happens) and **Force Exit** (`Environment.Exit(1)`).

No forced unblock is attempted — per the "Platform differences" section above, there's no safe
mechanism for one, and offering a fake "Retry" that can't actually unstick anything would be worse
than not offering it.

## 4. Minimize-to-tray

- New `TrayIconService` wrapping Avalonia's `TrayIcon`, using the existing
  `src/Paperbunkr.App/Assets/paperbunkr.ico`. No new package — `Avalonia.Desktop` 12.1.1 (already
  referenced) includes `TrayIcon`.
- New Preferences → Advanced toggle, "Minimize to tray," **off by default** (matching CE's own
  default), persisted alongside the other Advanced-tab settings.
- **When enabled:**
  - Minimizing the main window hides it and shows the tray icon.
  - The window's own close (X) button **also** hides to tray instead of exiting — a deliberate
    deviation from CE (where only minimizing goes to tray; closing always exits). Chosen because it
    matches the mental model of "minimize to tray" as most users encounter it in other apps today.
  - Double-click on the tray icon, or its context menu's "Restore," brings the window back
    (`IsVisible = true`, `WindowState = Normal`, activate).
  - The tray icon's context menu "Exit" (and the app's existing File→Exit, wherever that lives)
    perform a real shutdown — `IsVisible = false` on the tray icon, then normal application exit.
- **When disabled** (default): minimizing and closing behave exactly as they do today — no tray
  icon is ever created.
- **First-time explanation:** since Avalonia's `TrayIcon` has no balloon-tip API (see "Platform
  differences" above), the first time the window is hidden to tray, the existing overlay pattern
  shows a brief in-app notice ("Paperbunkr is still running in the tray…") with a "don't show
  again" checkbox, persisted as a single boolean setting (functionally equivalent to CE's
  `HiddenMessageBoxes` flag, scoped to just this one message rather than a general-purpose bit
  field, since there's no other suppressible message in this app yet).

## Testing

- `DiagnosticsService`-adjacent: existing tests for `LogCrash`/`LogMilestone` are unaffected — no
  behavior change there.
- New unit coverage: `CrashReportWindow`'s button outcomes (Restart/Exit/Continue availability per
  source), `FreezeWatchdogService`'s heartbeat/timeout logic (with an injectable clock/dispatcher
  stand-in so the test suite doesn't actually wait 10 real seconds), `TrayIconService`'s
  show/hide/restore state transitions, and the Preferences Advanced-tab toggle's persistence
  round-trip.
- On-screen verification (per this project's UI-automation standard): minimize-to-tray toggle on,
  minimize, confirm tray icon appears and window hides; double-click tray icon, confirm restore;
  close (X) with the toggle on, confirm it goes to tray instead of exiting; toggle off, confirm
  close behaves normally again. Crash dialog and freeze-watchdog are harder to trigger safely
  on-screen — verify via a deliberate throwaway exception/`Thread.Sleep` in a debug-only trigger
  rather than corrupting real app state, then remove the trigger before shipping.

## Explicitly not changing

- File association behavior — already shipped, out of scope here.
- `DiagnosticsService`'s existing capture/logging logic and log file format/retention — unchanged,
  only consumed by the new dialog.
- No attempt at CE's `Retry` (break-the-lock) semantics — not portable to Avalonia's single-dispatcher
  model, as explained above.
- No cross-platform tray/notification abstraction — this is Windows-first scope
  (`docs/onboarding.md` §1); the `MessageBoxW` P/Invoke shim and general tray behavior are expected
  to need real per-platform work if/when macOS/Linux support is picked up later.
