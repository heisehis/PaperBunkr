using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Paperbunkr.App.Controls;

/// <summary>An ItemsControl whose tag chips can be limited to a few measured wrap rows (two unless <see cref="CollapsedRows"/> says otherwise).</summary>
public sealed class CollapsibleTagWall : ItemsControl
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<CollapsibleTagWall, bool>(nameof(IsExpanded));

    public static readonly StyledProperty<int> CollapsedRowsProperty =
        AvaloniaProperty.Register<CollapsibleTagWall, int>(nameof(CollapsedRows), 2);

    public static readonly DirectProperty<CollapsibleTagWall, int> HiddenItemCountProperty =
        AvaloniaProperty.RegisterDirect<CollapsibleTagWall, int>(nameof(HiddenItemCount), o => o.HiddenItemCount);

    public static readonly DirectProperty<CollapsibleTagWall, bool> HasOverflowProperty =
        AvaloniaProperty.RegisterDirect<CollapsibleTagWall, bool>(nameof(HasOverflow), o => o.HasOverflow);

    private CollapsibleTagWrapPanel? _panel;
    private int _hiddenItemCount;
    private bool _hasOverflow;

    static CollapsibleTagWall()
    {
        IsExpandedProperty.Changed.AddClassHandler<CollapsibleTagWall>((wall, change) =>
        {
            wall._panel?.InvalidateMeasure();
            if (wall.IsExpanded)
            {
                wall.SetAndRaise(HiddenItemCountProperty, ref wall._hiddenItemCount, 0);
            }
            else if (wall._panel is { } panel)
            {
                wall.SetAndRaise(HiddenItemCountProperty, ref wall._hiddenItemCount, panel.OverflowItemCount);
            }
        });
    }

    public CollapsibleTagWall()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() =>
        {
            if (_panel is not null)
            {
                _panel.LayoutStateChanged -= OnLayoutStateChanged;
            }

            _panel = new CollapsibleTagWrapPanel { IsExpanded = IsExpanded, CollapsedRows = CollapsedRows };
            _panel.LayoutStateChanged += OnLayoutStateChanged;
            return _panel;
        });
    }

    protected override Type StyleKeyOverride => typeof(ItemsControl);

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>How many wrap rows show while collapsed.</summary>
    public int CollapsedRows
    {
        get => GetValue(CollapsedRowsProperty);
        set => SetValue(CollapsedRowsProperty, value);
    }

    public int HiddenItemCount => _hiddenItemCount;

    public bool HasOverflow => _hasOverflow;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty && _panel is not null)
        {
            _panel.IsExpanded = IsExpanded;
        }
        else if (change.Property == CollapsedRowsProperty && _panel is not null)
        {
            _panel.CollapsedRows = CollapsedRows;
        }
    }

    private void OnLayoutStateChanged(object? sender, EventArgs e)
    {
        if (sender is not CollapsibleTagWrapPanel panel)
        {
            return;
        }

        SetAndRaise(HiddenItemCountProperty, ref _hiddenItemCount, IsExpanded ? 0 : panel.OverflowItemCount);
        SetAndRaise(HasOverflowProperty, ref _hasOverflow, panel.OverflowItemCount > 0);
    }
}

/// <summary>
/// Wraps complete child controls and, while collapsed, arranges only the first <see cref="CollapsedRows"/> rows (two by default). Overflow
/// controls are made invisible so they cannot be clicked or focused until the wall is expanded.
/// </summary>
public sealed class CollapsibleTagWrapPanel : Panel
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<CollapsibleTagWrapPanel, bool>(nameof(IsExpanded));

    public static readonly StyledProperty<int> CollapsedRowsProperty =
        AvaloniaProperty.Register<CollapsibleTagWrapPanel, int>(nameof(CollapsedRows), 2);

    private readonly HashSet<Control> _hiddenByPanel = new();
    private readonly List<Placement> _placements = new();
    private int _overflowItemCount;

    public event EventHandler? LayoutStateChanged;

    static CollapsibleTagWrapPanel()
    {
        AffectsMeasure<CollapsibleTagWrapPanel>(IsExpandedProperty, CollapsedRowsProperty);
    }

    public CollapsibleTagWrapPanel()
    {
        Children.CollectionChanged += (_, _) =>
        {
            var current = Children.ToHashSet();
            foreach (var child in _hiddenByPanel.Where(c => !current.Contains(c)).ToArray())
            {
                _hiddenByPanel.Remove(child);
            }

            InvalidateMeasure();
        };
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>How many wrap rows show while collapsed.</summary>
    public int CollapsedRows
    {
        get => GetValue(CollapsedRowsProperty);
        set => SetValue(CollapsedRowsProperty, Math.Max(1, value));
    }

    public int OverflowItemCount => _overflowItemCount;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;

        _placements.Clear();
        double x = 0;
        double y = 0;
        double lineHeight = 0;
        int row = 0;
        int overflowCount = 0;

        foreach (var child in Children)
        {
            bool hiddenByPanel = _hiddenByPanel.Contains(child);
            Size desired;
            if (hiddenByPanel)
            {
                child.IsVisible = true;
            }

            // Measure every child, including hidden chips: font/theme changes and updated tag text
            // can alter wrapping even when the available width stays the same.
            child.Measure(new Size(width, double.PositiveInfinity));
            desired = child.DesiredSize;

            if (!child.IsVisible && !hiddenByPanel)
            {
                continue;
            }

            if (x > 0 && x + desired.Width > width)
            {
                x = 0;
                y += lineHeight;
                lineHeight = 0;
                row++;
            }

            _placements.Add(new Placement(child, new Rect(x, y, desired.Width, desired.Height), row));
            x += desired.Width;
            lineHeight = Math.Max(lineHeight, desired.Height);

            bool overflowsCollapsedRows = row >= CollapsedRows;
            if (overflowsCollapsedRows)
            {
                overflowCount++;
            }

            bool hidden = overflowsCollapsedRows && !IsExpanded;
            if (hidden)
            {
                _hiddenByPanel.Add(child);
                child.IsVisible = false;
            }
            else if (_hiddenByPanel.Remove(child))
            {
                child.IsVisible = true;
            }
        }

        _overflowItemCount = overflowCount;
        LayoutStateChanged?.Invoke(this, EventArgs.Empty);

        double visibleHeight = IsExpanded || overflowCount == 0
            ? y + lineHeight
            : _placements.Where(p => p.Row < CollapsedRows).Select(p => p.Bounds.Bottom).DefaultIfEmpty(0).Max();
        return new Size(double.IsInfinity(width) ? _placements.Select(p => p.Bounds.Right).DefaultIfEmpty(0).Max() : width, visibleHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var placement in _placements)
        {
            if (placement.Child.IsVisible)
            {
                placement.Child.Arrange(placement.Bounds);
            }
        }

        return finalSize;
    }

    private readonly record struct Placement(Control Child, Rect Bounds, int Row);
}
