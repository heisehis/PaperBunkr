using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>A person credited on an issue (the first credited one in that role).</summary>
public sealed record MapCreator(int Id, string Name);

/// <summary>What the map knows about one issue's characters, teams and creators (M5).</summary>
public sealed record MapIssueStructure(IReadOnlyList<int> Characters, IReadOnlyList<int> Teams, MapCreator? Writer, MapCreator? Artist,
    string? FaceName, string? FacePath);

/// <summary>A character or team that appears in the map's issues, with how many.</summary>
public sealed record MapEntity(ComicMetadataEntityKind Kind, int Id, string Name, int Count)
{
    public string Label => $"{Name} · {Count}";
}

public enum MapBracketKind { Run, Arc }

/// <summary>
/// A creator run or a story arc inside one series (M5 §3-§4), as the issue ids it covers. The view turns these into column stretches
/// on the series' lane.
/// </summary>
public sealed record MapBracket(int SeriesId, MapBracketKind Kind, string Role, int? CreatorId, string Label, string Tooltip, IReadOnlyList<int> IssueIds);

/// <summary>The map's structure data (docs/superpowers/specs/2026-11-11-continuity-map-structure-links-design.md).</summary>
public sealed record EventMapStructure(IReadOnlyDictionary<int, MapIssueStructure> Issues, IReadOnlyList<MapEntity> Entities,
    IReadOnlyList<MapBracket> Brackets, bool IndexReady)
{
    public static EventMapStructure Empty { get; } = new(new Dictionary<int, MapIssueStructure>(), Array.Empty<MapEntity>(), Array.Empty<MapBracket>(), true);
}

