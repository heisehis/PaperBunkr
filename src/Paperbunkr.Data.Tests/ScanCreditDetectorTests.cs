using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.ScanCredits;

namespace Paperbunkr.Data.Tests;

/// <summary><see cref="ScanCreditDetector"/>, <see cref="ScanCreditCatalog"/> and <see cref="ScanCreditSettings"/> (docs/superpowers/specs/2026-10-11-scan-credits-design.md).</summary>
public class ScanCreditDetectorTests
{
    private static readonly string[] Nothing = [];

    private static string? Detect(string fileName, params string[] known) => ScanCreditDetector.Detect(fileName, known);

    // --- The usual release-name shapes ---

    [Theory]
    [InlineData("Saga 054 (2018) (Digital) (Zone-Empire).cbz", "Zone-Empire")]
    [InlineData("Batman 001 (2011) (Digital) (Mephisto-Empire).cbr", "Mephisto-Empire")]
    [InlineData("Amazing Spider-Man 001 (of 04) (2016) (webrip) (Minutemen-Slayer).cbz", "Minutemen-Slayer")]
    [InlineData("X-Men 001 (1991) [SomeGroup].cbz", "SomeGroup")]
    [InlineData("Detective Comics 027 (1939) (c2c) (Golden Scans).cbz", "Golden Scans")]
    [InlineData("Hawkeye 001 (2012) (2 covers) (digital) (Darkness-Empire).cbz", "Darkness-Empire")]
    public void TheLastBracketThatIsNotNoise_IsTheScanner(string fileName, string expected)
    {
        Assert.Equal(expected, Detect(fileName));
    }

    [Theory]
    [InlineData("Saga 054 (2018).cbz")]
    [InlineData("Saga 054 (2018) (Digital).cbz")]
    [InlineData("Saga 054 (of 60) (2018) (digital) (c2c) (English).cbz")]
    [InlineData("Saga 054.cbz")]
    [InlineData("Saga 054 (TPB) (Annual) (One-Shot) (Black & White).cbz")]
    [InlineData("Saga 054 (1280px) (17+) (MA15+) (Dec 2018).cbz")]
    [InlineData("")]
    public void ANameWithOnlyNoiseInItsBrackets_HasNoScanner(string fileName)
    {
        Assert.Null(Detect(fileName));
    }

    [Fact]
    public void NoiseWordsAreTrimmedFromTheEndsOfACredit()
    {
        Assert.Equal("Shadowcat-Scans", Detect("Thing 001 (2019) (digital Shadowcat-Scans webrip).cbz"));
    }

    [Fact]
    public void AKnownGroupNameIsKeptWhole_EvenWhenItStartsWithANoiseWord()
    {
        Assert.Equal("digital-Empire", Detect("Thing 001 (2019) (digital-Empire).cbz"));
    }

    [Fact]
    public void TheTailAfterADoubleUnderscore_IsACandidate()
    {
        Assert.Equal("LoneScanner", Detect("Thing 001 (2019)__LoneScanner.cbz"));
    }

    [Fact]
    public void WithoutBrackets_AKnownGroupNameInTheFileName_IsFound()
    {
        Assert.Equal("Clickwheel", Detect("Thing 001 Clickwheel.cbz"));
        Assert.Equal("Clickwheel", Detect("Thing 001 clickwheel.cbz"));
    }

    [Fact]
    public void AKnownName_IsOnlyFoundAsAWholeWord()
    {
        Assert.Null(Detect("The Nileometer 001.cbz"));
    }

    // --- Things the book already says about itself are not credits ---

    [Fact]
    public void ABracketThatRepeatsAFieldOfTheBook_IsSkipped()
    {
        Assert.Null(Detect("Thing 001 (2019) (Marvel).cbz", "Marvel"));
        Assert.Equal("Group", Detect("Thing 001 (2019) (Group) (Marvel).cbz", "Marvel"));
    }

