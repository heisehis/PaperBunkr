using System;
using System.Collections.Generic;
using System.Linq;
using FluentIcons.Common;

namespace Paperbunkr.App.Models;

/// <summary>One choice in the glyph picker: the FluentIcons <see cref="FluentIcons.Common.Symbol"/> name that is stored, and its label.</summary>
public sealed record CollectionGlyphOption(string Name, string Label)
{
    public Symbol Symbol => CollectionGlyphs.TryParse(Name) ?? Symbol.Library;
}

/// <summary>
/// The curated glyph list for a collection (docs/superpowers/specs/2026-11-06-collection-identity-design.md): a small, stable set rather than the whole icon font,
/// stored on <c>Collection.IconGlyph</c> as the symbol name. An unknown or retired name resolves to null and the row falls back to the accent dot.
/// </summary>
public static class CollectionGlyphs
{
    public static IReadOnlyList<CollectionGlyphOption> All { get; } = new List<CollectionGlyphOption>
    {
        new("Library", "Stack"),
        new("Book", "Book"),
        new("Bookmark", "Bookmark"),
        new("Star", "Star"),
        new("Heart", "Heart"),
        new("Trophy", "Trophy"),
        new("Ribbon", "Ribbon"),
        new("Rocket", "Rocket"),
        new("Flash", "Lightning"),
        new("Sparkle", "Sparkle"),
        new("Lightbulb", "Idea"),
        new("Globe", "World"),
        new("Eye", "Eye"),
        new("Shield", "Shield"),
        new("Diamond", "Diamond"),
        new("Gift", "Gift"),
        new("Tag", "Tag"),
        new("Folder", "Folder"),
        new("Person", "Person"),
        new("Beaker", "Beaker"),
        new("Bug", "Bug"),
        new("Cube", "Cube"),
        new("Camera", "Camera"),
        new("Games", "Games"),
    };

    /// <summary>The symbol for a stored name, or null when it is empty or unknown.</summary>
    public static Symbol? TryParse(string? name) =>
        !string.IsNullOrWhiteSpace(name) && All.Any(g => g.Name == name) && Enum.TryParse<Symbol>(name, out var symbol)
            ? symbol
            : null;
}
