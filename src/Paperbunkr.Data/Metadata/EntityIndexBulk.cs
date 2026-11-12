using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Set-based sync of the four derived entity indexes (characters, teams, locations, creators) for many issues at once, or the whole library.
/// The per-issue resolvers (<see cref="CharacterResolver.SyncFromIssue"/> etc.) save and prune once per issue, which made a whole-library rebuild
/// take tens of thousands of saves and full orphan scans (9,387 issues in the user's library, 2026-11-10: the rebuild never finished a session).
/// This loads each kind's text once, diffs it against the existing rows in memory, writes only the differences in a few batches and prunes once.
/// Same matching rules as the resolvers: names split on <c>,</c>/<c>;</c>, trimmed, compared case-insensitively; a creator credit is keyed by
/// name and role; an entity with a custom image is never pruned; pruned characters, teams and creators drop their provider ids too.
/// </summary>
public static class EntityIndexBulk
{
    private const int Chunk = 500;

    private static readonly (string Role, Func<CreatorText, string?> Field)[] CreatorRoles =
    {
        ("Writer", t => t.Writer),
        ("Penciller", t => t.Penciller),
        ("Inker", t => t.Inker),
        ("Colorist", t => t.Colorist),
        ("Letterer", t => t.Letterer),
        ("CoverArtist", t => t.CoverArtist),
        ("Editor", t => t.Editor),
        ("Translator", t => t.Translator),
    };

    private sealed record CreatorText(int Id, string? Writer, string? Penciller, string? Inker, string? Colorist, string? Letterer,
        string? CoverArtist, string? Editor, string? Translator);

    private readonly record struct Link(int RowId, int EntityId, int IssueId, string Role);

    /// <summary>Syncs the given issues (null = every issue). Returns the number of index rows added or removed.</summary>
    public static int Sync(PaperbunkrDbContext context, IReadOnlyCollection<int>? issueIds, CancellationToken cancellationToken = default)
    {
        if (issueIds is { Count: 0 })
        {
            return 0;
        }

        bool autoDetect = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            int changed = 0;
            changed += SyncCharacters(context, issueIds, cancellationToken);
            changed += SyncTeams(context, issueIds, cancellationToken);
            changed += SyncLocations(context, issueIds, cancellationToken);
            changed += SyncCreators(context, issueIds, cancellationToken);
            return changed;
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    // ---- per kind: load text and existing links, then hand to the shared diff ----

    private static int SyncCharacters(PaperbunkrDbContext context, IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var desired = new List<(int IssueId, string Name, string Role)>();
        var existing = new List<Link>();
        foreach (var chunk in Chunks(context, ids))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var row in context.Issues.AsNoTracking().Where(i => chunk.Contains(i.Id)).Select(i => new { i.Id, i.Characters }).ToList())
                desired.AddRange(CharacterResolver.ParseNames(row.Characters).Select(n => (row.Id, n, "")));
            existing.AddRange(context.CharacterAppearances.AsNoTracking().Where(a => chunk.Contains(a.IssueId))
                .Select(a => new Link(a.Id, a.CharacterId, a.IssueId, "")).ToList());
        }

