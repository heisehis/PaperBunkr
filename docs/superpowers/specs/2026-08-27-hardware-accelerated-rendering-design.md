# Hardware-Accelerated Rendering — Design Spec

*Date: 2026-08-27. Scope: make Avalonia's GPU rendering backend on Windows **explicit and
observable** instead of implicit, add a pre-UI bootstrap config (`graphics.json` + env var) that
can force GPU-only or software rendering, and capture the backend that actually won — plus any
silent GPU-init failure or software fallback — into the existing `startup.log`. Windows-only.
Does **not** add a Preferences UI (deferred), multi-GPU adapter selection, Vulkan, or any
reader-specific performance work.*

## 1. Goals and non-goals

**Goal:** Two things the user asked for:

1. **Observed jank** in the paged reader, page-turns, and the Library cover-tile wall — all of
   which are Avalonia's built-in rendering paths (not the custom webtoon
   `CompositionCustomVisualHandler`). We need to know whether the machine is silently running on
   the software rasterizer.
2. **Proactive / correctness** — configure the GPU backend chain *explicitly* rather than relying
   on Avalonia's undocumented-to-us implicit defaults, and be able to see what backend is live.

**Context — GPU is already the Avalonia default.** Verified against `Avalonia.Win32.dll`'s own
XML docs (Avalonia 12.1.1), not assumed: `Win32PlatformOptions.RenderingMode` already defaults to
`[AngleEgl, Software]` (ANGLE/Direct3D 11 first, software rasterizer fallback), and
`CompositionMode` defaults to `[WinUIComposition, DirectComposition, RedirectionSurface]`. So the
app is *probably* already hardware-rendered on a healthy machine. This spec's value is therefore
**diagnostics first** (does the machine actually get GPU?), **explicit configuration second**
(pin the chain; add a native-GL rung before CPU), and **an escape hatch third** (force software
when a driver/RDP/VM makes GPU worse or broken).

**Non-goals (deferred, not dropped):**

- **Preferences UI.** The Advanced tab already exists
  (`docs/superpowers/specs/2026-08-07-preferences-advanced-tab-design.md`) and is the natural home
  for a rendering-backend dropdown. This spec designs the on-disk format so that UI is a thin
  follow-up (read/write `graphics.json`, show a "restart required" note), but builds no UI.
- **`GraphicsAdapterSelectionCallback`** — picking a specific GPU on multi-GPU laptops. Real CE
  had nothing like it; no user request yet.
- **Vulkan** (`Win32RenderingMode.Vulkan`) — still experimental on Win32 in Avalonia; not in the
  fallback chain.
- **Reader-specific perf** — bitmap virtualization, decode pipeline, `SkiaOptions` tuning are all
  already addressed elsewhere (`docs/onboarding.md` §8) and unchanged here.

**CE parity note.** ComicRack CE was WinForms/GDI+ with no GPU rendering concept at all. Every
choice here is a deliberate Paperbunkr deviation; there is no CE default to check against. The one
CE-shaped decision — writing diagnostics into a plain-text `startup.log` — already matches
`DiagnosticsService`'s existing CE-mirroring report shape.

## 2. Why a separate bootstrap file, not `AppSettings`

Every other app setting lives in the singleton `AppSettings` row
(`src/Paperbunkr.Data/Entities/AppSettings.cs`). The rendering backend deliberately does **not**,
for one hard reason: **the graphics stack is chosen inside `Program.BuildAvaloniaApp()`, before
Avalonia starts**, whereas the SQLite database isn't opened or migrated until
`App.OnFrameworkInitializationCompleted` — much later. Reaching into an un-migrated database file
at the very top of `Program.Main` (raw `SELECT`, bypassing EF, tolerating a missing table/column
on fresh installs) would put a fragile DB read at precisely the startup phase where a silent
`Database.Migrate()` stall once cost an hour of forensic debugging (see
`DiagnosticsService`'s class comment and the project's memory notes).

