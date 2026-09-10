using Paperbunkr.App.Controls;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Pure-logic coverage for <see cref="SuggestBox"/> (the ComboBox/AutoCompleteBox replacement in
/// the metadata editors - see that control's doc comment for the freeze it exists to avoid). The
/// interactive open/close/filter path is on-screen-verified only, matching this project's
/// established headless-vs-manual split.
/// </summary>
public class SuggestBoxTests
{
    [Theory]
    [InlineData(null, "Grant Morrison", "Grant Morrison, ")]
    [InlineData("", "Grant Morrison", "Grant Morrison, ")]
    [InlineData("Grant Morrison, ", "Frank Quitely", "Grant Morrison, Frank Quitely, ")]
    [InlineData("Grant Morrison, Fr", "Frank Quitely", "Grant Morrison, Frank Quitely, ")]
    [InlineData("Alan Moore,Dave", "Dave Gibbons", "Alan Moore, Dave Gibbons, ")]
    public void SpliceMultiValue_ReplacesTrailingSegment_KeepsPrefix(string? current, string picked, string expected)
    {
        Assert.Equal(expected, SuggestBox.SpliceMultiValue(current, picked));
    }

    [Theory]
    [InlineData("Batman", false, "Batman")]
    [InlineData("  Batman  ", false, "Batman")]
    [InlineData("Bruce, Dick, Ja", true, "Ja")]
    [InlineData("Bruce, Dick, ", true, "")]
    [InlineData("Solo", true, "Solo")]
    [InlineData(null, true, "")]
    [InlineData("", false, "")]
    public void CurrentSegment_ReturnsTheTextBeingCompleted(string? text, bool multiValue, string expected)
    {
        Assert.Equal(expected, SuggestBox.CurrentSegment(text, multiValue));
    }
}
