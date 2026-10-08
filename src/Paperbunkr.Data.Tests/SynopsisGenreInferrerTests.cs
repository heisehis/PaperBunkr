using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Synopsis genre suggestions and the auto-apply confidence gate (docs/superpowers/specs/2026-10-06-smart-features-design.md §6.1,
/// §6.2).
/// </summary>
public class SynopsisGenreInferrerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public SynopsisGenreInferrerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_synopsis_genre_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private const string HorrorSynopsis = "A family moves into a haunted house where a vengeful ghost and a demon wait in the dark.";

    // ----- Infer -----

    [Fact]
    public void Infer_NeedsTwoDifferentWordsForAGenre()
    {
        Assert.Null(SynopsisGenreInferrer.Infer("A detective arrives in a quiet town."));
        Assert.Null(SynopsisGenreInferrer.Infer("A ghost. Another ghost. So many ghost stories."));     // the same word three times is one word

        var crime = SynopsisGenreInferrer.Infer("A detective investigates a murder in a quiet town.");
        Assert.Equal("Crime", crime!.Genre);
        Assert.Equal(["detective", "murder"], crime.MatchedKeywords);
        Assert.Equal(0.6m, crime.Confidence);
    }

    [Fact]
    public void Infer_PicksTheGenreWithTheMostWords_AndConfidenceGrowsWithThemUpToACap()
    {
        var horror = SynopsisGenreInferrer.Infer(HorrorSynopsis + " A detective investigates a murder.");

        Assert.Equal("Horror", horror!.Genre);
        Assert.Equal(0.7m, horror.Confidence);     // haunted, ghost, demon
        Assert.Equal(0.8m, SynopsisGenreInferrer.ConfidenceFor(4));
        Assert.Equal(0.8m, SynopsisGenreInferrer.ConfidenceFor(9));
    }

    [Fact]
    public void Infer_MatchesWholeWordsOnly_IgnoringCase_AndNothingInAnEmptySynopsis()
    {
        Assert.Null(SynopsisGenreInferrer.Infer("The warden awards a reward for swordfish."));     // "war" and "sword" inside other words
        Assert.NotNull(SynopsisGenreInferrer.Infer("A WIZARD and a DRAGON."));
        Assert.Null(SynopsisGenreInferrer.Infer(null));
        Assert.Null(SynopsisGenreInferrer.Infer("   "));
    }

    // ----- SuggestForLibrary -----

    private Series AddSeries(string name, string? summary, string? genre = null)
    {
        var series = new Series { Name = name, Summary = summary, Genre = genre };
        series.Issues.Add(new Issue { Number = "1", FilePath = $"C:\\x\\{name}.cbz" });
        _context.Series.Add(series);
        _context.SaveChanges();
        return series;
    }

    [Fact]
    public void SuggestForLibrary_AddsAPendingSeriesGenreProposal_OnlyWhereThereIsASynopsisAndNoGenre()
    {
        var bare = AddSeries("Bare", HorrorSynopsis);
        AddSeries("Has genre", HorrorSynopsis, genre: "Drama");
        AddSeries("No synopsis", null);
        AddSeries("Unclear", "Two friends talk about their week.");
        var tagged = AddSeries("Issue has a genre tag", HorrorSynopsis);
        _context.IssueTags.Add(new IssueTag { IssueId = tagged.Issues[0].Id, Field = IssueTagField.Genre, Value = "Drama" });
        _context.SaveChanges();

        var result = SynopsisGenreInferrer.SuggestForLibrary(_context);

        var proposal = Assert.Single(_context.MetadataProposals.ToList());
        Assert.Equal(bare.Id, proposal.SeriesId);
        Assert.Null(proposal.IssueId);
        Assert.Equal(MetadataProposalField.Genre, proposal.Field);
        Assert.Equal("Horror", proposal.ProposedValue);
        Assert.Equal(MetadataProposalStatus.Pending, proposal.Status);
        Assert.True(SynopsisGenreInferrer.IsSynopsisSuggestion(proposal));
        Assert.Null(_context.Series.Single(s => s.Id == bare.Id).Genre);     // nothing is applied
        Assert.Equal(1, result.Suggested);
        Assert.Equal(2, result.Checked);                                      // Bare and Unclear
    }

    [Fact]
    public void SuggestForLibrary_StaysPending_EvenUnderTheAutomaticPolicy()
    {
        var settings = _context.GetOrCreateAppSettings();
        settings.MetadataResolutionPolicy = MetadataResolutionPolicy.Automatic;
        settings.AutoApplyMinConfidence = 0m;
        _context.SaveChanges();
        AddSeries("Bare", HorrorSynopsis);

        SynopsisGenreInferrer.SuggestForLibrary(_context);

        Assert.Equal(MetadataProposalStatus.Pending, _context.MetadataProposals.Single().Status);
    }

    [Fact]
    public void SuggestForLibrary_NeverSuggestsAgain_ForASeriesThatAlreadyHasAGenreProposal_EvenARejectedOne()
    {
        var series = AddSeries("Bare", HorrorSynopsis);
        SynopsisGenreInferrer.SuggestForLibrary(_context);
        var proposal = _context.MetadataProposals.Single();
        proposal.Status = MetadataProposalStatus.Rejected;
        _context.SaveChanges();

        var again = SynopsisGenreInferrer.SuggestForLibrary(_context);

        Assert.Equal(0, again.Suggested);
        Assert.Single(_context.MetadataProposals.Where(p => p.SeriesId == series.Id).ToList());
    }

    [Fact]
    public void SuggestForLibrary_LooksAtNoMoreThanItsBudget_AndReportsWhatIsLeft()
    {
        for (int i = 0; i < 5; i++)
        {
            AddSeries($"S{i}", HorrorSynopsis);
        }

        var result = SynopsisGenreInferrer.SuggestForLibrary(_context, budget: 2);

        Assert.Equal(2, result.Checked);
        Assert.Equal(2, result.Suggested);
        Assert.Equal(3, result.Remaining);
        Assert.Equal(2, _context.MetadataProposals.Count());
    }

    // ----- Auto-apply gate -----

    [Theory]
    [InlineData(MetadataResolutionPolicy.Automatic, 0.0, 0.6, true)]      // the default: everything applies, as before the threshold existed
    [InlineData(MetadataResolutionPolicy.Automatic, 0.6, 0.6, true)]      // at the threshold counts
    [InlineData(MetadataResolutionPolicy.Automatic, 0.65, 0.6, false)]    // filename proposals now wait
    [InlineData(MetadataResolutionPolicy.Automatic, 1.0, 1.0, true)]      // provider proposals always clear it
    [InlineData(MetadataResolutionPolicy.Automatic, 1.0, 0.95, false)]
    [InlineData(MetadataResolutionPolicy.Prompt, 0.0, 1.0, false)]        // Prompt: nothing applies itself, whatever the confidence
    public void ShouldApply_IsThePolicyGatedByTheThreshold(MetadataResolutionPolicy policy, double threshold, double confidence, bool expected)
    {
        var settings = new AppSettings { MetadataResolutionPolicy = policy, AutoApplyMinConfidence = (decimal)threshold };

        Assert.Equal(expected, MetadataAutoApply.ShouldApply(settings, (decimal)confidence));
    }

    [Fact]
    public void ShouldApply_ClampsAnOutOfRangeThreshold()
    {
        var tooHigh = new AppSettings { AutoApplyMinConfidence = 5m };
        var negative = new AppSettings { AutoApplyMinConfidence = -1m };

        Assert.True(MetadataAutoApply.ShouldApply(tooHigh, 1m));
        Assert.True(MetadataAutoApply.ShouldApply(negative, 0m));
    }
}
