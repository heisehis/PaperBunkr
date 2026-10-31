using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Tests.Theming;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>The fixed serif for book titles (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §1): the bookTitle modifier keeps
/// its size class's size, swaps the family to PbBookTitleFontFamily, and ignores the Preferences body-font override (PbFontFamily).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookTitleTypographyTests
{
    [Fact]
    public void BookTitleHeading_IsSerifAtHeadingSize_EvenWithABodyFontOverride()
    {
        FoundationStyleTests.WithFoundation("default", () =>
        {
            // What ThemeService.ApplyFontResource does for a user font override.
            Application.Current!.Resources["PbFontFamily"] = new FontFamily("Consolas");

            var title = new TextBlock { Text = "The Glass Orchard", Classes = { "pbTextHeading", "bookTitle" } };
            var window = FoundationStyleTests.Host(title);

            Assert.Contains("Source Serif 4", title.FontFamily.ToString());
            Assert.Equal(22, title.FontSize);
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
            Assert.True(double.IsNaN(title.LineHeight), "a wrapped serif title needs its natural line height, not the Bebas 1:1");
            window.Close();
            Application.Current.Resources.Remove("PbFontFamily");
        });
    }

    [Fact]
    public void BookTitleBody_KeepsBodySize()
    {
        FoundationStyleTests.WithFoundation("default", () =>
        {
            var title = new TextBlock { Text = "Paper Harbour", Classes = { "pbTextBody", "bookTitle" } };
            var window = FoundationStyleTests.Host(title);

            Assert.Contains("Source Serif 4", title.FontFamily.ToString());
            Assert.Equal(13, title.FontSize);
            window.Close();
        });
    }

    [Fact]
    public void PlainHeading_StaysOnTheDisplayFont()
    {
        FoundationStyleTests.WithFoundation("default", () =>
        {
            var heading = new TextBlock { Text = "Books", Classes = { "pbTextHeading" } };
            var window = FoundationStyleTests.Host(heading);

            Assert.Contains("Bebas Neue", heading.FontFamily.ToString());
            window.Close();
        });
    }
}
