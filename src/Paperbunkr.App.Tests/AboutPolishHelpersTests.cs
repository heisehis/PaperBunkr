using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The small pure helpers behind the About polish (docs/superpowers/specs/2026-09-26-about-polish-design.md): link routing, the
/// document registry, the copied version report and release-note URLs.
/// </summary>
public class AboutPolishHelpersTests
{
    [Theory]
    [InlineData("https://comicvine.gamespot.com/api/", LinkTargetKind.Web)]
    [InlineData("http://example.org", LinkTargetKind.Web)]
    [InlineData("LICENSE", LinkTargetKind.Document)]
    [InlineData("COMICVINE_NOTICE.md", LinkTargetKind.Document)]
    [InlineData("./third-party-notices.md", LinkTargetKind.Document)]
    [InlineData("README.md", LinkTargetKind.Ignored)]
    [InlineData("#section", LinkTargetKind.Ignored)]
    [InlineData("mailto:someone@example.org", LinkTargetKind.Ignored)]
    [InlineData("file:///C:/Windows/notepad.exe", LinkTargetKind.Ignored)]
    [InlineData("", LinkTargetKind.Ignored)]
    public void LinkTargetResolver_RoutesTargets(string target, LinkTargetKind expected)
    {
        Assert.Equal(expected, LinkTargetResolver.Resolve(target));
    }

    [Fact]
    public void LegalDocuments_OnlyLicenseIsPreformatted()
    {
        Assert.Equal(5, LegalDocuments.All.Count);
        Assert.True(LegalDocuments.Find("LICENSE")!.Preformatted);
        Assert.All(LegalDocuments.All, d => Assert.Equal(d.FileName == "LICENSE", d.Preformatted));
    }

    [Fact]
    public void VersionReport_WithBuild()
    {
        string report = AboutInfo.VersionReport("0.7.3-beta", "9cc0b62", "Microsoft Windows 10.0.26100 ", "x64", ".NET 10.0.2");

        Assert.Equal("Paperbunkr 0.7.3-beta (build 9cc0b62)\nMicrosoft Windows 10.0.26100 (x64)\n.NET 10.0.2", report);
    }

    [Fact]
    public void VersionReport_WithoutBuild_OmitsIt()
    {
        Assert.StartsWith("Paperbunkr 0.7.3-beta\n", AboutInfo.VersionReport("0.7.3-beta", null, "Windows", "x64", ".NET 10"));
    }

    [Fact]
    public void UpdateOverlay_LinksToTheOfferedVersionsReleasePage()
    {
        var vm = new Paperbunkr.App.ViewModels.UpdateAvailableOverlayViewModel(_ => System.Threading.Tasks.Task.CompletedTask, () => { });
        Assert.Null(vm.ReleaseNotesUrl);

        vm.Show(new NetSparkleUpdater.AppCastItem { Version = "0.7.1-beta" });

        Assert.Equal("v0.7.1-beta", vm.VersionText);
        Assert.Equal("https://github.com/heisehis/PaperBunkr/releases/tag/v0.7.1-beta", vm.ReleaseNotesUrl);
    }

    [Theory]
    [InlineData("0.7.0-beta", "https://github.com/heisehis/PaperBunkr/releases/tag/v0.7.0-beta")]
    [InlineData("v0.7.0-beta", "https://github.com/heisehis/PaperBunkr/releases/tag/v0.7.0-beta")]
    public void ReleaseNotes_UsesTheVTag(string version, string expected)
    {
        Assert.Equal(expected, ProjectLinks.ReleaseNotes(version));
    }
}
