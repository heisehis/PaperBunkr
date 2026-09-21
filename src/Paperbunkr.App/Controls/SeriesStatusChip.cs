using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using FluentIcons.Avalonia;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Series-status chip: coloured dot + icon + label in a <c>PbRadiusChip</c> squircle (docs/superpowers/specs/2026-09-21-
/// cosmetics-pitch-2-design.md #10). Colour is never the only signal - the icon and text both carry the status. All brushes
/// are bound to theme resources, so a skin switch restyles it live. Built in code (no .axaml) so it needs no XAML weave.
/// Hidden when <see cref="Status"/> is null/"Unknown".
/// </summary>
public sealed class SeriesStatusChip : Border
{
    public static readonly StyledProperty<string?> StatusProperty =
        AvaloniaProperty.Register<SeriesStatusChip, string?>(nameof(Status));

    private readonly Ellipse _dot = new() { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly SymbolIcon _icon = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new() { FontSize = 11, FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<IDisposable> _bindings = new();

    public SeriesStatusChip()
    {
        Padding = new Thickness(7, 2);
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { _dot, _icon, _text } };
        _bindings.Add(this.Bind(CornerRadiusProperty, this.GetResourceObservable("PbRadiusChip")));
        IsVisible = false;
    }

    public string? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty)
        {
            Apply(SeriesStatusStyle.For(Status));
        }
    }

    private readonly List<IDisposable> _styleBindings = new();

    private void Apply(SeriesStatusStyle? style)
    {
        foreach (var binding in _styleBindings)
        {
            binding.Dispose();
        }

        _styleBindings.Clear();
        IsVisible = style is not null;
        if (style is null)
        {
            return;
        }

        _text.Text = style.Label;
        _icon.Symbol = style.Icon;
        _styleBindings.Add(this.Bind(BackgroundProperty, this.GetResourceObservable(style.BackgroundKey)));
        _styleBindings.Add(_dot.Bind(Shape.FillProperty, this.GetResourceObservable(style.ForegroundKey)));
        _styleBindings.Add(_icon.Bind(TemplatedControl.ForegroundProperty, this.GetResourceObservable(style.ForegroundKey)));
        _styleBindings.Add(_text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(style.ForegroundKey)));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Resource subscriptions hold this control alive via the resource host; drop them when it leaves the tree
        // and re-establish on re-attach (tiles/rows are recycled).
        foreach (var binding in _styleBindings)
        {
            binding.Dispose();
        }

        _styleBindings.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_styleBindings.Count == 0)
        {
            Apply(SeriesStatusStyle.For(Status));
        }
    }
}
