using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Paperbunkr.App.Views.Reader;

/// <summary>Which of the three reader clusters a <see cref="ReaderCluster"/> shows.</summary>
public enum ReaderClusterPart
{
    Navigate,
    Actions,
    Page,
}

/// <summary>
/// One floating reader cluster (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §5.2). The host binds the commands it has;
/// a button with a null command hides itself. Previous / Next are events, not commands, because the EPUB reader turns a page in the page
/// script first and falls back to the view model (the same split the old <see cref="ReaderChrome"/> had).
/// </summary>
public partial class ReaderCluster : UserControl
{
    public static readonly StyledProperty<ReaderClusterPart> PartProperty = AvaloniaProperty.Register<ReaderCluster, ReaderClusterPart>(nameof(Part));
    public static readonly StyledProperty<IBrush?> ClusterBackgroundProperty = AvaloniaProperty.Register<ReaderCluster, IBrush?>(nameof(ClusterBackground));
    public static readonly StyledProperty<IBrush?> ClusterForegroundProperty = AvaloniaProperty.Register<ReaderCluster, IBrush?>(nameof(ClusterForeground));
    public static readonly StyledProperty<IBrush?> ClusterBorderProperty = AvaloniaProperty.Register<ReaderCluster, IBrush?>(nameof(ClusterBorder));
    public static readonly StyledProperty<IBrush?> HoverBrushProperty = AvaloniaProperty.Register<ReaderCluster, IBrush?>(nameof(HoverBrush));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ReaderCluster, string?>(nameof(Title));
    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<ReaderCluster, string?>(nameof(Subtitle));
    public static readonly StyledProperty<ICommand?> BackCommandProperty = AvaloniaProperty.Register<ReaderCluster, ICommand?>(nameof(BackCommand));
    public static readonly StyledProperty<ICommand?> ContentsCommandProperty = AvaloniaProperty.Register<ReaderCluster, ICommand?>(nameof(ContentsCommand));
    public static readonly StyledProperty<ICommand?> SearchCommandProperty = AvaloniaProperty.Register<ReaderCluster, ICommand?>(nameof(SearchCommand));
    public static readonly StyledProperty<ICommand?> BookmarkCommandProperty = AvaloniaProperty.Register<ReaderCluster, ICommand?>(nameof(BookmarkCommand));
    public static readonly StyledProperty<bool> IsBookmarkedProperty = AvaloniaProperty.Register<ReaderCluster, bool>(nameof(IsBookmarked));
    public static readonly StyledProperty<ICommand?> NotesCommandProperty = AvaloniaProperty.Register<ReaderCluster, ICommand?>(nameof(NotesCommand));
    public static readonly StyledProperty<ICommand?> CapturesCommandProperty = AvaloniaProperty.Register<ReaderCluster, ICommand?>(nameof(CapturesCommand));
    public static readonly StyledProperty<ICommand?> CaptureToggleCommandProperty = AvaloniaProperty.Register<ReaderCluster, ICommand?>(nameof(CaptureToggleCommand));
    public static readonly StyledProperty<bool> IsCaptureModeProperty = AvaloniaProperty.Register<ReaderCluster, bool>(nameof(IsCaptureMode));
    public static readonly StyledProperty<ICommand?> SettingsCommandProperty = AvaloniaProperty.Register<ReaderCluster, ICommand?>(nameof(SettingsCommand));
    public static readonly StyledProperty<bool> IsPreviousEnabledProperty = AvaloniaProperty.Register<ReaderCluster, bool>(nameof(IsPreviousEnabled), true);
    public static readonly StyledProperty<double> ProgressFractionProperty = AvaloniaProperty.Register<ReaderCluster, double>(nameof(ProgressFraction));
    public static readonly StyledProperty<IReadOnlyList<double>?> PartTicksProperty = AvaloniaProperty.Register<ReaderCluster, IReadOnlyList<double>?>(nameof(PartTicks));
    public static readonly StyledProperty<string?> LeftLabelProperty = AvaloniaProperty.Register<ReaderCluster, string?>(nameof(LeftLabel));
    public static readonly StyledProperty<string?> RightLabelProperty = AvaloniaProperty.Register<ReaderCluster, string?>(nameof(RightLabel));

    public ReaderCluster() => InitializeComponent();

    public event EventHandler<RoutedEventArgs>? PreviousRequested;

    public event EventHandler<RoutedEventArgs>? NextRequested;

    public ReaderClusterPart Part { get => GetValue(PartProperty); set => SetValue(PartProperty, value); }

