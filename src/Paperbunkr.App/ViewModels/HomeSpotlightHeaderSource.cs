using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Adapts the Home spotlight carousel's current pick (a <see cref="SpotlightIssueSample"/>) to the
/// shared <see cref="IDetailHeaderSource"/> so the Home hero renders through the same
/// <c>DetailHero</c> control as the three detail screens (docs/superpowers/specs/
/// 2026-08-28-home-screen-redesign-design.md §3).
///
/// Holds no state of its own - it reads <see cref="HomeScreenViewModel.CurrentSpotlight"/> live via
/// the supplied accessor, and <see cref="RaiseChanged"/> is called by the ViewModel whenever the
/// carousel advances or reloads. All the rotation / timer / dot logic stays in the ViewModel.
/// </summary>
public sealed class HomeSpotlightHeaderSource : IDetailHeaderSource
{
    private readonly Func<SpotlightIssueSample?> _current;
    private readonly IReadOnlyList<DetailHeroAction> _actions;

    public HomeSpotlightHeaderSource(Func<SpotlightIssueSample?> current, ICommand openCommand)
    {
        _current = current;
        _actions = new[] { new DetailHeroAction("Read now", openCommand, IsPrimary: true) };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private SpotlightIssueSample? Pick => _current();

    public IBrush CoverBrush => Pick?.CoverBrush ?? Brushes.Transparent;
    public Bitmap? CoverImage => Pick?.CoverImage;
    public Bitmap? BackdropImage => Pick?.BackdropImage;
    public string HeaderTitle => Pick?.Title ?? string.Empty;
    public string? SecondaryTitle => Pick?.SeriesName;
    public string MetaLine => Pick?.Meta ?? string.Empty;
    public string? Synopsis => Pick?.Synopsis;
    public IReadOnlyList<DetailHeroAction> Actions => _actions;
    public DetailHeroProgress? TrackerProgress => null;

    /// <summary>Signals that the underlying pick changed - a null/empty property name tells every
    /// bound consumer to re-read all members, which is exactly what a carousel advance needs.</summary>
    public void RaiseChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
