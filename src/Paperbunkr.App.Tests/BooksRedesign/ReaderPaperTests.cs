using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>The reader's paper, desk and page edges (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §5.1).</summary>
public class ReaderPaperTests
{
    private static BookReaderCssInput Input(bool paper, string? accent = null) =>
        new("Georgia", 18, 1.5, 0, 0, 12, 40, "#F2E6CE", "#3A2F22", Paper: paper, Accent: accent);

    [Fact]
    public void Paper_PutsThePageOnADeskWithEdgesAndAFolio()
    {
        string css = BookReaderCss.Build(Input(paper: true));

        Assert.Contains("--pb-desk", css);
        Assert.Contains("#pb-paper", css);
        Assert.Contains("#pb-edge-left", css);
        Assert.Contains("#pb-folio", css);
        Assert.Contains("background: var(--pb-desk) !important", css);
    }

    [Fact]
    public void NoPaper_LeavesTheOldFullWindowPage() =>
        Assert.DoesNotContain("#pb-paper", BookReaderCss.Build(Input(paper: false)));

    [Fact]
    public void Accent_ReachesThePagePills() =>
        Assert.Contains("--pb-accent: #C9803F", BookReaderCss.Build(Input(paper: true, accent: "#C9803F")));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 24)]
    [InlineData(0.5, 13)]
    [InlineData(-3, 2)]
    [InlineData(double.NaN, 2)]
    public void EdgeWidth_IsTwoToTwentyFourPixels(double share, double width) => Assert.Equal(width, PageEdgeMath.Width(share));
}
