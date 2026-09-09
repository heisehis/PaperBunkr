using System;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="MainViewModel.DecideFirstLook"/> - the mutually-exclusive startup modal decision
/// (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-design.md, Decision 7). Pure
/// static, no Avalonia/DB needed.
/// </summary>
public class StartupFirstLookDecisionTests
{
    private static readonly Version V030 = new(0, 3, 0, 0);

    [Fact]
    public void FreshInstall_IsWelcome()
    {
        Assert.Equal(MainViewModel.StartupFirstLook.Welcome,
            MainViewModel.DecideFirstLook(welcomeScreenShown: false, lastRunVersion: null, V030));
    }

    [Fact]
    public void WelcomeSeen_NoPriorVersion_IsUpdateCheck()
    {
        // e.g. an existing install upgrading from before LastRunVersion existed.
        Assert.Equal(MainViewModel.StartupFirstLook.UpdateCheck,
            MainViewModel.DecideFirstLook(welcomeScreenShown: true, lastRunVersion: null, V030));
    }

    [Fact]
    public void WelcomeSeen_OlderLastRun_IsWhatsNew()
    {
        Assert.Equal(MainViewModel.StartupFirstLook.WhatsNew,
            MainViewModel.DecideFirstLook(welcomeScreenShown: true, lastRunVersion: "0.2.0.0", V030));
    }

    [Fact]
    public void WelcomeSeen_SameLastRun_IsUpdateCheck()
    {
        Assert.Equal(MainViewModel.StartupFirstLook.UpdateCheck,
            MainViewModel.DecideFirstLook(welcomeScreenShown: true, lastRunVersion: "0.3.0.0", V030));
    }

    [Fact]
    public void WelcomeSeen_NewerLastRun_IsUpdateCheck_NotWhatsNew()
    {
        // Downgrade - never show What's New.
        Assert.Equal(MainViewModel.StartupFirstLook.UpdateCheck,
            MainViewModel.DecideFirstLook(welcomeScreenShown: true, lastRunVersion: "0.9.0.0", V030));
    }

    [Fact]
    public void WelcomeSeen_RevisionOnlyBump_IsUpdateCheck()
    {
        // Only the 4th segment differs - not a real release, not "newer".
        Assert.Equal(MainViewModel.StartupFirstLook.UpdateCheck,
            MainViewModel.DecideFirstLook(welcomeScreenShown: true, lastRunVersion: "0.3.0.5", V030));
    }

    [Fact]
    public void WelcomeSeen_GarbageLastRun_IsUpdateCheck()
    {
        Assert.Equal(MainViewModel.StartupFirstLook.UpdateCheck,
            MainViewModel.DecideFirstLook(welcomeScreenShown: true, lastRunVersion: "not-a-version", V030));
    }
}
