using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// Regression guard for the sandbox fence (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-
/// manager-design.md §7): a <c>.csx</c> script must fail to <em>compile</em> (not throw at invoke
/// time) when it reaches for an internal engine type or tries to <c>#r</c> an assembly outside the
/// fixed reference set.
/// </summary>
public sealed class PluginSandboxTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pb-sandbox-" + Guid.NewGuid().ToString("N"));

    public PluginSandboxTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private CSharpCommand Compile(string code)
    {
        string script = Path.Combine(_dir, "s.csx");
        File.WriteAllText(script, code);
        var cmd = new CSharpCommand
        {
            PluginKey = "sandbox",
            Hook = PluginHooks.Startup,
            Key = "sandbox.s",
            Name = "s",
            ScriptPath = script,
        };
        cmd.PreCompile();
        return cmd;
    }

    [Fact]
    public void Script_ReferencingSmartListQueryBuilderByName_FailsToCompile()
    {
        var cmd = Compile("return typeof(Paperbunkr.Data.SmartLists.SmartListQueryBuilder).Name;");
        Assert.True(cmd.IsBroken);
        Assert.NotNull(cmd.CompileError);
    }

    [Fact]
    public void Script_ReferencingContinuityResolverByName_FailsToCompile()
    {
        var cmd = Compile("return typeof(Paperbunkr.Data.Metadata.ContinuityResolver).Name;");
        Assert.True(cmd.IsBroken);
        Assert.NotNull(cmd.CompileError);
    }

    [Fact]
    public void Script_OpeningItsOwnPaperbunkrDbContext_FailsToCompile()
    {
        // PaperbunkrDbContext's constructor stays public (broad first-party test usage), but a
        // script still can't open one: the constructor needs DbContextOptions<T> from
        // Microsoft.EntityFrameworkCore, which is neither in the fixed reference set nor reachable
        // via #r, and BlockedMetadataReferenceResolver denies pulling the EF Core family in
        // transitively too - so DbContext / DbContextOptions never become nameable.
        var cmd = Compile("""
            var ctx = new Paperbunkr.Data.PaperbunkrDbContext(null);
            return ctx.Issues.Count();
            """);
        Assert.True(cmd.IsBroken);
        Assert.NotNull(cmd.CompileError);
    }

    [Fact]
    public void Script_UsingHashRToPullInEntityFrameworkCore_FailsToCompile()
    {
        // The spec flagged this as unverified: Roslyn's default resolver DOES honour #r against
        // loaded assemblies / package folders. BlockedMetadataReferenceResolver closes it - the #r
        // contributes nothing, so naming an EF Core type is a compile error.
        var cmd = Compile("""
            #r "Microsoft.EntityFrameworkCore"
            return typeof(Microsoft.EntityFrameworkCore.DbContext).Name;
            """);
        Assert.True(cmd.IsBroken);
        Assert.NotNull(cmd.CompileError);
    }

    [Fact]
    public void Script_UsingOnlyTheCuratedSurface_StillCompiles()
    {
        // The fence must not break a well-formed plugin: entities + IPluginEnvironment are fine.
        var cmd = Compile("return Environment.App.GetLibraryBooks().Count();");
        Assert.False(cmd.IsBroken);
        Assert.Null(cmd.CompileError);
    }
}
