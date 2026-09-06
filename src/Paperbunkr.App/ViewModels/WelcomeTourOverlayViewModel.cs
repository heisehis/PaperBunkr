using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>One stop of the live spotlight tour (docs/superpowers/specs/2026-08-31-first-run-
/// onboarding-design.md) - <see cref="TargetElementName"/> is the rail button's x:Name/AutomationId
/// in MainWindow.axaml, resolved to live bounds by WelcomeTourOverlay's own code-behind (a UI-tree
/// concern, deliberately kept out of this ViewModel).</summary>
public sealed record TourStep(string TargetElementName, string Title, string Body, Action Navigate);

/// <summary>
/// Drives the seven-stop live spotlight walkthrough of the nav rail, offered once after the
/// first-run welcome screen closes. Scoped to the lateral rail only (not screen-internal content -
/// see the design doc for why), so each step just re-invokes the matching <see cref="MainViewModel"/>
/// nav command and highlights the rail button that triggers it.
/// </summary>
public partial class WelcomeTourOverlayViewModel : ViewModelBase
{
    private readonly Action _onFinished;

    public WelcomeTourOverlayViewModel(
        IRelayCommand goHome, IRelayCommand goInsights, IRelayCommand goLibrary, IRelayCommand goBooks, IRelayCommand goSmart,
        IRelayCommand goReading, IRelayCommand goEvents, IRelayCommand goPreferences, Action onFinished)
    {
        _onFinished = onFinished;
        Steps = new List<TourStep>
        {
            new("HomeRailButton", "Home", "Your at-a-glance feed - what's new, what's in progress, what to read next.", () => goHome.Execute(null)),
            new("InsightsRailButton", "Insights", "Reading stats and a running list of what needs your attention - stalled series, near-complete runs, gaps in your collection.", () => goInsights.Execute(null)),
            new("LibraryRailButton", "Library", "Every comic and manga you own, sorted, grouped, and searchable however you like.", () => goLibrary.Execute(null)),
            new("BooksRailButton", "Books", "A separate shelf for EPUB and PDF novels, kept apart from the comics library.", () => goBooks.Execute(null)),
            new("SmartListsRailButton", "Smart Lists", "Rules-based lists that update themselves - \"Unread\", \"Recently Added\", or ones you build yourself.", () => goSmart.Execute(null)),
            new("ReadingListsRailButton", "Reading Lists", "Hand-curated reading orders, great for crossovers and event arcs that span series.", () => goReading.Execute(null)),
            new("EventsRailButton", "Continuity", "Track story events and continuities across series, so crossovers stay easy to follow.", () => goEvents.Execute(null)),
            new("PreferencesRailButton", "Preferences", "Everything else lives here - library folders, reader behavior, appearance, and more.", () => goPreferences.Execute(null)),
        };
    }

    public IReadOnlyList<TourStep> Steps { get; }

    [ObservableProperty]
    private int _currentStepIndex;

    public TourStep CurrentStep => Steps[CurrentStepIndex];

    public bool IsFirstStep => CurrentStepIndex == 0;

    public bool IsLastStep => CurrentStepIndex == Steps.Count - 1;

    partial void OnCurrentStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
    }

    /// <summary>Resets to the first stop and navigates there immediately, so the right screen is
    /// already showing behind the dim by the time the overlay becomes visible.</summary>
    public void Open()
    {
        CurrentStepIndex = 0;
        Steps[0].Navigate();
    }

    [RelayCommand]
    private void Next()
    {
        if (IsLastStep)
        {
            _onFinished();
            return;
        }

        CurrentStepIndex++;
        CurrentStep.Navigate();
    }

    [RelayCommand]
    private void Back()
    {
        if (IsFirstStep)
        {
            return;
        }

        CurrentStepIndex--;
        CurrentStep.Navigate();
    }

    [RelayCommand]
    private void Skip() => _onFinished();
}
