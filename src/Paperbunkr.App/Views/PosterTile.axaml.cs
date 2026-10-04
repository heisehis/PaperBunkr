using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;

namespace Paperbunkr.App.Views;

/// <summary>
/// PosterTile primitive (docs/superpowers/specs/2026-08-24-design-language-foundation-design.md
/// Component Primitives section) - surface2 background, glow-ring hover/focus, optional
/// badge/progress-bar slots. Consumed by the Phase-1 showcase view now; real Library/Home grids
/// (Phases 3-4) place instances of this rather than reinventing card markup per screen.
/// </summary>
public partial class PosterTile : UserControl
{
    public static readonly StyledProperty<IImage?> CoverSourceProperty =
        AvaloniaProperty.Register<PosterTile, IImage?>(nameof(CoverSource));

    public static readonly StyledProperty<string?> TitleTextProperty =
        AvaloniaProperty.Register<PosterTile, string?>(nameof(TitleText));

    public static readonly StyledProperty<string?> MetaTextProperty =
        AvaloniaProperty.Register<PosterTile, string?>(nameof(MetaText));

    /// <summary>Null/empty hides the badge slot entirely.</summary>
    public static readonly StyledProperty<string?> BadgeTextProperty =
        AvaloniaProperty.Register<PosterTile, string?>(nameof(BadgeText));

    public static readonly StyledProperty<bool> ShowProgressProperty =
        AvaloniaProperty.Register<PosterTile, bool>(nameof(ShowProgress));

    /// <summary>0.0-1.0. Only rendered when <see cref="ShowProgress"/> is true.</summary>
    public static readonly StyledProperty<double> ProgressFractionProperty =
        AvaloniaProperty.Register<PosterTile, double>(nameof(ProgressFraction));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CommandProperty =
        AvaloniaProperty.Register<PosterTile, System.Windows.Input.ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<PosterTile, object?>(nameof(CommandParameter));

    // --- Home pitch additions (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md / -improvements-design.md) ---

    /// <summary>"NEW" corner ribbon (C5).</summary>
    public static readonly StyledProperty<bool> ShowNewRibbonProperty =
        AvaloniaProperty.Register<PosterTile, bool>(nameof(ShowNewRibbon));

    /// <summary>Exactly four covers for a 2x2 collage (C6), or null for the single <see cref="CoverSource"/>.</summary>
    public static readonly StyledProperty<IReadOnlyList<IImage?>?> MosaicSourcesProperty =
        AvaloniaProperty.Register<PosterTile, IReadOnlyList<IImage?>?>(nameof(MosaicSources));

    /// <summary>Small book marker for books in the merged Continue Reading row (I2).</summary>
    public static readonly StyledProperty<bool> ShowBookGlyphProperty =
        AvaloniaProperty.Register<PosterTile, bool>(nameof(ShowBookGlyph));

    /// <summary>When set, a hover ✕ runs it with <see cref="CommandParameter"/> - "Not interested" (I5).</summary>
    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<PosterTile, ICommand?>(nameof(DismissCommand));

    /// <summary>Accent outline - the Because-You-Read lead card (C8).</summary>
    public static readonly StyledProperty<bool> OutlinedProperty =
        AvaloniaProperty.Register<PosterTile, bool>(nameof(Outlined));

    private bool _fillShown;

    static PosterTile()
    {
        ProgressFractionProperty.Changed.AddClassHandler<PosterTile>((tile, _) => tile.UpdateFill(animate: false));
    }

