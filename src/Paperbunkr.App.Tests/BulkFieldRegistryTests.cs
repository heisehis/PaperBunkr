using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BulkFieldRegistry"/>'s <c>Get</c>/<c>Set</c> round-trips - one representative
/// field per <see cref="FieldKind"/>/list-vs-scalar combination, plus the deliberate exclusions
/// (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md §3).
/// </summary>
public class BulkFieldRegistryTests
{
    private static BulkFieldDescriptor Find(string label) => Assert.Single(BulkFieldRegistry.All, f => f.Label == label);

    [Fact]
    public void TextScalarField_RoundTrips_AndNullsOutOnEmpty()
    {
        var descriptor = Find("Title");
        var issue = new Issue();

        descriptor.Set(issue, "New Title");
        Assert.Equal("New Title", descriptor.Get(issue));

        descriptor.Set(issue, "   ");
        Assert.Null(descriptor.Get(issue));
        Assert.Null(issue.Title);
    }

    [Fact]
    public void NumericField_RoundTrips_AndParsesInvalidToNull()
    {
        var descriptor = Find("Volume");
        var issue = new Issue();

        descriptor.Set(issue, "5");
        Assert.Equal(5, issue.Volume);
        Assert.Equal("5", descriptor.Get(issue));

        descriptor.Set(issue, "not-a-number");
        Assert.Null(issue.Volume);
    }

    [Fact]
    public void BooleanField_RoundTrips()
    {
        var descriptor = Find("Black and White");
        var issue = new Issue();

        descriptor.Set(issue, "true");
        Assert.True(issue.BlackAndWhite);
        Assert.Equal("true", descriptor.Get(issue));

        descriptor.Set(issue, "false");
        Assert.False(issue.BlackAndWhite);
    }

    [Fact]
    public void RatingField_RoundTrips_AndClearsToNull()
    {
        var descriptor = Find("My Rating");
        var issue = new Issue();

        descriptor.Set(issue, "4");
        Assert.Equal(4f, issue.Rating);
        Assert.Equal("4", descriptor.Get(issue));

        descriptor.Set(issue, string.Empty);
        Assert.Null(issue.Rating);
    }

    [Fact]
    public void ListField_IsFlaggedAndPassesThroughVerbatim()
    {
        var descriptor = Find("Writer");
        Assert.True(descriptor.IsListField);

        var issue = new Issue();
        descriptor.Set(issue, "Jack Kirby, Stan Lee");
        Assert.Equal("Jack Kirby, Stan Lee", descriptor.Get(issue));
    }

    [Fact]
    public void ScalarField_IsNotFlaggedAsListField()
    {
        Assert.False(Find("Title").IsListField);
    }

    [Fact]
    public void Find_ReturnsTheDescriptorWithTheGivenLabel()
    {
        var descriptor = BulkFieldRegistry.Find("Writer");

        Assert.Equal("Writer", descriptor.Label);
        Assert.True(descriptor.IsListField);
    }

    [Fact]
    public void Find_UnknownLabel_ThrowsRatherThanReturningNull()
    {
        Assert.Throws<ArgumentException>(() => BulkFieldRegistry.Find("Not A Real Field"));
    }

    [Theory]
    [InlineData("Story Arc Number")]
    [InlineData("Series")]
    [InlineData("Series Complete")]
    [InlineData("Manga")]
    [InlineData("Enable Proposed")]
    [InlineData("Alternate Count")]
    public void DeliberatelyExcludedFields_AreNotInRegistry(string label)
    {
        Assert.DoesNotContain(BulkFieldRegistry.All, f => f.Label == label);
    }
}
