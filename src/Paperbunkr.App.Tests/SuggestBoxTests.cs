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

    // Regression, found 2026-09-11: a strict (pick-only, IsReadOnly TextBox) field's Text always
    // holds its already-committed value - filtering the suggestion list by that value, the way the
    // non-strict typing-filter path does, means the current selection is the only thing that can
    // ever match itself, hiding every other option the moment a value is set. The reader's "Canvas
    // background" picker (Auto/Color/Texture) could only ever show "Auto" once that was selected.
    [Fact]
    public void FilterSuggestions_StrictField_ShowsFullListRegardlessOfCurrentText()
    {
        string[] modes = ["Auto", "Color", "Texture"];

        var result = SuggestBox.FilterSuggestions(modes, text: "Auto", isStrict: true, multiValue: false, maxRows: 60);

        Assert.Equal(modes, result);
    }

    [Fact]
    public void FilterSuggestions_NonStrictField_StillFiltersByTypedText()
    {
        string[] names = ["Grant Morrison", "Frank Quitely", "Dave Gibbons"];

        var result = SuggestBox.FilterSuggestions(names, text: "Gr", isStrict: false, multiValue: false, maxRows: 60);

        Assert.Equal(["Grant Morrison"], result);
    }

    [Fact]
    public void FilterSuggestions_NonStrictMultiValue_FiltersByTrailingSegmentOnly()
    {
        string[] names = ["Grant Morrison", "Frank Quitely", "Dave Gibbons"];

        var result = SuggestBox.FilterSuggestions(names, text: "Grant Morrison, Da", isStrict: false, multiValue: true, maxRows: 60);

        Assert.Equal(["Dave Gibbons"], result);
    }
}
