using Paperbunkr.App.Behaviors;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The pure segment-splitting core of <see cref="MultiValueAutoComplete"/> (docs/superpowers/specs/
/// 2026-09-05-metadata-editor-affordances-design.md §3.2) - per-item autocomplete on comma-separated
/// fields (Writer, Genre, Characters, ...).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MultiValueAutoCompleteTests
{
    [Fact]
    public void LastSegment_NoComma_WholeTextIsTheSegment()
    {
        var (prefix, segment) = MultiValueAutoComplete.LastSegment("Frank Q");
        Assert.Equal("", prefix);
        Assert.Equal("Frank Q", segment);
    }

    [Fact]
    public void LastSegment_SplitsAtFinalComma_AndTrimsSegment()
    {
        var (prefix, segment) = MultiValueAutoComplete.LastSegment("Grant Morrison, Frank Q");
        Assert.Equal("Grant Morrison, ", prefix);
        Assert.Equal("Frank Q", segment);
    }

    [Fact]
    public void LastSegment_TrailingComma_EmptySegment()
    {
        var (prefix, segment) = MultiValueAutoComplete.LastSegment("Grant Morrison, ");
        Assert.Equal("Grant Morrison, ", prefix);
        Assert.Equal("", segment);
    }

    [Fact]
    public void LastSegment_EmptyOrNull_BothEmpty()
    {
        Assert.Equal(("", ""), MultiValueAutoComplete.LastSegment(""));
        Assert.Equal(("", ""), MultiValueAutoComplete.LastSegment(null));
    }

    [Fact]
    public void Splice_JoinsPrefixAndChoice_WithTrailingSeparator()
    {
        Assert.Equal("Grant Morrison, Frank Quitely, ",
            MultiValueAutoComplete.Splice("Grant Morrison, ", "Frank Quitely"));
    }

    [Fact]
    public void Splice_FirstItem_NoLeadingPrefix()
    {
        Assert.Equal("Frank Quitely, ", MultiValueAutoComplete.Splice("", "  Frank Quitely  "));
    }
}
