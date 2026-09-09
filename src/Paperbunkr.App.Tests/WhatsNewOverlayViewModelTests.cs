using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="WhatsNewOverlayViewModel"/> - the "entries since last run" selection and the two
/// display modes (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-design.md).
/// </summary>
public class WhatsNewOverlayViewModelTests
{
    private static readonly IReadOnlyList<ChangelogEntry> Entries =
    [
        new ChangelogEntry("0.3.0-beta", "2026-09-09", "### Added\n- thing three"),
        new ChangelogEntry("0.2.0-beta", "2026-09-01", "### Added\n- thing two"),
        new ChangelogEntry("0.1.1-alpha", "2026-08-01", "### Fixed\n- thing one"),
    ];

    [Fact]
    public void SelectEntriesSince_ReturnsOnlyStrictlyNewer_NewestFirst()
    {
        var picked = WhatsNewOverlayViewModel.SelectEntriesSince(Entries, "0.1.1.0");

        Assert.Equal(["0.3.0-beta", "0.2.0-beta"], picked.Select(e => e.Version));
    }

    [Fact]
    public void SelectEntriesSince_OneReleaseBehind_ReturnsJustTheNewest()
    {
        var picked = WhatsNewOverlayViewModel.SelectEntriesSince(Entries, "0.2.0.0");

        Assert.Equal(["0.3.0-beta"], picked.Select(e => e.Version));
    }

    [Fact]
    public void SelectEntriesSince_UpToDate_ReturnsEmpty()
    {
        Assert.Empty(WhatsNewOverlayViewModel.SelectEntriesSince(Entries, "0.3.0.0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void SelectEntriesSince_NullOrUnparseableLastRun_ReturnsEmpty(string? lastRun)
    {
        Assert.Empty(WhatsNewOverlayViewModel.SelectEntriesSince(Entries, lastRun));
    }

    [Fact]
    public void Show_MultiEntry_SetsUpdatedHeader()
    {
        var vm = new WhatsNewOverlayViewModel(() => { }, () => { });
        vm.Show(Entries, currentEntryOnly: false);

        Assert.False(vm.CurrentEntryOnly);
        Assert.Equal(3, vm.Entries.Count);
        Assert.StartsWith("Updated to Paperbunkr ", vm.HeaderText);
    }

    [Fact]
    public void Show_CurrentOnly_SetsWhatsNewHeader()
    {
        var vm = new WhatsNewOverlayViewModel(() => { }, () => { });
        vm.Show([Entries[0]], currentEntryOnly: true);

        Assert.True(vm.CurrentEntryOnly);
        Assert.Single(vm.Entries);
        Assert.StartsWith("What's new in Paperbunkr ", vm.HeaderText);
    }

    [Fact]
    public void OpenFullChangelog_ClosesThenNavigates()
    {
        var order = new List<string>();
        var vm = new WhatsNewOverlayViewModel(() => order.Add("close"), () => order.Add("nav"));

        vm.OpenFullChangelogCommand.Execute(null);

        Assert.Equal(["close", "nav"], order);
    }

    [Fact]
    public void GotIt_Closes()
    {
        int closes = 0;
        var vm = new WhatsNewOverlayViewModel(() => closes++, () => { });

        vm.GotItCommand.Execute(null);

        Assert.Equal(1, closes);
    }
}
