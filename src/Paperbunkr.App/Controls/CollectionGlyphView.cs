using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FluentIcons.Avalonia;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Draws a collection's icon of any kind (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md): a FluentIcons symbol or a bundled hero shape in
/// <see cref="Accent"/>, a publisher logo or a "My icons" image in its own colours, all fitted into an <see cref="IconSize"/> square. <see cref="Value"/> is the stored
/// <c>Collection.IconGlyph</c> text; a value that cannot be drawn leaves the control empty (callers show the accent dot instead, through their own HasGlyph).
/// </summary>
public sealed class CollectionGlyphView : Decorator
{
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<CollectionGlyphView, string?>(nameof(Value));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<CollectionGlyphView, double>(nameof(IconSize), defaultValue: 16d);

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<CollectionGlyphView, IBrush?>(nameof(Accent));

    private int _loadVersion;

    static CollectionGlyphView()
    {
        ValueProperty.Changed.AddClassHandler<CollectionGlyphView>((view, _) => view.Rebuild());
        IconSizeProperty.Changed.AddClassHandler<CollectionGlyphView>((view, _) => view.Rebuild());
        AccentProperty.Changed.AddClassHandler<CollectionGlyphView>((view, _) => view.ApplyAccent());
    }

    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public double IconSize { get => GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }

    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    /// <summary>What is drawn now, for tests: the kind, or null when empty.</summary>
    public CollectionIconKind? DrawnKind { get; private set; }

    private void Rebuild()
    {
        _loadVersion++;
        var icon = CollectionIconResolver.Resolve(Value);
        DrawnKind = icon?.Kind;
        double size = IconSize > 0 ? IconSize : 16;
        Child = icon?.Kind switch
        {
            CollectionIconKind.Symbol => SymbolOf(icon.Key, size),
            CollectionIconKind.Hero => HeroPath(icon.Key, size),
            CollectionIconKind.Publisher => new BrandMark
            {
                Family = MarkFamily.Publisher,
                Value = icon.Key,
                MarkSize = size,
                MaxMarkWidth = size,
                ShowText = false,
                TextFallback = false,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
            CollectionIconKind.File => FileImage(icon.Key, size),
            _ => null,
        };
    }

    private Image FileImage(string fileName, double size)
    {
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        int version = _loadVersion;

        // Twice the display size so the downscale stays crisp at 150-200 % scaling; rendered off the UI thread and cached.
        int box = (int)Math.Ceiling(size * 2);
        _ = CollectionIconImages.GetAsync(fileName, box).ContinueWith(task =>
        {
            Bitmap? bitmap = task.IsCompletedSuccessfully ? task.Result : null;
            Dispatcher.UIThread.Post(() =>
            {
                if (version == _loadVersion)
                {
                    image.Source = bitmap;
                }
            });
        });
        return image;
    }

    private SymbolIcon SymbolOf(string name, double size)
    {
        var symbol = new SymbolIcon { Symbol = CollectionGlyphs.TryParse(name) ?? FluentIcons.Common.Symbol.Library, FontSize = size };
        SetForeground(symbol);
        return symbol;
    }

    /// <summary>The accent, or with none set the inherited foreground (a local null would hide the icon).</summary>
    private void SetForeground(SymbolIcon symbol)
    {
        if (Accent is { } accent)
        {
            symbol.Foreground = accent;
        }
        else
        {
            symbol.ClearValue(SymbolIcon.ForegroundProperty);
        }
    }

    private Avalonia.Controls.Shapes.Path HeroPath(string slug, double size)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = HeroGlyphs.Find(slug)!.Geometry,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
        };
        SetFill(path);
        return path;
    }

    /// <summary>The accent, or with none set (the picker's tiles) the inherited text colour, like a <see cref="SymbolIcon"/>.</summary>
    private void SetFill(Avalonia.Controls.Shapes.Path path)
    {
        if (Accent is { } accent)
        {
            path.Fill = accent;
        }
        else
        {
            path[!Avalonia.Controls.Shapes.Shape.FillProperty] = this[!Avalonia.Controls.Documents.TextElement.ForegroundProperty];
        }
    }

    private void ApplyAccent()
    {
        switch (Child)
        {
            case SymbolIcon symbol:
                SetForeground(symbol);
                break;
            case Avalonia.Controls.Shapes.Path path:
                SetFill(path);
                break;
        }
    }
}
