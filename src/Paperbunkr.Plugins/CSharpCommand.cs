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
            "Paperbunkr.Data.Events",
            "Paperbunkr.Plugins",
            "Paperbunkr.Plugins.Abstractions.Native",
            "Paperbunkr.Plugins.Automation",
            "Paperbunkr.Plugins.Theme",
            "Paperbunkr.Plugins.Hooks");

    /// <summary>Absolute path to the .csx script this command runs, resolved by <see cref="XmlPluginInitializer"/> against the manifest's own folder.</summary>
    public required string ScriptPath { get; init; }

    private Script<object>? _script;

    // Compiled scripts by file, its last write time and hook, so a compile done ahead of discovery on another thread
    // (Warm) is the one PreCompile then uses. Only a finished compile is kept: a failure to read the file is not.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Path, DateTime Stamp, string Hook), (Script<object>? Script, string? Error)> s_compiled = new();

    public override void PreCompile()
    {
        try
        {
            var (script, error) = CompileCached(ScriptPath, Hook);
            _script = script;
            if (error is not null)
            {
                CompileError = error;
            }
        }
        catch (Exception ex)
        {
            CompileError = ex.Message;
            _script = null;
        }
    }

    /// <summary>
    /// Compiles a script ahead of <see cref="PreCompile"/>, from any thread. Compiling is the slow part of plugin discovery
    /// (about 6 s of UI-thread freeze after startup on a 2-core machine, 2026-10-11) and touches nothing but the file, so the
    /// app does it in the background and discovery then finds the result here. Never throws.
    /// </summary>
    public static void Warm(string scriptPath, string hook)
    {
        try
        {
            CompileCached(scriptPath, hook);
        }
        catch (Exception)
        {
            // PreCompile will meet the same failure and report it on the command.
        }
    }

    private static (Script<object>? Script, string? Error) CompileCached(string scriptPath, string hook) =>
        s_compiled.GetOrAdd((scriptPath, File.GetLastWriteTimeUtc(scriptPath), hook), static key =>
        {
            string code = File.ReadAllText(key.Path);
            Type globalsType = PluginGlobalsTypeMap.Resolve(key.Hook);
            var script = CSharpScript.Create<object>(code, Options, globalsType);
            var errors = script.Compile().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            return errors.Count > 0
                ? ((Script<object>?)null, string.Join('\n', errors.Select(e => e.ToString())))
                : (script, (string?)null);
        });

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

    private readonly MetadataReferenceResolver _inner = ScriptMetadataResolver.Default;

    public override bool ResolveMissingAssemblies => true;

    public override PortableExecutableReference? ResolveMissingAssembly(MetadataReference definition, AssemblyIdentity referenceIdentity) =>
        PluginSandboxDenylist.IsDenied(referenceIdentity.Name) ? null : _inner.ResolveMissingAssembly(definition, referenceIdentity);

    public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties) =>
        ImmutableArray<PortableExecutableReference>.Empty;

    public override bool Equals(object? other) => other is BlockedMetadataReferenceResolver;

    public override int GetHashCode() => typeof(BlockedMetadataReferenceResolver).GetHashCode();
}
