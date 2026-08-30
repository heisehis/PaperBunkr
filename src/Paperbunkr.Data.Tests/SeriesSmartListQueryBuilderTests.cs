using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the Series-target smart-list engine (docs/superpowers/specs/2026-08-30-smart-
/// collections-design.md) - the field catalog reading straight off <see cref="Series"/> (not
/// through an <see cref="Issue"/> join), and AND/OR/NOT tree combination mirroring
/// <see cref="SmartListQueryBuilderTests"/>'s existing Issue-kind coverage.
/// </summary>
public class SeriesSmartListQueryBuilderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;
    private readonly int _alphaId;
    private readonly int _betaId;

    public SeriesSmartListQueryBuilderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_series_smartlist_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();

        var alpha = new Series
        {
            Name = "Alpha",
            SortName = "Alpha Sort",
            Genre = "Horror",
            Publisher = "Acme",
            ContentType = ContentType.Comic,
            ReadingMode = ReadingMode.LeftToRight,
            Status = SeriesStatus.Completed,
            ReadingStatus = ReadingStatus.Reading,
        };
        var beta = new Series
        {
            Name = "Beta",
            SortName = "Beta Sort",
            Genre = "Comedy",
            Publisher = "Zenith",
            ContentType = ContentType.Manga,
            ReadingMode = ReadingMode.RightToLeft,
            Status = SeriesStatus.Ongoing,
            ReadingStatus = ReadingStatus.Planned,
        };
        _context.Series.AddRange(alpha, beta);
        _context.SaveChanges();
        _alphaId = alpha.Id;
        _betaId = beta.Id;

        var continuity = new Continuity { Name = "Shared Universe" };
        _context.Continuities.Add(continuity);
        _context.SaveChanges();
        _context.ContinuityMemberships.Add(new ContinuityMembership { ContinuityId = continuity.Id, SeriesId = alpha.Id });
        _context.SaveChanges();
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
        new() { Name = "test", TargetKind = SmartListTargetKind.Series, RootGroup = new SmartListConditionGroup { Mode = mode, Conditions = conditions.ToList() } };

    private static SmartListCondition Cond(SmartListField field, SmartListOperator op, string value, bool not = false) =>
        new() { Field = field, Operator = op, Value = value, Not = not };

    [Fact]
    public void SeriesName_Is_MatchesExactSeries()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.SeriesName, SmartListOperator.Is, "Alpha"));
        Assert.Equal([_alphaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void SeriesSortName_Contains_MatchesSortName()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.SeriesSortName, SmartListOperator.Contains, "Beta"));
        Assert.Equal([_betaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void Genre_ReadsSeriesOwnColumn_NotAggregatedFromIssues()
    {
        // Approved design decision (docs/superpowers/specs/2026-08-30-smart-collections-design.md):
        // Series-target Genre/Publisher read Series.Genre/Series.Publisher directly.
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.Genre, SmartListOperator.Is, "Horror"));
        Assert.Equal([_alphaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void Publisher_Is_MatchesSeriesPublisher()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.Publisher, SmartListOperator.Is, "Zenith"));
        Assert.Equal([_betaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void ContentType_Is_MatchesEnumToString()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.ContentType, SmartListOperator.Is, "Manga"));
        Assert.Equal([_betaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void ReadingMode_Is_MatchesEnumToString()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.ReadingMode, SmartListOperator.Is, "RightToLeft"));
        Assert.Equal([_betaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void SeriesStatus_Is_MatchesPublisherCompletionState()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.SeriesStatus, SmartListOperator.Is, "Completed"));
        Assert.Equal([_alphaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void ReadingStatus_Is_MatchesReadersOwnRelationship()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.ReadingStatus, SmartListOperator.Is, "Planned"));
        Assert.Equal([_betaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void SeriesComplete_Toggle_MatchesComputedIsComplete()
    {
        var list = ListOf(SmartListGroupMode.And, new SmartListCondition { Field = SmartListField.SeriesComplete, Operator = SmartListOperator.Is, Value = "true" });
        Assert.Equal([_alphaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void Continuity_Contains_MatchesJoinedContinuityNames()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.Continuity, SmartListOperator.Contains, "Shared"));
        Assert.Equal([_alphaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void OrMode_MatchesEitherCondition()
    {
        var list = ListOf(
            SmartListGroupMode.Or,
            Cond(SmartListField.SeriesName, SmartListOperator.Is, "Alpha"),
            Cond(SmartListField.SeriesName, SmartListOperator.Is, "Beta"));
        Assert.Equal(2, SeriesSmartListQueryBuilder.Build(_context, list).Count);
    }

    [Fact]
    public void NotFlag_NegatesTheConditionsOwnResult()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.SeriesName, SmartListOperator.Is, "Alpha", not: true));
        Assert.Equal([_betaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void NestedGroup_CombinesWithParentByItsOwnMode()
    {
        var root = new SmartListConditionGroup
        {
            Mode = SmartListGroupMode.And,
            Conditions = [Cond(SmartListField.ContentType, SmartListOperator.Is, "Comic")],
            ChildGroups =
            [
                new SmartListConditionGroup
                {
                    Mode = SmartListGroupMode.Or,
                    Conditions =
                    [
                        Cond(SmartListField.SeriesName, SmartListOperator.Is, "Alpha"),
                        Cond(SmartListField.SeriesName, SmartListOperator.Is, "NoSuchSeries"),
                    ],
                },
            ],
        };
        var list = new SmartList { Name = "nested", TargetKind = SmartListTargetKind.Series, RootGroup = root };
        Assert.Equal([_alphaId], SeriesSmartListQueryBuilder.Build(_context, list).Select(s => s.Id));
    }

    [Fact]
    public void EmptyGroup_MatchesEverySeries()
    {
        var list = ListOf(SmartListGroupMode.And);
        Assert.Equal(2, SeriesSmartListQueryBuilder.Build(_context, list).Count);
    }

    [Fact]
    public void MatchCount_EqualsBuildCount()
    {
        var list = ListOf(SmartListGroupMode.And, Cond(SmartListField.SeriesName, SmartListOperator.Is, "Alpha"));
        Assert.Equal(1, SeriesSmartListQueryBuilder.MatchCount(_context, list));
    }
}