Instead: a tiny standalone JSON file under the same `%AppData%\Paperbunkr\` root the logs already
use, read with `System.Text.Json`, no EF, no SQLite, total error handling. This is the standard
pattern for "config that must be read before the graphics/UI stack initializes."

## 3. On-disk format and precedence

**File:** `%AppData%\Paperbunkr\graphics.json` (sibling of `logs/`). Absent by default — a fresh
install has no file and gets `Auto`.

```json
{
  "backend": "auto",
  "preferWgl": false
}
```

- `backend`: `"auto"` | `"gpu"` | `"software"` (case-insensitive). Unknown / missing → `Auto`.
- `preferWgl`: when `true`, native OpenGL (WGL) is tried *before* ANGLE. Default `false`. Only
  meaningful for `auto` and `gpu`. This is the one knob for "ANGLE is the thing misbehaving on
  this box, try real GL first."

**Environment variable:** `PAPERBUNKR_RENDER` = `auto` | `gpu` | `software` (case-insensitive).
When set to a recognized value it **overrides `backend` from the file entirely** (but not
`preferWgl` — an env-only user still gets the file's `preferWgl`, or `false` if no file). An
unrecognized value is ignored with a breadcrumb.

**Precedence:** `PAPERBUNKR_RENDER` (if recognized) → `graphics.json` `backend` → `Auto`.

## 4. Backend → `Win32RenderingMode` mapping

`Win32RenderingMode` values available in Avalonia 12.1.1: `AngleEgl`, `Wgl`, `Vulkan`, `Software`.
The array is a priority-ordered fallback chain (first element wins if it initializes).

| `RenderBackend` | `preferWgl` | `RenderingMode` array |
|---|---|---|
| `Auto` (default) | `false` | `[AngleEgl, Wgl, Software]` |
| `Auto` | `true` | `[Wgl, AngleEgl, Software]` |
| `Gpu` | `false` | `[AngleEgl, Wgl]` |
| `Gpu` | `true` | `[Wgl, AngleEgl]` |
| `Software` | (ignored) | `[Software]` |

- **`Auto`** makes today's implicit `[AngleEgl, Software]` explicit **and inserts `Wgl` as a rung
  before the CPU rasterizer** — a box where ANGLE/D3D fails to init still gets native GL before
  dropping all the way to software. This is the meaningful behavioral improvement for goal 1.
- **`Gpu`** removes the software fallback — for deliberately testing "is GPU actually working, or
  has it been silently falling back this whole time?" It *can* fail to start the app on a broken
  GPU; that is intentional and documented (recovery: delete `graphics.json` or set
  `PAPERBUNKR_RENDER=software`).
- **`Software`** is the escape hatch for broken drivers, RDP sessions, and VMs.

`CompositionMode` is **left at its default** (`[WinUIComposition, DirectComposition,
RedirectionSurface]`) in all cases — its defaults are already correct and composition-mode
problems are not what the user is seeing.

## 5. Components

### `GraphicsBootstrap` — `src/Paperbunkr.App/Services/GraphicsBootstrap.cs`

Pure, no Avalonia dependency, fully unit-testable.

```csharp
public enum RenderBackend { Auto, Gpu, Software }

public sealed record GraphicsConfig(RenderBackend Backend, bool PreferWgl)
{
    public static GraphicsConfig Default { get; } = new(RenderBackend.Auto, false);
}

public static class GraphicsBootstrap
{
    // Test-only redirect, same pattern as DiagnosticsService.LogDirectoryOverride.
    internal static string? ConfigPathOverride { get; set; }
    internal static Func<string, string?>? EnvReaderOverride { get; set; }

    public static string ConfigPath => ConfigPathOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Paperbunkr", "graphics.json");

    /// Resolves env var -> file -> Default. Never throws. Returns the config plus a
    /// human-readable source string ("env", "graphics.json", "default (no config)",
    /// "default (graphics.json unreadable)") for the startup breadcrumb.
    public static (GraphicsConfig Config, string Source) Resolve();