    public PosterTile()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        KeyDown += OnKeyDown;
    }

    public bool ShowNewRibbon
    {
        get => GetValue(ShowNewRibbonProperty);
        set => SetValue(ShowNewRibbonProperty, value);
    }

    public IReadOnlyList<IImage?>? MosaicSources
    {
        get => GetValue(MosaicSourcesProperty);
        set => SetValue(MosaicSourcesProperty, value);
    }

    public bool ShowBookGlyph
    {
        get => GetValue(ShowBookGlyphProperty);
        set => SetValue(ShowBookGlyphProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public bool Outlined
    {
        get => GetValue(OutlinedProperty);
        set => SetValue(OutlinedProperty, value);
    }

    /// <summary>The fill's current scale (0-1) - exposed for tests.</summary>
    public double FillScale => ProgressFill.RenderTransform is ScaleTransform scale ? scale.ScaleX : 1;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_fillShown)
        {
            UpdateFill(animate: false);
            return;
        }

        // C4: grow the fill in once, the first time this tile appears. Starts at 0, then moves to its value a tick later so the
        // ScaleX transition runs; Reduced Motion (zero token) makes that instant.
        _fillShown = true;
        ProgressFill.RenderTransform = new ScaleTransform(0, 1)
        {
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = ScaleTransform.ScaleXProperty,
                    Duration = TimeSpan.FromTicks(Controls.MotionTokens.GetStandard().Ticks * 3),
                    Easing = new CubicEaseOut(),
                },
            },
        };
        Dispatcher.UIThread.Post(() => UpdateFill(animate: true));
    }

    private void UpdateFill(bool animate)
    {
        if (ProgressFill is null)
        {
            return; // a property set during XAML load, before InitializeComponent wired the part
        }

        double value = Math.Clamp(ProgressFraction, 0, 1);
        if (ProgressFill.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = value;
        }
        else
        {
            ProgressFill.RenderTransform = new ScaleTransform(value, 1);
        }
    }

    // Root is a Border, not a Button - Border directly supports BoxShadow (needed for the
    // glow-ring hover/focus treatment) without depending on whether Avalonia's Button promotes
    // that property onto TemplatedControl. Focusable="True" on the root (set in the .axaml) is
    // what makes :pointerover/:focus-visible pseudo-classes actually apply.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Left button only - a right-click is for the card's context menu (Because-You-Read's "Not interested").
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    // Keyboard reach: Enter/Space open the tile (what a click does), Delete runs "Not interested" when the tile has one, and the arrows walk
    // the shelf - Left/Right/Home/End along the row, Up/Down to the nearest tile in the shelf above or below. Only the tile's own root
    // handles them: the hover-dismiss Button inside keeps its own Enter/Space.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !ReferenceEquals(e.Source, Root) || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter or Key.Space:
                if (Command?.CanExecute(CommandParameter) == true)
                {
                    Command.Execute(CommandParameter);
                }

                e.Handled = true;
                break;
            case Key.Delete:
                if (DismissCommand?.CanExecute(CommandParameter) == true)
                {
                    DismissWithKeyboard();
                    e.Handled = true;
                }

                break;
            case Key.Left or Key.Right or Key.Home or Key.End:
                e.Handled = MoveAlongShelf(e.Key);
                break;
            case Key.Up or Key.Down:
                e.Handled = MoveToNeighbouringShelf(e.Key == Key.Up ? NavigationDirection.Up : NavigationDirection.Down);
                break;
        }
    }

    // Dismissing removes this tile, so it runs one dispatcher tick after the key event has finished routing (removing a control from inside
    // the event it is still raising corrupts the visual tree - see CLAUDE.md), and focus moves to the tile that takes its place in the row.
    private void DismissWithKeyboard()
    {
        var command = DismissCommand;
        var parameter = CommandParameter;
        var row = ShelfRow();
        int at = row.IndexOf(this);
        var neighbour = at >= 0 ? (at + 1 < row.Count ? row[at + 1] : at > 0 ? row[at - 1] : null) : null;
        object? neighbourKey = neighbour?.CommandParameter;
        var scope = this.FindAncestorOfType<ScrollViewer>() as Visual ?? this.FindAncestorOfType<ItemsControl>();

        Dispatcher.UIThread.Post(() =>
        {
            command?.Execute(parameter);
            Dispatcher.UIThread.Post(() =>
            {
                PosterTile? target = neighbour is { } n && n.IsAttachedToVisualTree() ? n : null;
                if (target is null && scope is not null && neighbourKey is not null)
                {
                    target = scope.GetVisualDescendants().OfType<PosterTile>().FirstOrDefault(t => Equals(t.CommandParameter, neighbourKey));
                }

                target?.Root.Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Loaded);
        });
    }

    // A shelf is whatever tiles sit in the same visual row inside the nearest scroller: Because-You-Read puts its lead card outside the
    // row's item list, so walking the ItemsControl's containers would skip it and, from the lead card, land in the next row.
    private List<PosterTile> ShelfRow()
    {
        Visual? scope = this.FindAncestorOfType<ScrollViewer>() ?? (Visual?)this.FindAncestorOfType<ItemsControl>();
        if (scope is null || RectIn(this, scope) is not { } mine)
        {
            return new List<PosterTile>();
        }

        return scope.GetVisualDescendants().OfType<PosterTile>()
            .Where(t => t.IsEffectivelyVisible && t.Root.Focusable)
            .Select(t => (Tile: t, Rect: RectIn(t, scope)))
            .Where(t => t.Rect is { } r && r.Top < mine.Bottom && r.Bottom > mine.Top)
            .OrderBy(t => t.Rect!.Value.Left)
            .Select(t => t.Tile)
            .ToList();
    }

    private bool MoveAlongShelf(Key key)
    {
        var row = ShelfRow();
        int index = row.IndexOf(this);
        int target = key switch
        {
            Key.Left => index - 1,
            Key.Right => index + 1,
            Key.Home => 0,
            _ => row.Count - 1,
        };

        // Handled even when there is nowhere to go: an arrow must never scroll the shelf away from the focused tile. The one exception is Left on the first tile of a shelf, which has the nav
        // rail to its left, so it goes on to the screen and the shell.
        if (index >= 0 && target >= 0 && target < row.Count && target != index)
        {
            row[target].Root.Focus(NavigationMethod.Directional);
            FocusReclaimer.BringIntoViewWithRing(row[target]);
            return true;
        }

        return !(key == Key.Left && index == 0);
    }

    private static Rect? RectIn(Visual tile, Visual scope)
    {
        var topLeft = tile.TranslatePoint(default, scope);
        var bottomRight = tile.TranslatePoint(new Point(tile.Bounds.Width, tile.Bounds.Height), scope);
        return topLeft is { } a && bottomRight is { } b ? new Rect(a, b) : null;
    }

    private bool MoveToNeighbouringShelf(NavigationDirection direction)
    {
        // Search only inside this screen's outermost scroller: the whole window also holds the nav rail, status bar and closed overlays.
        Visual? page = null;
        for (var v = this.GetVisualParent(); v is not null; v = v.GetVisualParent())
        {
            if (v is ScrollViewer)
            {
                page = v;
            }
        }

        var options = page is InputElement root ? FocusReclaimer.InRegion(root) : new FindNextElementOptions { IgnoreOcclusivity = true };
        if (TopLevel.GetTopLevel(this)?.FocusManager?.FindNextElement(direction, options) is { } next)
        {
            next.Focus(NavigationMethod.Directional);
            if (next is Control moved)
            {
                FocusReclaimer.BringIntoViewWithRing(moved);
            }
        }

        return true;
    }

    public IImage? CoverSource
    {
        get => GetValue(CoverSourceProperty);
        set => SetValue(CoverSourceProperty, value);
    }

    public string? TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string? MetaText
    {
        get => GetValue(MetaTextProperty);
        set => SetValue(MetaTextProperty, value);
    }

    public string? BadgeText
    {
        get => GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public bool ShowProgress
    {
        get => GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    public double ProgressFraction
    {
        get => GetValue(ProgressFractionProperty);
        set => SetValue(ProgressFractionProperty, value);
    }

    public System.Windows.Input.ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }
}
