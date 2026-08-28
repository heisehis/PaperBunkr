using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="FormatSignalCatalog"/> (docs/superpowers/specs/2026-08-27-metadata-model-
/// phase4e-format-signal-suggestions-design.md) - pure classification, no database.
/// </summary>
public class FormatSignalCatalogTests
{
    [Theory]
    [InlineData("Prologue", FormatSignalStrength.Strong, EventMembershipRole.Prologue)]
    [InlineData("Epilogue", FormatSignalStrength.Strong, EventMembershipRole.Epilogue)]
    [InlineData("Annual", FormatSignalStrength.Strong, null)]
    [InlineData("Special", FormatSignalStrength.Strong, null)]
    [InlineData("One Shot", FormatSignalStrength.Strong, null)]
    [InlineData("Minus 1", FormatSignalStrength.Weak, EventMembershipRole.Prologue)]
    [InlineData("Giant", FormatSignalStrength.Weak, null)]
    [InlineData("King", FormatSignalStrength.Weak, null)]
    [InlineData("1/2", FormatSignalStrength.Weak, null)]
    [InlineData("Preview", FormatSignalStrength.Weak, null)]
    // CE defaults that are packaging/edition only - no event signal.
    [InlineData("Black & White", FormatSignalStrength.None, null)]
    [InlineData("Director's Cut", FormatSignalStrength.None, null)]
    [InlineData("Sketch", FormatSignalStrength.None, null)]
    [InlineData("Trade Paper Back", FormatSignalStrength.None, null)]
    [InlineData("Web Comic", FormatSignalStrength.None, null)]
    [InlineData("Hardcover", FormatSignalStrength.None, null)]
    public void Resolve_CeDefault_MatchesDocumentedStrengthAndRole(string format, FormatSignalStrength expectedStrength, EventMembershipRole? expectedRole)
    {
        var info = FormatSignalCatalog.Resolve(format);

        Assert.Equal(expectedStrength, info.Strength);
        Assert.Equal(expectedRole, info.SuggestedRole);
    }

    [Fact]
    public void Resolve_UnrecognizedString_ResolvesToNone()
    {
        var info = FormatSignalCatalog.Resolve("Totally Made Up Format");

        Assert.Equal(FormatSignalStrength.None, info.Strength);
        Assert.Null(info.SuggestedRole);
    }

    [Fact]
    public void Resolve_NullOrEmpty_ResolvesToNone()
    {
        Assert.Equal(FormatSignalStrength.None, FormatSignalCatalog.Resolve(null).Strength);
        Assert.Equal(FormatSignalStrength.None, FormatSignalCatalog.Resolve("").Strength);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.Equal(FormatSignalStrength.Strong, FormatSignalCatalog.Resolve("annual").Strength);
        Assert.Equal(FormatSignalStrength.Strong, FormatSignalCatalog.Resolve("ANNUAL").Strength);
    }

    [Fact]
    public void CeDefaultFormats_HasCeSixteenValues()
    {
        Assert.Equal(16, FormatSignalCatalog.CeDefaultFormats.Count);
        Assert.Contains("Prologue", FormatSignalCatalog.CeDefaultFormats);
        Assert.Contains("Hardcover", FormatSignalCatalog.CeDefaultFormats);
    }
}
