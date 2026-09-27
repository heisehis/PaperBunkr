using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Maintains the derived <see cref="Publisher"/> pointer on <see cref="Series"/>/<see cref="Issue"/>
/// (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md). Unlike <see cref="CharacterResolver"/>
/// and its siblings, publisher is single-valued per series/issue, not a list - so there's no
/// appearance-style join table, no sync-and-prune, just resolve-or-create and overwrite the FK.
/// <see cref="Issue.Publisher"/> is the real source of truth (<see cref="Series.Publisher"/>'s own doc
/// comment says it's only "populated once at CE-migration time"), so <see cref="SyncSeries"/> falls
/// back to the series' own issues rather than trusting <see cref="Series.Publisher"/> directly.
/// </summary>
public static class PublisherResolver
{
    public static Publisher GetOrCreate(PaperbunkrDbContext context, string name)
    {
        string trimmed = name.Trim();
        var existing = context.Publishers.FirstOrDefault(p => p.Name.ToLower() == trimmed.ToLower());
        if (existing is not null)
        {
            return existing;
        }

        var publisher = new Publisher { Name = trimmed };
        context.Publishers.Add(publisher);
        context.SaveChanges();
        return publisher;
    }

    /// <summary>Resolves-or-creates a <see cref="Publisher"/> from <see cref="Issue.Publisher"/> and points <see cref="Issue.PublisherEntityId"/> at it. No-op when the issue has no publisher text.</summary>
    public static void SyncIssue(PaperbunkrDbContext context, int issueId)
    {
        var issue = context.Issues.FirstOrDefault(i => i.Id == issueId);
        if (issue is null || string.IsNullOrWhiteSpace(issue.Publisher))
        {
            return;
        }

        var publisher = GetOrCreate(context, issue.Publisher);
        if (issue.PublisherEntityId != publisher.Id)
        {
            issue.PublisherEntityId = publisher.Id;
            context.SaveChanges();
        }
    }

    /// <summary>
    /// Resolves-or-creates a <see cref="Publisher"/> for a series, preferring its issues' own
    /// (per-issue-authoritative) <see cref="Issue.Publisher"/> text over the series' own
    /// CE-migration-only <see cref="Series.Publisher"/> string. No-op when neither has a value.
    /// </summary>
    public static void SyncSeries(PaperbunkrDbContext context, int seriesId)
    {
        var series = context.Series.FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return;
        }

        string? name = context.Issues
            .Where(i => i.SeriesId == seriesId && i.Publisher != null && i.Publisher != "")
            .Select(i => i.Publisher)
            .FirstOrDefault() ?? series.Publisher;

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var publisher = GetOrCreate(context, name);
        if (series.PublisherEntityId != publisher.Id)
        {
            series.PublisherEntityId = publisher.Id;
            context.SaveChanges();
        }
    }
}
