using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Controls.Books;

/// <summary>
/// One book cover with its badge, hover rating, select box and captions (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §2).
/// The DataContext is a <see cref="Models.BookCardSample"/>; the host supplies the commands. The cover is 2:3, so <see cref="CoverHeight"/>
/// follows <see cref="CoverWidth"/>.
/// </summary>
public partial class BookCoverCard : UserControl
{
    public const double DefaultCoverWidth = 128;

    public static readonly StyledProperty<double> CoverWidthProperty =
        AvaloniaProperty.Register<BookCoverCard, double>(nameof(CoverWidth), DefaultCoverWidth);

    public static readonly StyledProperty<double> CoverHeightProperty =
        AvaloniaProperty.Register<BookCoverCard, double>(nameof(CoverHeight), DefaultCoverWidth * 1.5);

    public static readonly StyledProperty<ICommand?> ClickCommandProperty =
        AvaloniaProperty.Register<BookCoverCard, ICommand?>(nameof(ClickCommand));

    /// <summary>Null hides the select box (Detail and the nightstand have no selection).</summary>
    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<BookCoverCard, ICommand?>(nameof(SelectCommand));

    /// <summary>Keeps every select box showing while anything is selected.</summary>
    public static readonly StyledProperty<bool> ForceSelectionVisibleProperty =
        AvaloniaProperty.Register<BookCoverCard, bool>(nameof(ForceSelectionVisible));

    public static readonly StyledProperty<bool> ShowCaptionsProperty =
        AvaloniaProperty.Register<BookCoverCard, bool>(nameof(ShowCaptions), true);

    public BookCoverCard() => InitializeComponent();

    public double CoverWidth
    {
        get => GetValue(CoverWidthProperty);
        set => SetValue(CoverWidthProperty, value);
    }

    /// <summary>Always <see cref="CoverWidth"/> × 1.5; set by the control, not the host.</summary>
    public double CoverHeight
    {
        get => GetValue(CoverHeightProperty);
        private set => SetValue(CoverHeightProperty, value);
    }

    public ICommand? ClickCommand
    {
        get => GetValue(ClickCommandProperty);
        set => SetValue(ClickCommandProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public bool ForceSelectionVisible
    {
        get => GetValue(ForceSelectionVisibleProperty);
        set => SetValue(ForceSelectionVisibleProperty, value);
    }

    public bool ShowCaptions
    {
        get => GetValue(ShowCaptionsProperty);
        set => SetValue(ShowCaptionsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CoverWidthProperty)
        {
            CoverHeight = CoverWidth * 1.5;
        }
    }
}
