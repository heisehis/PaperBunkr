# Python Plugin Scripting — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-30-python-plugin-scripting-design.md*

Working in worktree `C:\Users\DeeDee\PaperBunkr-plugin-automation`, branch `plugin-api-gap-closure`,
on top of the already-committed gaps 1-3 work.

Survey confirmed: `PluginEngine`/`CommandCollection` are entirely `Command`-abstraction-based -
`PythonCommand` slots in with **zero changes** to either. `CSharpCommand` has no dedicated unit
test file (only exercised via `PluginEngineTests`/`DuplicateFinderPluginTests`); the real
end-to-end fixture pattern is a `SamplePlugins/<name>/` folder glob-copied to test output
(`<None Include="SamplePlugins\**" CopyToOutputDirectory="PreserveNewest" />` already covers any
new subfolder, no csproj change needed for that part) and discovered via real
`PluginEngine.Discover`.

## Step 1: Add the IronPython package reference

**Files:** `src/Paperbunkr.Plugins/Paperbunkr.Plugins.csproj` (edit)

**What:** Add `<PackageReference Include="IronPython" Version="3.4.2" />` alongside the existing
`Microsoft.CodeAnalysis.CSharp.Scripting` reference (confirmed on NuGet: targets .NET Standard 2.0/
.NET Framework 4.6.2/.NET 8.0+, compatible with this project's `net10.0` TFM).

**Depends on:** none.

**Verify:** `dotnet restore` then `dotnet build src/Paperbunkr.Plugins/Paperbunkr.Plugins.csproj` -
confirms the package actually resolves and builds clean against `net10.0` before anything is
written against it (the design's feasibility check was web research, not a real restore - do this
first so a real problem surfaces immediately, not after Step 3's work).

## Step 2: `method` attribute on the manifest

**Files:** `src/Paperbunkr.Plugins/PluginManifest.cs` (edit)

**What:** Add to `CommandManifestEntry`:
```csharp
/// <summary>Function name within a .py Script to invoke - required for Python commands (a .csx
/// script's whole body is the command; a .py file can define several top-level functions). Ignored
/// for .csx entries.</summary>
[XmlAttribute("method")]
public string? Method { get; set; }
```

**Depends on:** none.

**Verify:** `dotnet build` - purely additive, existing manifests/tests unaffected.

## Step 3: `PythonCommand`

**Files:** `src/Paperbunkr.Plugins/PythonCommand.cs` (new)

**What:** Mirrors `CSharpCommand.cs`'s shape exactly (same file, same namespace):
```csharp
public sealed class PythonCommand : Command
{
    public required string ScriptPath { get; init; }
    public required string Method { get; init; }

    private ScriptScope? _scope;

    public override void PreCompile()
    {
        try
        {
            var engine = Python.CreateEngine();
            // Fixed small assembly set only - mirrors CSharpCommand.Options.WithReferences.
            // Never LoadAssembly EF Core/SQLite (design's sandbox section).
            engine.Runtime.LoadAssembly(typeof(object).Assembly);           // BCL
            engine.Runtime.LoadAssembly(typeof(Issue).Assembly);            // Paperbunkr.Data entities
            engine.Runtime.LoadAssembly(typeof(IPluginEnvironment).Assembly); // Paperbunkr.Plugins

            var scope = engine.CreateScope();
            var source = engine.CreateScriptSourceFromFile(ScriptPath);
            source.Execute(scope);

            if (!scope.ContainsVariable(Method))
            {
                CompileError = $"Function '{Method}' not found in '{ScriptPath}'.";
                return;
            }

            _scope = scope;
        }
        catch (Exception ex)
        {
            CompileError = ex.Message;
            _scope = null;
        }
    }

    protected override Task<object?> OnInvokeAsync(PluginGlobals globals)
    {
        if (_scope is null)
        {
            throw new InvalidOperationException($"Command '{Key}' has no compiled script (CompileError: {CompileError}).");
        }

        dynamic function = _scope.GetVariable(Method);
        object? result = function(globals);
        return Task.FromResult(result);
    }
}
```
Notes for whoever implements this:
- `IronPython.Hosting.Python.CreateEngine()` / `ScriptEngine.CreateScope()` /
  `CreateScriptSourceFromFile(...).Execute(scope)` / `ScriptScope.GetVariable`/`ContainsVariable` -
  confirm exact method names against the installed package's actual API (IronPython 3.4's docs
  reference these, but verify against IntelliSense/decompiled signatures once the package is
  restored - don't assume the 2.x-era method names carried over 1:1 without checking).
  `Python.CreateEngine()` lives in `IronPython.Hosting`; `ScriptEngine`/`ScriptScope`/
  `ScriptSource` live in `Microsoft.Scripting.Hosting`.
  `IPluginEnvironment` is in `Paperbunkr.Plugins` already, no extra using needed beyond what
  `CSharpCommand.cs` already has for that type.
- `dynamic function = _scope.GetVariable(Method); function(globals)` - calling an IronPython
  function via C#'s `dynamic` binder is the standard IronPython-hosting pattern (confirmed via
  research). One positional argument, matches the design's calling convention exactly.
- A new `ScriptEngine` per command, no sharing across commands or across `PreCompile` calls -
  matches CE's own `PythonCommand.OnPreCompile` (fresh `CreateEngine()` every time), not
  `CSharpCommand`'s shared static `Options`.

**Depends on:** Step 1 (package must resolve to write real code against its types).

**Verify:** `dotnet build src/Paperbunkr.Plugins/Paperbunkr.Plugins.csproj`.

## Step 4: Extension-based dispatch in `XmlPluginInitializer`

**Files:** `src/Paperbunkr.Plugins/XmlPluginInitializer.cs` (edit)

**What:** Replace the single `.Select(entry => (Command)new CSharpCommand { ... })` with a
per-entry dispatch on `Path.GetExtension(entry.Script)`:
- `.csx` → existing `CSharpCommand` construction, unchanged.
- `.py` → new `PythonCommand { ..., ScriptPath = Path.Combine(pluginDir, entry.Script), Method = entry.Method ?? "" }`.
  An empty/missing `Method` isn't rejected here (per the design, this becomes a `CompileError` at
  `PreCompile` time via the `!scope.ContainsVariable(Method)` check in Step 3, not a discovery-time
  skip) - `PythonCommand`'s `Method` property is `required` so pass `entry.Method ?? string.Empty`
  and let `PreCompile` produce the real error message.
