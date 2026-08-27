using System.Linq;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Cheap CI guardrail against architectural drift (docs/superpowers/specs/2026-08-19-metadata-model-
/// resolver-layering-design.md) - the concrete, adoptable half of the source architecture review's
/// "add an architecture test enforcing project boundaries" recommendation, applied to this
/// codebase's real 4-project structure (App/Common/Data/Engine) rather than a hypothetical
/// Domain/Application/Infrastructure split that review also proposed and this project explicitly
/// isn't adopting. No new NuGet dependency (no architecture-testing library needed) - reflecting on
/// <c>Assembly.GetReferencedAssemblies()</c> is enough for four projects with one known-good
/// dependency direction: App -&gt; {Common, Engine, Data}, Data -&gt; Engine -&gt; Common -&gt; nothing.
/// </summary>
public class ArchitectureBoundaryTests
{
    [Fact]
    public void Data_DoesNotReferenceApp()
    {
        var referenced = typeof(PaperbunkrDbContext).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("Paperbunkr.App", referenced);
    }

    [Fact]
    public void Engine_DoesNotReferenceAppOrData()
    {
        var referenced = typeof(cYo.Projects.ComicRack.Engine.BookEventArgs).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("Paperbunkr.App", referenced);
        Assert.DoesNotContain("Paperbunkr.Data", referenced);
    }

    [Fact]
    public void Common_ReferencesNothingElseInThisSolution()
    {
        var referenced = typeof(cYo.Common.BitUtility).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("Paperbunkr.App", referenced);
        Assert.DoesNotContain("Paperbunkr.Data", referenced);
        Assert.DoesNotContain("Paperbunkr.Engine", referenced);
    }
}
