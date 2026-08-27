using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the smart-list rule engine (docs/superpowers/specs/2026-08-06-smart-lists-design.md)
/// against a small seeded library: per-<see cref="SmartListDataType"/> evaluator behavior, every
/// built-in system list's exact CE-sourced threshold, duplicate detection, and multi-condition AND
/// composition.
/// </summary>
public class SmartListQueryBuilderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;
    private readonly int _seriesAId;
    private readonly int _issueReadId;
    private readonly int _issueReadingId;
    private readonly int _issueNeverReadId;
    private readonly int _issueFavoriteId;
    private readonly int _issueMissingId;
    private readonly int _issueRecentlyAddedId;
    private readonly int _issueOldAddedId;
    private readonly int _issueMetaDupAId;
    private readonly int _issueMetaDupBId;
    private readonly int _issuePathDupId;
    private readonly int _enabledTagId;
    private readonly int _disabledTagId;

    /// <summary>Test-fixture helper: Genre used to be a plain string initializer; now it's a structured tag (docs/superpowers/specs/2026-08-23-weighted-categorized-tags-design.md).</summary>
    private static Issue WithGenre(Issue issue, string genre = "Horror")
    {
        issue.MergeFrom(IssueTagField.Genre, new[] { genre });
        return issue;
    }

    public SmartListQueryBuilderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smartlist_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();

        var seriesA = new Series
        {
            Name = "Alpha",
            Genre = "Horror",
            Publisher = "Acme",
            ContentType = ContentType.Comic,
            ReadingMode = ReadingMode.LeftToRight,
            Status = SeriesStatus.Completed,
        };
        _context.Series.Add(seriesA);
        _context.SaveChanges();
        _seriesAId = seriesA.Id;

        // Genre/Publisher set on every issue here too (not just seriesA) - SmartListCatalog reads
        // the issue-level value (P3 audit fix), matching what Issue Properties/Bulk actually edit.
        var issueRead = WithGenre(new Issue { SeriesId = seriesA.Id, Number = "1", PageCount = 100, LastPageRead = 99, Publisher = "Acme" });
        var issueReading = WithGenre(new Issue { SeriesId = seriesA.Id, Number = "2", PageCount = 100, LastPageRead = 50, Publisher = "Acme" });
        var issueNeverRead = WithGenre(new Issue { SeriesId = seriesA.Id, Number = "3", PageCount = 100, LastPageRead = null, Publisher = "Acme" });
        var issueFavorite = WithGenre(new Issue { SeriesId = seriesA.Id, Number = "4", Rating = 4, Publisher = "Acme" });
        var issueMissing = WithGenre(new Issue { SeriesId = seriesA.Id, Number = "5", FileIsMissing = true, Publisher = "Acme" });
        var issueRecentlyAdded = WithGenre(new Issue { SeriesId = seriesA.Id, Number = "6", AddedTime = DateTime.Now.AddDays(-1), Publisher = "Acme" });
        var issueOldAdded = WithGenre(new Issue { SeriesId = seriesA.Id, Number = "7", AddedTime = DateTime.Now.AddDays(-40), Publisher = "Acme" });

        // Metadata duplicates: same Series/Format/Count/Number/Volume/LanguageISO/Year/Month/Day, different files.
        var issueMetaDupA = WithGenre(new Issue
        {
            SeriesId = seriesA.Id, Number = "10", Format = "CBZ", LanguageISO = "en", Year = 2020, Month = 1, Day = 1,
            FilePath = @"C:\lib\a10.cbz", Publisher = "Acme",
        });
        var issueMetaDupB = WithGenre(new Issue
        {
            SeriesId = seriesA.Id, Number = "10", Format = "CBZ", LanguageISO = "en", Year = 2020, Month = 1, Day = 1,
            FilePath = @"C:\lib\a10_alt.cbz", Publisher = "Acme",
        });
        // Path duplicate: shares issueMetaDupA's exact FilePath but different metadata.
        var issuePathDup = WithGenre(new Issue { SeriesId = seriesA.Id, Number = "99", FilePath = @"C:\lib\a10.cbz", Publisher = "Acme" });

        _context.Issues.AddRange(
            issueRead, issueReading, issueNeverRead, issueFavorite, issueMissing,
            issueRecentlyAdded, issueOldAdded, issueMetaDupA, issueMetaDupB, issuePathDup);
        _context.SaveChanges();

        _issueReadId = issueRead.Id;
        _issueReadingId = issueReading.Id;
        _issueNeverReadId = issueNeverRead.Id;
        _issueFavoriteId = issueFavorite.Id;
        _issueMissingId = issueMissing.Id;
        _issueRecentlyAddedId = issueRecentlyAdded.Id;
        _issueOldAddedId = issueOldAdded.Id;
        _issueMetaDupAId = issueMetaDupA.Id;
        _issueMetaDupBId = issueMetaDupB.Id;
        _issuePathDupId = issuePathDup.Id;

        var enabledTag = new VirtualTagDefinition { Name = "Series Tag", CaptionFormat = "{Series}", IsEnabled = true };
        var disabledTag = new VirtualTagDefinition { Name = "Off Tag", CaptionFormat = "{Series}", IsEnabled = false };
        _context.VirtualTagDefinitions.AddRange(enabledTag, disabledTag);
        _context.SaveChanges();
        _enabledTagId = enabledTag.Id;
        _disabledTagId = disabledTag.Id;
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

    private static SmartList ListOf(params SmartListCondition[] conditions) =>
        new() { Name = "test", Conditions = conditions.ToList() };

    private static SmartListCondition Cond(SmartListField field, SmartListOperator op, string value, string? value2 = null) =>
        new() { Field = field, Operator = op, Value = value, Value2 = value2 };

    private static SmartListCondition VirtualTagCond(int? virtualTagId, SmartListOperator op, string value) =>
        new() { Field = SmartListField.VirtualTag, VirtualTagId = virtualTagId, Operator = op, Value = value };

    // --- Text evaluator ---

    [Fact]
    public void Text_Contains_MatchesPerIssueGenre()
    {
        var list = ListOf(Cond(SmartListField.Genre, SmartListOperator.Contains, "Horr"));
        var result = SmartListQueryBuilder.Build(_context, list);
        Assert.Equal(10, result.Count); // every issue's own Genre is "Horror"
    }

    [Fact]
    public void Text_Is_IsCaseInsensitiveExactMatch()
    {
        var match = ListOf(Cond(SmartListField.Publisher, SmartListOperator.Is, "acme"));
        Assert.Equal(10, SmartListQueryBuilder.Build(_context, match).Count);

        var noMatch = ListOf(Cond(SmartListField.Publisher, SmartListOperator.Is, "Acme Comics"));
        Assert.Empty(SmartListQueryBuilder.Build(_context, noMatch));
    }

    /// <summary>
    /// P3 audit regression test (docs/alpha-roadmap.md) - Genre/Publisher used to read
    /// <c>Issue.Series.Genre</c>/<c>Publisher</c>, so an issue-level edit via Issue
    /// Properties/Bulk (the actual real editors for these fields) never showed up in Smart List
    /// filtering. Diverges one issue's own Genre from its series' Genre to prove the selector
    /// reads the issue's value, not the series'.
    /// </summary>
    [Fact]
    public void Genre_ReadsIssueLevelValue_NotStaleSeriesLevelValue()
    {
        var series = new Series { Name = "Beta", Genre = "Comedy" }; // series-level says Comedy...
        _context.Series.Add(series);
        var issue = WithGenre(new Issue { Series = series, Number = "1" }, "Horror"); // ...but this issue was edited to Horror
        _context.Issues.Add(issue);
        _context.SaveChanges();

        var matchesIssueValue = ListOf(Cond(SmartListField.Genre, SmartListOperator.Is, "Horror"));
        Assert.Contains(issue.Id, SmartListQueryBuilder.Build(_context, matchesIssueValue).Select(i => i.Id));

        var wouldMatchStaleSeriesValue = ListOf(Cond(SmartListField.Genre, SmartListOperator.Is, "Comedy"));
        Assert.DoesNotContain(issue.Id, SmartListQueryBuilder.Build(_context, wouldMatchStaleSeriesValue).Select(i => i.Id));
    }

    [Fact]
    public void Text_StartsWithAndEndsWith()
    {
        var startsWith = ListOf(Cond(SmartListField.Number, SmartListOperator.StartsWith, "1"));
        var ids = SmartListQueryBuilder.Build(_context, startsWith).Select(i => i.Id).ToHashSet();
        Assert.Contains(_issueReadId, ids); // "1"
        Assert.Contains(_issueMetaDupAId, ids); // "10"
        Assert.DoesNotContain(_issueReadingId, ids); // "2"

        var endsWith = ListOf(Cond(SmartListField.Number, SmartListOperator.EndsWith, "9"));
        var endsIds = SmartListQueryBuilder.Build(_context, endsWith).Select(i => i.Id).ToHashSet();
        Assert.Contains(_issuePathDupId, endsIds); // "99"
    }

    [Fact]
    public void Text_ContainsAnyAndContainsAll()
    {
        var any = ListOf(Cond(SmartListField.ContentType, SmartListOperator.ContainsAny, "Manga, Comic"));
        Assert.Equal(10, SmartListQueryBuilder.Build(_context, any).Count);

        var all = ListOf(Cond(SmartListField.ContentType, SmartListOperator.ContainsAll, "Com, ic"));
        Assert.Equal(10, SmartListQueryBuilder.Build(_context, all).Count);

        var allFails = ListOf(Cond(SmartListField.ContentType, SmartListOperator.ContainsAll, "Com, Manga"));
        Assert.Empty(SmartListQueryBuilder.Build(_context, allFails));
    }

    // --- Number evaluator ---

    [Fact]
    public void Number_GreaterThan_MatchesFavoriteRatingThreshold()
    {
        var list = ListOf(Cond(SmartListField.Rating, SmartListOperator.GreaterThan, "3"));
        var result = SmartListQueryBuilder.Build(_context, list);
        Assert.Equal([_issueFavoriteId], result.Select(i => i.Id));
    }

    [Fact]
    public void Number_LessThanAndInRange()
    {
        var lessThan = ListOf(Cond(SmartListField.ReadPercentage, SmartListOperator.LessThan, "10"));
        var lessIds = SmartListQueryBuilder.Build(_context, lessThan).Select(i => i.Id).ToHashSet();
        Assert.Contains(_issueNeverReadId, lessIds);
        Assert.DoesNotContain(_issueReadingId, lessIds);

        var inRange = ListOf(Cond(SmartListField.ReadPercentage, SmartListOperator.InRange, "10", "95"));
        var rangeIds = SmartListQueryBuilder.Build(_context, inRange).Select(i => i.Id).ToHashSet();
        Assert.Contains(_issueReadingId, rangeIds); // 51%
    }

    // --- Toggle evaluator ---

    [Fact]
    public void Toggle_Is_MatchesMissingFiles()
    {
        var list = ListOf(Cond(SmartListField.IsMissing, SmartListOperator.Is, "true"));
        Assert.Equal([_issueMissingId], SmartListQueryBuilder.Build(_context, list).Select(i => i.Id));
    }

    // --- Date evaluator ---

    [Fact]
    public void Date_WithinLastDays_ExcludesOlderIssues()
    {
        var list = ListOf(Cond(SmartListField.Added, SmartListOperator.WithinLastDays, "14"));
        var ids = SmartListQueryBuilder.Build(_context, list).Select(i => i.Id).ToHashSet();
        Assert.Contains(_issueRecentlyAddedId, ids);
        Assert.DoesNotContain(_issueOldAddedId, ids);
    }

    [Fact]
    public void Date_InRange_MatchesWithinExplicitBounds()
    {
        var start = DateTime.Now.AddDays(-3).ToString("O");
        var end = DateTime.Now.AddDays(1).ToString("O");
        var list = ListOf(Cond(SmartListField.Added, SmartListOperator.DateInRange, start, end));
        var ids = SmartListQueryBuilder.Build(_context, list).Select(i => i.Id).ToHashSet();
        Assert.Contains(_issueRecentlyAddedId, ids);
        Assert.DoesNotContain(_issueOldAddedId, ids);
    }

    // --- Virtual Tag evaluator (Alpha to-do P2 - not in CE, see SmartListField.VirtualTag) ---

    [Fact]
    public void VirtualTag_EvaluatesTemplateAgainstIssueAndMatchesAsText()
    {
        var list = ListOf(VirtualTagCond(_enabledTagId, SmartListOperator.Is, "Alpha"));
        var result = SmartListQueryBuilder.Build(_context, list);
        Assert.Equal(10, result.Count); // every issue's Series is "Alpha", so {Series} evaluates to "Alpha" for all
    }

    [Fact]
    public void VirtualTag_DisabledTag_NeverMatches()
    {
        var list = ListOf(VirtualTagCond(_disabledTagId, SmartListOperator.Is, "Alpha"));
        Assert.Empty(SmartListQueryBuilder.Build(_context, list));
    }

    [Fact]
    public void VirtualTag_UnsetOrUnknownId_NeverMatches()
    {
        var unset = ListOf(VirtualTagCond(null, SmartListOperator.Is, "Alpha"));
        Assert.Empty(SmartListQueryBuilder.Build(_context, unset));

        var unknown = ListOf(VirtualTagCond(-1, SmartListOperator.Is, "Alpha"));
        Assert.Empty(SmartListQueryBuilder.Build(_context, unknown));
    }

    // --- Built-in system lists (docs/superpowers/specs/2026-08-06-smart-lists-design.md §5) ---

    [Fact]
    public void SystemList_Read_MatchesAbove95Percent()
    {
        var list = ListOf(Cond(SmartListField.ReadPercentage, SmartListOperator.GreaterThan, "95"));
        Assert.Equal([_issueReadId], SmartListQueryBuilder.Build(_context, list).Select(i => i.Id));
    }

    [Fact]
    public void SystemList_Reading_MatchesInclusive10To95()
    {
        var list = ListOf(Cond(SmartListField.ReadPercentage, SmartListOperator.InRange, "10", "95"));
        var ids = SmartListQueryBuilder.Build(_context, list).Select(i => i.Id).ToHashSet();
        Assert.Contains(_issueReadingId, ids); // 51%
        Assert.DoesNotContain(_issueReadId, ids); // 100% — outside the range, in Read instead
        Assert.DoesNotContain(_issueNeverReadId, ids); // 0%
    }

    [Fact]
    public void SystemList_NeverRead_MatchesBelow10Percent()
    {
        var list = ListOf(Cond(SmartListField.ReadPercentage, SmartListOperator.LessThan, "10"));
        var ids = SmartListQueryBuilder.Build(_context, list).Select(i => i.Id).ToHashSet();
        Assert.Contains(_issueNeverReadId, ids);
        Assert.DoesNotContain(_issueReadingId, ids);
    }

    [Fact]
    public void SystemList_ReadReadingNeverRead_BoundaryOverlapsAtExactly10And95()
    {
        // CE's own two code paths disagree at the boundary; Paperbunkr uses [10,95] inclusive for
        // Reading (ComicLibrary.DefaultReadingList), meaning a value of exactly 10 or 95 matches
        // both Reading and its neighbor — confirmed intentional, not a bug, per the design spec.
        var boundaryIssue = new Issue { SeriesId = _seriesAId, Number = "b", PageCount = 100, LastPageRead = 94 }; // (94+1)*100/100 = 95
        _context.Issues.Add(boundaryIssue);
        _context.SaveChanges();

        var reading = ListOf(Cond(SmartListField.ReadPercentage, SmartListOperator.InRange, "10", "95"));
        var read = ListOf(Cond(SmartListField.ReadPercentage, SmartListOperator.GreaterThan, "95"));

        Assert.Contains(boundaryIssue.Id, SmartListQueryBuilder.Build(_context, reading).Select(i => i.Id));
        Assert.DoesNotContain(boundaryIssue.Id, SmartListQueryBuilder.Build(_context, read).Select(i => i.Id));
    }

    [Fact]
    public void SystemList_Favorites_MatchesRatingGreaterThan3()
    {
        var list = ListOf(Cond(SmartListField.Rating, SmartListOperator.GreaterThan, "3"));
        Assert.Equal([_issueFavoriteId], SmartListQueryBuilder.Build(_context, list).Select(i => i.Id));
    }

    [Fact]
    public void SystemList_MissingFiles_MatchesFileIsMissing()
    {
        var list = ListOf(Cond(SmartListField.IsMissing, SmartListOperator.Is, "true"));
        Assert.Equal([_issueMissingId], SmartListQueryBuilder.Build(_context, list).Select(i => i.Id));
    }

    // --- Duplicate detection ---

    [Fact]
    public void Duplicate_UnionsMetadataAndPathMatchesWithoutDoubleCounting()
    {
        var list = ListOf(Cond(SmartListField.Duplicate, SmartListOperator.Is, "true"));
        var ids = SmartListQueryBuilder.Build(_context, list).Select(i => i.Id).ToHashSet();

        Assert.Equal(3, ids.Count);
        Assert.Contains(_issueMetaDupAId, ids); // metadata-key match with MetaDupB
        Assert.Contains(_issueMetaDupBId, ids);
        Assert.Contains(_issuePathDupId, ids); // path match with MetaDupA
        Assert.DoesNotContain(_issueReadId, ids);
    }

    [Fact]
    public void DuplicateIssueIds_DirectHelper_MatchesBothKeys()
    {
        var issues = _context.Issues.ToList();
        var duplicateIds = SmartListQueryBuilder.DuplicateIssueIds(issues);

        Assert.Equal(3, duplicateIds.Count);
        Assert.Contains(_issueMetaDupAId, duplicateIds);
        Assert.Contains(_issueMetaDupBId, duplicateIds);
        Assert.Contains(_issuePathDupId, duplicateIds);
    }

    // --- Multi-condition AND composition ---

    [Fact]
    public void MultipleConditions_AreAllRequiredToMatch()
    {
        var list = ListOf(
            Cond(SmartListField.ContentType, SmartListOperator.Is, "Comic"),
            Cond(SmartListField.SeriesComplete, SmartListOperator.Is, "true"),
            Cond(SmartListField.IsMissing, SmartListOperator.Is, "true"));

        var result = SmartListQueryBuilder.Build(_context, list);
        Assert.Equal([_issueMissingId], result.Select(i => i.Id));
    }

    [Fact]
    public void MultipleConditions_NarrowsToEmptyWhenContradictory()
    {
        var list = ListOf(
            Cond(SmartListField.IsMissing, SmartListOperator.Is, "true"),
            Cond(SmartListField.Rating, SmartListOperator.GreaterThan, "3"));

        Assert.Empty(SmartListQueryBuilder.Build(_context, list));
    }
}
