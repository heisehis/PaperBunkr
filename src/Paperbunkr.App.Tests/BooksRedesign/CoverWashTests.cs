using Avalonia.Media;
using Paperbunkr.App.Controls.Books;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>The Books colour wash (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §2).</summary>
public class CoverWashTests
{
    private static readonly Color Accent = Color.FromRgb(0xC9, 0x80, 0x3F);
    private static readonly Color Cover = Color.FromRgb(0x2D, 0x4A, 0x5A);

    [Fact]
    public void NoMix_IsTheCoverColour() => Assert.Equal(Cover, CoverWash.WashColor(Cover, Accent, 0));

    [Fact]
    public void FullMix_IsTheAccent() => Assert.Equal(Accent, CoverWash.WashColor(Cover, Accent, 1));

    [Fact]
    public void UnknownCover_FallsBackToTheAccent() => Assert.Equal(Accent, CoverWash.WashColor(null, Accent, 0));

    [Fact]
    public void HalfMix_IsHalfway() =>
        Assert.Equal(Color.FromRgb(0x7B, 0x65, 0x4C), CoverWash.WashColor(Cover, Accent, 0.5));

    [Fact]
    public void MixIsClamped() => Assert.Equal(Accent, CoverWash.WashColor(Cover, Accent, 3));
}