/// <summary>
/// Loads <see cref="EventMapStructure"/> for the map's issues from the Series structure indexes: character and team appearances, the first
/// credited writer and artist (artist = penciller or artist, D2's combined role), faces from <c>MainCharacterOrTeam</c> (a character, else a
/// team, and only when it has a real image: custom or cached provider, never fetched here), and per-series creator runs and story arcs.
/// Read-only.
/// </summary>
public static class EventMapStructureLoader
{
    public static EventMapStructure Load(PaperbunkrDbContext context, IReadOnlyCollection<int> issueIds)
    {
        if (issueIds.Count == 0)
        {
            return EventMapStructure.Empty;
        }

        bool ready = context.AppSettings.AsNoTracking().Select(s => s.EntityIndexVersion).FirstOrDefault() >= EntityIndexSync.CurrentVersion;
        var ids = issueIds.Where(i => i > 0).Distinct().ToList();
        var characters = context.CharacterAppearances.AsNoTracking().Where(a => ids.Contains(a.IssueId))
            .Select(a => new { a.IssueId, a.CharacterId, a.Character!.Name }).ToList();
        var teams = context.TeamAppearances.AsNoTracking().Where(a => ids.Contains(a.IssueId))
            .Select(a => new { a.IssueId, a.TeamId, a.Team!.Name }).ToList();
        var credits = context.CreatorCredits.AsNoTracking()
            .Where(c => ids.Contains(c.IssueId) && (c.Role == "Writer" || c.Role == "Penciller" || c.Role == "Artist"))
            .Include(c => c.Creator)
            .ToList();
        var issues = context.Issues.AsNoTracking().Where(i => ids.Contains(i.Id)).Include(i => i.Series).Include(i => i.MetadataProposals).ToList();

        var faces = Faces(context, issues.Select(i => FaceName(i.MainCharacterOrTeam)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        var perIssue = new Dictionary<int, MapIssueStructure>();
        foreach (var issue in issues)
        {
            MapCreator? First(Func<string?, bool> role) => credits.Where(c => c.IssueId == issue.Id && role(c.Role)).OrderBy(c => c.Id)
                .Select(c => new MapCreator(c.CreatorId, c.Creator?.Name ?? string.Empty)).FirstOrDefault();
            string? face = FaceName(issue.MainCharacterOrTeam);
            perIssue[issue.Id] = new MapIssueStructure(
                characters.Where(a => a.IssueId == issue.Id).Select(a => a.CharacterId).Distinct().ToList(),
                teams.Where(a => a.IssueId == issue.Id).Select(a => a.TeamId).Distinct().ToList(),
                First(r => r == "Writer"), First(r => r is "Penciller" or "Artist"),
                face, face is not null && faces.TryGetValue(face, out var path) ? path : null);
        }

        var entities = characters.GroupBy(a => a.CharacterId)
            .Select(g => new MapEntity(ComicMetadataEntityKind.Character, g.Key, g.First().Name, g.Select(a => a.IssueId).Distinct().Count()))
            .OrderByDescending(e => e.Count).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Concat(teams.GroupBy(a => a.TeamId)
                .Select(g => new MapEntity(ComicMetadataEntityKind.Team, g.Key, g.First().Name, g.Select(a => a.IssueId).Distinct().Count()))
                .OrderByDescending(e => e.Count).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            .ToList();

        var brackets = new List<MapBracket>();
        foreach (var series in issues.GroupBy(i => i.SeriesId))
        {
            var seriesIssues = series.ToList();
            var seriesCredits = credits.Where(c => seriesIssues.Any(i => i.Id == c.IssueId)).ToList();
            foreach (var role in new[] { "Writer", "Artist" })
            {
                foreach (var run in CreatorRunDetector.Find(seriesIssues, seriesCredits, role))
                {
                    int read = run.ReadCount;
                    brackets.Add(new MapBracket(series.Key, MapBracketKind.Run, run.Role, run.CreatorId, $"{run.CreatorName} · {run.Range}",
                        $"{run.CreatorName} · {run.Role.ToLowerInvariant()} · {run.Issues.Count} issues, {read} read", run.Issues.Select(i => i.Id).ToList()));
                }
            }

            var contentType = seriesIssues[0].Series?.ContentType ?? ContentType.Unknown;
            foreach (var arc in SeriesArcDetector.Find(seriesIssues, contentType))
            {
                string source = arc.Source.Contains("title", StringComparison.OrdinalIgnoreCase) ? " · from titles" : string.Empty;
                brackets.Add(new MapBracket(series.Key, MapBracketKind.Arc, string.Empty, null, arc.Name + source,
                    $"{arc.Name} · {arc.Range} · {arc.ReadCount} of {arc.Issues.Count} read", arc.Issues.Select(i => i.Id).ToList()));
            }
        }

        return new EventMapStructure(perIssue, entities, brackets, ready);
    }

    /// <summary>The issue's main character or team: the text before the first <c>,</c> or <c>;</c>, trimmed; null when blank.</summary>
    public static string? FaceName(string? mainCharacterOrTeam)
    {
        if (string.IsNullOrWhiteSpace(mainCharacterOrTeam))
        {
            return null;
        }

        string first = mainCharacterOrTeam.Split(new[] { ',', ';' }, 2)[0].Trim();
        return first.Length == 0 ? null : first;
    }

    /// <summary>Face image paths by name: the character of that name, else the team; only custom or cached provider images (§2).</summary>
    public static Dictionary<string, string> Faces(PaperbunkrDbContext context, IReadOnlyList<string> names)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            string? path = null;
            if (EntityNameLookup.Find(context, ComicMetadataEntityKind.Character, name) is int characterId
                && context.Characters.AsNoTracking().FirstOrDefault(c => c.Id == characterId) is { } character)
            {
                path = RealImage(EntityImageResolver.Choose(character, null));
            }

            if (path is null && EntityNameLookup.Find(context, ComicMetadataEntityKind.Team, name) is int teamId
                && context.Teams.AsNoTracking().FirstOrDefault(t => t.Id == teamId) is { } team)
            {
                path = RealImage(EntityImageResolver.Choose(team, null));
            }

            if (path is not null)
            {
                result[name] = path;
            }
        }

        return result;
    }

    private static string? RealImage(EntityImageChoice choice) =>
        choice.Kind is EntityImageKind.Custom or EntityImageKind.Provider ? choice.Path : null;
}
