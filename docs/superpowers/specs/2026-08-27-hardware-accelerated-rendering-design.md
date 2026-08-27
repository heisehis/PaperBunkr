# Hardware-Accelerated Rendering — Design Spec

*Date: 2026-08-27. Scope: make Avalonia's GPU rendering backend on Windows **explicit and
observable** instead of implicit; add a persisted `AppSettings.RenderingBackend` /
`PreferNativeOpenGl` (Auto / GPU-only / Software) with an EF migration, mirrored to a pre-UI
`graphics.json` cache (+ an env-var override) so the choice can be read before the database is
available at startup; and capture the backend that actually won — plus any silent GPU-init
failure or software fallback — into the existing `startup.log`. Windows-only. Does **not** add
the Preferences UI (deferred), multi-GPU adapter selection, Vulkan, or any reader-specific
performance work.*

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
  for a rendering-backend dropdown. This spec adds the backing `AppSettings` fields and the
  bootstrap cache so that UI is a thin follow-up (bind two fields, call one sync method, show a
  "restart required" note), but builds no UI.
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

## 2. Storage: `AppSettings` is the source of truth, `graphics.json` is a pre-UI cache

The setting lives in the singleton `AppSettings` row
(`src/Paperbunkr.Data/Entities/AppSettings.cs`), like every other app setting — two new fields,
`RenderingBackend` (enum) and `PreferNativeOpenGl` (bool), with a migration. That is the value
the (future) Advanced-tab UI reads and writes, and the value that survives a DB backup/restore.

