using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.CeMigration;

/// <summary>
/// The provider ids <see cref="IssueToMetronInfoMapper"/> writes into a <c>MetronInfo.xml</c>
/// (docs/superpowers/specs/2026-10-05-metroninfo-write-back-design.md, D4/D5). Built from the database
/// by the write-back service so the mapper itself stays a pure function of its arguments.
///
/// <see cref="IssueIds"/> feeds <c>&lt;IDS&gt;</c>, one entry per provider we hold an id for. Everything
/// else is an <c>id=</c> attribute, which the schema defines as "the identification number from the
/// source of information" - so those are already narrowed to <see cref="Primary"/>'s provider, and
/// are all empty when there is no primary. Ids from two providers never share a document.
/// </summary>
public sealed record MetronIdContext
{
    private static readonly IReadOnlyDictionary<string, string> NoNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static MetronIdContext Empty { get; } = new();

    /// <summary>The issue's <see cref="Issue.MetadataSource"/>, but only when its id is in <see cref="IssueIds"/>.</summary>
    public ComicProvider? Primary { get; init; }

    public IReadOnlyDictionary<ComicProvider, string> IssueIds { get; init; } = new Dictionary<ComicProvider, string>();

    public string? SeriesId { get; init; }

    public string? PublisherId { get; init; }

    /// <summary>Name (case-insensitive) to the primary provider's id.</summary>
    public IReadOnlyDictionary<string, string> CreatorIds { get; init; } = NoNames;

    public IReadOnlyDictionary<string, string> CharacterIds { get; init; } = NoNames;

    public IReadOnlyDictionary<string, string> TeamIds { get; init; } = NoNames;

    public IReadOnlyDictionary<string, string> LocationIds { get; init; } = NoNames;

    public static MetronIdContext FromDatabase(PaperbunkrDbContext context, Issue issue)
    {
        var issueIds = context.ComicMetadataExternalIds
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Issue && e.EntityId == issue.Id)
            .AsEnumerable()
            .GroupBy(e => e.Provider)
            .ToDictionary(g => g.Key, g => g.First().ExternalId);

        // A source we were scraped from but hold no id for can't be named as primary: the ID element
        // is where the primary flag lives.
        if (issue.MetadataSource is not ComicProvider primary || !issueIds.ContainsKey(primary))
        {
            return new MetronIdContext { IssueIds = issueIds };
        }

        string? IdOf(ComicMetadataEntityKind kind, int? entityId) => entityId is int id
            ? context.ComicMetadataExternalIds
                .Where(e => e.EntityKind == kind && e.EntityId == id && e.Provider == primary)
                .Select(e => e.ExternalId)
                .FirstOrDefault()
            : null;

        IReadOnlyDictionary<string, string> IdsByName(ComicMetadataEntityKind kind, IEnumerable<string> names, Func<List<string>, List<KeyValuePair<int, string>>> load)
        {
            var wanted = names.Select(n => n.ToLowerInvariant()).Distinct().ToList();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0)
            {
                return result;
            }

            var rows = load(wanted);
            var entityIds = rows.Select(r => r.Key).ToList();
            var externalIds = context.ComicMetadataExternalIds
                .Where(e => e.EntityKind == kind && e.Provider == primary && entityIds.Contains(e.EntityId))
                .AsEnumerable()
                .GroupBy(e => e.EntityId)
                .ToDictionary(g => g.Key, g => g.First().ExternalId);

            foreach (var row in rows)
            {
                if (externalIds.TryGetValue(row.Key, out string? externalId))
                {
                    result[row.Value] = externalId;
                }
            }

            return result;
        }

        var creatorNames = new[] { issue.Writer, issue.Penciller, issue.Inker, issue.Colorist, issue.Letterer, issue.CoverArtist, issue.Editor, issue.Translator }
            .SelectMany(CharacterResolver.ParseNames);

        return new MetronIdContext
        {
            Primary = primary,
            IssueIds = issueIds,
            SeriesId = IdOf(ComicMetadataEntityKind.Series, issue.SeriesId),
            PublisherId = IdOf(ComicMetadataEntityKind.Publisher, issue.PublisherEntityId),
            CreatorIds = IdsByName(ComicMetadataEntityKind.Creator, creatorNames, wanted => context.Creators.Where(c => wanted.Contains(c.Name.ToLower())).Select(c => new { c.Id, c.Name }).AsEnumerable().Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToList()),
            CharacterIds = IdsByName(ComicMetadataEntityKind.Character, CharacterResolver.ParseNames(issue.Characters), wanted => context.Characters.Where(c => wanted.Contains(c.Name.ToLower())).Select(c => new { c.Id, c.Name }).AsEnumerable().Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToList()),
            TeamIds = IdsByName(ComicMetadataEntityKind.Team, CharacterResolver.ParseNames(issue.Teams), wanted => context.Teams.Where(c => wanted.Contains(c.Name.ToLower())).Select(c => new { c.Id, c.Name }).AsEnumerable().Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToList()),
            LocationIds = IdsByName(ComicMetadataEntityKind.Location, CharacterResolver.ParseNames(issue.Locations), wanted => context.Locations.Where(c => wanted.Contains(c.Name.ToLower())).Select(c => new { c.Id, c.Name }).AsEnumerable().Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToList()),
        };
    }
}