    public bool IsNavigate => Part == ReaderClusterPart.Navigate;

    public bool IsActions => Part == ReaderClusterPart.Actions;

    public bool IsPage => Part == ReaderClusterPart.Page;

    public IBrush? ClusterBackground { get => GetValue(ClusterBackgroundProperty); set => SetValue(ClusterBackgroundProperty, value); }

    public IBrush? ClusterForeground { get => GetValue(ClusterForegroundProperty); set => SetValue(ClusterForegroundProperty, value); }

    public IBrush? ClusterBorder { get => GetValue(ClusterBorderProperty); set => SetValue(ClusterBorderProperty, value); }

    /// <summary>The theme's text colour at low alpha: hover and the progress track. Set from <see cref="ClusterForeground"/>.</summary>
    public IBrush? HoverBrush { get => GetValue(HoverBrushProperty); private set => SetValue(HoverBrushProperty, value); }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    public ICommand? BackCommand { get => GetValue(BackCommandProperty); set => SetValue(BackCommandProperty, value); }

    public ICommand? ContentsCommand { get => GetValue(ContentsCommandProperty); set => SetValue(ContentsCommandProperty, value); }

    public ICommand? SearchCommand { get => GetValue(SearchCommandProperty); set => SetValue(SearchCommandProperty, value); }

    public ICommand? BookmarkCommand { get => GetValue(BookmarkCommandProperty); set => SetValue(BookmarkCommandProperty, value); }

    public bool IsBookmarked { get => GetValue(IsBookmarkedProperty); set => SetValue(IsBookmarkedProperty, value); }

    public ICommand? NotesCommand { get => GetValue(NotesCommandProperty); set => SetValue(NotesCommandProperty, value); }

    public ICommand? CapturesCommand { get => GetValue(CapturesCommandProperty); set => SetValue(CapturesCommandProperty, value); }

    public ICommand? CaptureToggleCommand { get => GetValue(CaptureToggleCommandProperty); set => SetValue(CaptureToggleCommandProperty, value); }

    public bool IsCaptureMode { get => GetValue(IsCaptureModeProperty); set => SetValue(IsCaptureModeProperty, value); }

    public ICommand? SettingsCommand { get => GetValue(SettingsCommandProperty); set => SetValue(SettingsCommandProperty, value); }

    public bool IsPreviousEnabled { get => GetValue(IsPreviousEnabledProperty); set => SetValue(IsPreviousEnabledProperty, value); }

    public double ProgressFraction { get => GetValue(ProgressFractionProperty); set => SetValue(ProgressFractionProperty, value); }

    /// <summary>Where parts begin, as fractions of the book (0-1): a tick each on the progress bar.</summary>
    public IReadOnlyList<double>? PartTicks { get => GetValue(PartTicksProperty); set => SetValue(PartTicksProperty, value); }

    /// <summary>"p. 214" or "37%".</summary>
    public string? LeftLabel { get => GetValue(LeftLabelProperty); set => SetValue(LeftLabelProperty, value); }

    /// <summary>"640 · 2h 10m left".</summary>
    public string? RightLabel { get => GetValue(RightLabelProperty); set => SetValue(RightLabelProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PartProperty)
        {
            RaisePropertyChanged(IsNavigateProperty, false, IsNavigate);
            RaisePropertyChanged(IsActionsProperty, false, IsActions);
            RaisePropertyChanged(IsPageProperty, false, IsPage);
        }
        else if (change.Property == ClusterForegroundProperty)
        {
            HoverBrush = ClusterForeground is ISolidColorBrush solid
                ? new SolidColorBrush(Color.FromArgb(0x24, solid.Color.R, solid.Color.G, solid.Color.B))
                : null;
        }
    }

    public static readonly DirectProperty<ReaderCluster, bool> IsNavigateProperty =
        AvaloniaProperty.RegisterDirect<ReaderCluster, bool>(nameof(IsNavigate), c => c.IsNavigate);

    public static readonly DirectProperty<ReaderCluster, bool> IsActionsProperty =
        AvaloniaProperty.RegisterDirect<ReaderCluster, bool>(nameof(IsActions), c => c.IsActions);

    public static readonly DirectProperty<ReaderCluster, bool> IsPageProperty =
        AvaloniaProperty.RegisterDirect<ReaderCluster, bool>(nameof(IsPage), c => c.IsPage);

    private void OnPreviousClick(object? sender, RoutedEventArgs e) => PreviousRequested?.Invoke(this, e);

    private void OnNextClick(object? sender, RoutedEventArgs e) => NextRequested?.Invoke(this, e);
}