        var names = context.Characters.AsNoTracking().Select(c => new { c.Id, c.Name }).ToList().Select(c => (c.Id, c.Name)).ToList();
        return Apply(context, desired, existing, names, ct,
            name => new Character { Name = name }, e => ((Character)e).Id,
            (entityId, issueId, _) => new CharacterAppearance { CharacterId = entityId, IssueId = issueId },
            rowIds => context.CharacterAppearances.Where(a => rowIds.Contains(a.Id)).ExecuteDelete(),
            () =>
            {
                var orphanIds = context.Characters.Where(c => !c.Appearances.Any() && c.CustomImagePath == null).Select(c => c.Id).ToList();
                DeleteExternalIds(context, ComicMetadataEntityKind.Character, orphanIds);
                foreach (var chunk in orphanIds.Chunk(Chunk)) context.Characters.Where(c => chunk.Contains(c.Id)).ExecuteDelete();
                return orphanIds.Count;
            }, alwaysPrune: ids is null);
    }

    private static int SyncTeams(PaperbunkrDbContext context, IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var desired = new List<(int IssueId, string Name, string Role)>();
        var existing = new List<Link>();
        foreach (var chunk in Chunks(context, ids))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var row in context.Issues.AsNoTracking().Where(i => chunk.Contains(i.Id)).Select(i => new { i.Id, i.Teams }).ToList())
                desired.AddRange(TeamResolver.ParseNames(row.Teams).Select(n => (row.Id, n, "")));
            existing.AddRange(context.TeamAppearances.AsNoTracking().Where(a => chunk.Contains(a.IssueId))
                .Select(a => new Link(a.Id, a.TeamId, a.IssueId, "")).ToList());
        }

        var names = context.Teams.AsNoTracking().Select(t => new { t.Id, t.Name }).ToList().Select(t => (t.Id, t.Name)).ToList();
        return Apply(context, desired, existing, names, ct,
            name => new Team { Name = name }, e => ((Team)e).Id,
            (entityId, issueId, _) => new TeamAppearance { TeamId = entityId, IssueId = issueId },
            rowIds => context.TeamAppearances.Where(a => rowIds.Contains(a.Id)).ExecuteDelete(),
            () =>
            {
                var orphanIds = context.Teams.Where(t => !t.Appearances.Any() && t.CustomImagePath == null).Select(t => t.Id).ToList();
                DeleteExternalIds(context, ComicMetadataEntityKind.Team, orphanIds);
                foreach (var chunk in orphanIds.Chunk(Chunk)) context.Teams.Where(t => chunk.Contains(t.Id)).ExecuteDelete();
                return orphanIds.Count;
            }, alwaysPrune: ids is null);
    }

    private static int SyncLocations(PaperbunkrDbContext context, IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var desired = new List<(int IssueId, string Name, string Role)>();
        var existing = new List<Link>();
        foreach (var chunk in Chunks(context, ids))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var row in context.Issues.AsNoTracking().Where(i => chunk.Contains(i.Id)).Select(i => new { i.Id, i.Locations }).ToList())
                desired.AddRange(LocationResolver.ParseNames(row.Locations).Select(n => (row.Id, n, "")));
            existing.AddRange(context.LocationAppearances.AsNoTracking().Where(a => chunk.Contains(a.IssueId))
                .Select(a => new Link(a.Id, a.LocationId, a.IssueId, "")).ToList());
        }

        var names = context.Locations.AsNoTracking().Select(l => new { l.Id, l.Name }).ToList().Select(l => (l.Id, l.Name)).ToList();
        return Apply(context, desired, existing, names, ct,
            name => new Location { Name = name }, e => ((Location)e).Id,
            (entityId, issueId, _) => new LocationAppearance { LocationId = entityId, IssueId = issueId },
            rowIds => context.LocationAppearances.Where(a => rowIds.Contains(a.Id)).ExecuteDelete(),
            () =>
            {
                var orphanIds = context.Locations.Where(l => !l.Appearances.Any() && l.CustomImagePath == null).Select(l => l.Id).ToList();
                DeleteExternalIds(context, ComicMetadataEntityKind.Location, orphanIds);
                foreach (var chunk in orphanIds.Chunk(Chunk)) context.Locations.Where(l => chunk.Contains(l.Id)).ExecuteDelete();
                return orphanIds.Count;
            }, alwaysPrune: ids is null);
    }

    private static int SyncCreators(PaperbunkrDbContext context, IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var desired = new List<(int IssueId, string Name, string Role)>();
        var existing = new List<Link>();
        foreach (var chunk in Chunks(context, ids))
        {
            ct.ThrowIfCancellationRequested();
            var rows = context.Issues.AsNoTracking().Where(i => chunk.Contains(i.Id))
                .Select(i => new CreatorText(i.Id, i.Writer, i.Penciller, i.Inker, i.Colorist, i.Letterer, i.CoverArtist, i.Editor, i.Translator))
                .ToList();
            foreach (var row in rows)
            {
                foreach (var (role, field) in CreatorRoles)
                    desired.AddRange(CreatorResolver.ParseNames(field(row)).Select(n => (row.Id, n, role)));
            }

            existing.AddRange(context.CreatorCredits.AsNoTracking().Where(c => chunk.Contains(c.IssueId))
                .Select(c => new Link(c.Id, c.CreatorId, c.IssueId, c.Role)).ToList());
        }

        var names = context.Creators.AsNoTracking().Select(c => new { c.Id, c.Name }).ToList().Select(c => (c.Id, c.Name)).ToList();
        return Apply(context, desired, existing, names, ct,
            name => new Creator { Name = name }, e => ((Creator)e).Id,
            (entityId, issueId, role) => new CreatorCredit { CreatorId = entityId, IssueId = issueId, Role = role },
            rowIds => context.CreatorCredits.Where(c => rowIds.Contains(c.Id)).ExecuteDelete(),
            () =>
            {
                var orphanIds = context.Creators.Where(c => !c.Credits.Any() && c.CustomImagePath == null).Select(c => c.Id).ToList();
                DeleteExternalIds(context, ComicMetadataEntityKind.Creator, orphanIds);
                foreach (var chunk in orphanIds.Chunk(Chunk)) context.Creators.Where(c => chunk.Contains(c.Id)).ExecuteDelete();
                return orphanIds.Count;
            }, alwaysPrune: ids is null);
    }

    // ---- the shared diff ----

    private static int Apply(
        PaperbunkrDbContext context,
        List<(int IssueId, string Name, string Role)> desired,
        List<Link> existing,
        List<(int Id, string Name)> entities,
        CancellationToken ct,
        Func<string, object> newEntity,
        Func<object, int> entityId,
        Func<int, int, string, object> newLink,
        Func<int[], int> deleteLinks,
        Func<int> prune,
        bool alwaysPrune)
    {
        var idByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nameById = new Dictionary<int, string>();
        foreach (var (id, name) in entities)
        {
            nameById[id] = name;
            idByName.TryAdd(name.Trim(), id);
        }

        var comparer = StringComparer.OrdinalIgnoreCase;
        var wanted = new HashSet<(int, string, string)>(desired.Select(d => (d.IssueId, d.Name.ToUpperInvariant(), d.Role)));
        var have = new HashSet<(int, string, string)>();
        var stale = new List<int>();
        foreach (var link in existing)
        {
            string key = nameById.TryGetValue(link.EntityId, out var n) ? n.Trim().ToUpperInvariant() : string.Empty;
            var triple = (link.IssueId, key, link.Role);
            if (key.Length == 0 || !wanted.Contains(triple) || !have.Add(triple))
            {
                stale.Add(link.RowId);
            }
        }

        int changed = 0;
        foreach (var chunk in stale.Chunk(Chunk))
        {
            ct.ThrowIfCancellationRequested();
            changed += deleteLinks(chunk);
        }

        var toAdd = desired
            .Where(d => !have.Contains((d.IssueId, d.Name.ToUpperInvariant(), d.Role)))
            .GroupBy(d => (d.IssueId, d.Name.ToUpperInvariant(), d.Role))
            .Select(g => g.First())
            .ToList();

        if (toAdd.Count > 0)
        {
            var created = new Dictionary<string, object>(comparer);
            foreach (var name in toAdd.Select(d => d.Name).Where(n => !idByName.ContainsKey(n)).Distinct(comparer))
            {
                var entity = newEntity(name);
                created[name] = entity;
                context.Add(entity);
            }

            if (created.Count > 0)
            {
                context.ChangeTracker.DetectChanges();
                context.SaveChanges();
                foreach (var (name, entity) in created)
                {
                    idByName[name] = entityId(entity);
                    context.Entry(entity).State = EntityState.Detached;
                }
            }

            foreach (var batch in toAdd.Chunk(2000))
            {
                ct.ThrowIfCancellationRequested();
                var links = batch.Select(d => newLink(idByName[d.Name], d.IssueId, d.Role)).ToList();
                foreach (var link in links) context.Add(link);
                context.ChangeTracker.DetectChanges();
                context.SaveChanges();
                foreach (var link in links) context.Entry(link).State = EntityState.Detached;
                changed += links.Count;
            }
        }

        if (stale.Count > 0 || alwaysPrune)
        {
            changed += prune();
        }

        return changed;
    }

    private static void DeleteExternalIds(PaperbunkrDbContext context, ComicMetadataEntityKind kind, List<int> entityIds)
    {
        foreach (var chunk in entityIds.Chunk(Chunk))
        {
            context.ComicMetadataExternalIds.Where(e => e.EntityKind == kind && chunk.Contains(e.EntityId)).ExecuteDelete();
        }
    }

    /// <summary>The issue ids in chunks small enough for SQLite's parameter limit; the whole library when <paramref name="ids"/> is null.</summary>
    private static IEnumerable<int[]> Chunks(PaperbunkrDbContext context, IReadOnlyCollection<int>? ids)
    {
        var all = ids?.Distinct().ToArray() ?? context.Issues.AsNoTracking().Select(i => i.Id).ToArray();
        return all.Chunk(Chunk);
    }
}
