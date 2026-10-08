using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The series-target progress fields (<see cref="SmartListField.UnreadCount"/>, <see cref="SmartListField.ReadCount"/>,
/// <see cref="SmartListField.DaysSinceLastRead"/>) and the issue-target <see cref="SmartListField.HasPendingProposal"/>
/// (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.1).
/// </summary>
public class SeriesProgressFieldTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public SeriesProgressFieldTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_series_progress_test_{Guid.NewGuid():N}.db");
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

    private static Issue Issue(string number, int? lastPage = null, DateTime? opened = null, bool placeholder = false) => new()
    {
        Number = number,
        PageCount = 10,
        LastPageRead = lastPage,
        OpenedTime = opened,
        IsPlaceholder = placeholder,
        FilePath = placeholder ? null : $"C:\\x\\{number}.cbz",
    };

    private static SmartList ListOf(SmartListTargetKind kind, params SmartListCondition[] conditions) =>
        new() { Name = "test", TargetKind = kind, RootGroup = new SmartListConditionGroup { Mode = SmartListGroupMode.And, Conditions = conditions.ToList() } };

    private static SmartListCondition Cond(SmartListField field, SmartListOperator op, string value, bool not = false) =>
        new() { Field = field, Operator = op, Value = value, Not = not };

    private List<int> MatchingSeries(params SmartListCondition[] conditions)
    {
        var list = ListOf(SmartListTargetKind.Series, conditions);
        var snapshot = SeriesSmartListQueryBuilder.LoadSnapshot(_context, conditions, Now);
        return SeriesSmartListQueryBuilder.Evaluate(snapshot, list).Select(s => s.Id).ToList();
    }

    [Fact]
    public void ReadAndUnreadCounts_UseTheNinetyFivePercentRule_AndIgnorePlaceholders()
    {
        // 10 pages: LastPageRead 9 is 100% (read), 3 is 40% (in progress = unread), none is unread; the placeholder is not counted at all.
        var series = AddSeries("Mixed", Issue("1", lastPage: 9), Issue("2", lastPage: 3), Issue("3"), Issue("4", placeholder: true));

        Assert.Equal([series.Id], MatchingSeries(Cond(SmartListField.ReadCount, SmartListOperator.Is, "1")));
        Assert.Equal([series.Id], MatchingSeries(Cond(SmartListField.UnreadCount, SmartListOperator.Is, "2")));
        Assert.Empty(MatchingSeries(Cond(SmartListField.UnreadCount, SmartListOperator.Is, "3")));
    }

    [Fact]
    public void UnreadCount_GreaterThan_SelectsOnlyTheSeriesWithMoreLeft()
    {
        var few = AddSeries("Few", Issue("1", lastPage: 9), Issue("2"));
        var many = AddSeries("Many", Issue("1"), Issue("2"), Issue("3"), Issue("4"));

        var matches = MatchingSeries(Cond(SmartListField.UnreadCount, SmartListOperator.GreaterThan, "2"));

        Assert.Equal([many.Id], matches);
        Assert.DoesNotContain(few.Id, matches);
    }

    [Fact]
    public void DaysSinceLastRead_NeverOpenedSeries_HasNoValue_AndNeverMatches()
    {
        AddSeries("Never", Issue("1"), Issue("2"));

        Assert.Empty(MatchingSeries(Cond(SmartListField.DaysSinceLastRead, SmartListOperator.LessThan, "30")));
        Assert.Empty(MatchingSeries(Cond(SmartListField.DaysSinceLastRead, SmartListOperator.GreaterThan, "0")));
    }

    [Fact]
    public void DaysSinceLastRead_ComparesTheNewestOpen_AgainstTheInjectedClock()
    {
        var recent = AddSeries("Recent", Issue("1", lastPage: 2, opened: Now.AddDays(-3)));
        var old = AddSeries("Old", Issue("1", lastPage: 2, opened: Now.AddDays(-40)), Issue("2", lastPage: 2, opened: Now.AddDays(-60)));

        Assert.Equal([old.Id], MatchingSeries(Cond(SmartListField.DaysSinceLastRead, SmartListOperator.GreaterThan, "30")));
        Assert.Equal([recent.Id], MatchingSeries(Cond(SmartListField.DaysSinceLastRead, SmartListOperator.LessThan, "30")));
    }

    [Fact]
    public void DaysSinceLastRead_UsesANewerReadingEvent_OverTheIssuesOpenedTime()
    {
        var series = AddSeries("Evented", Issue("1", lastPage: 2, opened: Now.AddDays(-90)));
        _context.ReadingEvents.Add(new ReadingEvent
        {
            ItemType = ReadingItemType.Comic,
            ItemId = series.Issues[0].Id,
            Kind = ReadingEventKind.Opened,
            TimestampUtc = Now.AddDays(-2),
            SeriesId = series.Id,
        });
        _context.SaveChanges();

        Assert.Equal([series.Id], MatchingSeries(Cond(SmartListField.DaysSinceLastRead, SmartListOperator.LessThan, "7")));
    }

    [Fact]
    public void ProgressFields_CombineWithOtherSeriesFields_InOneList()
    {
        var ongoing = AddSeries("Ongoing", Issue("1", lastPage: 9, opened: Now.AddDays(-45)), Issue("2"), Issue("3"), Issue("4"));
        ongoing.Status = SeriesStatus.Ongoing;
        var done = AddSeries("Done", Issue("1", lastPage: 9, opened: Now.AddDays(-45)), Issue("2"), Issue("3"), Issue("4"));
        done.Status = SeriesStatus.Completed;
        _context.SaveChanges();

        var matches = MatchingSeries(
            Cond(SmartListField.SeriesStatus, SmartListOperator.Is, "Ongoing"),
            Cond(SmartListField.UnreadCount, SmartListOperator.GreaterThan, "2"),
            Cond(SmartListField.DaysSinceLastRead, SmartListOperator.GreaterThan, "30"));

        Assert.Equal([ongoing.Id], matches);
    }

    [Fact]
    public void HasPendingProposal_MatchesOnlyIssuesWithAPendingProposal()
    {
        var series = AddSeries("Proposals", Issue("1"), Issue("2"), Issue("3"));
        _context.MetadataProposals.Add(new MetadataProposal
        {
            IssueId = series.Issues[0].Id,
            Field = MetadataProposalField.Year,
            ProposedValue = "1990",
            Source = MetadataProposalSource.FilenameParser,
            Status = MetadataProposalStatus.Pending,
        });
        _context.MetadataProposals.Add(new MetadataProposal
        {
            IssueId = series.Issues[1].Id,
            Field = MetadataProposalField.Year,
            ProposedValue = "1991",
            Source = MetadataProposalSource.FilenameParser,
            Status = MetadataProposalStatus.Accepted,
        });
        _context.SaveChanges();

        var list = ListOf(SmartListTargetKind.Issue, Cond(SmartListField.HasPendingProposal, SmartListOperator.Is, "true"));
        var ids = SmartListQueryBuilder.Build(_context, list).Select(i => i.Id).ToList();

        Assert.Equal([series.Issues[0].Id], ids);
    }
}
