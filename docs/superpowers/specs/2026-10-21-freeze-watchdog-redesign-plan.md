# Freeze watchdog redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-21-freeze-watchdog-redesign-design.md*

Already done (2026-10-21, before this plan): `DefaultFreezeThreshold` = 30 s, test
`Tick_WithDefaultThreshold_WaitsThirtySeconds`.

## Step 1: Common-Controls v6 manifest dependency
**Files:** src/Paperbunkr.App/app.manifest (edit)
**What:** add the `Microsoft.Windows.Common-Controls` 6.0.0.0 `<dependency>` so `TaskDialogIndirect` is exported.
**Depends on:** none
**Verify:** build; Step 5's on-screen check shows the TaskDialog, not the fallback box.

## Step 2: FreezeDialog (native)
**Files:** src/Paperbunkr.App/Services/FreezeDialog.cs (new)
**What:** `FreezeDialogResult { KeepWaiting, Close, Recovered }`; `IFreezeDialog { void Show(Func<TimeSpan> elapsed, Action<FreezeDialogResult> onClosed); void RequestClose(); }`;
`NativeFreezeDialog : IFreezeDialog` — own background thread, `TaskDialogIndirect` with custom buttons, `TDF_CALLBACK_TIMER` counter
via `TDM_SET_ELEMENT_TEXT`, footer hyperlink opening `DiagnosticsService.LogDirectory`, `TDM_CLICK_BUTTON` on `RequestClose`;
`EntryPointNotFoundException` → `NativeMessageBox.ShowNotResponding` fallback. Also `FreezeDurationText.Format` (47 s / 1 min 12 s).
**Depends on:** none (Step 1 only for it to render)
**Verify:** build; format unit test; on-screen (Step 5).

## Step 3: Watchdog state machine
**Files:** src/Paperbunkr.App/Services/FreezeWatchdogService.cs (edit), src/Paperbunkr.App.Tests/FreezeWatchdogServiceTests.cs (edit)
**What:** Healthy/Stalled/Frozen per spec §1: stall at 10 s (milestone + stack capture, once per episode, ≤3 per session, off-thread),
freeze at 30 s (LogCrash + dialog), no second dialog while open, Keep waiting re-arms 30 s from the click, Close → log + Exit,
recovery → log duration, close dialog, alert callback only if the episode reached Frozen; debugger and sleep guards. All collaborators injectable.
**Depends on:** Step 2 (interface)
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~FreezeWatchdogServiceTests"` (tests per spec "Testing").

## Step 4: Wiring
**Files:** src/Paperbunkr.App/App.axaml.cs (edit), src/Paperbunkr.App/Models/ActivityLinkKind.cs (edit)
**What:** construct the watchdog with `onRecoveredAfterFreeze` → `mainViewModel.Activity.RaiseAlert(...)` (Open logs = ExternalUrl + log folder,
dedupe key `freeze-watchdog-recovered`); `#if DEBUG` `PAPERBUNKR_SIMULATE_FREEZE` posts one UI-thread sleep 5 s after startup; ExternalUrl doc comment.
**Depends on:** Step 3
**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj`.

## Step 5: On-screen check and records
**Files:** docs/paperbunkr-todo.md (edit)
**What:** user runs a Debug build with `PAPERBUNKR_SIMULATE_FREEZE=45` (then 75 + Keep waiting, then Close) per spec "Testing"; record status in the todo doc.
**Depends on:** Steps 1–4
**Verify:** on screen (needs the user — the app can't be watched from here).
