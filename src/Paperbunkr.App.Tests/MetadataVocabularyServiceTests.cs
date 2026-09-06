using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="MetadataVocabularyService"/> - library-learned + static-catalog autocomplete/dropdown
/// vocabularies for the metadata editors (docs/superpowers/specs/2026-09-05-metadata-editor-
/// affordances-design.md §3.1). Runs under <see cref="AvaloniaTestCollection"/> because the Format /
/// Age Rating merge reads <c>MarkResolver</c>'s <c>avares://</c> alias tables.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MetadataVocabularyServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _opts;

    public MetadataVocabularyServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_vocab_test_{Guid.NewGuid():N}.db");
        _opts = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var ctx = new PaperbunkrDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private void Seed(params Issue[] issues)
    {
        using var ctx = new PaperbunkrDbContext(_opts);
        var series = new Series { Name = "S" };
        ctx.Series.Add(series);
        ctx.SaveChanges();
        foreach (var i in issues)
        {
            i.SeriesId = series.Id;
            ctx.Issues.Add(i);
        }
        ctx.SaveChanges();
    }

    private MetadataVocabulary Build()
    {
        using var ctx = new PaperbunkrDbContext(_opts);
        return MetadataVocabularyService.Build(ctx);
    }

    [Fact]
    public void ScalarField_DistinctTrimmedAndSorted()
    {
        Seed(
            new Issue { Number = "1", Publisher = "  Marvel " },
            new Issue { Number = "2", Publisher = "DC Comics" },
            new Issue { Number = "3", Publisher = "marvel" });

        Assert.Equal(new[] { "DC Comics", "Marvel" }, Build()[VocabField.Publisher]);
    }

    [Fact]
    public void ListField_SplitIntoIndividualTokens()
    {
        Seed(new Issue { Number = "1", Writer = "Grant Morrison, Frank Quitely" });

        var writers = Build()[VocabField.Writer];
        Assert.Contains("Grant Morrison", writers);
        Assert.Contains("Frank Quitely", writers);
        Assert.DoesNotContain("Grant Morrison, Frank Quitely", writers);
    }

    [Fact]
    public void Genre_ReadsStructuredTagsAndMergesNoStaticList()
    {
        using (var ctx = new PaperbunkrDbContext(_opts))
        {
            var series = new Series { Name = "S" };
            ctx.Series.Add(series);
            ctx.SaveChanges();
            var issue = new Issue { SeriesId = series.Id, Number = "1" };
            issue.MergeFrom(IssueTagField.Genre, new[] { "Horror, Sci-Fi" });
            ctx.Issues.Add(issue);
            ctx.SaveChanges();
        }

        var genres = Build()[VocabField.Genre];
        Assert.Contains("Horror", genres);
        Assert.Contains("Sci-Fi", genres);
    }

    [Fact]
    public void Format_MergesShippedDefaults_EvenWithEmptyLibrary()
    {
        var formats = Build()[VocabField.Format];
        Assert.Contains("Hardcover", formats);   // a CE [Book Formats] default
    }

    [Fact]
    public void AgeRating_MergesCanonicalList()
    {
        var ratings = Build()[VocabField.AgeRating];
        Assert.Contains("Mature 17+", ratings);
    }

    [Fact]
    public void EmptyLibrary_EveryPurelyLearnedFieldIsEmpty_NeverNull()
    {
        var v = Build();
        Assert.Empty(v[VocabField.Publisher]);
        Assert.Empty(v[VocabField.Writer]);
        Assert.Empty(v[VocabField.Title]);
    }

    [Fact]
    public void Empty_IsAllEmpty()
    {
        Assert.Empty(MetadataVocabulary.Empty[VocabField.Publisher]);
        Assert.Empty(MetadataVocabulary.Empty[VocabField.Format]);
    }
}
