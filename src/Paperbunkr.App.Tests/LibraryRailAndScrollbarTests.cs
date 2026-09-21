using System.Text;
using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #26 - the rail's current-letter mapping and the accent-tinted Library scrollbar.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryRailAndScrollbarTests
{
    private static IssueListRow Row(string series) => new() { SeriesName = series, Title = "T", CoverBrush = Brushes.Black };

    private static SeriesCardSample Card(string name) => new()
    {
        Title = name, Name = name, Sub = "s", ContentTypeLabel = "Comic", CoverBrush = Brushes.Black, RepresentativeRow = Row(name),
    };

    [Fact]
    public void LetterForItem_HeadersAndGroups_AreTheirOwnLetter()
    {
        Assert.Equal("C", AlphabetIndexEntry.LetterForItem(new GridSectionHeader("C", 12)));
        Assert.Equal("Q", AlphabetIndexEntry.LetterForItem(new SeriesCardGroup { Header = "Q", Items = new() }));
        Assert.Equal("#", AlphabetIndexEntry.LetterForItem(new IssueListRowGroup { Header = "#", Items = new() }));
    }

    [Fact]
    public void LetterForItem_RowsAndCards_AreTheLetterOfTheirSeriesName()
    {
        Assert.Equal("A", AlphabetIndexEntry.LetterForItem(Row("Absolute Batman")));
        Assert.Equal("W", AlphabetIndexEntry.LetterForItem(Card("  Warhammer 40k")));
        Assert.Equal("#", AlphabetIndexEntry.LetterForItem(Card("2099")));
        Assert.Equal("Z", AlphabetIndexEntry.LetterForItem("zatanna"));
    }

    [Fact]
    public void LetterForItem_AnythingElse_IsNull()
    {
        Assert.Null(AlphabetIndexEntry.LetterForItem(null));
        Assert.Null(AlphabetIndexEntry.LetterForItem(42));
    }

    [Theory]
    [InlineData("ScrollBarThumbFillPointerOver")]
    [InlineData("ScrollBarThumbFillPressed")]
    public void FluentAvalonia_StillUsesTheScrollbarThumbKeysWeOverride(string key)
    {
        // The Library tints these two keys. If a theme upgrade renamed them, the override would silently stop working, so guard the names.
        string path = typeof(FluentAvalonia.Styling.FluentAvaloniaTheme).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(path);
        byte[] needle = Encoding.Unicode.GetBytes(key);

        Assert.True(bytes.AsSpan().IndexOf(needle) >= 0, $"{key} is no longer in {Path.GetFileName(path)} - update the Library's scrollbar tint");
    }

    [Fact]
    public void LibraryScreenXaml_DeclaresTheAccentTintedThumbOverrides_AsDynamicAccentBrushes()
    {
        // Constructing LibraryScreen needs the whole app's resources, which the headless test app doesn't load - so check the source it is built from.
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "src", "Paperbunkr.App", "Views", "LibraryScreen.axaml")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        string xaml = File.ReadAllText(Path.Combine(dir!, "src", "Paperbunkr.App", "Views", "LibraryScreen.axaml"));

        Assert.Contains("x:Key=\"ScrollBarThumbFillPointerOver\" Color=\"{DynamicResource PbAccentColor}\"", xaml);
        Assert.Contains("x:Key=\"ScrollBarThumbFillPressed\" Color=\"{DynamicResource PbAccentColor}\"", xaml);
    }
}
