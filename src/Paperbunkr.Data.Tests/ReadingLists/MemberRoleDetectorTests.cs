using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests.ReadingLists;

public sealed class MemberRoleDetectorTests
{
    private static MemberRoleFacts Facts(
        string? format = null, string? title = null, string? series = "Amazing Spider-Man", string? number = "5", string? annotation = null,
        string? dominant = "Amazing Spider-Man", int position = 2, int total = 10) =>
        new(format, title, series, number, annotation, dominant, position, total);

    [Theory]
    [InlineData("Tie-Ins", EventMembershipRole.TieIn)]
    [InlineData("Prelude", EventMembershipRole.Prologue)]
    [InlineData("Epilogue", EventMembershipRole.Epilogue)]
    [InlineData("Aftermath", EventMembershipRole.Aftermath)]
    public void ASourceAnnotation_IsHighConfidence(string annotation, EventMembershipRole expected)
    {
        var suggestion = MemberRoleDetector.Detect(Facts(annotation: annotation));

        Assert.NotNull(suggestion);
        Assert.Equal(expected, suggestion.Role);
        Assert.Equal(RoleConfidence.High, suggestion.Confidence);
        Assert.Contains(annotation, suggestion.Reason);
    }

    [Theory]
    [InlineData("Prologue", EventMembershipRole.Prologue)]
    [InlineData("Minus 1", EventMembershipRole.Prologue)]
    [InlineData("Minus-1", EventMembershipRole.Prologue)]
    [InlineData("Epilogue", EventMembershipRole.Epilogue)]
    public void TheFormatField_IsHighConfidence(string format, EventMembershipRole expected)
    {
        var suggestion = MemberRoleDetector.Detect(Facts(format: format));

        Assert.Equal(expected, suggestion!.Role);
        Assert.Equal(RoleConfidence.High, suggestion.Confidence);
    }

    [Theory]
    [InlineData("Civil War: Prelude", EventMembershipRole.Prologue)]
    [InlineData("The Prologue", EventMembershipRole.Prologue)]
    [InlineData("Epilogue", EventMembershipRole.Epilogue)]
    [InlineData("Aftermath", EventMembershipRole.Aftermath)]
    [InlineData("Spider-Man: Aftermath, Part 2", EventMembershipRole.Aftermath)]
    [InlineData("Siege (Tie-In)", EventMembershipRole.TieIn)]
    [InlineData("Siege tie in", EventMembershipRole.TieIn)]
    public void AKeywordInTheIssueTitle_IsHighConfidence(string title, EventMembershipRole expected)
    {
        var suggestion = MemberRoleDetector.Detect(Facts(title: title));

        Assert.Equal(expected, suggestion!.Role);
        Assert.Equal(RoleConfidence.High, suggestion.Confidence);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void AnIssueNumberOfZeroOrMinusOne_IsAPrologue(string number)
    {
        var suggestion = MemberRoleDetector.Detect(Facts(number: number));

        Assert.Equal(EventMembershipRole.Prologue, suggestion!.Role);
        Assert.Equal(RoleConfidence.High, suggestion.Confidence);
    }

    [Fact]
    public void TheSameWordOnlyInTheSeriesName_IsLowConfidence()
    {
        var suggestion = MemberRoleDetector.Detect(Facts(series: "Aftermath", dominant: "Aftermath"));

        Assert.Equal(EventMembershipRole.Aftermath, suggestion!.Role);
        Assert.Equal(RoleConfidence.Low, suggestion.Confidence);
    }

    [Theory]
    [InlineData("Aftermathematics")]
    [InlineData("Preludes")]
    [InlineData("Epiloguey")]
    public void PartialWords_DoNotMatch(string title)
    {
        Assert.Null(MemberRoleDetector.Detect(Facts(title: title)));
    }

    [Fact]
    public void AnIssueFromADifferentSeriesThanTheRestOfTheArc_IsALowConfidenceTieIn()
    {
        var suggestion = MemberRoleDetector.Detect(Facts(series: "Thor", dominant: "Amazing Spider-Man", position: 4, total: 10));

        Assert.Equal(EventMembershipRole.TieIn, suggestion!.Role);
        Assert.Equal(RoleConfidence.Low, suggestion.Confidence);
    }

    [Fact]
    public void TheFirstAndLastIssueOfALongArcFromADifferentSeries_ReadAsPrologueAndEpilogue()
    {
        var first = MemberRoleDetector.Detect(Facts(series: "Thor", position: 0, total: 8));
        var last = MemberRoleDetector.Detect(Facts(series: "Thor", position: 7, total: 8));
        var shortArcFirst = MemberRoleDetector.Detect(Facts(series: "Thor", position: 0, total: 4));

        Assert.Equal(EventMembershipRole.Prologue, first!.Role);
        Assert.Equal(EventMembershipRole.Epilogue, last!.Role);
        Assert.Equal(RoleConfidence.Low, first.Confidence);
        Assert.Equal(EventMembershipRole.TieIn, shortArcFirst!.Role);          // too short an arc for the stronger reading
    }

    [Fact]
    public void AnIssueOfTheMainSeries_WithNoKeywords_GetsNoSuggestion()
    {
        Assert.Null(MemberRoleDetector.Detect(Facts()));
        Assert.Null(MemberRoleDetector.Detect(Facts(dominant: null, series: "Thor")));      // no clear main series -> no structural guess
    }

    [Fact]
    public void Precedence_IsAnnotationThenFormatThenTitleThenSeriesThenStructure()
    {
        Assert.Equal(EventMembershipRole.TieIn, MemberRoleDetector.Detect(Facts(annotation: "Tie-Ins", format: "Epilogue", title: "Aftermath"))!.Role);
        Assert.Equal(EventMembershipRole.Epilogue, MemberRoleDetector.Detect(Facts(format: "Epilogue", title: "Aftermath"))!.Role);
        Assert.Equal(EventMembershipRole.Aftermath, MemberRoleDetector.Detect(Facts(title: "Aftermath", series: "Prelude"))!.Role);
    }

    [Fact]
    public void ANoteThatMerelyDescribesTheIssue_SuggestsNothing()
    {
        Assert.Null(MemberRoleDetector.Detect(Facts(annotation: "Takes place during Absolute Power #3")));
    }

    [Fact]
    public void OptionalIsNeverSuggested()
    {
        foreach (string text in new[] { "Optional", "optional reading", "Tie-In", "Prelude", "Aftermath", "Epilogue" })
        {
            Assert.NotEqual(EventMembershipRole.Optional, MemberRoleDetector.Detect(Facts(annotation: text))?.Role);
        }
    }

    [Fact]
    public void TheDominantSeries_NeedsAClearMajority()
    {
        Assert.Equal("Spider-Man", MemberRoleDetector.DominantSeries(new[] { "Spider-Man", "spider-man", "Thor", "Spider-Man" }));
        Assert.Null(MemberRoleDetector.DominantSeries(new[] { "Spider-Man", "Thor" }));
        Assert.Null(MemberRoleDetector.DominantSeries(new string?[] { null, " " }));
    }
}
