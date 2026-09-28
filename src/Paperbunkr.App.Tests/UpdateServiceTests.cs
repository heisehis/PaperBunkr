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

    [Fact]
    public void ApplyUpdatesAndRestart_MissingInstaller_ReturnsFailure()
    {
        var service = new UpdateService();
        string missing = Path.Combine(Path.GetTempPath(), $"paperbunkr_update_missing_{Guid.NewGuid():N}.exe");

        string? failure = service.ApplyUpdatesAndRestart(new NetSparkleUpdater.AppCastItem(), missing);

        Assert.NotNull(failure);
    }

    [Fact]
    public void ApplyUpdatesAndRestart_BadSignature_ReturnsFailure_WithoutRunningIt()
    {
        var service = new UpdateService();
        // .txt, not .exe: if verification were ever skipped, shell-executing this opens a text
        // file rather than running anything.
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_update_unsigned_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "not a real installer");
        try
        {
            var item = new NetSparkleUpdater.AppCastItem { DownloadSignature = "AAAA" };

            string? failure = service.ApplyUpdatesAndRestart(item, path);

            Assert.NotNull(failure);
            Assert.Contains("signature", failure);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
