using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// Live spotlight tour overlay (docs/superpowers/specs/2026-08-31-first-run-onboarding-design.md) -
/// draws its own full-window dimmed scrim with a rectangular cutout around the current step's rail
/// button, plus a callout positioned near it. Bounds lookup is a UI-tree concern kept in code-behind
/// rather than <see cref="WelcomeTourOverlayViewModel"/>, which only knows step data/sequencing.
/// </summary>
public partial class WelcomeTourOverlay : UserControl
{
    private const double CutoutPadding = 6;

    private Path? _scrimPath;
    private Border? _callout;
    private WelcomeTourOverlayViewModel? _viewModel;

    public WelcomeTourOverlay()
    {
        InitializeComponent();
        _scrimPath = this.FindControl<Path>("ScrimPath");
        _callout = this.FindControl<Border>("Callout");

        DataContextChanged += (_, _) => AttachViewModel();
        SizeChanged += (_, _) => UpdateHighlight();
        AttachedToVisualTree += (_, _) => UpdateHighlight();
    }

    private void AttachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as WelcomeTourOverlayViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateHighlight();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WelcomeTourOverlayViewModel.CurrentStepIndex))
        {
            UpdateHighlight();
        }
    }

    /// <summary>
    /// Resolves the current step's target rail button by name and repositions the scrim cutout +
    /// callout around it. If the target can't be found (shouldn't happen - every target is a
    /// permanently-present rail button, not conditionally rendered), the step is skipped rather than
    /// rendering a broken frame, per the design's error-handling posture.
    /// </summary>
    private void UpdateHighlight()
    {
        if (_scrimPath is null || _callout is null || _viewModel is null || !IsEffectivelyVisible)
        {
            return;
        }

        var mainWindow = this.FindAncestorOfType<MainWindow>();
        var target = mainWindow?.FindControl<Control>(_viewModel.CurrentStep.TargetElementName);
        if (mainWindow is null || target is null)
        {
            _viewModel.NextCommand.Execute(null);
            return;
        }

        Point? origin = target.TranslatePoint(new Point(0, 0), this);
        if (origin is null)
        {
            _viewModel.NextCommand.Execute(null);
            return;
        }

        var cutout = new Rect(
            origin.Value.X - CutoutPadding,
            origin.Value.Y - CutoutPadding,
            target.Bounds.Width + CutoutPadding * 2,
            target.Bounds.Height + CutoutPadding * 2);

        _scrimPath.Data = new CombinedGeometry
        {
            GeometryCombineMode = GeometryCombineMode.Exclude,
            Geometry1 = new RectangleGeometry(new Rect(Bounds.Size)),
            Geometry2 = new RectangleGeometry(cutout),
        };

        // Callout sits to the right of the cutout with a little breathing room, clamped so it never
        // renders off the right edge of the window at any nav-rail width (collapsed 64px/expanded 200px).
        double calloutX = Math.Min(cutout.Right + 16, Math.Max(0, Bounds.Width - _callout.Bounds.Width - 16));
        double calloutY = Math.Max(0, origin.Value.Y);
        Canvas.SetLeft(_callout, calloutX);
        Canvas.SetTop(_callout, calloutY);
    }
}
