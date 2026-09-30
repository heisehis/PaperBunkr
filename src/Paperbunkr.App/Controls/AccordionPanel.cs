using System;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A row where one child is open at a fixed width and the rest share what's left as slivers (docs/superpowers/specs/2026-09-29-home-
/// spotlight-accordion-design.md) - the Home spotlight's accordion carousel. The panel animates its own layout: on a change it
/// blends from the current widths to <see cref="AccordionLayout"/>'s new ones, so no child's <c>Width</c> is ever animated and the
/// row always adds up to its full width. Hovering a sliver peeks it wider; keyboard focus reaching a child (Tab or ←/→) runs
/// <see cref="ActivateCommand"/> with that child's DataContext. Reduced Motion jumps straight to the new layout.
/// </summary>
public sealed class AccordionPanel : Panel
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(450);
    private static readonly IEasing Ease = new CubicEaseInOut();

    public static readonly StyledProperty<int> OpenIndexProperty =
        AvaloniaProperty.Register<AccordionPanel, int>(nameof(OpenIndex));

    public static readonly StyledProperty<double> OpenWidthProperty =
        AvaloniaProperty.Register<AccordionPanel, double>(nameof(OpenWidth), 400);

    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<AccordionPanel, double>(nameof(MinItemWidth), 44);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AccordionPanel, double>(nameof(Spacing), 8);

    public static readonly StyledProperty<double> PeekWeightProperty =
        AvaloniaProperty.Register<AccordionPanel, double>(nameof(PeekWeight), 1.6);

    public static readonly StyledProperty<ICommand?> ActivateCommandProperty =
        AvaloniaProperty.Register<AccordionPanel, ICommand?>(nameof(ActivateCommand));

    private double[] _from = Array.Empty<double>();
    private double[] _to = Array.Empty<double>();
    private double[] _current = Array.Empty<double>();
    private DateTime _animationStart;
    private DispatcherTimer? _timer;
    private int _peekIndex = -1;
    private double _lastWidth = -1;

    static AccordionPanel()
    {
        AffectsMeasure<AccordionPanel>(OpenWidthProperty, MinItemWidthProperty, SpacingProperty, PeekWeightProperty);
        OpenIndexProperty.Changed.AddClassHandler<AccordionPanel>((panel, _) => panel.Retarget(animate: true));
    }

    public AccordionPanel()
    {
        PointerMoved += (_, e) => SetPeek(ChildIndexAt(e.GetPosition(this)));
        PointerExited += (_, _) => SetPeek(-1);
        AddHandler(GotFocusEvent, OnChildGotFocus);
        AddHandler(KeyDownEvent, OnKeyDown);
    }

    public int OpenIndex
    {
        get => GetValue(OpenIndexProperty);
        set => SetValue(OpenIndexProperty, value);
    }

    public double OpenWidth
    {
        get => GetValue(OpenWidthProperty);
        set => SetValue(OpenWidthProperty, value);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double PeekWeight
    {
        get => GetValue(PeekWeightProperty);
        set => SetValue(PeekWeightProperty, value);
    }

    public ICommand? ActivateCommand
    {
        get => GetValue(ActivateCommandProperty);
        set => SetValue(ActivateCommandProperty, value);
    }

    /// <summary>The widths the children are currently laid out at - exposed for tests.</summary>
    public double[] CurrentWidths => _current;

    private double[] Target(double width) => AccordionLayout.Compute(Children.Count, OpenIndex, _peekIndex, width, OpenWidth,
        MinItemWidth, Spacing, PeekWeight);

    private void SetPeek(int index)
    {
        if (index == OpenIndex)
        {
            index = -1; // the open child doesn't peek
        }

        if (index != _peekIndex)
        {
            _peekIndex = index;
            Retarget(animate: true);
        }
    }

    private void Retarget(bool animate)
    {
        if (_lastWidth <= 0)
        {
            InvalidateMeasure();
            return;
        }

        var target = Target(_lastWidth);
        if (!animate || MotionTokens.IsReducedMotion() || _current.Length != target.Length)
        {
            _timer?.Stop();
            _from = _to = _current = target;
            InvalidateMeasure();
            return;
        }

        _from = (double[])_current.Clone();
        _to = target;
        _animationStart = DateTime.UtcNow;
        _timer ??= CreateTimer();
        _timer.Start();
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double t = Math.Clamp((DateTime.UtcNow - _animationStart).TotalMilliseconds / Duration.TotalMilliseconds, 0, 1);
            double eased = Ease.Ease(t);
            _current = _from.Zip(_to, (a, b) => a + ((b - a) * eased)).ToArray();
            if (t >= 1)
            {
                timer.Stop();
                _current = _to;
            }

            InvalidateMeasure();
        };
        return timer;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? OpenWidth + (Children.Count * (MinItemWidth + Spacing)) : availableSize.Width;
        if (Math.Abs(width - _lastWidth) > 0.5 || _current.Length != Children.Count)
        {
            // A resize or a new child set: lay out straight at the target, no animation.
            _lastWidth = width;
            _from = _to = _current = Target(width);
        }

        double height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        double tallest = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(_current[i], availableSize.Height));
            tallest = Math.Max(tallest, Children[i].DesiredSize.Height);
        }

        return new Size(width, height > 0 ? height : tallest);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            double w = i < _current.Length ? _current[i] : 0;
            bool visible = w > 0.5;
            child.IsHitTestVisible = visible;
            child.Arrange(new Rect(x, 0, Math.Max(0, w), finalSize.Height));
            if (visible)
            {
                x += w + Spacing;
            }
        }

        return finalSize;
    }

    private int ChildIndexAt(Point point)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            if (Children[i].Bounds.Contains(point) && Children[i].IsHitTestVisible)
            {
                return i;
            }
        }

        return -1;
    }

    private int ChildIndexOf(object? source)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            if (source is Visual v && (ReferenceEquals(v, Children[i]) || Children[i].IsVisualAncestorOf(v)))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Keyboard focus landing on a closed child opens it (the demo's focus-in behaviour). Pointer focus doesn't - a click
    /// already runs the child's own command.</summary>
    private void OnChildGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod is not (NavigationMethod.Tab or NavigationMethod.Directional))
        {
            return;
        }

        int index = ChildIndexOf(e.Source);
        if (index >= 0 && index != OpenIndex)
        {
            Activate(index);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right) || Children.Count == 0)
        {
            return;
        }

        int from = ChildIndexOf(e.Source);
        if (from < 0)
        {
            from = OpenIndex;
        }

        int step = e.Key == Key.Right ? 1 : -1;
        int to = ((from + step) % Children.Count + Children.Count) % Children.Count;
        // Focus alone opens it (OnChildGotFocus) - activating here too would hit the now-open child and open the reader. Inside an
        // ItemsControl the children are item containers, which can't take focus themselves, so focus the control they hold.
        if (FocusTargetIn(Children[to]) is { } target)
        {
            target.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
    }

    private static Control? FocusTargetIn(Control child) =>
        child.Focusable && child.IsEffectivelyEnabled && child.IsEffectivelyVisible
            ? child
            : child.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);

    private void Activate(int index)
    {
        object? item = Children[index].DataContext;
        if (ActivateCommand?.CanExecute(item) == true)
        {
            ActivateCommand.Execute(item);
        }
    }
}
