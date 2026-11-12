using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.EventEnrichment;

namespace Paperbunkr.Data.Metadata;

/// <summary>Looks a manga creator up among the staff of one series on AniList or MangaDex.</summary>
public interface IMangaStaffSource
{
    /// <summary>The profile of the staff member of that series whose name matches <paramref name="personName"/>, or null when none matches.
    /// Throws on network and API errors.</summary>
    Task<EntityProfile?> FindAsync(ExternalMetadataProvider provider, string seriesExternalId, string personName, CancellationToken cancellationToken);
}

/// <summary>
/// Manga creator profiles (2026-11-11 follow-up to docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3): a creator who
/// has no Metron or ComicVine id is matched against the staff of the series they are credited on that are already linked to AniList or MangaDex.
/// Never an open search by name, so two people who share a name cannot be confused. AniList staff carry a picture, a bio and native-script and
/// alternative names (the native name becomes an alias); MangaDex authors and artists carry a picture when they have one, and a bio.
/// </summary>
public sealed class MangaStaffProfiles : IMangaStaffSource
{
    private const string AniListEndpoint = "https://graphql.anilist.co";
    private const string MangaDexBaseUrl = "https://api.mangadex.org/";

    private const string StaffQuery = """
        query ($id: Int) {
          Media(id: $id) {
            staff(perPage: 50) { nodes { id name { full native alternative } image { large } description(asHtml: false) } }
          }
        }
        """;

    private readonly HttpClient _aniList;
    private readonly HttpClient _mangaDex;

    public MangaStaffProfiles(HttpClient? aniList = null, HttpClient? mangaDex = null)
    {
        _aniList = aniList ?? AniListHttpClient.Shared;
        _mangaDex = mangaDex ?? MangaDexHttpClient.Shared;
    }

    public async Task<EntityProfile?> FindAsync(ExternalMetadataProvider provider, string seriesExternalId, string personName, CancellationToken cancellationToken)
    {
        switch (provider)
        {
            case ExternalMetadataProvider.AniList when int.TryParse(seriesExternalId, out int mediaId):
            {
                using var response = await _aniList.PostAsJsonAsync(AniListEndpoint, new { query = StaffQuery, variables = new { id = mediaId } }, cancellationToken)
                    .ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                return Match(ParseAniList(root), personName);
            }

            case ExternalMetadataProvider.MangaDex when seriesExternalId.Length > 0:
            {
                using var response = await _mangaDex.GetAsync($"{MangaDexBaseUrl}manga/{Uri.EscapeDataString(seriesExternalId)}?includes[]=author&includes[]=artist", cancellationToken)
                    .ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                return Match(ParseMangaDex(root), personName);
            }

            default:
                return null;
        }
    }

    /// <summary>One staff member as a provider lists them: every name they go by, and their profile.</summary>
    public sealed record StaffMember(IReadOnlyList<string> Names, EntityProfile Profile);

    /// <summary>AniList <c>Media.staff.nodes</c>. The first name is the full name; native and alternative names follow and become aliases.</summary>
    public static IReadOnlyList<StaffMember> ParseAniList(JsonNode? root)
    {
        var nodes = root?["data"]?["Media"]?["staff"]?["nodes"] as JsonArray;
        var result = new List<StaffMember>();
        foreach (var node in nodes ?? new JsonArray())
        {
            if (node is not JsonObject o || Text(o["name"]?["full"]) is not { } full) continue;
            var others = new List<string>();
            if (Text(o["name"]?["native"]) is { } native) others.Add(native);
            if (o["name"]?["alternative"] is JsonArray alternatives) others.AddRange(alternatives.Select(Text).Where(a => a is not null).Select(a => a!));
            string? image = Text(o["image"]?["large"]);
            if (image is not null && image.Contains("/default.jpg", StringComparison.OrdinalIgnoreCase)) image = null; // AniList's placeholder
            string? bio = EventDescriptionText.Overview(Text(o["description"]));
            var aliases = others.Where(a => !SameName(a, full)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.Add(new StaffMember(new[] { full }.Concat(others).ToList(), new EntityProfile(image, bio, aliases)));
        }

        return result;
    }

    /// <summary>MangaDex <c>data.relationships</c> of type author/artist (requested with <c>includes[]</c>), deduplicated by id.</summary>
    public static IReadOnlyList<StaffMember> ParseMangaDex(JsonNode? root)
    {
        var relationships = root?["data"]?["relationships"] as JsonArray;
        var result = new List<StaffMember>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in relationships ?? new JsonArray())
        {
            if (node is not JsonObject o || Text(o["type"]) is not ("author" or "artist")) continue;
            if (o["attributes"] is not JsonObject attributes || Text(attributes["name"]) is not { } name) continue;
            if (Text(o["id"]) is { } id && !seen.Add(id)) continue;
            string? bio = null;
            if (attributes["biography"] is JsonObject biographies)
            {
                bio = Text(biographies["en"]) ?? biographies.Select(b => Text(b.Value)).FirstOrDefault(b => b is not null);
            }

            result.Add(new StaffMember(new[] { name }, new EntityProfile(Text(attributes["imageUrl"]), EventDescriptionText.Overview(bio), Array.Empty<string>())));
        }

        return result;
    }

    /// <summary>The staff member any of whose names matches <paramref name="personName"/> (case, spacing and punctuation ignored; a two-part name
    /// also matches in reversed order, "Oda Eiichiro" / "Eiichiro Oda"). Null when none, or when two different people match.</summary>
    public static EntityProfile? Match(IReadOnlyList<StaffMember> staff, string personName)
    {
        var hits = staff.Where(s => s.Names.Any(n => SameName(n, personName))).ToList();
        return hits.Count == 1 ? hits[0].Profile : null;
    }

    public static bool SameName(string a, string b)
    {
        string na = Normalize(a), nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        if (na == nb) return true;
        var pa = na.Split(' '); var pb = nb.Split(' ');
        return pa.Length == 2 && pb.Length == 2 && pa[0] == pb[1] && pa[1] == pb[0];
    }

    private static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch is '-' or '.' or '_') sb.Append(' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var s)) return null;
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }
}
