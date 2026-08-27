using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Hooks;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.Plugins;

/// <summary>
/// Replaces CE's <c>PythonCommand</c> - the C# scripting initializer from docs/superpowers/specs/
/// 2026-08-24-plugin-api-v2-design.md §2/§3. Compiles <see cref="ScriptPath"/> eagerly via
/// <see cref="PreCompile"/> against the globals type <see cref="Hooks.PluginGlobalsTypeMap"/>
/// resolves for this command's <see cref="Command.Hook"/>.
/// </summary>
public sealed class CSharpCommand : Command
{
    private static readonly ScriptOptions Options = ScriptOptions.Default
        .WithReferences(
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Issue).Assembly,
            typeof(IPluginEnvironment).Assembly)
        .WithImports(
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "System.Threading.Tasks",
            "Paperbunkr.Data.Entities",
            "Paperbunkr.Plugins",
            "Paperbunkr.Plugins.Automation",
            "Paperbunkr.Plugins.Theme",
            "Paperbunkr.Plugins.Hooks");

    /// <summary>Absolute path to the .csx script this command runs, resolved by <see cref="XmlPluginInitializer"/> against the manifest's own folder.</summary>
    public required string ScriptPath { get; init; }

    private Script<object>? _script;

    public override void PreCompile()
    {
        try
        {
            string code = File.ReadAllText(ScriptPath);
            Type globalsType = PluginGlobalsTypeMap.Resolve(Hook);
            var script = CSharpScript.Create<object>(code, Options, globalsType);
            var diagnostics = script.Compile();
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                CompileError = string.Join('\n', errors.Select(e => e.ToString()));
                _script = null;
                return;
            }

            _script = script;
        }
        catch (Exception ex)
        {
            CompileError = ex.Message;
            _script = null;
        }
    }

    protected override async Task<object?> OnInvokeAsync(PluginGlobals globals)
    {
        if (_script is null)
        {
            throw new InvalidOperationException($"Command '{Key}' has no compiled script (CompileError: {CompileError}).");
        }

        ScriptState<object> state = await _script.RunAsync(globals).ConfigureAwait(false);
        return state.ReturnValue;
    }
}
