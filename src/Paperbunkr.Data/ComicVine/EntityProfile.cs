using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.EventEnrichment;

namespace Paperbunkr.Data.ComicVine;

/// <summary>
/// What a provider says about a character, team or creator (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3):
/// an image link, a short description and other names. Any of them can be missing.
/// </summary>
public sealed record EntityProfile(string? ImageUrl, string? Description, IReadOnlyList<string> Aliases);

/// <summary>A provider that can look up one character, team or creator by its own id.</summary>
public interface IEntityProfileSource
{
    /// <summary>The profile, or null when the provider has no such record. Throws on network and API errors.</summary>
    Task<EntityProfile?> GetEntityProfileAsync(ComicMetadataEntityKind kind, string externalId, CancellationToken cancellationToken);
}

/// <summary>Turns Metron and ComicVine records into <see cref="EntityProfile"/>. Pure, so it is tested on canned JSON.</summary>
public static class EntityProfileParser
{
    /// <summary>The Metron path segment for a kind, or null for kinds Metron has no record for.</summary>
    public static string? MetronPath(ComicMetadataEntityKind kind) => kind switch
    {
        ComicMetadataEntityKind.Character => "character",
        ComicMetadataEntityKind.Team => "team",
        ComicMetadataEntityKind.Creator => "creator",
        _ => null,
    };

    /// <summary>The ComicVine resource and type prefix for a kind ("character", "4005"), or null.</summary>
    public static (string Resource, string Prefix)? ComicVineResource(ComicMetadataEntityKind kind) => kind switch
    {
        ComicMetadataEntityKind.Character => ("character", "4005"),
        ComicMetadataEntityKind.Team => ("team", "4060"),
        ComicMetadataEntityKind.Creator => ("person", "4040"),
        ComicMetadataEntityKind.Location => ("location", "4020"),
        _ => null,
    };

    /// <summary>Metron: <c>image</c> is a URL string, <c>desc</c> plain text, <c>alias</c> a list of strings.</summary>
    public static EntityProfile? FromMetron(JsonNode? root)
    {
        if (root is not JsonObject o || o["name"] is null)
        {
            return null;
        }

        string? image = Text(o["image"]);
        string? description = EventDescriptionText.Overview(Text(o["desc"]));
        var aliases = o["alias"] is JsonArray list
            ? list.Select(Text).Where(a => a is not null).Select(a => a!).ToList()
            : Split(Text(o["alias"]));
        return new EntityProfile(image, description, Clean(aliases));
    }

    /// <summary>ComicVine: the record sits under <c>results</c>; <c>image</c> is an object of sized URLs, <c>deck</c> a one-line summary, <c>aliases</c> newline-separated text.</summary>
    public static EntityProfile? FromComicVine(JsonNode? root)
    {
        if (root?["results"] is not JsonObject o)
        {
            return null;
        }

        string? image = o["image"] is JsonObject img
            ? Text(img["medium_url"]) ?? Text(img["super_url"]) ?? Text(img["screen_large_url"]) ?? Text(img["original_url"])
            : null;
        if (image is not null && image.Contains("/img/blank.png", StringComparison.OrdinalIgnoreCase))
        {
            image = null; // ComicVine's placeholder for "no image"
        }

        string? description = EventDescriptionText.Overview(Text(o["deck"]));
        return new EntityProfile(image, description, Clean(Split(Text(o["aliases"]))));
    }

    private static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var s))
        {
            return null;
        }

        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    private static List<string> Split(string? text) =>
        text is null ? new List<string>() : text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static IReadOnlyList<string> Clean(IEnumerable<string> aliases) =>
        aliases.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