    /// Maps a resolved config to the priority-ordered Win32RenderingMode fallback chain (§4).
    public static IReadOnlyList<Win32RenderingMode> ToRenderingModes(GraphicsConfig config);
}
```

- `Resolve()` reads `EnvReaderOverride ?? Environment.GetEnvironmentVariable`, then tries
  `File.ReadAllText(ConfigPath)` + `JsonSerializer.Deserialize` into a private DTO with
  `[JsonStringEnumConverter]`-style tolerant parsing (do the enum parse by hand:
  `Enum.TryParse(..., ignoreCase: true)`, fall back to `Auto`). Any `IOException` /
  `JsonException` / unknown-value path: swallow, record it in the returned `Source` string,
  return `Default` (or `Default with { PreferWgl = ... }` if only `backend` was bad).
- `ToRenderingModes` is the table in §4 as a `switch`.
- **`GraphicsBootstrap` does not log directly** — it returns the `Source` string and lets
  `Program` emit the milestone, so the pure unit stays free of `DiagnosticsService` coupling.

### `DiagnosticsLogSink` — `src/Paperbunkr.App/Services/DiagnosticsLogSink.cs`

Implements `Avalonia.Logging.ILogSink` (three members in Avalonia 12.1.1: `IsEnabled(level,
area)`, `Log(level, area, source, template)`, `Log(level, area, source, template, object[])`).

- `IsEnabled` returns `true` only for `area` in `{ LogArea.Platform, LogArea.Win,
  LogArea.Visual }` **and** `level >= LogEventLevel.Warning`. This is where Avalonia emits
  "unable to initialize the GPU / falling back to software" and D3D/ANGLE/WGL context failures.
- `Log(...)` formats the template (reuse Avalonia's own `{0}`-style substitution: a small
  `string.Format`-with-named-holes helper, or just append the args — messages here are rare and
  diagnostic, exact fidelity of the template isn't critical) and calls
  `DiagnosticsService.LogMilestone($"[render] {area}/{level}: {message}")`.
- Entire `Log` body wrapped in `try { } catch { }` — the existing "diagnostics must never throw
  into the path they observe" rule.

### `CompositeLogSink` — `src/Paperbunkr.App/Services/CompositeLogSink.cs`

`Logger.Sink` is single-valued and `.LogToTrace()` claims it. To keep trace logging *and* add
capture:

- Constructor takes `params ILogSink?[] sinks` (nulls filtered — `Logger.Sink` could
  theoretically be null).
- `IsEnabled` = logical OR across inners.
- Both `Log` overloads: iterate inners, call each inside its own `try { } catch { }` so one
  faulting sink can't suppress the other.

### `Program.cs` changes

```csharp
public static void Main(string[] args)
{
    DiagnosticsService.Install();
    var (gfx, source) = GraphicsBootstrap.Resolve();
    DiagnosticsService.LogMilestone(
        $"Render backend requested: {gfx.Backend} preferWgl={gfx.PreferWgl} (source: {source})");
    try
    {
        BuildAvaloniaApp(gfx).StartWithClassicDesktopLifetime(args);
    }
    finally { DiagnosticsService.LogMilestone("Process exiting."); }
}

// Parameterless overload kept for the Avalonia visual designer, which calls BuildAvaloniaApp()
// by reflection and must not depend on GraphicsBootstrap.
public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(GraphicsConfig.Default);

public static AppBuilder BuildAvaloniaApp(GraphicsConfig gfx)
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
#if DEBUG
        .WithDeveloperTools()
#endif
        .WithInterFont()
        .With(new SkiaOptions { MaxGpuResourceSizeBytes = 384L * 1024 * 1024 })
        .With(new Win32PlatformOptions
        {
            RenderingMode = GraphicsBootstrap.ToRenderingModes(gfx).ToArray(),
        })
        .LogToTrace()
        .AfterSetup(_ =>
        {
            // .LogToTrace() has just set Logger.Sink; wrap it so we also capture render events.
            Avalonia.Logging.Logger.Sink =
                new CompositeLogSink(Avalonia.Logging.Logger.Sink, new DiagnosticsLogSink());
        });
```

- `Win32PlatformOptions` is in namespace `Avalonia` (confirmed from the XML: type is
  `Avalonia.Win32PlatformOptions`, enum is `Avalonia.Win32RenderingMode`). It is a no-op on
  non-Windows platforms, so no `OperatingSystem.IsWindows()` guard is needed, but the `.csproj`
  already targets Windows-primary use.
- `.AfterSetup` is used rather than sinking before `.LogToTrace()` because `.LogToTrace()`
  overwrites `Logger.Sink` wholesale; wrapping afterward is the only ordering that keeps both.
  Verify during implementation that `.AfterSetup` runs *before* the first platform-graphics
  init log line — if ANGLE init logging happens earlier, move the `Logger.Sink =` assignment to
  the top of `App.Initialize()` or keep a `DiagnosticsLogSink` installed as the sink from the
  very start of `Main` and have `.LogToTrace()`'s sink be the one that's wrapped in. (Fallback
  plan: set `Logger.Sink = new DiagnosticsLogSink()` in `Main` immediately, then in
  `.AfterSetup` compose it with whatever `.LogToTrace()` installed.)

## 6. Data flow

```
Program.Main
  -> DiagnosticsService.Install()                    (existing)
  -> GraphicsBootstrap.Resolve()                     env var -> graphics.json -> Default
  -> LogMilestone("Render backend requested: ...")   into startup.log
  -> BuildAvaloniaApp(gfx)
       -> .With(Win32PlatformOptions{ RenderingMode })
       -> .AfterSetup: Logger.Sink = Composite(trace, DiagnosticsLogSink)
  -> StartWithClassicDesktopLifetime
       -> Avalonia picks a backend from the chain, emitting Platform/Win/Visual log events
            -> DiagnosticsLogSink forwards Warning+ ones as "[render] ..." into startup.log
