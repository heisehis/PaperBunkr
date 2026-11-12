using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
namespace Paperbunkr.Data.Tests;

public class SeriesArcDetectorTests
{
    private static Issue Issue(int n, string? title = null, string? arc = null, string? part = null) => new()
        { Id = n, Number = n.ToString(), Title = title, StoryArc = arc, StoryArcNumber = part };
    [Fact] public void ExplicitFieldsAllowMultipleArcs_AndSortByArcNumber()
    {
        var a = Issue(1, arc: "Alpha, Beta", part: "2"); var b = Issue(2, arc: "alpha", part: "1");
        var arcs = SeriesArcDetector.Find(new[] { a, b }, ContentType.Comic);
        Assert.Equal(2, arcs.Count); Assert.Equal(new[] { 2, 1 }, arcs[0].Issues.Select(i => i.Id));
        Assert.Equal("from the Story Arc field", arcs[0].Source); Assert.Single(arcs[1].Issues);
        Assert.Equal("Alpha, Beta", a.StoryArc);
    }
    [Theory]
    [InlineData("Saga, Part 1 of 2", "Saga, Part 2 of 2")]
    [InlineData("Saga Part One", "Saga Part Two")]
    [InlineData("Book One: Saga", "Book Two: Saga")]
    [InlineData("Saga: Chapter One", "Saga: Chapter Two")]
    [InlineData("Saga: A beginning", "Saga: An ending")]
    public void TitlesRecognizeSupportedPatterns(string one, string two)
    {
        var arc = Assert.Single(SeriesArcDetector.Find(new[] { Issue(1, one), Issue(2, two) }, ContentType.Comic));
        Assert.Equal("Saga", arc.Name); Assert.Equal(2, arc.Issues.Count); Assert.Equal("from titles", arc.Source);
    }
    [Fact] public void LoneChapterAndUnrelatedPrefixesAreIgnored()
    {
        Assert.Empty(SeriesArcDetector.Find(new[] { Issue(1, "Chapter 1"), Issue(2, "Chapter 2") }, ContentType.Comic));
        Assert.Empty(SeriesArcDetector.Find(new[] { Issue(1, "Alpha: One"), Issue(2, "Beta: Two") }, ContentType.Comic));
    }
    [Theory] [InlineData(ContentType.Manga)] [InlineData(ContentType.Manhwa)] [InlineData(ContentType.Manhua)]
    public void MangaUsesOnlyExplicitFields(ContentType type)
    {
        var arcs = SeriesArcDetector.Find(new[] { Issue(1, "Saga Part One", "Real"), Issue(2, "Saga Part Two") }, type);
        Assert.Equal("Real", Assert.Single(arcs).Name); Assert.Single(arcs[0].Issues);
    }
    [Fact] public void ExplicitSourceWins_AndRepeatedNamedTitlePartsShareOneSection()
    {
        var issues = new[] { Issue(1, "Saga Part One", "Real"), Issue(2, "Saga Part Two"), Issue(3, "Saga Part One"), Issue(4, "Saga Part Two") };
        var arcs = SeriesArcDetector.Find(issues, ContentType.Comic);
        Assert.Equal("Real", arcs[0].Name); Assert.DoesNotContain(arcs.Skip(1).SelectMany(a => a.Issues), i => i.Id == 1);
        Assert.Equal(new[] { 2, 3, 4 }, arcs.Last().Issues.Select(i => i.Id));
    }
    [Fact] public void MixedSourcesAndMissingParts_KeepOneNamedArcWithHonestProvenance()
    {
        var arcs = SeriesArcDetector.Find(new[] { Issue(1, "Saga Part One", "Saga"), Issue(3, "Saga Part Three"), Issue(4, "Unrelated"), Issue(5, "Saga Part Five") }, ContentType.Comic);
        var arc = Assert.Single(arcs); Assert.Equal(new[] { 1, 3, 5 }, arc.Issues.Select(i => i.Id));
        Assert.Equal("from the Story Arc field and titles", arc.Source);
    }
}