- anything else → skip that entry (matches CE's own `.py`-only gate; an unrecognized extension is
  simply not a command, same "one bad entry doesn't abort the rest" contract the method already
  has for a fully malformed manifest).

**Depends on:** Steps 2 and 3.

**Verify:** `dotnet build`. `dotnet test` filtered to any existing `XmlPluginInitializer`/manifest
parsing tests (search for the actual file name first - don't assume it exists under a guessed
name) to confirm `.csx` dispatch is genuinely unchanged, not just "still compiles."

## Step 5: `PythonCommandTests`

**Files:**
- `src/Paperbunkr.Plugins.Tests/PythonCommandTests.cs` (new)
- Real `.py` fixture files written to a temp directory per test (mirrors how `CSharpCommand`'s
  compile-error coverage - if any exists under `PluginEngineTests` - writes throwaway `.csx` files;
  check that file first to match its exact temp-file-cleanup convention)

**What:**
- `PreCompile_ValidScript_Succeeds` - a real `.py` file defining one function, `PreCompile()`
  leaves `IsBroken` false.
- `PreCompile_SyntaxError_SetsCompileError_DoesNotThrow` - malformed `.py` content, `IsBroken` true,
  `PreCompile()` itself doesn't throw.
- `PreCompile_MethodNotFoundInScript_SetsCompileError` - valid Python, but `Method` names a
  function that isn't defined in the file.
- `OnInvokeAsync_CallsTheNamedFunction_PassesGlobalsAsOneArgument_ReturnsItsResult` - a `.py`
  function that reads `globals.Environment` and `globals.Books` (using a `BooksHookGlobals`
  instance) off the passed argument and returns a value derived from them; assert the returned
  value round-trips correctly through `object?`.
