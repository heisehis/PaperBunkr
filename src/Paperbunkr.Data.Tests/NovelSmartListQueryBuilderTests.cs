using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the Novel-target smart-list engine (docs/superpowers/specs/2026-08-30-smart-
/// collections-design.md) against <see cref="Book"/> rows - a brand-new field catalog with no
/// Issue-catalog precedent, so every field gets its own coverage, plus AND/OR/NOT tree combination
/// mirroring <see cref="SmartListQueryBuilderTests"/>.
/// </summary>
public class NovelSmartListQueryBuilderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;
    private readonly int _dunehouseId;
    private readonly int _lighthouseId;

    public NovelSmartListQueryBuilderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_novel_smartlist_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();

        var series = new BookSeries { Name = "The Dune Saga" };
        _context.BookSeries.Add(series);
        _context.SaveChanges();

        var dunehouse = new Book
        {
            Title = "Dunehouse",
            Author = "F. Herbert",
            BookSeriesId = series.Id,
            Format = BookFormat.Epub,
            Summary = "A desert epic.",
            Finished = true,
            ChapterCount = 40,
            AddedTime = new DateTime(2020, 1, 1),
            LastOpenedTime = new DateTime(2020, 6, 1),
            PublishedDate = new DateTime(1965, 1, 1),
            FilePath = @"C:\books\dunehouse.epub",
        };
        var lighthouse = new Book
        {
            Title = "Lighthouse Keeping",
            Author = "V. Woolf",
            Format = BookFormat.Pdf,
            Summary = "A modernist novel.",
            Finished = false,
            ChapterCount = 0,
            AddedTime = new DateTime(2021, 1, 1),
            LastOpenedTime = null,
            PublishedDate = new DateTime(1927, 1, 1),
            FilePath = @"C:\books\lighthouse.pdf",
        };
        _context.Books.AddRange(dunehouse, lighthouse);
        _context.SaveChanges();
        _dunehouseId = dunehouse.Id;
        _lighthouseId = lighthouse.Id;
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

    private static SmartList ListOf(SmartListGroupMode mode, params SmartListCondition[] conditions) =>
        new() { Name = "test", TargetKind = SmartListTargetKind.Novel, RootGroup = new SmartListConditionGroup { Mode = mode, Conditions = conditions.ToList() } };

    private static SmartListCondition Cond(SmartListField field, SmartListOperator op, string value, string? value2 = null, bool not = false) =>
        new() { Field = field, Operator = op, Value = value, Value2 = value2, Not = not };

    [Fact]
    public void NovelTitle_Contains_MatchesTitle()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelTitle, SmartListOperator.Contains, "Dune"));
        Assert.Equal([_dunehouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void NovelAuthor_Is_MatchesAuthor()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelAuthor, SmartListOperator.Is, "V. Woolf"));
        Assert.Equal([_lighthouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void NovelSeries_Contains_MatchesLinkedBookSeriesName()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelSeries, SmartListOperator.Contains, "Dune Saga"));
        Assert.Equal([_dunehouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void NovelSeries_Empty_ForStandaloneNovel()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelSeries, SmartListOperator.Is, string.Empty));
        Assert.Equal([_lighthouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void NovelFormat_Is_MatchesEnumToString()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelFormat, SmartListOperator.Is, "Pdf"));
        Assert.Equal([_lighthouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void NovelSummary_Contains_MatchesSummary()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelSummary, SmartListOperator.Contains, "modernist"));
        Assert.Equal([_lighthouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void NovelFinished_Toggle_MatchesFinishedFlag()
    {
        var list = ListOf(SmartListGroupMode.And, new SmartListCondition { Field = SmartListField.NovelFinished, Operator = SmartListOperator.Is, Value = "true" });
        Assert.Equal([_dunehouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void NovelChapterCount_GreaterThan_MatchesNumericComparison()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelChapterCount, SmartListOperator.GreaterThan, "10"));
        Assert.Equal([_dunehouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void NovelAdded_IsAfter_MatchesDateComparison()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelAdded, SmartListOperator.IsAfter, "2020-06-01"));
        Assert.Equal([_lighthouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void NovelOpened_UnsetDate_NeverMatches()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelOpened, SmartListOperator.IsAfter, "2000-01-01"));
        Assert.Equal([_dunehouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void NovelPublished_DateInRange_MatchesBoth()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelPublished, SmartListOperator.DateInRange, "1960-01-01", value2: "1970-01-01"));
        Assert.Equal([_dunehouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void OrMode_MatchesEitherCondition()
    {
        var list = ListOf(
            SmartListGroupMode.Or,
            Cond(SmartListField.NovelTitle, SmartListOperator.Is, "Dunehouse"),
            Cond(SmartListField.NovelTitle, SmartListOperator.Is, "Lighthouse Keeping"));
        Assert.Equal(2, NovelSmartListQueryBuilder.Build(_context, list).Count);
    }

    [Fact]
    public void NotFlag_NegatesTheConditionsOwnResult()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelTitle, SmartListOperator.Is, "Dunehouse", not: true));
        Assert.Equal([_lighthouseId], NovelSmartListQueryBuilder.Build(_context, list).Select(b => b.Id));
    }

    [Fact]
    public void EmptyGroup_MatchesEveryBook()
    {
        var list = ListOf(SmartListGroupMode.And);
        Assert.Equal(2, NovelSmartListQueryBuilder.Build(_context, list).Count);
    }

    [Fact]
    public void MatchCount_EqualsBuildCount()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.NovelFinished, SmartListOperator.Is, "true"));
        Assert.Equal(1, NovelSmartListQueryBuilder.MatchCount(_context, list));
    }
}
