using Paperbunkr.Data.Naming;

namespace Paperbunkr.Data.Tests.Naming;

/// <summary>Implementation plan Phase 3 Step 3.3 verification - table-driven over CE's exact
/// replace-map (design doc §5) plus the trailing-period/whitespace-collapse rules.</summary>
public sealed class SanitizerTests
{
    [Theory]
    [InlineData("What?", "What")]
    [InlineData("A/B", "AB")]
    [InlineData("A\\B", "AB")]
    [InlineData("A*B", "AB")]
    [InlineData("Time: 3PM", "Time - 3PM")]
    [InlineData("<tag>", "[tag]")]
    [InlineData("A|B", "A!B")]
    [InlineData("\"Quoted\"", "'Quoted'")]
    public void SanitizeSegment_applies_CEs_exact_replace_map(string input, string expected) =>
        Assert.Equal(expected, Sanitizer.SanitizeSegment(input));

    [Fact]
    public void SanitizeSegment_strips_trailing_periods()
    {
        Assert.Equal("Batman", Sanitizer.SanitizeSegment("Batman..."));
    }

    [Fact]
    public void SanitizeSegment_collapses_multiple_spaces_and_trims()
    {
        Assert.Equal("Batman Returns", Sanitizer.SanitizeSegment("  Batman    Returns  "));
    }

    [Fact]
    public void SanitizePath_sanitizes_each_segment_independently_and_rejoins()
    {
        string result = Sanitizer.SanitizePath("DC Comics/Batman: The Dark Knight/Batman #1?");
        Assert.Equal(Path.Combine("DC Comics", "Batman - The Dark Knight", "Batman #1"), result);
    }

    [Fact]
    public void SanitizePath_drops_empty_segments_so_the_path_is_never_rooted_or_doubled()
    {
        // A missing publisher/imprint leaves the template's own literal separators behind.
        Assert.Equal(Path.Combine("Batman", "Batman #1"), Sanitizer.SanitizePath("\\Batman\\Batman #1"));
        Assert.Equal(Path.Combine("Pub", "Series"), Sanitizer.SanitizePath("Pub\\Series"));
        Assert.Equal(string.Empty, Sanitizer.SanitizePath("\\\\"));
    }

    [Fact]
    public void SanitizePath_strips_leading_dots_so_a_segment_is_never_dot_or_dotdot_or_hidden()
    {
        Assert.Equal(Path.Combine("hack", "Sign"), Sanitizer.SanitizePath(".hack/Sign"));
        Assert.Equal("Series", Sanitizer.SanitizePath("../Series/.."));
    }
}
