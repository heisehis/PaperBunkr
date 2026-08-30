using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins;

/// <summary>
/// Python-scripted sibling of <see cref="CSharpCommand"/> (docs/superpowers/specs/2026-08-30-
/// python-plugin-scripting-design.md), replacing CE's <c>PythonCommand</c> (IronPython 2.7 there,
/// IronPython 3.4 here - targets .NET 8.0/10.0, unlike the legacy 2.x NuGet line). Discovered the
/// same way <see cref="CSharpCommand"/> is - one <c>plugin.xml</c>, <see cref="XmlPluginInitializer"/>
/// dispatches by <see cref="Script"/>'s file extension, not a manifest schema difference.
///
/// <para>
/// Sandbox note (mirrors <see cref="CSharpCommand"/>'s own <see cref="BlockedMetadataReferenceResolver"/>
/// rationale, verify at test time - see <c>PythonCommandTests</c>' sandbox probe): the engine only
/// ever <see cref="ScriptRuntime.LoadAssembly"/>s the same fixed small set <c>CSharpCommand.Options</c>
/// already uses (BCL, <see cref="Issue"/>'s assembly, this assembly). EF Core/SQLite are never
/// registered with it - whether a script's own <c>clr.AddReference</c> can still probe an assembly
/// this process loaded for other reasons is the open question that test settles, not this comment.
/// </para>
/// </summary>
public sealed class PythonCommand : Command
{
    /// <summary>Absolute path to the .py script this command's <see cref="Method"/> lives in, resolved by <see cref="XmlPluginInitializer"/> against the manifest's own folder.</summary>
    public required string ScriptPath { get; init; }

    /// <summary>Top-level function name within <see cref="ScriptPath"/> this command invokes - a .py file can define several, unlike a .csx script whose whole body is the command.</summary>
    public required string Method { get; init; }

    private ScriptScope? _scope;

    /// <summary>
    /// Executes the whole .py file as a module into a fresh <see cref="ScriptScope"/>, once - a
    /// fresh <see cref="ScriptEngine"/> per command, no sharing across commands or across
    /// <see cref="PreCompile"/> calls, matching CE's own <c>PythonCommand.OnPreCompile</c> (not
    /// <see cref="CSharpCommand"/>'s shared static <c>Options</c>, since IronPython's hosting model
    /// doesn't separate "compile options" from "engine instance" the way Roslyn scripting does).
    /// </summary>
    public override void PreCompile()
    {
        try
        {
            ScriptEngine engine = Python.CreateEngine();
            engine.Runtime.LoadAssembly(typeof(object).Assembly); // BCL
            engine.Runtime.LoadAssembly(typeof(Issue).Assembly); // Paperbunkr.Data entities
            engine.Runtime.LoadAssembly(typeof(PluginGlobals).Assembly); // this assembly (Paperbunkr.Plugins)

            ScriptScope scope = engine.CreateScope();
            ScriptSource source = engine.CreateScriptSourceFromFile(ScriptPath);
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

        // Calling an IronPython function via C#'s dynamic binder is the standard IronPython-hosting
        // pattern - the DLR's callable protocol makes this work without any manual marshaling.
        // One positional argument (the same PluginGlobals-derived instance CSharpCommand's script
        // gets as its implicit globals object) - Python has no equivalent to Roslyn's globals-object
        // trick, so the calling convention is explicit here instead.
        dynamic function = _scope.GetVariable(Method);
        object? result = function(globals);
        return Task.FromResult(result);
    }
}
