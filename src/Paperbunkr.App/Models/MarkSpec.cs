using FluentIcons.Common;

namespace Paperbunkr.App.Models;

/// <summary>How a resolved brand / metadata mark should be drawn
/// (docs/superpowers/specs/2026-08-28-brand-metadata-iconography-design.md §1).</summary>
public enum MarkKind
{
    /// <summary>No value to show at all.</summary>
    None,

    /// <summary>Value present but nothing better than plain text - the call site keeps its own
    /// <c>TextBlock</c>.</summary>
    Text,

    /// <summary>A short letters chip (publisher initials, "TPB", "17+").</summary>
    LetterMark,

    /// <summary>A FluentIcons glyph, optionally with a short label beside it.</summary>
    Glyph,

    /// <summary>A bundled brand SVG under <c>Assets/Marks/{Services,Publishers}/</c>.</summary>
    SvgAsset,

    /// <summary>A bundled country flag SVG under <c>Assets/Marks/Flags/</c>.</summary>
    Flag,
}

/// <summary>
/// The outcome of a <see cref="Services.MarkResolver"/> lookup - a small value object the
/// <c>BrandMark</c> control renders. <see cref="Kind"/> is <see cref="MarkKind.Text"/> or
/// <see cref="MarkKind.None"/> whenever there is no real mark, so a consumer can always swap a
/// bare <c>&lt;TextBlock Text="{Binding X}"/&gt;</c> for a <c>BrandMark</c> without a layout regression.
/// </summary>
public sealed record MarkSpec(
    MarkKind Kind,
    string? AssetPath = null,
    Symbol? Glyph = null,
    string? Text = null,
    string? Background = null,
    string? Foreground = null)
{
    public static readonly MarkSpec None = new(MarkKind.None);

    public static MarkSpec PlainText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? None : new MarkSpec(MarkKind.Text, Text: value);

    public bool HasMark => Kind is not (MarkKind.None or MarkKind.Text);
}
