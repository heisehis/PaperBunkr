using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Paperbunkr.App.Controls;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class HomeScreen : UserControl
{
    // Scoped-down Ken-Burns (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md §4)
    // - one slow pan/scale per spotlight rotation, not IterationCount="Infinite": the masthead
    // subskill flags perpetual looping decorative animation on anything but a small element as a
    // common mistake, and a full-bleed 210px background is exactly that. Alternates between two
    // fixed transforms on SpotlightAccentColor change (recomputed on every rotation/click/load, see
    // HomeScreenViewModel.UpdateSpotlightAccentColor) - the XAML-declared Transitions on
    // MastheadVisualLayer animates the move smoothly, this code only decides the destination.
    private static readonly ITransform PannedTransform = new TransformGroup
    {
        Children = { new ScaleTransform(1.09, 1.09), new TranslateTransform(-20, -14) },
    };

    private Panel? _mastheadVisualLayer;
    private Border? _heroCardWrapper;
    private HomeScreenViewModel? _viewModel;
    private bool _kenBurnsAlt;
    private bool _firstReadyEffectsStarted;
    private DispatcherTimer? _heroFadeBackTimer;

    public HomeScreen()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) =>
        {
            _mastheadVisualLayer = this.FindControl<Panel>("MastheadVisualLayer");
            _heroCardWrapper = this.FindControl<Border>("HeroCardWrapper");
            TryStartFirstReadyEffects();
        };
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as HomeScreenViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        TryStartFirstReadyEffects();
    }

    /// <summary>
    /// Kicks off the masthead's first Ken-Burns step and the shelf entrance-stagger as soon as both
    /// the view and its ViewModel are ready, instead of relying on flags/events the ViewModel raised
    /// during its own constructor. Fixes two real bugs found via a headless probe, same root cause
    /// for both: <see cref="HomeScreenViewModel"/>'s constructor computes
    /// <see cref="HomeScreenViewModel.SpotlightAccentColor"/> and (previously) flipped
    /// <see cref="HomeScreenViewModel.PlayEntranceAnimation"/> before this View exists - so the
    /// PropertyChanged this View would need to react to fired into the void, and the XAML-bound
    /// <c>EntranceAnimation.Enabled</c> attached property was already <see langword="true"/> by the
    /// time its container-preparation subscription got wired, meaning the already-realized
    /// containers were never re-visited. Neither the masthead nor the shelves ever animated on the
    /// very first display of Home - confirmed empirically, not assumed - only on a later spotlight
    /// rotation or an explicit Refresh, where the View is already attached and subscribed. The hero
    /// crossfade (below) has the same "first change is missed" shape and is left as-is: a silent
    /// snap-in on first load reads fine (it's the screen's very first paint), the gap only matters
    /// for actual rotations/clicks afterward, which this doesn't affect.
    /// <see cref="DataContextChanged"/> and <see cref="Visual.AttachedToVisualTree"/> can happen in
    /// either order, so both call this; <see cref="_firstReadyEffectsStarted"/> makes it a one-shot
    /// regardless of which fires second.
    /// </summary>
    private void TryStartFirstReadyEffects()
    {
        if (_firstReadyEffectsStarted || _viewModel is null || _mastheadVisualLayer is null)
        {
            return;
        }

        _firstReadyEffectsStarted = true;
        ApplyKenBurnsStep();
        _viewModel.PlayEntranceAnimation = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeScreenViewModel.SpotlightAccentColor))
        {
            ApplyKenBurnsStep();
        }
        else if (e.PropertyName == nameof(HomeScreenViewModel.CurrentSpotlight))
        {
            ApplyHeroCrossfade();
        }
    }

    private void ApplyKenBurnsStep()
    {
        if (_mastheadVisualLayer is null)
        {
            return;
        }

        if (MotionTokens.IsReducedMotion())
        {
            _mastheadVisualLayer.RenderTransform = null;
            return;
        }

        _kenBurnsAlt = !_kenBurnsAlt;
        _mastheadVisualLayer.RenderTransform = _kenBurnsAlt ? PannedTransform : null;
    }

    /// <summary>
    /// Crossfades the hero card when the spotlight rotates/is clicked (docs/superpowers/specs/
    /// 2026-09-08-home-navrail-visual-v2-design.md §4 follow-up - a real gap the shipped v2 left:
    /// swapping had no transition at all, DetailHero's bound properties just updated instantly).
    /// DetailHero itself stays untouched per the design's own boundary - its content updates
    /// synchronously the instant <c>SpotlightHeader</c> re-fires PropertyChanged, so there's no
    /// "old content" to hold onto for a true crossfade. This dips <see cref="HeroCardWrapper"/>'s
    /// opacity down (the XAML-declared <c>PbMotionStandard</c> transition animates it) and restores
    /// it after roughly that same duration, so the actually-instant content swap happens while the
    /// card is near-invisible - reads as a soft crossfade rather than a hard cut. Reduced Motion
    /// collapses <c>PbMotionStandard</c> to zero, so both the dip and the restore become instant
    /// no-ops with no separate check needed here.
    /// </summary>
    private void ApplyHeroCrossfade()
    {
        if (_heroCardWrapper is null)
        {
            return;
        }

        _heroFadeBackTimer?.Stop();

        _heroCardWrapper.Opacity = 0.05;

        _heroFadeBackTimer = new DispatcherTimer { Interval = MotionTokens.GetStandard() };
        _heroFadeBackTimer.Tick += (_, _) =>
        {
            _heroFadeBackTimer!.Stop();
            if (_heroCardWrapper is not null)
            {
                _heroCardWrapper.Opacity = 1;
            }
        };
        _heroFadeBackTimer.Start();
    }

    // Hero hover-pause (docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md §5) -
    // DetailHero itself has no pointer wiring and stays untouched per the design; these two handlers
    // live on the Home-side wrapper StackPanel (hero card + dots) instead.
    private void HeroRegion_PointerEntered(object? sender, PointerEventArgs e) => _viewModel?.PauseSpotlightRotation();

    private void HeroRegion_PointerExited(object? sender, PointerEventArgs e) => _viewModel?.ResumeSpotlightRotation();
}
