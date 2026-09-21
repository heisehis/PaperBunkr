using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Views;

/// <summary>
/// Typographic stand-in for a cover that does not exist or failed to decode (docs/superpowers/specs/2026-09-21-
/// cosmetics-pitch-2-design.md #18): the series' initials large, its title small, drawn over the deterministic per-series
/// gradient the tile already has as its background (<c>SeriesCardSample.CoverBrushFor</c>) - so the same series always looks
/// the same and it never flickers between loads. Hidden until <see cref="AsyncCoverImage"/> reports an EMPTY decode for the
/// sibling <see cref="Image"/> (never while a cover is merely still loading, which would flash text during scrolling).
/// One text-only control, no template or bindings: it reads its own DataContext through <see cref="IPlaceholderCoverSource"/>.
/// </summary>
public sealed class PlaceholderCoverText : Control
{
    private static readonly IBrush InitialsBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)).ToImmutable();
    private static readonly IBrush TitleBrush = new SolidColorBrush(Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF)).ToImmutable();

    private FormattedText? _initials;
    private FormattedText? _title;
    private string? _cachedFor;
    private int _cachedWidth;

    public PlaceholderCoverText()
    {
        IsHitTestVisible = false;
        IsVisible = false;
        DataContextChanged += (_, _) => { _cachedFor = null; InvalidateVisual(); };
    }

    /// <summary>Called by <see cref="AsyncCoverImage"/>: true after an empty decode, false whenever the image is re-pointed.</summary>
    public void SetMissing(bool missing)
    {
        if (IsVisible != missing)
        {
            IsVisible = missing;
        }
    }

    /// <summary>Up to three initials: first letter of each word, skipping a leading "The". Falls back to "?" for an empty name.</summary>
    public static string InitialsFor(string? name)
    {
        var words = (name ?? string.Empty)
            .Split(new[] { ' ', '\t', '-', '_', ':', '·' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Any(char.IsLetterOrDigit))
            .ToList();
        if (words.Count > 1 && words[0].Equals("the", StringComparison.OrdinalIgnoreCase))
        {
            words.RemoveAt(0);
        }

        var letters = words
            .Select(w => w.First(char.IsLetterOrDigit))
            .Take(3)
            .Select(c => char.ToUpperInvariant(c));
        string result = string.Concat(letters);
        return result.Length == 0 ? "?" : result;
    }

    public override void Render(DrawingContext context)
    {
        if (DataContext is not IPlaceholderCoverSource source || Bounds.Width < 24 || Bounds.Height < 24)
        {
            return;
        }

        string title = source.CoverTitle ?? string.Empty;
        int width = (int)Bounds.Width;
        if (_initials is null || _title is null || _cachedFor != title || _cachedWidth != width)
        {
            double initialsSize = Math.Clamp(width * 0.30, 16, 46);
            _initials = new FormattedText(
                InitialsFor(title), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), initialsSize, InitialsBrush);
            _title = new FormattedText(
                title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), Math.Clamp(width * 0.085, 9, 12), TitleBrush)
            {
                MaxTextWidth = Math.Max(1, width - 16),
                MaxLineCount = 2,
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
            };
            _cachedFor = title;
            _cachedWidth = width;
        }

        double blockHeight = _initials.Height + 6 + _title.Height;
        double top = Math.Max(6, (Bounds.Height * 0.42) - (blockHeight / 2));
        context.DrawText(_initials, new Point((Bounds.Width - _initials.Width) / 2, top));
        context.DrawText(_title, new Point(8, top + _initials.Height + 6));
    }
}
