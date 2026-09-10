# Bootstrap-Crash Sentinel + Safe-Mode Auto-Recovery — Design Spec

*Date: 2026-09-10. Scope: give Paperbunkr an automatic recovery path when it crashes during
Avalonia/CLR **bootstrap** — before the first window is ever shown — instead of becoming
permanently unlaunchable with no in-app feedback. A disk sentinel written before
`StartWithClassicDesktopLifetime` and cleared once the splash renders lets the next launch detect
"the previous start never completed" and respond: first by forcing software rendering, then, if
that also fails, by showing a native message box that points at the logs. Windows-only concerns,
though the sentinel logic itself is platform-neutral. Does **not** add: a settings UI (the
existing Preferences → Advanced graphics controls are the fix destination), post-window freeze
detection (`FreezeWatchdogService` already owns that), R2R-specific relaunch handling (out of
scope — see §7), or crash telemetry/upload.*

## 1. Motivation

`0.3.0-beta` shipped with `PublishReadyToRun=true`. On at least one user's machine the CI-built
crossgen2 images faulted during CLR assembly load — a native access violation, before Avalonia
created any window. The symptom: setup completes, the app never opens, and
`%AppData%\Paperbunkr\logs\startup.log` ends at the `"Render backend requested"` milestone
(logged in `Program.Main` immediately before `BuildAvaloniaApp(...).StartWithClassicDesktopLifetime`)
with nothing after it — no Avalonia trace output, no `crash-*.log` (corrupted-state exceptions
are not delivered to managed handlers), no `"Process exiting"` from `Main`'s `finally`. The
`0.3.1-beta` hotfix reverts the R2R flag, but the underlying fragility remains:

- **`RenderBackend.Gpu`** resolves to a `Win32RenderingMode` chain with **no `Software` rung**
  (`GraphicsBootstrap.ToRenderingModes`, per
  `docs/superpowers/specs/2026-08-27-hardware-accelerated-rendering-design.md` §4). A GPU that
  fails hard leaves the app unable to start, and that spec explicitly documents the only recovery
  as "set `PAPERBUNKR_RENDER=software`, or edit `graphics.json`, or use the (then-hypothetical)
  Advanced-tab UI."
- Any future bad graphics driver, ANGLE/WGL regression, or re-enabled AOT step reproduces the
  same dead end.

A user in this state has no path back that doesn't involve editing a JSON file by hand or setting
an environment variable. This spec closes that.

## 2. Goals and non-goals

**Goals:**

1. A crash anywhere between `StartWithClassicDesktopLifetime` and the splash window rendering
   causes the **next** launch to retry with software rendering forced on, automatically.
2. If that software-rendering launch *also* crashes in bootstrap, the launch after it shows a
   **native** (non-Avalonia) message box explaining the app can't start and opens the log folder.
3. Once a launch succeeds in safe mode, the user sees a **dismissible** in-app notice with a link
   to the graphics settings, and the *following* launch retries the normal (hardware) path — a
   one-off glitch must not permanently downgrade rendering.
