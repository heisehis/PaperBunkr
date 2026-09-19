using Paperbunkr.App.Behaviors;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The pure string-mutation core of <see cref="TextSpinner"/> (docs/superpowers/specs/
/// 2026-09-05-metadata-editor-affordances-design.md §3.3) - the up/down arrow behaviour for
/// Number / Volume / Alternate Number / Story Arc Number, which can hold non-numeric text.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class TextSpinnerTests
{
    [Theory]
    [InlineData("5", 1, "6")]
    [InlineData("5", -1, "4")]
    [InlineData("  12  ", 1, "13")]
    [InlineData("0", -1, "0")]        // clamped at min 0
    public void Step_WholeInteger_IncrementsAndClamps(string input, int delta, string expected)
    {
        Assert.Equal(expected, TextSpinner.Step(input, delta, min: 0, max: 100));
    }

    [Theory]
    [InlineData("Vol 3", 1, "Vol 4")]
    [InlineData("Vol 3", -1, "Vol 2")]
    public void Step_TrailingDigitRun_IncrementsInPlace(string input, int delta, string expected)
    {
        Assert.Equal(expected, TextSpinner.Step(input, delta, 0, int.MaxValue));
    }

    [Theory]
    [InlineData("1.MU", 1, "2.MU")]
    [InlineData("1a", 1, "2a")]
    [InlineData("10-ish", -1, "9-ish")]
    public void Step_LeadingDigitRun_IncrementsInPlace(string input, int delta, string expected)
    {
        Assert.Equal(expected, TextSpinner.Step(input, delta, 0, int.MaxValue));
    }

    [Theory]
    [InlineData("½", 1)]
    [InlineData("", 1)]
    [InlineData("MU", -1)]
    public void Step_NoDigits_FallsBackToOne_WhenMinIsZero(string input, int delta)
    {
        Assert.Equal("1", TextSpinner.Step(input, delta, min: 0, max: int.MaxValue));
    }

    [Fact]
    public void Step_NoDigits_FallsBackToMin_WhenMinIsNonZero()
    {
        Assert.Equal("1", TextSpinner.Step("x", 1, min: 1, max: 12));
    }

    [Fact]
    public void Step_ClampsAtMax()
    {
        Assert.Equal("12", TextSpinner.Step("12", 1, min: 1, max: 12));
    }

    // --- Decimal step (docs/superpowers/specs/2026-09-18-per-tracker-score-and-finish-date-
    // design.md's Score field - 0-5 scale, 0.1 step) ---

    [Theory]
    [InlineData("3.5", "0.1", "3.6")]
    [InlineData("3.5", "-0.1", "3.4")]
    [InlineData("5", "0.1", "5")]   // clamped at max 5
    [InlineData("0", "-0.1", "0")]  // clamped at min 0
    public void Step_FractionalDelta_UsesDecimalArithmetic_NoFloatRoundingDrift(string input, string delta, string expected)
    {
        Assert.Equal(expected, TextSpinner.Step(input, decimal.Parse(delta), min: 0, max: 5));
    }

    [Fact]
    public void Step_FractionalDelta_MixedText_StaysWholeNumber()
    {
        // "1.MU" never sets a fractional Step in practice, but the digit-run branch truncates any
        // fractional delta to a whole number rather than throwing or misbehaving.
        Assert.Equal("2.MU", TextSpinner.Step("1.MU", 1.0m, 0, int.MaxValue));
    }
}
