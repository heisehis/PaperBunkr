using System.Collections.Immutable;
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
///
/// Sandbox note (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md §7,
/// verified at implementation time): Roslyn scripting's default <c>MetadataReferenceResolver</c>
/// (<c>RuntimeMetadataReferenceResolver</c>) <b>does</b> resolve <c>#r "SomeAssembly"</c> directives
/// against the running app's assemblies and NuGet package folders - a throwaway
/// <c>#r "Microsoft.EntityFrameworkCore"</c> script compiled cleanly and could name EF Core types.
/// <see cref="Options"/> pins <see cref="BlockedMetadataReferenceResolver"/> so directive-driven
/// assembly loading resolves to nothing: the fixed <see cref="ScriptOptions.WithReferences(System.Reflection.Assembly[])"/>
/// list is the complete set a script can ever see, and a script trying to pull in EF Core / any
/// other assembly this way fails to *compile* rather than gaining access.
/// </summary>
public sealed class CSharpCommand : Command
{
    private static readonly ScriptOptions Options = ScriptOptions.Default
        .WithReferences(
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Issue).Assembly,
            typeof(IPluginEnvironment).Assembly)
        .WithMetadataResolver(BlockedMetadataReferenceResolver.Instance)
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

/// <summary>
/// Closes the <c>#r</c>-directive assembly-loading hole (docs/superpowers/specs/2026-08-28-plugin-
/// api-v3-data-manager-design.md §7).
///
/// Verified at implementation time: Roslyn scripting's default resolver
/// (<c>RuntimeMetadataReferenceResolver</c>, via <see cref="ScriptMetadataResolver.Default"/>)
/// <b>does</b> honour <c>#r "SomeAssembly"</c> against the running app's loaded assemblies and its
/// NuGet package folders — a throwaway <c>#r "Microsoft.EntityFrameworkCore"</c> script compiled
/// cleanly and could name <c>DbContext</c>. This resolver:
/// <list type="bullet">
/// <item>resolves <b>every</b> <c>#r</c> directive to nothing, so the fixed
/// <see cref="ScriptOptions.WithReferences(System.Reflection.Assembly[])"/> list is the complete
/// set a script can ever name;</item>
/// <item>still delegates <em>transitive</em> assembly resolution (types reachable from the fixed
/// references) to the default resolver — otherwise a legitimate <c>Environment.App.GetLibraryBooks()</c>
/// script won't compile — but denies the EF Core / SQLite family specifically, so
/// <c>PaperbunkrDbContext</c>'s <c>DbContext</c> base still can't be pulled in that way either.</item>
/// </list>
/// </summary>
internal sealed class BlockedMetadataReferenceResolver : MetadataReferenceResolver
{
    public static readonly BlockedMetadataReferenceResolver Instance = new();

    private static readonly string[] DeniedAssemblyPrefixes =
    {
        "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite", "SQLitePCLRaw",
    };

    private readonly MetadataReferenceResolver _inner = ScriptMetadataResolver.Default;

    public override bool ResolveMissingAssemblies => true;

    public override PortableExecutableReference? ResolveMissingAssembly(MetadataReference definition, AssemblyIdentity referenceIdentity) =>
        IsDenied(referenceIdentity.Name) ? null : _inner.ResolveMissingAssembly(definition, referenceIdentity);

    public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties) =>
        ImmutableArray<PortableExecutableReference>.Empty;

    public override bool Equals(object? other) => other is BlockedMetadataReferenceResolver;

    public override int GetHashCode() => typeof(BlockedMetadataReferenceResolver).GetHashCode();

    private static bool IsDenied(string assemblyName) =>
        DeniedAssemblyPrefixes.Any(p => assemblyName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
