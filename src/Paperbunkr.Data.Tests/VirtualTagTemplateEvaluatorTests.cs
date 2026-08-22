using Paperbunkr.Data.Entities;
using Paperbunkr.Data.VirtualTags;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="VirtualTagTemplateEvaluator"/> (docs/superpowers/specs/
/// 2026-08-07-preferences-libraries-tab-design.md §1.2).
/// </summary>
public class VirtualTagTemplateEvaluatorTests
{
    private static readonly Series Series = new() { Name = "Kilo Station" };

    private static readonly Issue Issue = new()
    {
        Number = "12",
        Volume = "2",
        Year = 2021,
        Title = "Departure",
        Publisher = "Nightshift Press",
        Writer = "A. Author",
        Penciller = "P. Penciller",
    };

    [Fact]
    public void Evaluate_SubstitutesEveryKnownField()
    {
        string result = VirtualTagTemplateEvaluator.Evaluate(
            "{Series} #{Number} Vol.{Volume} ({Year}) {Title} by {Writer}/{Penciller} - {Publisher}",
            Issue,
            Series);

        Assert.Equal(
            "Kilo Station #12 Vol.2 (2021) Departure by A. Author/P. Penciller - Nightshift Press",
            result);
    }

    [Fact]
    public void Evaluate_IsCaseInsensitiveOnFieldNames()
    {
        string result = VirtualTagTemplateEvaluator.Evaluate("{SERIES} {number}", Issue, Series);

        Assert.Equal("Kilo Station 12", result);
    }

    [Fact]
    public void Evaluate_UnrecognizedToken_PassesThroughUnchanged()
    {
        string result = VirtualTagTemplateEvaluator.Evaluate("{Series} {NotAField}", Issue, Series);

        Assert.Equal("Kilo Station {NotAField}", result);
    }

    [Fact]
    public void Evaluate_KnownFieldWithNullValue_RendersEmptyString_NotLiteralNull()
    {
        var sparseIssue = new Issue();

        string result = VirtualTagTemplateEvaluator.Evaluate("[{Title}]", sparseIssue, series: null);

        Assert.Equal("[]", result);
    }

    [Fact]
    public void Evaluate_NullSeries_RendersEmptyStringForSeriesToken()
    {
        string result = VirtualTagTemplateEvaluator.Evaluate("{Series} #{Number}", Issue, series: null);

        Assert.Equal(" #12", result);
    }

    [Fact]
    public void Evaluate_EmptyTemplate_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, VirtualTagTemplateEvaluator.Evaluate("", Issue, Series));
    }
}
