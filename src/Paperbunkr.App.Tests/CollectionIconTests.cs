using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>The stored icon value (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md).</summary>
public class CollectionIconTests
{
    [Theory]
    [InlineData("Star", CollectionIconKind.Symbol, "Star")]
    [InlineData("hero:cape", CollectionIconKind.Hero, "cape")]
    [InlineData("publisher:DC Comics", CollectionIconKind.Publisher, "DC Comics")]
    [InlineData("file:Batman.svg", CollectionIconKind.File, "Batman.svg")]
    public void Parse_ReadsEachKind_AndRoundTrips(string stored, CollectionIconKind kind, string key)
    {
        var icon = CollectionIcon.Parse(stored);

        Assert.NotNull(icon);
        Assert.Equal(kind, icon!.Kind);
        Assert.Equal(key, icon.Key);
        Assert.Equal(stored, icon.ToStored());
    }

    [Fact]
    public void EveryExistingSymbolName_StillParses()
    {
        foreach (var option in CollectionGlyphs.All)
        {
            Assert.Equal(new CollectionIcon(CollectionIconKind.Symbol, option.Name), CollectionIcon.Parse(option.Name));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotASymbol")]
    [InlineData("hero:")]
    [InlineData("hero:bat-mask")]
    [InlineData("publisher:")]
    [InlineData("publisher:   ")]
    [InlineData("file:")]
    [InlineData("file:../secrets.png")]
    [InlineData(@"file:sub\x.png")]
    [InlineData("file:..")]
    public void Parse_RejectsEmptyUnknownAndUnsafeValues(string? stored) => Assert.Null(CollectionIcon.Parse(stored));
}
