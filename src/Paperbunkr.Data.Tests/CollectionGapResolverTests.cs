using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Library Health's collection-wide gap view (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.3): every hole in every
/// run of owned numbers, with the Insights tile's stricter thresholds left as they were.
/// </summary>
public class CollectionGapResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public CollectionGapResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_gaps_test_{Guid.NewGuid():N}.db");
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

    private Series AddSeries(string name, params Issue[] issues)
    {
        var series = new Series { Name = name, Issues = issues.ToList() };
        _context.Series.Add(series);
        _context.SaveChanges();
        return series;
    }

    private static Issue Owned(string number, string? publisher = null) => new() { Number = number, Publisher = publisher, FilePath = $"C:\\x\\{number}.cbz" };

    private static Issue[] Run(params int[] numbers) => numbers.Select(n => Owned(n.ToString())).ToArray();

    [Fact]
    public void ForLibrary_ListsMissingNumbersAsRanges_WithTheOwnedRun()
    {
        var series = AddSeries("Invincible", Run(1, 2, 3, 4, 5, 6, 9, 11, 12, 13, 14));

        var row = Assert.Single(CollectionGapResolver.ForLibrary(_context));

        Assert.Equal(series.Id, row.SeriesId);
        Assert.Equal("1–6, 9, 11–14", row.Owned);
        Assert.Equal("#7–8, #10", row.Missing);
        Assert.Equal(3, row.MissingCount);
    }

    [Fact]
    public void ForLibrary_HasNoOwnershipFloorOrMissingCap_AndNeedsOnlyTwoIssues()
    {
        // Insights' tile would skip all three: two issues only, far below 75% owned, and more than 10 missing.
        AddSeries("Sparse", Run(1, 20));

        var row = Assert.Single(CollectionGapResolver.ForLibrary(_context));

        Assert.Equal("#2–19", row.Missing);
        Assert.Equal(18, row.MissingCount);
        Assert.Empty(CollectionGapResolver.Compute(_context.Issues.Include(i => i.Series).ToList(), CollectionGapOptions.Insights));
    }

    [Fact]
    public void ForLibrary_IgnoresFractionalNumbersAnnualsAndPlaceholders()
    {
        AddSeries(
            "Mixed",
            Owned("1"), Owned("2"), Owned("3.5"), Owned("Annual 1"),
            new Issue { Number = "3", IsPlaceholder = true },
            Owned("5"));

        var row = Assert.Single(CollectionGapResolver.ForLibrary(_context));

        // #3 is only wanted (a placeholder) and 3.5 is not a whole number, so both #3 and #4 are holes.
        Assert.Equal("1–2, 5", row.Owned);
        Assert.Equal("#3–4", row.Missing);
    }

    [Fact]
    public void ForLibrary_CountsAnIssueWhoseFileIsMissingAsOwned()
    {
        AddSeries("Lost file", Owned("1"), new Issue { Number = "2", FilePath = "C:\\x\\2.cbz", FileIsMissing = true }, Owned("3"));

        Assert.Empty(CollectionGapResolver.ForLibrary(_context));
    }

    [Fact]
    public void ForLibrary_OrdersClosestToCompleteFirst_ThenByName_AndNamesTheCommonPublisher()
    {
        AddSeries("Zeta", Owned("1", "Image"), Owned("3", "Image"), Owned("4", "Dark Horse"));
        AddSeries("Alpha", Run(1, 3));
        AddSeries("Wide", Run(1, 5));
        AddSeries("Complete", Run(1, 2, 3));

        var rows = CollectionGapResolver.ForLibrary(_context);

        Assert.Equal(["Alpha", "Zeta", "Wide"], rows.Select(r => r.SeriesName));
        Assert.Equal("Image", rows.Single(r => r.SeriesName == "Zeta").Publisher);
        Assert.Null(rows.Single(r => r.SeriesName == "Alpha").Publisher);
    }

    [Fact]
    public void DismissalKey_ChangesWhenANewHoleAppears_SoADismissedSeriesComesBack()
    {
        var series = AddSeries("Saga", Run(1, 2, 4));
        var before = Assert.Single(CollectionGapResolver.ForLibrary(_context));
        HealthDismissals.Dismiss(_context, HealthDismissals.CollectionGap, before.DismissalKey, before.SeriesName);

        _context.Issues.Add(new Issue { SeriesId = series.Id, Number = "7", FilePath = "C:\\x\\7.cbz" });
        _context.SaveChanges();
        var after = Assert.Single(CollectionGapResolver.ForLibrary(_context));

        Assert.Equal($"{series.Id}:3", before.DismissalKey);
        Assert.Equal($"{series.Id}:3,5-6", after.DismissalKey);
        Assert.DoesNotContain(after.DismissalKey, HealthDismissals.KeysFor(_context, HealthDismissals.CollectionGap));
    }

    [Fact]
    public void InsightsOptions_KeepTheTilesStricterRules()
    {
        AddSeries("Mostly there", Run(1, 2, 3, 4, 6, 7, 8));     // 7 of 8 owned: listed
        AddSeries("Two only", Run(1, 3));                        // under three issues: skipped

        var gaps = CollectionGapResolver.Compute(_context.Issues.Include(i => i.Series).ToList(), CollectionGapOptions.Insights);

        var gap = Assert.Single(gaps);
        Assert.Equal("Mostly there", gap.SeriesName);
        Assert.Equal([5], gap.MissingNumbers);
    }
}