4. A fresh install or a version upgrade always gets one clean normal-path attempt first.
5. None of this machinery can itself prevent the app from starting (every disk operation is
   best-effort, failures degrade to today's behavior).

**Non-goals (deferred or owned elsewhere):**

- **Settings UI** — Preferences → Advanced already has the `RenderingBackend` / `PreferNativeOpenGl`
  controls (`Views/Preferences/AdvancedSection.axaml`). The safe-mode notice links there.
- **Post-window hangs** — `FreezeWatchdogService` (started at the end of `RunStartupSequenceAsync`)
  already detects a wedged UI thread after the window exists. The sentinel is cleared before that
  service starts; the two never overlap.
- **R2R-specific relaunch** (`Process.Start` with `DOTNET_ReadyToRun=0` in the environment) —
  `0.3.1-beta` turned R2R off. Re-adding it later should come with this stage; not before. See §7.
- **"Keep software rendering" as a one-click action on the notice** — the notice links to the
  setting; the user flips it there. Avoids a bespoke `ActivityLinkKind` / command wiring for a
  rare path.
- **Crash telemetry / dump upload.**

## 3. Approaches considered

**A. Disk sentinel (chosen).** Write a small state file before Avalonia starts; clear it when the
splash renders; branch on its presence next launch. The only approach that survives a native
access violation, because it depends on nothing in-process after the crash.

**B. `try/catch` around `StartWithClassicDesktopLifetime` with a software-mode retry in the same
process.** Does not fire for the actual failure mode — an access violation / `FailFast` is not a
catchable managed exception in modern .NET. Would help only the (rarer) case where GPU init
throws a normal exception, which Avalonia's own fallback chain already handles.

**C. Windows Error Reporting local dumps / Restart Manager.** Diagnostic only; neither provides a
recovery path, and both require registry/manifest configuration users won't have.

## 4. Component: `BootstrapSentinel`

New class, `src/Paperbunkr.App/Services/BootstrapSentinel.cs`. Static, no instance state, every
public method wrapped so it never throws into `Program.Main`. Path-overridable for tests, mirroring
`GraphicsBootstrap` / `DiagnosticsService`.

```csharp
namespace Paperbunkr.App.Services;

public enum BootstrapDecision { Normal, SafeMode, GiveUp }

public static class BootstrapSentinel
{
    /// <summary>Test-only redirect for <see cref="StatePath"/>. Never set outside a test.</summary>
    internal static string? StatePathOverride { get; set; }

    public static string StatePath => StatePathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Paperbunkr", "bootstrap.state");

    /// <summary>
    /// Read the sentinel and decide how this launch should proceed. Never throws — any I/O or
    /// parse failure resolves to <see cref="BootstrapDecision.Normal"/>.
    /// </summary>
    public static BootstrapDecision Evaluate(Version currentVersion);

    /// <summary>
    /// Record that a bootstrap attempt is starting: increment <c>attempts</c> (or create the file
    /// at <c>attempts = 1</c>), stamp <c>appVersion</c> and, on first failure, <c>firstFailUtc</c>.
    /// Called in <c>Program.Main</c> immediately before <c>StartWithClassicDesktopLifetime</c>.
    /// Best-effort; a write failure is logged and ignored.
    /// </summary>
    public static void Record(Version currentVersion);

    /// <summary>
    /// Delete the sentinel — bootstrap reached a rendered window. Called once, immediately after
    /// <c>splash.Show()</c> returns in <c>App.RunDesktopStartupAsync</c>. Best-effort.
    /// </summary>
    public static void Clear();

    /// <summary>
    /// Delete the sentinel after the <see cref="BootstrapDecision.GiveUp"/> notice has been shown,
    /// so a later manual launch (post driver-fix / reinstall) starts clean at
    /// <see cref="BootstrapDecision.Normal"/> instead of looping straight back to give-up.
    /// Behaviourally identical to <see cref="Clear"/>; named separately for intent and logging.
    /// </summary>
    public static void ResetAfterGiveUp();
}
```

### 4.1 State file — `%AppData%\Paperbunkr\bootstrap.state`

```json
{ "attempts": 1, "firstFailUtc": "2026-09-10T21:48:00Z", "appVersion": "0.3.1.0" }
```

- `attempts` — incremented by every `Record`. A normal launch writes `1`; a crash before `Clear`
  leaves `1` on disk; the next `Record` makes it `2`; and so on.
- `appVersion` — `ReleaseVersion.Current.ToString()` (four-part, e.g. `"0.3.1.0"`). Used only to
  reset on a version change.
- `firstFailUtc` — UTC of the first `Record` that found an existing file (i.e. the first detected
  failure). Diagnostic only in v1; written so a later version can add an age-based reset without a
  format change.

Serialized with the same `JsonSerializerOptions` shape `GraphicsBootstrap` uses (camelCase,
indented).

### 4.2 `Evaluate` logic

```
file missing / unreadable / invalid JSON      -> Normal
parsed.appVersion != currentVersion.ToString() -> Normal   (fresh install or upgrade: clean slate)
parsed.attempts <= 1                            -> SafeMode
parsed.attempts >= 2                            -> GiveUp
```

Rationale for the thresholds: `attempts == 1` means exactly one prior launch wrote the sentinel
and never cleared it → one crash → try safe mode. `attempts >= 2` means the safe-mode launch
*also* failed to clear → software rendering didn't help → stop trying to start Avalonia.

### 4.3 `Record` details

- If the file is absent: write `{ attempts: 1, appVersion: <current>, firstFailUtc: null }`.
- If present and `appVersion` matches: `attempts++`; set `firstFailUtc` to now if still null.
- If present and `appVersion` differs: overwrite as if absent (`attempts: 1`) — the upgrade reset
  also happens here, so `Evaluate` and `Record` agree.
- All writes via `File.WriteAllText` inside the same `try/catch` pattern as
  `DiagnosticsService.LogCrash`. A failure writes a `DiagnosticsService.LogMilestone` breadcrumb
  and returns.

### 4.4 `Clear` / `ResetAfterGiveUp` details

`File.Delete(StatePath)` inside try/catch. Missing file is not an error. Each logs its own
milestone (`"Bootstrap sentinel cleared."` vs `"Bootstrap give-up: … reset sentinel."`).

## 5. Wiring

### 5.1 `Program.Main`

Inserted after `GraphicsBootstrap.Resolve()` and its milestone log, before the `try` that calls
`BuildAvaloniaApp`:

```csharp
var decision = BootstrapSentinel.Evaluate(ReleaseVersion.Current);
DiagnosticsService.LogMilestone($"Bootstrap sentinel decision: {decision}");

if (decision == BootstrapDecision.GiveUp)
{
    SafeModeNotice.ShowGiveUpMessageBox(DiagnosticsService.LogDirectory);
    BootstrapSentinel.ResetAfterGiveUp();   // see 5.4
    return;
}

if (decision == BootstrapDecision.SafeMode)
{
    graphics = graphics with { Backend = RenderBackend.Software };
    SafeModeState.Active = true;
    DiagnosticsService.LogMilestone("Safe mode: forcing Software rendering for this launch.");
}

BootstrapSentinel.Record(ReleaseVersion.Current);

try
{
    BuildAvaloniaApp(graphics).StartWithClassicDesktopLifetime(args);
}
finally
{
    DiagnosticsService.LogMilestone("Process exiting.");
}
```

Placement notes:

- **After** the `--register-file-associations` / `--unregister-file-associations` early return, so
  the headless installer path never writes a sentinel.
- **After** `GraphicsBootstrap.Resolve()` so the `graphics` value exists to override. The override
  is a `with`-expression on the existing `GraphicsConfig` record; `ToRenderingModes` already maps
  `Software` to `[Win32RenderingMode.Software]`.
- The `PAPERBUNKR_RENDER` env override still wins for the *normal* path (it's applied inside
  `Resolve()`); safe mode overrides everything because it runs after.

### 5.2 `SafeModeState`

Trivial holder, `src/Paperbunkr.App/Services/SafeModeState.cs`:

```csharp
public static class SafeModeState
{
    /// <summary>Set by Program.Main when this launch is a post-crash software-rendering retry.
    /// Read once by App.RunStartupSequenceAsync to raise the user-facing notice.</summary>
    public static bool Active { get; set; }
}
```

A static is acceptable here for the same reason `GraphicsBootstrap`'s cache path is: single
process, set once before Avalonia starts, read once during startup, never mutated afterward.

### 5.3 `App` — clear point and notice

In `App.RunDesktopStartupAsync`, immediately after `splash.Show()`:

```csharp
var splash = new SplashWindow { DataContext = splashViewModel };
splash.Show();
BootstrapSentinel.Clear();          // the render stack that crashes has come up
var splashShownAtUtc = DateTime.UtcNow;
```

`splash.Show()` returning proves: Avalonia platform init, Skia/ANGLE/WGL device creation, XAML
load of `App.axaml` + every merged dictionary, and a top-level window reaching the compositor.
That is precisely the span where the observed crashes occur. Clearing here (rather than after
`MainWindow.Show()` or at end of startup) means a force-quit during the slow DB-integrity /
migration phase is **not** misread as a bootstrap crash — and that phase already has its own
`DatabaseRecoveryWindow` recovery flow.

In `App.RunStartupSequenceAsync`, after `mainWindow.Show()` and the splash hand-off, alongside the
existing first-look / What's-New logic:

```csharp
if (SafeModeState.Active)
{
    mainViewModel.Activity.RaiseAlert(new ActivityAlert
    {
        Severity   = ActivityAlertSeverity.Warning,
        Title      = "Running in safe mode",
        Detail     = "Paperbunkr didn't finish starting last time, so hardware "
                   + "acceleration is off for this session. Everything works — it may feel "
                   + "slower. If your graphics driver is the cause, set the rendering backend "
                   + "to Software to keep it; otherwise the next launch tries hardware again.",
        ActionLabel = "Graphics settings",
        ActionLink  = new ActivityLink(ActivityLinkKind.Preferences, "Advanced"),
        DedupeKey   = "safe-mode-active",
    });
}
```

### 5.4 `MainViewModel.ResolveActivityLink`

Extend the existing `ActivityLinkKind.Preferences` case — it currently special-cases payloads
`"Automation"` and `"LibraryHealth"`:

```csharp
case ActivityLinkKind.Preferences:
    GoPreferencesCommand.Execute(null);
    switch (link.Payload)
    {
        case "Automation":     Preferences.GoAutomationCommand.Execute(null);    break;
        case "LibraryHealth":  Preferences.GoLibraryHealthCommand.Execute(null); break;
        case "Advanced":       Preferences.GoAdvancedCommand.Execute(null);      break;
    }
    break;
```

`GoAdvancedCommand` and `PreferencesSection.Advanced` already exist
(`PreferencesScreenViewModel`).

### 5.5 `SafeModeNotice.ShowGiveUpMessageBox`

New file `src/Paperbunkr.App/Services/SafeModeNotice.cs`. No Avalonia — the whole point is that
Avalonia can't be trusted at this point.

```csharp
internal static class SafeModeNotice
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x0, MB_ICONERROR = 0x10, MB_SYSTEMMODAL = 0x1000;

    public static void ShowGiveUpMessageBox(string logDirectory)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logDirectory}\"")
                { UseShellExecute = true });
        }
        catch { /* opening the folder is a nicety, not required */ }

        try
        {
            MessageBoxW(IntPtr.Zero,
                "Paperbunkr couldn't start, even with hardware acceleration turned off.\n\n"
                + "Its diagnostic logs have been opened for you:\n" + logDirectory + "\n\n"
                + "Updating your graphics driver often fixes this. If it keeps happening, "
                + "reinstall Paperbunkr or report it at\n"
                + "github.com/heisehis/PaperBunkr/issues",
                "Paperbunkr", MB_OK | MB_ICONERROR | MB_SYSTEMMODAL);
        }
        catch { /* if even MessageBox fails there is nothing left to do */ }
    }
}
```

`BootstrapSentinel.ResetAfterGiveUp()` deletes the state file (same as `Clear`, separate name for
intent). After the user has been told, a later manual launch — post driver update, post reinstall
— starts clean at `Normal` rather than looping straight back to `GiveUp`.

## 6. Data flow — the three launch outcomes

```
Launch 1 (normal):
  Evaluate -> Normal ; Record writes attempts=1
  → GPU init access-violation, process dies, sentinel NOT cleared

Launch 2 (auto safe mode):
  Evaluate -> SafeMode (attempts==1) ; graphics.Backend := Software ; SafeModeState.Active := true
  Record makes attempts=2
  → splash.Show() succeeds → Clear() deletes the sentinel
  → window up → Activity alert "Running in safe mode"

Launch 3 (normal again):
  Evaluate -> Normal (file gone) ; Record writes attempts=1
  → succeeds normally, hardware rendering
```

If Launch 2 *also* dies before the splash:

```
Launch 2: Record makes attempts=2 ; crash before Clear
Launch 3: Evaluate -> GiveUp (attempts>=2)
  → native MessageBox + open logs folder ; ResetAfterGiveUp deletes the file ; return (exit 0)
Launch 4 (user retries after fixing drivers): Evaluate -> Normal
```

## 7. If ReadyToRun is re-enabled later

Not in this spec, but the sentinel is the right hook. Add a middle stage between `SafeMode` and
`GiveUp`: when `attempts == 2`, relaunch the process once with `DOTNET_ReadyToRun=0` (and likely
`DOTNET_TieredPGO=0`) injected into the child environment, distinguished by a
`PAPERBUNKR_BOOTSTRAP_STAGE` env var so the child doesn't recurse; `GiveUp` then moves to
`attempts >= 3`. This requires `Process.Start` with a modified environment and child-detection —
deliberately excluded now because `0.3.1-beta` ships without R2R and the extra process plumbing
has no live vector to justify it.

## 8. Edge cases

| Case | Behavior |
|---|---|
| `%AppData%` unwritable | `Record`/`Clear` no-op; app behaves exactly as today (no recovery, but no regression). Logged. |
| Multiple app instances launched together | Instance B, starting while A is mid-bootstrap, sees `attempts>=1` and runs software-rendered for that session; both clear on splash. Mild, accepted — no PID tracking. |
| Force-quit during DB migration | `Clear()` already ran at `splash.Show()`; not misread as a bootstrap crash. `DatabaseRecoveryWindow` unaffected. |
| NetSparkle applies an update and relaunches | Update check runs post-window; sentinel already cleared. The relaunched (new-version) process hits the `appVersion != current` reset → clean `Normal`. |
| Crash reporter "Restart" | Post-window; sentinel cleared. |
| `--register-file-associations` invocation (from the installer) | Returns before `Record`; no sentinel. |
| Splash constructor throws (not a native crash) | Caught by the existing `try` in `RunDesktopStartupAsync` → `ReportFatalStartupError`. Sentinel not cleared (Clear is after `splash.Show()`), so next launch tries safe mode — correct, a splash/XAML failure is exactly what software rendering might fix. |
| User sets `PAPERBUNKR_RENDER=software` themselves | Normal path already software; a crash still trips the sentinel, `Evaluate` still returns `SafeMode`, the redundant override is harmless. |
| Reduced-motion / no splash? | Splash always shows (per the startup pipeline spec); no branch without it. |

## 9. Logging

`DiagnosticsService` is untouched. New milestones in `startup.log`:

- `Bootstrap sentinel decision: {Normal|SafeMode|GiveUp}` (every launch)
- `Safe mode: forcing Software rendering for this launch.` (safe-mode launches)
- `Bootstrap sentinel recorded: attempts={n}` / `Bootstrap sentinel cleared.`
- `Bootstrap sentinel write failed: {ExceptionType} {message}` (best-effort failures)
- `Bootstrap give-up: shown native notice, reset sentinel.` (give-up path)

These make the sentinel's behavior fully reconstructable from the same log the crash investigation
already relies on.

## 10. Testing

`src/Paperbunkr.App.Tests/BootstrapSentinelTests.cs` — uses `StatePathOverride` pointed at a temp
directory, no other I/O:

- absent file → `Normal`
- corrupt / non-JSON file → `Normal` (no throw)
- `appVersion` mismatch → `Normal` regardless of `attempts`
- `attempts: 1` + matching version → `SafeMode`
- `attempts: 2` (and higher) → `GiveUp`
- `Record` on absent file → writes `attempts: 1`, current version, null `firstFailUtc`
- `Record` on existing matching file → `attempts` incremented, `firstFailUtc` set once and then
  stable across further `Record`s
- `Record` on version-mismatched file → resets to `attempts: 1`
- `Clear` / `ResetAfterGiveUp` delete the file; calling them when absent does not throw
- `StatePathOverride` pointed at an unwritable path → `Record`/`Clear` swallow and return

`Program.Main`, `SafeModeNotice` (P/Invoke), and the `App` wiring are not unit-tested — same
boundary as the existing `Program.cs` / `GraphicsBootstrap` bootstrap code. The
`ResolveActivityLink` `"Advanced"` case is covered by an assertion in the existing
`MainViewModel` link-resolution tests if one exists for the sibling `"Automation"` /
`"LibraryHealth"` payloads; otherwise add a focused test there.

Manual verification (documented for the plan's QA step): with a debug build, throw from
`BuildAvaloniaApp`'s builder chain (or set `RenderingBackend=Gpu` on a VM with no GPU) and confirm
the three-launch sequence: crash → software-rendering retry + notice → normal. Then force two
consecutive bootstrap failures and confirm the native message box + log folder open.

## 11. Files touched

| File | Change |
|---|---|
| `src/Paperbunkr.App/Services/BootstrapSentinel.cs` | **new** — state file read/write/clear, `Evaluate` |
| `src/Paperbunkr.App/Services/SafeModeState.cs` | **new** — one-line static flag |
| `src/Paperbunkr.App/Services/SafeModeNotice.cs` | **new** — Win32 `MessageBoxW` give-up notice |
| `src/Paperbunkr.App/Program.cs` | evaluate sentinel, override graphics to Software, record, give-up branch |
| `src/Paperbunkr.App/App.axaml.cs` | `BootstrapSentinel.Clear()` after `splash.Show()`; raise the safe-mode `ActivityAlert` |
| `src/Paperbunkr.App/ViewModels/MainViewModel.cs` | `"Advanced"` payload case in `ResolveActivityLink` |
| `src/Paperbunkr.App.Tests/BootstrapSentinelTests.cs` | **new** |
| `docs/superpowers/specs/2026-08-27-hardware-accelerated-rendering-design.md` | one-line note that `Gpu` mode is no longer a permanent brick — the sentinel recovers on the next launch |
