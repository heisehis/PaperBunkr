using Paperbunkr.Data.Naming;

namespace Paperbunkr.Data.Tests.Naming;

public class NameTemplateTranslatorTests
{
    [Theory]
    [InlineData("{series}", "{<series>}")]
    [InlineData("{publisher}/{series} ({volumeyear})/{series} #{number:000}", "{<publisher>}/{<series>} ({<volumeyear>})/{<series>} #{<number3>}")]
    [InlineData("{series} #{number:00}", "{<series>} #{<number2>}")]
    [InlineData("{Series}", "{<series>}")]                                  // import tokens are case-insensitive
    [InlineData("{month}-{day}", "{<month#2>}-{<Day2>}")]                  // the import grammar's month/day are two-digit numbers
    [InlineData("{year:0000}", "{<year4>}")]
    [InlineData("{series}[ ({volumeyear})]", "{<series>}{ (<volumeyear>)}")]
    [InlineData("{series}[ - {title}]", "{<series>}{ - <title>}")]
    [InlineData("{series}[ (no token)]", "{<series>} (no token)")]          // a group with no token is always shown
    [InlineData(@"{series}\ #{number}", "{<series>} #{<number>}")]           // an escaped space is just a space
    public void Translates(string import, string expected)
    {
        var result = NameTemplateTranslator.Translate(import);

        Assert.True(result.Success, result.Error);
        Assert.Equal(expected, result.Template);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("{colour}", "Unknown token")]
    [InlineData("{series", "never closed")]
    [InlineData("[ {series}", "never closed")]
    [InlineData("{series}]", "no matching")]
    [InlineData("{series}}", "no matching")]
    [InlineData("[ {series} {number} ]", "more than one token")]
    [InlineData("[ a [ {series} ] ]", "Nested")]
    [InlineData("{number:0.0}", "can't be converted")]
    [InlineData("{number:abc}", "can't be converted")]
    [InlineData("{series:000}", "can't be zero-padded")]
    [InlineData(@"{series}\{", "can't be used as literal")]
    [InlineData("[<{series}]", "can't be used as literal")]                // '<' cannot sit in a group's prefix
    [InlineData("{series}\\", "lone backslash")]
    public void Refuses_WithAReason_InsteadOfGuessing(string import, string reasonFragment)
    {
        var result = NameTemplateTranslator.Translate(import);

        Assert.False(result.Success);
        Assert.Null(result.Template);
        Assert.Contains(reasonFragment, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALessThanSign_IsFine_AsTopLevelTextOrInAPostfix()
    {
        Assert.Equal("a<b {<series>}", NameTemplateTranslator.Translate("a<b {series}").Template);
        Assert.Equal("{<series><x}", NameTemplateTranslator.Translate("[{series}<x]").Template);
    }
}
