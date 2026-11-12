using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// One tile of the Edit Collection dialog's icon picker (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md): <see cref="Name"/> is the value stored
/// in <c>Collection.IconGlyph</c> (a symbol name, <c>hero:</c>, <c>publisher:</c> or <c>file:</c>); <see cref="IsSelected"/> draws the ring.
/// </summary>
public sealed partial class GlyphChoice : ObservableObject
{
    public GlyphChoice(string name, string label, string automationName, bool isSuggested = false)
    {
        Name = name;
        Label = label;
        AutomationName = automationName;
        IsSuggested = isSuggested;
    }

    public GlyphChoice(CollectionGlyphOption option)
        : this(option.Name, option.Label, $"{option.Label} icon")
    {
    }

    public static GlyphChoice ForHero(HeroGlyph hero) =>
        new(new CollectionIcon(CollectionIconKind.Hero, hero.Slug).ToStored(), hero.Label, $"{hero.Label} icon");

    public static GlyphChoice ForPublisher(string publisher, bool isSuggested) =>
        new(new CollectionIcon(CollectionIconKind.Publisher, publisher).ToStored(), isSuggested ? $"{publisher} (suggested)" : publisher, $"{publisher} logo", isSuggested);

    public static GlyphChoice ForFile(string fileName, string label) =>
        new(new CollectionIcon(CollectionIconKind.File, fileName).ToStored(), label, $"{label} image");

    /// <summary>The stored value.</summary>
    public string Name { get; }

    public string Label { get; }

    public string AutomationName { get; }

    /// <summary>The publisher most common in this collection; listed first.</summary>
    public bool IsSuggested { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One segment of the Edit Collection dialog's cover-style choice.</summary>
public sealed partial class CoverStyleChoice : ObservableObject
{
    public CoverStyleChoice(CollectionCoverStyle style, string label, string description)
    {
        Style = style;
        Label = label;
        Description = description;
    }

    public CollectionCoverStyle Style { get; }

    public string Label { get; }

    public string Description { get; }

    [ObservableProperty]
    private bool _isSelected;
}
