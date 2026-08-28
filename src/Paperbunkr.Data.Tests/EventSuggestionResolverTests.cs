using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="EventSuggestionResolver"/> (docs/superpowers/specs/2026-08-27-metadata-
/// model-phase4e-format-signal-suggestions-design.md) against a real SQLite database.
/// </summary>
public class EventSuggestionResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public EventSuggestionResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_eventsuggestion_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static int SeedSeries(PaperbunkrDbContext context, string name)
    {
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private static Issue SeedIssue(PaperbunkrDbContext context, int seriesId, string number, string? format = null, int? year = null, string? seriesGroup = null, string? storyArc = null)
    {
        var issue = new Issue { SeriesId = seriesId, Number = number, Format = format, Year = year, SeriesGroup = seriesGroup, StoryArc = storyArc };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue;
    }

    private static int SeedEvent(PaperbunkrDbContext context, string name, DateTime? start = null, DateTime? end = null)
    {
        var storyEvent = new StoryEvent { Name = name, StartDate = start, EndDate = end, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        return storyEvent.Id;
    }

    [Fact]
    public void StrongSignal_InsideDateRange_IsSuggested_WithCorrectRole()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        SeedIssue(context, seriesId, "1", format: "Prologue", year: 2015);
        int eventId = SeedEvent(context, "Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1));

        var suggestion = Assert.Single(EventSuggestionResolver.GetSuggestions(context, eventId));

        Assert.Equal(FormatSignalStrength.Strong, suggestion.Strength);
        Assert.Equal(EventMembershipRole.Prologue, suggestion.SuggestedRole);
        Assert.Contains("within event range", suggestion.Reason);
    }

    [Fact]
    public void SameIssue_OutsideDateRange_NoTextMatch_IsNotSuggested()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        SeedIssue(context, seriesId, "1", format: "Annual", year: 1999);
        int eventId = SeedEvent(context, "Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1));

        Assert.Empty(EventSuggestionResolver.GetSuggestions(context, eventId));
    }

    [Fact]
    public void AlreadyAMember_IsExcluded_EvenIfItWouldOtherwiseMatch()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        var issue = SeedIssue(context, seriesId, "1", format: "Prologue", year: 2015);
        int eventId = SeedEvent(context, "Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1));
        EventMembershipResolver.AddMember(context, eventId, issue.Id, EventMembershipRole.Core);

        Assert.Empty(EventSuggestionResolver.GetSuggestions(context, eventId));
    }

    [Fact]
    public void EventWithNoDates_FallsBackToTextMatchOnly()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        // Right Format, but no date range and no text match -> not suggested.
        SeedIssue(context, seriesId, "1", format: "Annual", year: 2015);
        // Right Format + Story Arc names the event -> suggested.
        SeedIssue(context, seriesId, "2", format: "Special", year: 1990, storyArc: "Part of Secret Wars crossover");
        int eventId = SeedEvent(context, "Secret Wars");

        var suggestion = Assert.Single(EventSuggestionResolver.GetSuggestions(context, eventId));
        Assert.Equal("2", suggestion.Issue.Number);
        Assert.Contains("Story Arc matches event name", suggestion.Reason);
    }

    [Fact]
    public void NoSignalFormat_IsNeverSuggested()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        SeedIssue(context, seriesId, "1", format: "Hardcover", year: 2015, storyArc: "Secret Wars");
        int eventId = SeedEvent(context, "Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1));

        Assert.Empty(EventSuggestionResolver.GetSuggestions(context, eventId));
    }
}
