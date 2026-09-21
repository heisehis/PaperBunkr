using Paperbunkr.Daemon.Import;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Naming;

namespace Paperbunkr.Daemon.Tests.Import;

public class ImportNamingTests
{
    private static WatchedSeries Watched(string name = "Spawn", string? publisher = "Image", int? startYear = 1992) => new() { Name = name, Publisher = publisher, StartYear = startYear };

    private static WantedIssue Wanted(string number = "263", string? title = "Endgame", DateTime? store = null) =>
        new() { IssueNumber = number, Name = title, StoreDate = store ?? new DateTime(2016, 5, 4) };

    /// <summary>The importer's original token lookup, kept here as the oracle for the equivalence test.</summary>
    private static string? OldToken(string name, WatchedSeries watched, WantedIssue wanted, string sourcePath) => name switch
    {
        "series" => watched.Name,
        "title" => wanted.Name,
        "volume" => watched.StartYear is int y ? $"v{y}" : null,
        "volumeyear" => watched.StartYear?.ToString(),
        "number" => wanted.IssueNumber,
        "year" => wanted.StoreDate?.Year.ToString(),
        "month" => wanted.StoreDate?.Month.ToString("00"),
        "day" => wanted.StoreDate?.Day.ToString("00"),
        "publisher" => watched.Publisher,
        "filename" => Path.GetFileNameWithoutExtension(sourcePath),
        _ => null,
    };

    public static IEnumerable<object[]> Templates() => new[]
    {
        "{publisher}/{series} ({volumeyear})/{series} #{number:000}",
        "{series} #{number:000}",
        "{series}/{series} v{volume} #{number}",
        "{publisher}/{series}[ ({volumeyear})]/{series}[ - {title}] #{number:00}",
        "{series} {year}-{month}-{day}",
        "{series} ({year:0000})",
        "{filename}",
        "{series}[ ({year})]",
    }.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(Templates))]
    public void TheTranslatedTemplate_ProducesTheSamePath_AsTheOriginalOne(string original)
    {
        var translated = NameTemplateTranslator.Translate(original);
        Assert.True(translated.Success, translated.Error);

        var cases = new (WatchedSeries Watched, WantedIssue Wanted, string Source)[]
        {
            (Watched(), Wanted(), "Spawn 263 (2016).cbr"),
            (Watched("Batman", "DC Comics", 2016), Wanted("5", "The Rebirth", new DateTime(2016, 11, 9)), "Batman 005.cbz"),
            (Watched(publisher: null), Wanted(), "x.cbz"),                                  // no publisher: the folder segment is dropped
            (Watched(startYear: null), Wanted(), "x.cbz"),                                  // no start year: groups drop, literals stay
            (Watched(), Wanted("1.5", null), "x.cbz"),                                      // a non-whole number is never rounded or padded
            (Watched(), Wanted("Annual 1", "Big One"), "x.cbz"),
            (Watched(), Wanted("7", "Title", new DateTime(2016, 1, 2)), "x.cbz"),
        };

        foreach (var (watched, wanted, source) in cases)
        {
            var expected = LegacyNameTemplate.FormatPath(original, t => OldToken(t, watched, wanted, source), ".cbz");
            var actual = ImportNaming.FormatPath(translated.Template!, watched, wanted, source, ".cbz");
            Assert.True(expected == actual, $"template '{original}' -> '{translated.Template}' for {watched.Name} #{wanted.IssueNumber}: expected '{expected}', got '{actual}'");
        }
    }

    [Fact]
    public void TheDefaultTemplate_IsTheTranslationOfTheOriginalDefault()
    {
        Assert.Equal(ImportNaming.DefaultTemplate, NameTemplateTranslator.Translate("{publisher}/{series} ({volumeyear})/{series} #{number:000}").Template);
        Assert.Equal("Image/Spawn (1992)/Spawn #263.cbz", ImportNaming.FormatPath(ImportNaming.DefaultTemplate, Watched(), Wanted(), "x.cbz", ".cbz"));
        Assert.Equal("Image/Spawn (1992)/Spawn #007.cbz", ImportNaming.FormatPath(ImportNaming.DefaultTemplate, Watched(), Wanted("7"), "x.cbz", ".cbz"));
    }

    [Fact]
    public void AColon_IsSanitizedTheCeWay_ADeliberateChangeFromTheOldUnderscoreDash()
    {
        // CE's own sanitizer maps ':' to " - " (the old importer wrote "Batman- Year One"); one canonical rule now serves the importer and the organizer.
        var path = ImportNaming.FormatPath(ImportNaming.DefaultTemplate, Watched("Batman: Year One", "DC", 1987), Wanted("1"), "x.cbz", ".cbz");
        Assert.Equal("DC/Batman - Year One (1987)/Batman - Year One #001.cbz", path);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    public void ReservedWindowsNames_AreStillNeutralized(string series)
    {
        var path = ImportNaming.FormatPath("{<series>}", Watched(series), Wanted(), "x.cbz", ".cbz");
        Assert.StartsWith("_", path);
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("Trailing dots...", "Trailing dots")]
    [InlineData("..", "")]
    public void MakeSafe_KeepsNamesLegalOnWindows(string input, string expected) => Assert.Equal(expected, ImportNaming.MakeSafe(input));

    [Fact]
    public void ALongName_IsBounded_AndAnEmptyResultBecomesUnnamed()
    {
        Assert.True(ImportNaming.FormatPath("{<series>}", Watched(new string('x', 500)), Wanted(), "x.cbz", ".cbz").Length <= 124);
        Assert.Equal("Unnamed.cbz", ImportNaming.FormatPath("{<title>}", Watched(), Wanted(title: null), "x.cbz", ".cbz"));
    }

    [Theory]
    [InlineData("{<series>} #{<number3>}", null)]
    [InlineData("", "empty")]
    [InlineData("{<nonsense>}", "not supported")]
    [InlineData("{<series>} {", "unbalanced")]
    public void Validate_ReportsProblemsBeforeAnythingIsImported(string template, string? fragment)
    {
        var error = ImportNaming.Validate(template);
        if (fragment is null) Assert.Null(error);
        else Assert.Contains(fragment, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preview_ShowsAnExampleOrTheError()
    {
        Assert.Equal("Image/Spawn (1992)/Spawn #263.cbz", ImportNaming.Preview(ImportNaming.DefaultTemplate));
        Assert.Contains("not supported", ImportNaming.Preview("{<nonsense>}"), StringComparison.OrdinalIgnoreCase);
    }
}
