using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The live spotlight tour's step sequencing (docs/superpowers/specs/2026-08-31-first-run-
/// onboarding-design.md) - pure ViewModel logic, no Avalonia rendering involved (bounds/cutout
/// geometry lives in WelcomeTourOverlay's own code-behind, out of scope for this test class).
/// </summary>
public class WelcomeTourOverlayViewModelTests
{
    private int _homeCount;
    private int _insightsCount;
    private int _libraryCount;
    private int _booksCount;
    private int _smartCount;
    private int _readingCount;
    private int _eventsCount;
    private int _preferencesCount;
    private int _finishedCount;

    private WelcomeTourOverlayViewModel CreateViewModel() => new(
        new RelayCommand(() => _homeCount++),
        new RelayCommand(() => _insightsCount++),
        new RelayCommand(() => _libraryCount++),
        new RelayCommand(() => _booksCount++),
        new RelayCommand(() => _smartCount++),
        new RelayCommand(() => _readingCount++),
        new RelayCommand(() => _eventsCount++),
        new RelayCommand(() => _preferencesCount++),
        () => _finishedCount++);

    [Fact]
    public void Steps_AreAllEightInRailOrder()
    {
        var vm = CreateViewModel();

        Assert.Equal(8, vm.Steps.Count);
        Assert.Equal(new[] { "Home", "Insights", "Library", "Books", "Smart Lists", "Reading Lists", "Continuity", "Preferences" },
            vm.Steps.Select(s => s.Title));
        Assert.Equal(
            new[] { "HomeRailButton", "InsightsRailButton", "LibraryRailButton", "BooksRailButton", "SmartListsRailButton", "ReadingListsRailButton", "EventsRailButton", "PreferencesRailButton" },
            vm.Steps.Select(s => s.TargetElementName));
    }

    [Fact]
    public void Open_ResetsToFirstStepAndNavigatesThere()
    {
        var vm = CreateViewModel();
        vm.CurrentStepIndex = 3;

        vm.Open();

        Assert.Equal(0, vm.CurrentStepIndex);
        Assert.Equal(1, _homeCount);
    }

    [Fact]
    public void Next_AdvancesAndNavigatesToNextStep()
    {
        var vm = CreateViewModel();
        vm.Open();

        vm.NextCommand.Execute(null);

        Assert.Equal(1, vm.CurrentStepIndex);
        Assert.Equal(1, _insightsCount);
    }

    [Fact]
    public void Next_OnLastStep_FinishesInsteadOfAdvancing()
    {
        var vm = CreateViewModel();
        vm.CurrentStepIndex = vm.Steps.Count - 1;

        vm.NextCommand.Execute(null);

        Assert.Equal(vm.Steps.Count - 1, vm.CurrentStepIndex);
        Assert.Equal(1, _finishedCount);
    }

    [Fact]
    public void Back_OnFirstStep_IsANoOp()
    {
        var vm = CreateViewModel();
        vm.Open();

        vm.BackCommand.Execute(null);

        Assert.Equal(0, vm.CurrentStepIndex);
        Assert.Equal(1, _homeCount); // only from Open(), Back did nothing more
    }

    [Fact]
    public void Back_MidTour_RetreatsAndRenavigates()
    {
        var vm = CreateViewModel();
        vm.CurrentStepIndex = 2;

        vm.BackCommand.Execute(null);

        Assert.Equal(1, vm.CurrentStepIndex);
        Assert.Equal(1, _insightsCount);
    }

    [Fact]
    public void Skip_Finishes()
    {
        var vm = CreateViewModel();

        vm.SkipCommand.Execute(null);

        Assert.Equal(1, _finishedCount);
    }

    [Fact]
    public void IsFirstStep_And_IsLastStep_ReflectBoundaries()
    {
        var vm = CreateViewModel();

        Assert.True(vm.IsFirstStep);
        Assert.False(vm.IsLastStep);

        vm.CurrentStepIndex = vm.Steps.Count - 1;

        Assert.False(vm.IsFirstStep);
        Assert.True(vm.IsLastStep);
    }
}