    [Fact]
    public void DetectForAnIssue_UsesItsOwnFields_AndWhatIsInParenthesesInItsSeriesName()
    {
        var issue = new Issue
        {
            FilePath = @"C:\lib\Hellboy (Library Edition) 001 (2008) (Library Edition) (Dark Horse) (Bean-Empire).cbz",
            Series = new Series { Name = "Hellboy (Library Edition)" },
            Publisher = "Dark Horse",
        };

        Assert.Equal("Bean-Empire", ScanCreditDetector.Detect(issue));

        issue.FilePath = @"C:\lib\Hellboy (Library Edition) 001 (2008) (Library Edition) (Dark Horse).cbz";
        Assert.Null(ScanCreditDetector.Detect(issue));
    }

    [Fact]
    public void AnIssueWithoutAFile_HasNoScanner()
    {
        Assert.Null(ScanCreditDetector.Detect(new Issue { Series = new Series { Name = "S" } }));
    }

    // --- The user's additions ---

    [Fact]
    public void AUserIgnoredWord_StopsABracketFromBeingACredit()
    {
        var catalog = ScanCreditCatalog.Create(new ScanCreditSettings([], ["remastered"]));

        Assert.Equal("Remastered", ScanCreditDetector.Detect("Thing 001 (2019) (Remastered).cbz", Nothing));
        Assert.Null(ScanCreditDetector.Detect("Thing 001 (2019) (Remastered).cbz", Nothing, catalog));
    }

    [Fact]
    public void AUserScannerName_IsFoundWithoutBrackets()
    {
        var catalog = ScanCreditCatalog.Create(new ScanCreditSettings(["My Scan Crew"], []));

        Assert.Null(ScanCreditDetector.Detect("Thing 001 My Scan Crew.cbz", Nothing));
        Assert.Equal("My Scan Crew", ScanCreditDetector.Detect("Thing 001 my scan crew.cbz", Nothing, catalog));
    }

    [Fact]
    public void LongerKnownNamesAreTriedFirst()
    {
        var catalog = ScanCreditCatalog.Create(new ScanCreditSettings(["Crew", "Big Crew"], []));

        Assert.Equal("Big Crew", ScanCreditDetector.Detect("Thing 001 Big Crew.cbz", Nothing, catalog));
    }

    // --- Merging into Scan Information ---

    [Theory]
    [InlineData(null, "Zone-Empire", "Zone-Empire")]
    [InlineData("", "Zone-Empire", "Zone-Empire")]
    [InlineData("c2c", "Zone-Empire", "c2c, Zone-Empire")]
    [InlineData("Zulu, Alpha", "Mike", "Alpha, Mike, Zulu")]
    public void Merge_AddsTheCredit_AndSortsTheEntries(string? existing, string credit, string expected)
    {
        Assert.Equal(expected, ScanCreditDetector.Merge(existing, credit));
    }

    [Fact]
    public void Merge_ReturnsNull_WhenTheCreditIsAlreadyThere()
    {
        Assert.Null(ScanCreditDetector.Merge("c2c, zone-empire", "Zone-Empire"));
    }

    // --- The settings file ---

    [Fact]
    public void Settings_RoundTrip_AndDropBlanksAndRepeats()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pb_scan_credits_{Guid.NewGuid():N}.json");
        try
        {
            ScanCreditSettings.Save(new ScanCreditSettings(["Crew", " crew ", "", "Other"], ["remastered", "Remastered"]), path);

            var loaded = ScanCreditSettings.Load(path);

            Assert.Equal(["Crew", "Other"], loaded.Scanners);
            Assert.Equal(["remastered"], loaded.Ignored);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_AMissingOrBrokenFile_IsNoAdditions()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pb_scan_credits_{Guid.NewGuid():N}.json");
        Assert.Same(ScanCreditSettings.Empty, ScanCreditSettings.Load(path));

        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.Same(ScanCreditSettings.Empty, ScanCreditSettings.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
