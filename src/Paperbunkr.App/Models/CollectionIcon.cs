using System;

namespace Paperbunkr.App.Models;

/// <summary>What kind of picture a collection's icon is (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md).</summary>
public enum CollectionIconKind
{
    /// <summary>A FluentIcons symbol from <see cref="CollectionGlyphs"/>; stored as the plain name, as before.</summary>
    Symbol,

    /// <summary>A bundled <see cref="HeroGlyphs"/> shape; stored as <c>hero:&lt;slug&gt;</c>.</summary>
    Hero,

    /// <summary>A publisher logo from the mark set; stored as <c>publisher:&lt;name&gt;</c>.</summary>
    Publisher,

    /// <summary>An image from the user's "My icons" folder; stored as <c>file:&lt;file name&gt;</c>.</summary>
    File,
}

/// <summary>
/// A collection's icon as stored in <c>Collection.IconGlyph</c>. <see cref="Parse"/> only reads the text; whether the picture still exists (a deleted file, a logo
/// that went away) is decided where it is drawn, which then falls back to the accent dot.
/// </summary>
public sealed record CollectionIcon(CollectionIconKind Kind, string Key)
{
    public const string HeroPrefix = "hero:";
    public const string PublisherPrefix = "publisher:";
    public const string FilePrefix = "file:";

    /// <summary>The icon a stored value names, or null when it is empty, unknown or malformed.</summary>
    public static CollectionIcon? Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        string value = stored.Trim();
        if (value.StartsWith(HeroPrefix, StringComparison.Ordinal))
        {
            return HeroGlyphs.Find(value[HeroPrefix.Length..]) is { } hero ? new CollectionIcon(CollectionIconKind.Hero, hero.Slug) : null;
        }

        if (value.StartsWith(PublisherPrefix, StringComparison.Ordinal))
        {
            string name = value[PublisherPrefix.Length..].Trim();
            return name.Length > 0 ? new CollectionIcon(CollectionIconKind.Publisher, name) : null;
        }

        if (value.StartsWith(FilePrefix, StringComparison.Ordinal))
        {
            string file = value[FilePrefix.Length..].Trim();
            return IsPlainFileName(file) ? new CollectionIcon(CollectionIconKind.File, file) : null;
        }

        return CollectionGlyphs.TryParse(value) is not null ? new CollectionIcon(CollectionIconKind.Symbol, value) : null;
    }

    /// <summary>The text saved in <c>Collection.IconGlyph</c>.</summary>
    public string ToStored() => Kind switch
    {
        CollectionIconKind.Hero => HeroPrefix + Key,
        CollectionIconKind.Publisher => PublisherPrefix + Key,
        CollectionIconKind.File => FilePrefix + Key,
        _ => Key,
    };

    /// <summary>A file name with no folder part, so a stored value can never reach outside the icons folder.</summary>
    private static bool IsPlainFileName(string file) =>
        file.Length > 0 && file.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0 && file is not "." and not ".." &&
        file.IndexOf('/') < 0 && file.IndexOf('\\') < 0;
}
