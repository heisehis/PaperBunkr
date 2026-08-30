# Python Plugin Scripting — Design

2026-08-30

*Gap 4 of the plugin-api-automation-gaps series (docs/superpowers/specs/2026-08-30-plugin-api-
automation-gaps-design.md), scoped out of that spec deliberately - closed here on its own.*

## Background

CE's plugin scripts were IronPython (`PythonCommand`, confirmed pinned to **IronPython 2.7.4** in
`ComicRack.Plugins.csproj` - genuine Python 2 syntax). Plugin API v2 (2026-08-24) replaced this
with C# `.csx` scripting (`CSharpCommand`, via Roslyn's `Microsoft.CodeAnalysis.CSharp.Scripting`)
as a deliberate architectural choice - `Command`'s own doc comment records it: "CE's XML/Python
split collapses into one script-backed command type." Paperbunkr currently has no way to run a
Python-scripted plugin at all.

Feasibility checked before designing anything: **IronPython 3.4.2** (current stable, package
`IronPython` on NuGet, `IronLanguages/ironpython3`) targets .NET Standard 2.0, .NET Framework
4.6.2, and .NET 8.0/10.0 - a real, embeddable, actively-maintained engine for this app's actual
runtime. Its hosting API (`Python.CreateEngine()` → `ScriptEngine.CreateScope()` →
`ScriptSource.Execute(scope)` → `scope.GetVariable<T>(name)`) is materially unchanged from the
2.x line CE used, so CE's own `PythonCommand.cs` is a faithful mechanical reference for how this
should work, not just a historical curiosity.

**IronPython 3.4 targets Python 3**, not the Python 2 CE actually shipped against. A real existing
CE `.py` plugin may need minor manual porting (most commonly the `print` statement → function) to
run here - this spec adds a Python *scripting capability*, not a guaranteed-unmodified path for
every historical CE script.

## Goal

A second concrete `Command` subclass, `PythonCommand`, sitting alongside `CSharpCommand` in the
same plugin engine - same manifest, same hooks, same `IPluginEnvironment` surface, same sandbox
posture. A plugin author picks C# or Python per command; nothing about discovery, hook wiring, or
the Plugin screen's UI needs to know which.

## Manifest dispatch — no schema change for the type itself

Confirmed by reading `PluginManifest`/`CommandManifestEntry`: the manifest is "deliberately
flat/single-concrete-type" today, no `Command` polymorphism to declare. Rather than add an
explicit `type="python"` attribute, `XmlPluginInitializer.GetCommands` dispatches on the script
file's own extension - `.py` → `PythonCommand`, `.csx` → `CSharpCommand`, anything else → skipped
(logged as a compile error on that entry, same "one bad plugin can't abort discovery" contract the
method already has). This mirrors CE's own convention (`PythonPluginInitializer` gates on
`Path.GetExtension(file) == ".py"`) and needs zero manifest-schema churn for existing `.csx`
plugins.

One real addition: `CommandManifestEntry` gains an optional `method` attribute. A `.csx` script's
entire body *is* the command (current contract, unchanged); a `.py` file can define several
top-level functions, so a `<Command>` entry pointing at a `.py` script needs to say which function
it invokes - directly ports CE's `PythonCommand.Method`. Missing/empty `method` on a `.py` entry is
a compile-time error for that command (surfaced via `CompileError`, same as any other broken
command), not a runtime one.

## Execution model

`PreCompile()`: creates a fresh `ScriptEngine` (`IronPython.Hosting.Python.CreateEngine()`, one per
command - matches CE's own per-`PreCompile` engine creation, no cross-command sharing), executes
the *entire* `.py` file once via `CreateScriptSourceFromFile(...).Execute(scope)` into a
`ScriptScope`, same as CE. Any exception (syntax error, import failure) becomes `CompileError`,
same contract `CSharpCommand.PreCompile` already has.

`OnInvokeAsync(PluginGlobals globals)`: looks up `Method` as a variable in the compiled scope
(`scope.GetVariable<dynamic>(Method)` - IronPython functions come back as callable dynamic objects,
no need for CE's per-hook strongly-typed-delegate dictionary since Python has no compile-time
signature to satisfy) and invokes it with **one positional argument: the same `PluginGlobals`-
derived instance `CSharpCommand`'s script gets as its implicit globals object**. Roslyn scripting's
"globals object" trick has no Python equivalent, so the calling convention is explicit instead:

```python
# @method: OnBooks in the manifest's <Command method="OnBooks" .../>
def OnBooks(globals):
    for book in globals.Books:
        globals.Environment.App.ScanFolders()
```

IronPython reads .NET properties/methods directly off a CLR object handed into the scope, so
`globals.Books`, `globals.Environment.App.GetLibraryBooks()`, etc. work with no marshaling layer -
the exact same `IPluginEnvironment`/`IApplication`/`IBrowser`/etc. surface every C# script already
gets, just accessed with Python attribute syntax instead of C#'s implicit-globals syntax.

Return value: whatever `Method` returns, boxed the same way `CSharpCommand.OnInvokeAsync` returns
its `ScriptState<object>.ReturnValue` - callers on the hook-dispatch side already treat the return
as `object?` regardless of which command type produced it.

## Sandbox

Mirrors `CSharpCommand`'s existing, explicitly non-adversarial bar (its own doc comment: "not a
hardened boundary against a plugin author who's actively trying to escape it," just closing
*accidental* overreach). The `ScriptEngine`'s runtime only gets `LoadAssembly`d against the same
fixed small set `CSharpCommand.Options.WithReferences` already uses - BCL, `Paperbunkr.Data`
entities assembly, `Paperbunkr.Plugins` assembly. EF Core / SQLite are never registered.

