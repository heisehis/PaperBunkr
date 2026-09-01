using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="UpdateService"/> (docs/superpowers/specs/2026-09-01-auto-update-and-
/// changelog-design.md) - deliberately minimal. Unlike the earlier Velopack-based version, NetSparkle
/// has no "is this a managed install" concept to test around; the real check/download/apply cycle
/// only makes sense against a real GitHub release with a real appcast.xml, which doesn't exist under
/// the test runner - that's a manual verification step (push a tagged release, confirm the flow),
/// not something this suite can or should fake coverage of.
/// </summary>
public class UpdateServiceTests
{
    [Fact]
    public void Construction_DoesNotThrow()
    {
        var service = new UpdateService();

        Assert.NotNull(service);
    }
}
