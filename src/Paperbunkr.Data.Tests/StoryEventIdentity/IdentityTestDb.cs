using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>A temp SQLite database plus builders for the Story Event resolver tests (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md).</summary>
public abstract class IdentityTestDb : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_event_identity_test_{Guid.NewGuid():N}.db");

    protected IdentityTestDb()
    {
        using var context = NewContext();
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

    protected PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    protected Func<PaperbunkrDbContext> Factory => NewContext;

    protected List<Issue> AddSeries(string name, params (string Number, int? Year, int? Month)[] issues)
    {
        using var context = NewContext();
        var series = new Series { Name = name };
        foreach (var (number, year, month) in issues)
        {
            series.Issues.Add(new Issue { Number = number, Year = year, Month = month });
        }

        context.Series.Add(series);
        context.SaveChanges();
        return series.Issues.ToList();
    }

    protected List<Issue> AddSeries(string name, params string[] numbers) =>
        AddSeries(name, numbers.Select(n => (n, (int?)null, (int?)null)).ToArray());

    protected int AddEvent(string name, StoryEventOrigin origin, IEnumerable<Issue> members,
        string? comicVineId = null, string? metronId = null, EventMembershipRole role = EventMembershipRole.Core)
    {
        using var context = NewContext();
        var storyEvent = new StoryEvent
        {
            Name = name, Origin = origin, ComicVineArcId = comicVineId, MetronArcId = metronId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        int position = 1;
        foreach (var issue in members)
        {
            storyEvent.Members.Add(new EventMembership { IssueId = issue.Id, Position = position++, Role = role });
        }

        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        return storyEvent.Id;
    }

    protected void AttachIssueId(Issue issue, ComicProvider provider, string externalId)
    {
        using var context = NewContext();
        context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
        {
            EntityKind = ComicMetadataEntityKind.Issue, EntityId = issue.Id, Provider = provider, ExternalId = externalId,
        });
        context.SaveChanges();
    }
}
