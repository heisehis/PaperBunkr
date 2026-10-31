using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>The Slice 2a novel smart-list fields (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §4).</summary>
public class NovelSmartListSlice2Tests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_novel_slice2_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;
    private readonly int _loved;
    private readonly int _plain;

    public NovelSmartListSlice2Tests()
    {
        _context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _context.Database.EnsureCreated();

        var loved = new Book
        {
            Title = "Loved", Format = BookFormat.Epub, FilePath = @"C:\b\loved.epub", AddedTime = DateTime.UtcNow,
            Rating = 4.5f, Status = BookReadingStatus.Reading, Publisher = "Tor", LanguageISO = "en", SeriesIndex = 2f,
            ChapterCount = 10, LastChapterIndex = 4, LastProgressionFraction = 0.5,
        };
        loved.Tags.Add(new BookTag { Field = BookTagField.Genre, Value = "Fantasy" });
        loved.Tags.Add(new BookTag { Field = BookTagField.Tags, Value = "dragons" });
        var plain = new Book { Title = "Plain", Format = BookFormat.Pdf, FilePath = @"C:\b\plain.pdf", AddedTime = DateTime.UtcNow };
        _context.Books.AddRange(loved, plain);
        _context.SaveChanges();
        _loved = loved.Id;
        _plain = plain.Id;
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private static SmartList ListOf(SmartListGroupMode mode, params SmartListCondition[] conditions) =>
        new() { Name = "t", TargetKind = SmartListTargetKind.Novel, RootGroup = new SmartListConditionGroup { Mode = mode, Conditions = conditions.ToList() } };

    private static SmartListCondition Cond(SmartListField field, SmartListOperator op, string value) =>
        new() { Field = field, Operator = op, Value = value };

    private int[] Match(params SmartListCondition[] conditions) =>
        NovelSmartListQueryBuilder.Build(_context, ListOf(SmartListGroupMode.And, conditions)).Select(b => b.Id).ToArray();

    [Fact]
    public void Rating_GreaterThan_TreatsUnratedAsZero()
    {
        Assert.Equal(new[] { _loved }, Match(Cond(SmartListField.NovelRating, SmartListOperator.GreaterThan, "4")));
        Assert.Equal(new[] { _plain }, Match(Cond(SmartListField.NovelRating, SmartListOperator.Is, "0")));
    }

    [Fact]
    public void Status_Is_MatchesTheDisplayName()
    {
        Assert.Equal(new[] { _loved }, Match(Cond(SmartListField.NovelStatus, SmartListOperator.Is, "Reading")));
        Assert.Equal(new[] { _plain }, Match(Cond(SmartListField.NovelStatus, SmartListOperator.Is, "Unread")));
    }

    [Fact]
    public void Tag_Contains_FindsAGenreOrATag()
    {
        Assert.Equal(new[] { _loved }, Match(Cond(SmartListField.NovelTag, SmartListOperator.Contains, "fantasy")));
        Assert.Equal(new[] { _loved }, Match(Cond(SmartListField.NovelTag, SmartListOperator.Contains, "dragons")));
        Assert.Empty(Match(Cond(SmartListField.NovelTag, SmartListOperator.Contains, "romance")));
    }

    [Fact]
    public void PublisherAndLanguage_MatchText_AndBlankIsEmpty()
    {
        Assert.Equal(new[] { _loved }, Match(Cond(SmartListField.NovelPublisher, SmartListOperator.Is, "Tor")));
        Assert.Equal(new[] { _plain }, Match(Cond(SmartListField.NovelPublisher, SmartListOperator.Is, string.Empty)));
        Assert.Equal(new[] { _loved }, Match(Cond(SmartListField.NovelLanguage, SmartListOperator.Is, "en")));
    }

    [Fact]
    public void SeriesIndexAndProgress_AreNumeric()
    {
        Assert.Equal(new[] { _loved }, Match(Cond(SmartListField.NovelSeriesIndex, SmartListOperator.GreaterThan, "1")));
        Assert.Equal(new[] { _loved }, Match(Cond(SmartListField.NovelProgress, SmartListOperator.GreaterThan, "40")));
        Assert.Equal(new[] { _plain }, Match(Cond(SmartListField.NovelProgress, SmartListOperator.LessThan, "1")));
    }

    [Fact]
    public void NewFields_Combine_WithAndOr()
    {
        var and = ListOf(SmartListGroupMode.And,
            Cond(SmartListField.NovelRating, SmartListOperator.GreaterThan, "3"), Cond(SmartListField.NovelTag, SmartListOperator.Contains, "fantasy"));
        Assert.Equal(new[] { _loved }, NovelSmartListQueryBuilder.Build(_context, and).Select(b => b.Id));

        var or = ListOf(SmartListGroupMode.Or,
            Cond(SmartListField.NovelRating, SmartListOperator.GreaterThan, "3"), Cond(SmartListField.NovelPublisher, SmartListOperator.Is, string.Empty));
        Assert.Equal(2, NovelSmartListQueryBuilder.Build(_context, or).Count);
    }

    [Fact]
    public void EveryNovelField_HasADefinition_AndASelectorOfItsType()
    {
        foreach (var (field, definition) in NovelSmartListCatalog.Definitions)
        {
            bool hasSelector = definition.DataType switch
            {
                SmartListDataType.Text => NovelSmartListCatalog.TextSelectors.ContainsKey(field),
                SmartListDataType.Number => NovelSmartListCatalog.NumberSelectors.ContainsKey(field),
                SmartListDataType.Toggle => NovelSmartListCatalog.ToggleSelectors.ContainsKey(field),
                SmartListDataType.Date => NovelSmartListCatalog.DateSelectors.ContainsKey(field),
                _ => false,
            };
            Assert.True(hasSelector, $"{field} has no selector for {definition.DataType}");
        }
    }
}
