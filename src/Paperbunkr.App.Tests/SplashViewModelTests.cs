using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="SplashViewModel"/> - phase progress reporting and the 400ms anti-flash floor
/// (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-design.md, Decision 2).
/// </summary>
public class SplashViewModelTests
{
    [Fact]
    public void ReportPhase_AdvancesProgressAndStatus()
    {
        var vm = new SplashViewModel(_ => Task.CompletedTask);

        vm.ReportPhase(0, 4, "Checking database…");
        Assert.Equal(0d, vm.Progress);
        Assert.Equal("Checking database…", vm.StatusMessage);

        vm.ReportPhase(2, 4, "Loading appearance…");
        Assert.Equal(0.5d, vm.Progress);
        Assert.Equal("Loading appearance…", vm.StatusMessage);

        vm.ReportPhase(4, 4, "Almost there…");
        Assert.Equal(1d, vm.Progress);
    }

    [Fact]
    public void ReportPhase_ClampsAndIgnoresBlankMessage()
    {
        var vm = new SplashViewModel(_ => Task.CompletedTask);
        vm.ReportPhase(1, 2, "Phase");

        vm.ReportPhase(9, 2, "  ");

        Assert.Equal(1d, vm.Progress);
        Assert.Equal("Phase", vm.StatusMessage); // blank message left the previous one in place
    }

    [Fact]
    public async Task EnforceMinimumVisibleAsync_WaitsOutTheRemainingFloor_WhenInitFinishedEarly()
    {
        var delays = new List<TimeSpan>();
        var vm = new SplashViewModel(d => { delays.Add(d); return Task.CompletedTask; });

        // "Shown" 100ms ago -> most of the ~1.1s floor still to wait.
        await vm.EnforceMinimumVisibleAsync(DateTime.UtcNow - TimeSpan.FromMilliseconds(100));

        Assert.Single(delays);
        Assert.InRange(delays[0], TimeSpan.FromMilliseconds(700), SplashViewModel.MinimumVisible);
    }

    [Fact]
    public async Task EnforceMinimumVisibleAsync_ReturnsImmediately_WhenFloorAlreadyElapsed()
    {
        var delays = new List<TimeSpan>();
        var vm = new SplashViewModel(d => { delays.Add(d); return Task.CompletedTask; });

        await vm.EnforceMinimumVisibleAsync(DateTime.UtcNow - TimeSpan.FromSeconds(5));

        Assert.Empty(delays);
    }

    [Fact]
    public void VersionText_CarriesTheBetaDisplayString()
    {
        var vm = new SplashViewModel(_ => Task.CompletedTask);
        Assert.StartsWith("Paperbunkr ", vm.VersionText);
        Assert.EndsWith("-beta", vm.VersionText);
    }
}
