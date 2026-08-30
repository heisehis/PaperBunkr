using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="SpecialFormatCatalog"/> (docs/superpowers/specs/2026-08-28-series-detail-
/// specials-tab-design.md) - pure classification, no database.
/// </summary>
public class SpecialFormatCatalogTests
{
    [Theory]
    [InlineData("Special")]
    [InlineData("Director's Cut")]
    [InlineData("Annual")]
    [InlineData("Epilogue")]
    [InlineData("One Shot")]
    [InlineData("Prologue")]
    [InlineData("Trade Paper Back")]
    [InlineData("Reference")]
    [InlineData("Box Set")]
    [InlineData("Anthology")]
    [InlineData("Omnibus")]
    [InlineData("Compendium")]
    [InlineData("Absolute")]
    [InlineData("Graphic Novel")]
    [InlineData("GN")]
    [InlineData("FCBD")]
    [InlineData("Giant Size")]
    public void IsSpecial_SpecialTriggeringValue_ReturnsTrue(string format)
    {
        Assert.True(SpecialFormatCatalog.IsSpecial(format));
    }

    // CE values that are real Format entries but NOT special-triggering under Kavita's own logic.
    [Theory]
    [InlineData("Hardcover")]
    [InlineData("Giant")]
    [InlineData("Sketch")]
    [InlineData("1/2")]
    [InlineData("King")]
    [InlineData("Minus 1")]
    [InlineData("Preview")]
    [InlineData("Web Comic")]
    [InlineData("Black & White")]
    public void IsSpecial_CeValueNotSpecialTriggering_ReturnsFalse(string format)
    {
        Assert.False(SpecialFormatCatalog.IsSpecial(format));
    }

    [Fact]
    public void IsSpecial_NullEmptyOrWhitespace_ReturnsFalse()
    {
        Assert.False(SpecialFormatCatalog.IsSpecial(null));
        Assert.False(SpecialFormatCatalog.IsSpecial(""));
        Assert.False(SpecialFormatCatalog.IsSpecial("   "));
    }

    [Fact]
    public void IsSpecial_ArbitraryUserTypedValue_ReturnsFalse()
    {
        Assert.False(SpecialFormatCatalog.IsSpecial("Totally Made Up Format"));
    }

    [Fact]
    public void IsSpecial_IsCaseInsensitive()
    {
        Assert.True(SpecialFormatCatalog.IsSpecial("annual"));
        Assert.True(SpecialFormatCatalog.IsSpecial("OMNIBUS"));
    }
}
