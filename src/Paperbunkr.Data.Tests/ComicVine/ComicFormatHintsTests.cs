using Paperbunkr.Data.ComicVine.Scraping;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary><see cref="ComicFormatHints"/>: formats and file-name hints for matching (asked for 2026-10-21).</summary>
public class ComicFormatHintsTests
{
    [Theory]
    [InlineData(null, @"C:\Comics\Lobo\Lobo Annual 001 (2026).cbz", ComicFormatKind.Annual)]
    [InlineData(null, @"C:\Comics\Saga\Saga_v01_TPB.cbz", ComicFormatKind.TradePaperback)]
    [InlineData(null, @"C:\Comics\Saga\Saga Vol. 1 (Trade Paperback).cbz", ComicFormatKind.TradePaperback)]
    [InlineData(null, @"C:\Comics\Batman\Batman - The Long Halloween Deluxe Edition HC.cbz", ComicFormatKind.Hardcover)]
    [InlineData(null, @"C:\Comics\X-Men\Uncanny X-Men Omnibus v01.cbz", ComicFormatKind.Omnibus)]
    [InlineData(null, @"C:\Comics\Misc\Spawn One-Shot (1999).cbz", ComicFormatKind.OneShot)]
    [InlineData(null, @"C:\Comics\FCBD\Free Comic Book Day 2024 - Spider-Man.cbz", ComicFormatKind.FreeComicBookDay)]
    [InlineData(null, @"C:\Comics\Lobo Annual\001.cbz", ComicFormatKind.Annual)]                 // the folder says it
    [InlineData(null, @"C:\Comics\Batman Beyond\Batman_Beyond_Neo-Gothic_(2023-)_#1.cbz", ComicFormatKind.Regular)]
    [InlineData(null, @"C:\Comics\Absolute Batman\Absolute Batman 001.cbz", ComicFormatKind.Regular)] // an ongoing series, not a format
    [InlineData("Annual", @"C:\Comics\Lobo\Lobo 001.cbz", ComicFormatKind.Annual)]               // the Format field wins
    [InlineData("Series", @"C:\Comics\Lobo Annual\001.cbz", ComicFormatKind.Regular)]
    public void Detects_the_format_from_the_field_file_name_or_folder(string? format, string path, ComicFormatKind expected) =>
        Assert.Equal(expected, ComicFormatHints.Detect(format, path));

    [Theory]
    [InlineData("Batman_Beyond_Neo-Gothic_(2023-)", "Batman Beyond Neo-Gothic")]
    [InlineData("Lobo (2026) [Digital] #8", "Lobo")]
    [InlineData("Lobo", "Lobo")]
    public void Cleans_file_style_series_names_for_searching(string raw, string expected) =>
        Assert.Equal(expected, ComicFormatHints.CleanQuery(raw));

    [Fact]
    public void A_year_comes_from_a_file_style_name()
    {
        Assert.Equal(2023, ComicFormatHints.YearHint("Batman_Beyond_Neo-Gothic_(2023-)", null));
        Assert.Equal(1993, ComicFormatHints.YearHint("Lobo", @"C:\x\Lobo (1993) 001.cbz"));
        Assert.Null(ComicFormatHints.YearHint("Lobo", @"C:\x\Lobo 001.cbz"));
    }

    [Fact]
    public void A_candidate_of_the_same_format_is_preferred()
    {
        Assert.True(ComicFormatHints.Agreement(ComicFormatKind.Annual, "Lobo Annual") > ComicFormatHints.Agreement(ComicFormatKind.Annual, "Lobo"));
        Assert.True(ComicFormatHints.Agreement(ComicFormatKind.Regular, "Lobo") > ComicFormatHints.Agreement(ComicFormatKind.Regular, "Lobo Annual"));
        Assert.Equal(0, ComicFormatHints.Agreement(ComicFormatKind.Regular, "Lobo"));
        // A collected edition named exactly like its series is only nudged, not ruled out.
        Assert.Equal(-5, ComicFormatHints.Agreement(ComicFormatKind.TradePaperback, "Saga"));
    }

    [Theory]
    [InlineData("C:/Comics/The_Amazing_Spider-Man_(2025-)_#Halloween_Special_2026.cbz", "Halloween Special")]
    [InlineData("C:/Comics/X-Men #37.cbz", null)]
    [InlineData("C:/Comics/Batman_Beyond_Neo-Gothic_(2023-)_#1.cbz", null)]
    [InlineData("C:/Comics/Saga #1.5.cbz", null)]
    [InlineData("C:/Comics/Lobo 001.cbz", null)]
    [InlineData("C:/Comics/Spawn #Director's Cut (2023).cbz", "Director's Cut")]
    public void A_worded_issue_in_the_file_name_is_read_as_a_designation(string path, string? expected) =>
        Assert.Equal(expected, ComicFormatHints.IssueDesignation(path));

    [Theory]
    [InlineData("C:/x/Secret_Wars_2099_001_(2015).cbz", "Secret Wars 2099")]
    [InlineData("C:/x/X-Men #37.cbz", "X-Men")]
    [InlineData("C:/x/The_Amazing_Spider-Man_(2025-)_#Halloween_Special_2026.cbz", "The Amazing Spider-Man")]
    public void The_file_title_drops_number_year_and_tags(string path, string expected) => Assert.Equal(expected, ComicFormatHints.FileTitle(path));

    [Fact]
    public void The_file_title_is_searched_only_when_it_extends_the_series_name()
    {
        Assert.Equal("Secret Wars 2099", ComicFormatHints.SearchName("Secret Wars", "C:/x/Secret_Wars_2099_001.cbz"));
        Assert.Equal("Secret Wars", ComicFormatHints.SearchName("Secret Wars", "C:/x/Secret Wars 001.cbz"));
        Assert.Equal("Batman", ComicFormatHints.SearchName("Batman", "C:/x/scan_0042.cbz"));
        Assert.True(ComicFormatHints.SameTitle("The Amazing Spider-Man", "Amazing Spider-Man (2025)"));
        Assert.False(ComicFormatHints.SameTitle("Secret Wars", "Secret Wars 2099"));
    }
}