- Sandbox probe (design's open caveat, made concrete rather than left abstract): a `.py` script
  containing `import clr; clr.AddReference("Microsoft.EntityFrameworkCore")` (or the SQLite
  equivalent) followed by an attempt to reference a real EF Core type. Two acceptable outcomes,
  both are a *pass* for this test, distinguished by an explicit assertion + comment so the result
  is legible either way:
  - it throws / fails to resolve → sandbox holds, assert the failure.
  - it succeeds → the caveat is real; assert success **and** leave a prominent comment pointing at
    the design doc's "Sandbox" section, since this is the trigger for the design's own stated
    follow-up (an `AssemblyLoadContext`-level block or an `import`/`clr.AddReference` hook) - don't
    silently let a passing test hide a real finding.

**Depends on:** Step 3.

**Verify:** `dotnet test src/Paperbunkr.Plugins.Tests/Paperbunkr.Plugins.Tests.csproj --filter FullyQualifiedName~PythonCommandTests`.

## Step 6: Real end-to-end Python sample plugin

**Files:**
- `src/Paperbunkr.Plugins.Tests/SamplePlugins/PythonHello/plugin.xml` (new)
- `src/Paperbunkr.Plugins.Tests/SamplePlugins/PythonHello/startup.py` (new)
- `src/Paperbunkr.Plugins.Tests/PythonHelloPluginTests.cs` (new)

**What:** Smallest real plugin that proves the whole path - manifest → discovery → precompile →
invoke - works through `PluginEngine` itself, not just `PythonCommand` in isolation. Mirrors
`DuplicateFinderPluginTests`' `DiscoverEngine()` helper and `FakePluginEnvironment` fixture
exactly. Startup hook (simplest payload, matches `DuplicateFinder`'s own first/simplest command):

```xml
<Plugin key="python-hello" name="Python Hello">
  <Command hook="Startup" key="python-hello.startup" name="Python Hello Activated"
           description="Proves a Python-scripted command runs end-to-end through the real plugin engine."
           script="startup.py" method="on_startup" />
</Plugin>
```
```python
# Python Hello - Startup hook, proves PythonCommand works end-to-end through the real PluginEngine.
def on_startup(globals):
    return "Python Hello is active for this session."
```

Test:
```csharp
[Fact]
public async Task Startup_hook_runs_through_the_real_engine_and_returns_its_message()
{
    var engine = new PluginEngine();
    engine.Discover(PluginsRoot, new FakePluginEnvironment());
    Assert.Single(engine.AllCommands);
    Assert.False(engine.AllCommands[0].IsBroken);

    var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });

    var result = Assert.Single(results);
    Assert.True(result.Success);
    Assert.Contains("active", (string)result.ReturnValue!);
}
```
(`PluginsRoot` resolved the same way `DuplicateFinderPluginTests` does -
`Path.Combine(AppContext.BaseDirectory, "SamplePlugins")` - both sample plugins live side by side
under the one glob-copied folder, `PluginEngine.Discover` walks all of them regardless of which
test asks; scope this test's own `Discover` call to a subfolder if that cross-plugin visibility
turns out to make assertions awkward, but try the shared-root approach first since
`DuplicateFinderPluginTests` already assumes exactly 3 commands exist under the shared root, so a
new sample plugin changes that count - check and update that assertion if needed.)

**Depends on:** Step 4 (needs the real manifest-to-`PythonCommand` dispatch working).

**Verify:** `dotnet test src/Paperbunkr.Plugins.Tests/Paperbunkr.Plugins.Tests.csproj` - full
project run, not just the filtered new tests, specifically because this step's own note above
flags a likely need to update `DuplicateFinderPluginTests.All_three_commands_compile_cleanly`'s
hardcoded command count.

## Step 7: Full regression pass

**Verify:**
- `dotnet test src/Paperbunkr.Plugins.Tests/Paperbunkr.Plugins.Tests.csproj` (full).
- `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj` (full) - `PaperbunkrApplication`/
  `PluginHostService` are untouched by this feature, but this is the same regression-safety pass
  the gaps-1-3 work already established as this branch's convention; expect it to stay clean.
- Commit.

## Notes for whoever picks this up

- Step 1 is the one step worth *not* skipping ahead of - if the IronPython package genuinely
  doesn't restore/build clean against `net10.0` despite the design's web-research feasibility
  check, that's a stop-and-reconsider moment, not a "work around it and continue" one.
- Steps 2-3 have no real dependency on each other and can be done in either order or together;
  Step 4 needs both.
- The sandbox probe in Step 5 is the one piece of this plan whose "correct" outcome is genuinely
  unknown ahead of time - both outcomes are valid, treat whichever one actually happens as a real
  finding to report, not a test to make pass by construction.