But **the graphics stack is chosen inside `Program.BuildAvaloniaApp()`, before Avalonia starts**,
whereas the SQLite database isn't opened or migrated until
`App.OnFrameworkInitializationCompleted` — much later. Reaching into an un-migrated database file
at the very top of `Program.Main` (raw `SELECT`, bypassing EF, tolerating a missing table/column
on fresh installs) would put a fragile DB read at precisely the startup phase where a silent
`Database.Migrate()` stall once cost an hour of forensic debugging (see `DiagnosticsService`'s
class comment and the project's memory notes).

So there is a second artifact: **`graphics.json`**, a tiny standalone file under the same
`%AppData%\Paperbunkr\` root the logs already use, read with `System.Text.Json` — no EF, no
SQLite, total error handling. It is a **write-through cache of the two `AppSettings` fields**, not
an independent config:

- `Program.Main` reads `graphics.json` (the DB isn't available yet) to choose the backend.
- After the DB is up (`App.OnFrameworkInitializationCompleted`, post-`EnsureCreated()`), a sync
  step compares the live `AppSettings` values to `graphics.json` and rewrites the file if they
  differ, logging a milestone. A DB restore, a manual DB edit, or a first run after this feature
  ships therefore propagates to the cache on the *next* launch and takes effect the launch after
  — a two-launch lag, documented and acceptable for a restart-only setting.
- The future Advanced-tab UI eliminates the lag for the normal path by writing `AppSettings` and
  calling the same sync method immediately, so the cache is current before the user restarts.

On a fresh install with no `graphics.json` and no DB yet, `Main` gets `Auto` — which is also the
`AppSettings` default, so the first launch is consistent and the first post-DB sync writes the
cache for subsequent launches.

## 3. On-disk format and precedence

**File:** `%AppData%\Paperbunkr\graphics.json` (sibling of `logs/`). Absent by default — a fresh
install has no file and gets `Auto`. Written by the post-DB sync step (§2), not hand-authored,
though a user *may* edit it directly as an escape hatch when the app won't start.

```json
{
  "backend": "auto",
  "preferNativeOpenGl": false
}
```

- `backend`: `"auto"` | `"gpu"` | `"software"` (case-insensitive). Unknown / missing → `Auto`.
  Mirrors `AppSettings.RenderingBackend`.
- `preferNativeOpenGl`: when `true`, native OpenGL (WGL) is tried *before* ANGLE. Default
  `false`. Only meaningful for `auto` and `gpu`. Mirrors `AppSettings.PreferNativeOpenGl`. This
  is the one knob for "ANGLE is the thing misbehaving on this box, try real GL first."

**Environment variable:** `PAPERBUNKR_RENDER` = `auto` | `gpu` | `software` (case-insensitive).
A last-resort override read only at bootstrap — when set to a recognized value it **overrides
`backend` for that launch** (but not `preferNativeOpenGl` — an env-only user still gets the
file's value, or `false` if no file). It is **never written back** to `graphics.json` or
`AppSettings`; unset the variable and the persisted setting applies again. An unrecognized value
is ignored with a breadcrumb.

**Bootstrap precedence (in `Main`):** `PAPERBUNKR_RENDER` (if recognized) → `graphics.json` → 
`Auto`. The database is deliberately *not* consulted here — `graphics.json` is its stand-in
until the sync step reconciles them.

## 4. Backend → `Win32RenderingMode` mapping

`Win32RenderingMode` values available in Avalonia 12.1.1: `AngleEgl`, `Wgl`, `Vulkan`, `Software`.
The array is a priority-ordered fallback chain (first element wins if it initializes).

| `RenderBackend` | `PreferNativeOpenGl` | `RenderingMode` array |
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
  GPU; that is intentional and documented (recovery: set `PAPERBUNKR_RENDER=software`, or edit
  `graphics.json`'s `backend` to `"software"`, or — once the Advanced-tab UI exists — change it
  there).
- **`Software`** is the escape hatch for broken drivers, RDP sessions, and VMs.

`CompositionMode` is **left at its default** (`[WinUIComposition, DirectComposition,
RedirectionSurface]`) in all cases — its defaults are already correct and composition-mode
problems are not what the user is seeing.

## 5. Components

### `RenderBackend` enum — `src/Paperbunkr.Data/Entities/RenderBackend.cs`

```csharp
namespace Paperbunkr.Data.Entities;

/// <summary>
/// Avalonia GPU rendering backend selection, backing <see cref="AppSettings.RenderingBackend"/>
/// (docs/superpowers/specs/2026-08-27-hardware-accelerated-rendering-design.md). No CE
/// equivalent - CE was WinForms/GDI+ with no GPU rendering concept.
/// </summary>
public enum RenderBackend { Auto, Gpu, Software }
```

Lives in `Data.Entities` alongside `ImageFitMode` / `PageLayoutMode` (the codebase convention for
enums that are `AppSettings` columns). `GraphicsBootstrap` in the App project references it.

### `AppSettings` fields — `src/Paperbunkr.Data/Entities/AppSettings.cs`

```csharp
/// <summary>
/// Avalonia GPU rendering backend (docs/superpowers/specs/2026-08-27-hardware-accelerated-
/// rendering-design.md). Restart-only. Source of truth; mirrored to %AppData%\Paperbunkr\
/// graphics.json (read before the DB is available at startup) by GraphicsBootstrap.SyncCache.
/// No CE equivalent. Default Auto = GPU-first with software fallback.
/// </summary>
public RenderBackend RenderingBackend { get; set; } = RenderBackend.Auto;

/// <summary>
/// When true, native OpenGL (WGL) is tried before ANGLE/Direct3D in the rendering fallback
/// chain (spec §4). Default false - ANGLE is the better default on Windows. Restart-only,
/// mirrored to graphics.json with <see cref="RenderingBackend"/>.
/// </summary>
public bool PreferNativeOpenGl { get; set; }
```

Migration: `dotnet ef migrations add AddRenderingBackendSettings` in `src/Paperbunkr.Data`, two
columns, defaults `Auto` / `false`. Follows the one-migration-per-spec convention noted in
`AppSettings`'s own class comment.

### `GraphicsBootstrap` — `src/Paperbunkr.App/Services/GraphicsBootstrap.cs`

Pure, no Avalonia dependency (references only `Paperbunkr.Data.Entities` + BCL), fully
unit-testable.

```csharp
public sealed record GraphicsConfig(RenderBackend Backend, bool PreferNativeOpenGl)
{
    public static GraphicsConfig Default { get; } = new(RenderBackend.Auto, false);
}

public static class GraphicsBootstrap
{
    // Test-only redirects, same pattern as DiagnosticsService.LogDirectoryOverride.
    internal static string? CachePathOverride { get; set; }
    internal static Func<string, string?>? EnvReaderOverride { get; set; }

    public static string CachePath => CachePathOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Paperbunkr", "graphics.json");

    /// Bootstrap read (called from Program.Main, DB not yet available):
    /// env var -> graphics.json -> Default. Never throws. Source string is one of
    /// "env", "graphics.json", "default (no cache)", "default (graphics.json unreadable)".
    public static (GraphicsConfig Config, string Source) Resolve();

    /// Maps a resolved config to the priority-ordered Win32RenderingMode fallback chain (§4).
    public static IReadOnlyList<Win32RenderingMode> ToRenderingModes(GraphicsConfig config);

    /// Post-DB reconciliation (called from App.OnFrameworkInitializationCompleted).
    /// Writes graphics.json to match the persisted settings iff it currently differs (or is
    /// missing/corrupt). Returns true if it rewrote the file. Never throws.
    public static bool SyncCache(RenderBackend backend, bool preferNativeOpenGl);
}
```

- `Resolve()` reads `EnvReaderOverride ?? Environment.GetEnvironmentVariable`, then tries
  `File.ReadAllText(CachePath)` + `JsonSerializer.Deserialize` into a private DTO. Enum parse by
  hand (`Enum.TryParse(..., ignoreCase: true)`), fall back to `Auto`. Any `IOException` /
  `JsonException` / unknown-value path: swallow, record it in the returned `Source`, return
  `Default` (keeping a successfully-parsed `preferNativeOpenGl` if only `backend` was bad). The
  env var overrides only `Backend`, never `PreferNativeOpenGl`, and is never persisted.
- `ToRenderingModes` is the table in §4 as a `switch`.
- `SyncCache` serializes `{ backend, preferNativeOpenGl }` with `JsonSerializerOptions`
  `{ WriteIndented = true }`, lowercase enum string. It reads the current file first and skips
  the write when already equal (avoids touching the file's mtime every launch). `Directory.
  CreateDirectory` on the parent first. All wrapped so a read-only `%AppData%` can't crash
  startup — a failed sync just means the stale cache is used next launch, logged as a milestone.
- **`GraphicsBootstrap` does not log directly** — callers (`Program`, `App`) emit milestones, so
  the unit stays free of `DiagnosticsService` coupling.

### `App.OnFrameworkInitializationCompleted` change — reconciliation

Immediately after the existing `PaperbunkrDb.EnsureCreated()` call (DB now migrated and open):

```csharp
using (var ctx = PaperbunkrDb.CreateContext())
{
    var s = ctx.AppSettings.Single();
    if (GraphicsBootstrap.SyncCache(s.RenderingBackend, s.PreferNativeOpenGl))
        DiagnosticsService.LogMilestone(
            $"graphics.json synced to settings: {s.RenderingBackend} preferNativeOpenGl={s.PreferNativeOpenGl} (restart to apply)");
}
```

(`AppSettings` singleton access should reuse whatever accessor the codebase already has — if
there's an `AppSettingsService` or similar, use it rather than a raw `Single()`; implementation
detail for the plan.)

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
        $"Render backend requested: {gfx.Backend} preferNativeOpenGl={gfx.PreferNativeOpenGl} (source: {source})");
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
  -> GraphicsBootstrap.Resolve()                     env var -> graphics.json -> Default(Auto)
  -> LogMilestone("Render backend requested: ...")   into startup.log
  -> BuildAvaloniaApp(gfx)
       -> .With(Win32PlatformOptions{ RenderingMode })
       -> .AfterSetup: Logger.Sink = Composite(trace, DiagnosticsLogSink)
  -> StartWithClassicDesktopLifetime
       -> Avalonia picks a backend from the chain, emitting Platform/Win/Visual log events
            -> DiagnosticsLogSink forwards Warning+ ones as "[render] ..." into startup.log
       -> App.OnFrameworkInitializationCompleted
            -> PaperbunkrDb.EnsureCreated()          (existing; DB now migrated)
            -> GraphicsBootstrap.SyncCache(settings.RenderingBackend, settings.PreferNativeOpenGl)
                 rewrites graphics.json iff it differs -> milestone "synced ... (restart to apply)"
```

The DB (`AppSettings`) is the source of truth; `graphics.json` trails it by up to one launch
(§2), except when the future Advanced-tab UI writes both together.

Net observable result in `startup.log`: one line stating what was *requested*, optionally a
"synced" line if the cache was stale, and — only if Avalonia had trouble — follow-up `[render]`
lines stating what failed and what it fell back to. A clean GPU start produces just the request
line (Avalonia doesn't log at Warning+ on success), which is itself the signal: "requested Auto,
no render warnings" = GPU is fine.

## 7. Error handling

| Situation | Behavior |
|---|---|
| No `graphics.json` (fresh install) | `Auto`; breadcrumb `source: default (no cache)`; first post-DB `SyncCache` writes it |
| Malformed / unreadable `graphics.json` | `Auto`; breadcrumb `source: default (graphics.json unreadable)`; no throw; `SyncCache` overwrites it with the DB value |
| Unknown `backend` string in file | `Auto` (keep parsed `preferNativeOpenGl`); breadcrumb notes the bad value |
| Unknown `PAPERBUNKR_RENDER` value | Ignored; fall through to file/default; breadcrumb notes it |
| `SyncCache` write fails (read-only `%AppData%`) | Swallowed; milestone notes it; stale cache used next launch; no startup crash |
| DB read for reconciliation fails | Reconciliation skipped with a milestone; bootstrap already succeeded from the cache, so rendering is unaffected |
| `Gpu` mode, GPU init fails | App may fail to start (intentional). Recovery: `PAPERBUNKR_RENDER=software`, edit `graphics.json`, or the future UI. `DiagnosticsLogSink` captures the failure reason first. |
| `DiagnosticsLogSink.Log` throws | Swallowed per-call; never propagates into Avalonia's render path |
| One sink in `CompositeLogSink` throws | Other sink(s) still receive the event |

## 8. Testing

All headless, no real GPU required.

**`GraphicsBootstrapTests`:**
- `PAPERBUNKR_RENDER=software` overrides `graphics.json` `backend: "gpu"` → `Software`.
- `preferNativeOpenGl` from the file is retained even when the env var overrides `backend`.
- Valid file parses each of `auto` / `gpu` / `software` (case-insensitive).
- Missing file → `GraphicsConfig.Default`, `Source` == "default (no cache)".
- Malformed JSON → `Default`, `Source` mentions "unreadable", no exception.
- Unknown `backend` value → `Auto` with `preferNativeOpenGl` still honored.
- `ToRenderingModes` returns the exact ordered array from §4 for all six rows (order asserted).
- `SyncCache`: writes a well-formed file when none exists (returns `true`); rewrites when the
  file differs (returns `true`); returns `false` without rewriting when the file already matches;
  round-trips (`SyncCache` then `Resolve` yields the same config); swallows a write into a
  non-existent drive path (returns `false`, no throw).
- Uses `CachePathOverride` (temp file) and `EnvReaderOverride` — no real env var / `%AppData%`
  writes.

**`AddRenderingBackendSettingsMigrationTests`** (`Paperbunkr.Data.Tests`, matching the existing
`MetadataModelPhase2aMigrationTests` pattern): migrating a DB with a pre-existing `AppSettings`
row yields `RenderingBackend = Auto`, `PreferNativeOpenGl = false`.

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
- `src/Paperbunkr.Data/Entities/RenderBackend.cs`
- `src/Paperbunkr.Data/Migrations/<timestamp>_AddRenderingBackendSettings.cs` (+ `.Designer.cs`,
  + snapshot update) — via `dotnet ef migrations add`
- `src/Paperbunkr.App/Services/GraphicsBootstrap.cs`
- `src/Paperbunkr.App/Services/DiagnosticsLogSink.cs`
- `src/Paperbunkr.App/Services/CompositeLogSink.cs`
- `src/Paperbunkr.App.Tests/GraphicsBootstrapTests.cs`
- `src/Paperbunkr.App.Tests/CompositeLogSinkTests.cs`
- `src/Paperbunkr.App.Tests/DiagnosticsLogSinkTests.cs`
- `src/Paperbunkr.Data.Tests/AddRenderingBackendSettingsMigrationTests.cs`

**Modified:**
- `src/Paperbunkr.Data/Entities/AppSettings.cs` — `RenderingBackend`, `PreferNativeOpenGl` fields
- `src/Paperbunkr.App/Program.cs` — resolve config, log request, pass to `BuildAvaloniaApp`,
  `Win32PlatformOptions`, compose the log sink
- `src/Paperbunkr.App/App.axaml.cs` — `GraphicsBootstrap.SyncCache` call after `EnsureCreated()`

**No XAML change.**

## 10. Follow-ups (out of scope, listed for the roadmap)

- Advanced-tab "Rendering backend" dropdown (Auto / GPU only / Software) + "Prefer native
  OpenGL" checkbox, bound to the `AppSettings` fields, calling `GraphicsBootstrap.SyncCache` on
  save, with a "takes effect after restart" note.
- Surface the resolved-at-runtime backend (not just the request) in the Advanced tab / a
  diagnostics panel — needs a reliable Avalonia API to query the live backend; investigate
  `Compositor` GPU-context introspection.
- `GraphicsAdapterSelectionCallback` for multi-GPU machines, if a user hits the
  integrated-vs-discrete problem.