**The caveat this section originally flagged turned out to be real, and worse than expected -
verified empirically, twice, before landing on the actual mitigation below:**

1. **The obvious fix (a custom `PlatformAdaptationLayer` on the `ScriptHost`) does not work.**
   Confirmed against IronPython 3's actual source (`src/core/IronPython/Runtime/ClrModule.cs`):
   `clr.AddReference`'s implementation wraps its call to `LoadAssemblyByName` (which *does* route
   through the host's `PlatformAdaptationLayer`) in a bare `try { } catch { }`, then unconditionally
   falls back to `Assembly.LoadWithPartialName` on *any* failure - including a deliberate denial.
   The host-customization point exists and works exactly as documented; IronPython's own fallback
   just never asks it a second time.
2. **True `AssemblyLoadContext` isolation does not work either** - confirmed via a real, running
   repro, not assumed. A custom ALC correctly gates its own `Load(AssemblyName)` override (verified:
   the override fires, correctly denies `Microsoft.EntityFrameworkCore`, and - genuinely useful
   finding - correctly *shares* `Paperbunkr.Data`/`Paperbunkr.Plugins` types by identity, so
   `Issue` round-trips through an isolated engine with no duplicate-type breakage). But
   `Assembly.LoadWithPartialName` resolves by simple-name lookup against **whatever's already
   loaded anywhere in the process**, not scoped to the calling code's ALC at all. Since Paperbunkr's
   own main process always has EF Core loaded (for its own database access), `LoadWithPartialName`
   finds it regardless of which ALC asked. The repro's isolated `Load()` override visibly firing and
   denying the assembly, immediately followed by the script reaching `DbContext` anyway, proved this
   directly.
3. **The only mechanism that would truly close this is out-of-process plugin execution** - real
   IPC, marshaling `Issue`/`IPluginEnvironment` across a process boundary. Categorically bigger than
   this feature, and a limitation the *existing* C# `.csx` sandbox already explicitly accepts too
   (its own spec: "no AppDomain/process isolation... that boundary was never part of the design").
   This isn't a Python-specific gap - it's a property of the whole in-process plugin architecture -
   and is explicitly **out of scope** here.

**Actual mitigation, chosen once the above ruled out anything stronger**: `PreCompile()` does a
static text scan of the raw `.py` source for `clr.AddReference(...)` calls naming any assembly in
the *same* denylist `CSharpCommand`'s `BlockedMetadataReferenceResolver` already uses
(`Microsoft.EntityFrameworkCore`, `Microsoft.Data.Sqlite`, `SQLitePCLRaw`) - extracted into one
shared list both command types reference, so the two sandboxes can't quietly drift apart. A match
is a `CompileError`, same as any other broken command - the script never executes at all, so there
is no "make the call itself fail" race to lose. This is a text-level check, not a parse of Python
semantics - trivially defeated by a determined author (string concatenation, `getattr` indirection),
which is consistent with, not a regression from, the "accidental overreach, not adversarial
isolation" bar this whole sandbox has always targeted.

## Python 2 → 3

No auto-porting tooling, no compatibility shim for Python-2-only syntax. New plugin scripts are
written directly against Python 3 (what IronPython 3.4 actually runs); an existing CE `.py` script
using Python-2-isms (most commonly the `print` statement, `unicode`/`str` handling, integer
division) needs the same manual porting any real Python 2→3 migration needs. This is a genuine,
bounded gap, not a hidden one.

## Testing

Mirrors `PluginEngineTests`/`DuplicateFinderPluginTests`' existing "real fixture, not a mock"
style:

- `PythonCommandTests` (new - `CSharpCommand` itself has no dedicated unit test file; it's
  exercised indirectly through `PluginEngineTests`/`DuplicateFinderPluginTests`, so this is a new,
  slightly more granular file, not a mirror of an existing one): a real `.py` fixture file,
  `PreCompile` succeeds, `OnInvokeAsync` calls the named function and the `globals` argument's
  `Books`/`Environment` round-trip correctly.
  - A `.py` file with a genuine syntax error → `CompileError` set, `IsBroken` true, discovery of
    other commands in the same/other plugins unaffected (mirrors the existing broken-`.csx`
    coverage).
  - A manifest `<Command>` entry pointing at a `.py` script with no `method` attribute →
    `CompileError`, not a crash.
- A real end-to-end fixture plugin (mirrors `DuplicateFinderPluginTests`' whole-plugin style, but
  Python) exercising at least one real hook (Books is the simplest - matches CE's own most common
  plugin shape) through the actual `PluginEngine.Discover` → `Invoke` path, not just
  `PythonCommand` in isolation.
- Sandbox: a `.py` script attempting `clr.AddReference("Microsoft.EntityFrameworkCore")` (or
  `System.Data.Sqlite`) either fails to resolve or, if the probing caveat above turns out to be
  real, is documented as a known gap rather than silently passing.

## Out of scope

- CE's real annotation-scanning discovery (`# @Hook X` comments, no manifest) - explicitly decided
  against; Python commands are declared in `plugin.xml` exactly like C# ones.
- Any Python-2-syntax compatibility layer.
- Hardening against an adversarial plugin author (matches the existing, already-documented scope
  limit of the C# sandbox).
