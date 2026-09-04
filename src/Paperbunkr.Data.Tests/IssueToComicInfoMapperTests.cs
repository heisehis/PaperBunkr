using cYo.Projects.ComicRack.Engine;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="IssueToComicInfoMapper"/> - the inverse of <see cref="CeLibraryMigrator.MapStoryFields"/>
/// used by file metadata write-back (docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md).
/// Field-by-field so "which fields round-trip to the file" is one reviewable list.
/// </summary>
public class IssueToComicInfoMapperTests
{
    private static MetadataProposal Accepted(MetadataProposalField field, string value) =>
        new() { Field = field, ProposedValue = value, Status = MetadataProposalStatus.Accepted, CreatedAt = System.DateTime.UtcNow };

    [Fact]
    public void Apply_WritesEveryModeledField()
    {
        var issue = new Issue
        {
            Series = new Series { Name = "Kilo Station" },
            Title = "First Contact",
            Number = "12",
            Count = 24,
            Volume = "2",
            AlternateSeries = "Kilo Station Annual",
            AlternateNumber = "1",
            StoryArc = "Signal War",
            SeriesGroup = "Kilo",
            Summary = "A summary.",
            Notes = "Some notes.",
            Review = "A review.",
            Year = 2021,
            Month = 6,
            Day = 15,
            Writer = "A. Writer",
            Penciller = "B. Penciller",
            Inker = "C. Inker",
            Colorist = "D. Colorist",
            Letterer = "E. Letterer",
            CoverArtist = "F. Cover",
            Editor = "G. Editor",
            Translator = "H. Translator",
            Publisher = "Big Publisher",
            Imprint = "Small Imprint",
            Web = "https://example.com",
            LanguageISO = "en",
            Format = "One Shot",
            AgeRating = "Teen",
            Characters = "Alice, Bob",
            Teams = "Crew",
            Locations = "The Station",
            MainCharacterOrTeam = "Alice",
            ScanInformation = "Scanned by X",
            CommunityRating = 4.5f,
            ColorMode = ColorMode.BlackAndWhite,
        };
        issue.MergeFrom(IssueTagField.Genre, new[] { "Sci-Fi, Drama" });
        issue.MergeFrom(IssueTagField.Tags, new[] { "space, war" });

        var info = new ComicInfo();
        IssueToComicInfoMapper.Apply(issue, info);

        Assert.Equal("Kilo Station", info.Series);
        Assert.Equal("First Contact", info.Title);
        Assert.Equal("12", info.Number);
        Assert.Equal(24, info.Count);
        Assert.Equal(2, info.Volume);
        Assert.Equal("Kilo Station Annual", info.AlternateSeries);
        Assert.Equal("1", info.AlternateNumber);
        Assert.Equal("Signal War", info.StoryArc);
        Assert.Equal("Kilo", info.SeriesGroup);
        Assert.Equal("A summary.", info.Summary);
        Assert.Equal("Some notes.", info.Notes);
        Assert.Equal("A review.", info.Review);
        Assert.Equal(2021, info.Year);
        Assert.Equal(6, info.Month);
        Assert.Equal(15, info.Day);
        Assert.Equal("A. Writer", info.Writer);
        Assert.Equal("B. Penciller", info.Penciller);
        Assert.Equal("C. Inker", info.Inker);
        Assert.Equal("D. Colorist", info.Colorist);
        Assert.Equal("E. Letterer", info.Letterer);
        Assert.Equal("F. Cover", info.CoverArtist);
        Assert.Equal("G. Editor", info.Editor);
        Assert.Equal("H. Translator", info.Translator);
        Assert.Equal("Big Publisher", info.Publisher);
        Assert.Equal("Small Imprint", info.Imprint);
        Assert.Equal("https://example.com", info.Web);
        Assert.Equal("en", info.LanguageISO);
        Assert.Equal("One Shot", info.Format);
        Assert.Equal("Teen", info.AgeRating);
        Assert.Equal("Alice, Bob", info.Characters);
        Assert.Equal("Crew", info.Teams);
        Assert.Equal("The Station", info.Locations);
        Assert.Equal("Alice", info.MainCharacterOrTeam);
        Assert.Equal("Scanned by X", info.ScanInformation);
        Assert.Equal(4.5f, info.CommunityRating);
        Assert.Equal(YesNo.Yes, info.BlackAndWhite);
        Assert.Contains("Sci-Fi", info.Genre);
        Assert.Contains("space", info.Tags);
    }

    [Fact]
    public void Apply_UsesEffectiveValues_AcceptedProposalWins_OverNullField()
    {
        var issue = new Issue
        {
            Number = null,
            Year = null,
            MetadataProposals =
            {
                Accepted(MetadataProposalField.Number, "7"),
                Accepted(MetadataProposalField.Year, "2019"),
            },
        };

        var info = new ComicInfo();
        IssueToComicInfoMapper.Apply(issue, info);

        Assert.Equal("7", info.Number);
        Assert.Equal(2019, info.Year);
    }

    [Fact]
    public void Apply_NullFields_BecomeEmptyOrZero_NotNull()
    {
        var info = new ComicInfo { Writer = "stale", Year = 1999 };
        IssueToComicInfoMapper.Apply(new Issue(), info);

        Assert.Equal(string.Empty, info.Writer);
        Assert.Equal(0, info.Year);
    }

    [Fact]
    public void Apply_SeriesNavNotLoaded_LeavesTargetSeriesUntouched()
    {
        var info = new ComicInfo { Series = "From The File" };
        IssueToComicInfoMapper.Apply(new Issue { Series = null }, info);

        Assert.Equal("From The File", info.Series);
    }

    [Fact]
    public void Apply_PreservesUnmodeledElements()
    {
        var info = new ComicInfo { AlternateCount = 5, PreferredFrontCover = 3 };
        IssueToComicInfoMapper.Apply(new Issue { Title = "X" }, info);

        Assert.Equal(5, info.AlternateCount);
        Assert.Equal(3, info.PreferredFrontCover);
    }

    [Theory]
    [InlineData(ColorMode.Color, YesNo.No)]
    [InlineData(ColorMode.BlackAndWhite, YesNo.Yes)]
    [InlineData(ColorMode.Grayscale, YesNo.Yes)]
    [InlineData(ColorMode.Unknown, YesNo.Unknown)]
    [InlineData(ColorMode.Mixed, YesNo.Unknown)]
    public void Apply_MapsColorModeToBlackAndWhite(ColorMode mode, YesNo expected)
    {
        var info = new ComicInfo();
        IssueToComicInfoMapper.Apply(new Issue { ColorMode = mode }, info);
        Assert.Equal(expected, info.BlackAndWhite);
    }

    [Theory]
    [InlineData(ContentType.Manga, ReadingMode.RightToLeft, MangaYesNo.YesAndRightToLeft)]
    [InlineData(ContentType.Manga, ReadingMode.LeftToRight, MangaYesNo.Yes)]
    [InlineData(ContentType.Comic, ReadingMode.LeftToRight, MangaYesNo.No)]
    [InlineData(ContentType.Manhwa, ReadingMode.LeftToRight, MangaYesNo.Unknown)]
    [InlineData(ContentType.Unknown, ReadingMode.LeftToRight, MangaYesNo.Unknown)]
    public void Apply_MapsSeriesClassificationToManga(ContentType contentType, ReadingMode readingMode, MangaYesNo expected)
    {
        var issue = new Issue { Series = new Series { ContentType = contentType, ReadingMode = readingMode } };
        var info = new ComicInfo();
        IssueToComicInfoMapper.Apply(issue, info);
        Assert.Equal(expected, info.Manga);
    }
}