```

Net observable result in `startup.log`: one line stating what was *requested*, and — only if
Avalonia had trouble — follow-up `[render]` lines stating what failed and what it fell back to. A
clean GPU start produces just the request line (Avalonia doesn't log at Warning+ on success),
which is itself the signal: "requested Auto, no render warnings" = GPU is fine.

## 7. Error handling

| Situation | Behavior |
|---|---|
| No `graphics.json` | `Auto`; breadcrumb `source: default (no config)` |
| Malformed JSON / unreadable file | `Auto`; breadcrumb `source: default (graphics.json unreadable)`; no throw |
| Unknown `backend` string | `Auto` (keep parsed `preferWgl`); breadcrumb notes the bad value |
| Unknown `PAPERBUNKR_RENDER` value | Ignored; fall through to file/default; breadcrumb notes it |
| `Gpu` mode, GPU init fails | App may fail to start (intentional). Recovery documented: delete `graphics.json` or `PAPERBUNKR_RENDER=software`. The `DiagnosticsLogSink` captures the failure reason first. |
| `DiagnosticsLogSink.Log` throws | Swallowed per-call; never propagates into Avalonia's render path |
| One sink in `CompositeLogSink` throws | Other sink(s) still receive the event |

## 8. Testing

All headless (`Paperbunkr.App.Tests`), no real GPU required.

**`GraphicsBootstrapTests`:**
- `PAPERBUNKR_RENDER=software` overrides `graphics.json` `backend: "gpu"` → `Software`.
- `preferWgl` from the file is retained even when the env var overrides `backend`.
- Valid file parses each of `auto` / `gpu` / `software` (case-insensitive).
- Missing file → `GraphicsConfig.Default`, `Source` == "default (no config)".
- Malformed JSON → `Default`, `Source` mentions "unreadable", no exception.
- Unknown `backend` value → `Auto` with `preferWgl` still honored.
- `ToRenderingModes` returns the exact ordered array from §4 for all six rows (order asserted).
- Uses `ConfigPathOverride` (temp file) and `EnvReaderOverride` — no real env var / `%AppData%`
  writes.

**`CompositeLogSinkTests`:**
- Event fans out to all inners.
- `IsEnabled` is OR (true if any inner enables).
- An inner throwing in `Log` doesn't stop the others; no exception escapes.
- Null inners are filtered.

**`DiagnosticsLogSinkTests`:**
- `IsEnabled` true for `Platform`/`Win`/`Visual` at `Warning`/`Error`/`Fatal`; false for those
  areas at `Info`/`Debug`; false for unrelated areas (`Binding`, `Layout`) at any level.
- A `Warning` in `LogArea.Platform` produces a `[render]`-prefixed line in a redirected
  `DiagnosticsService` log dir (via existing `LogDirectoryOverride`).
- `Log` swallows a thrown formatting error.

**Not tested:** `Program.BuildAvaloniaApp` / `.AfterSetup` wiring — not practically testable
without launching Avalonia. It is thin glue (three statements) over the three tested units.
Manual verification step in the plan: launch the app, confirm `startup.log` shows the request
line; set `PAPERBUNKR_RENDER=software`, relaunch, confirm the line changes and (optionally) the
FPS/overlay dev tools show the software renderer.

## 9. Files touched

**New:**
- `src/Paperbunkr.App/Services/GraphicsBootstrap.cs`
- `src/Paperbunkr.App/Services/DiagnosticsLogSink.cs`
- `src/Paperbunkr.App/Services/CompositeLogSink.cs`
- `src/Paperbunkr.App.Tests/GraphicsBootstrapTests.cs`
- `src/Paperbunkr.App.Tests/CompositeLogSinkTests.cs`
- `src/Paperbunkr.App.Tests/DiagnosticsLogSinkTests.cs`

**Modified:**
- `src/Paperbunkr.App/Program.cs` — resolve config, log request, pass to `BuildAvaloniaApp`,
  `Win32PlatformOptions`, compose the log sink.

**No migration, no `AppSettings` change, no XAML change.**

## 10. Follow-ups (out of scope, listed for the roadmap)

- Advanced-tab "Rendering backend" dropdown (Auto / GPU only / Software) + "Prefer native
  OpenGL" checkbox, writing `graphics.json`, with a "takes effect after restart" note.
- Surface the resolved-at-runtime backend (not just the request) in the Advanced tab / a
  diagnostics panel — needs a reliable Avalonia API to query the live backend; investigate
  `Compositor` GPU-context introspection.
- `GraphicsAdapterSelectionCallback` for multi-GPU machines, if a user hits the
  integrated-vs-discrete problem.
