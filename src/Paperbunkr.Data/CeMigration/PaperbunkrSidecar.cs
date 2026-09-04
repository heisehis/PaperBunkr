using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.CeMigration;

/// <summary>
/// The <c>paperbunkr.json</c> archive sidecar (docs/superpowers/specs/2026-09-03-file-metadata-
/// write-back-design.md) - carries the fields a metadata editor can set that have <b>no
/// <see cref="cYo.Projects.ComicRack.Engine.ComicInfo"/> standard home</b>, so a Paperbunkr library
/// round-trips through the files themselves without needing CE's proprietary <c>ComicBook.xml</c>.
/// Written alongside <c>ComicInfo.xml</c> in the same archive update when
/// <see cref="AppSettings.WriteNativeSidecar"/> is on.
///
/// <see cref="Schema"/> is bumped whenever the shape changes so a future reader can migrate an older
/// sidecar. v1 deliberately excludes per-page type/rotation (<c>IssuePage</c>) - a follow-up - and
/// <see cref="MetadataProposal"/> values (an internal review concept, not user-authored; their
/// accepted values already flow into <c>ComicInfo.xml</c> via <see cref="IssueToComicInfoMapper"/>).
/// </summary>
public sealed record PaperbunkrSidecar
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int Schema { get; init; } = 1;

    /// <summary>Personal rating (<see cref="Issue.Rating"/>). ComicInfo only carries CommunityRating.</summary>
    public float? Rating { get; init; }

    public bool IsFinalIssue { get; init; }

    public string? StoryArcNumber { get; init; }

    // Physical-collection fields (Issue.Book*) - CE's own catalog-only fields, no ComicInfo home.
    public string? BookAge { get; init; }
    public string? BookCollectionStatus { get; init; }
    public string? BookCondition { get; init; }
    public string? BookLocation { get; init; }
    public string? BookNotes { get; init; }
    public string? BookOwner { get; init; }
    public float? BookPrice { get; init; }
    public string? BookStore { get; init; }

    /// <summary>
    /// Structured tags - <c>ComicInfo.xml</c> only gets the flat <c>Genre</c>/<c>Tags</c> CSV, so
    /// the per-tag <see cref="IssueTag.Category"/> / <see cref="IssueTag.Weight"/> would be lost
    /// without this.
    /// </summary>
    public List<SidecarTag> Tags { get; init; } = new();

    public sealed record SidecarTag(string Field, string Value, string? Category, string Weight);

    public static PaperbunkrSidecar FromIssue(Issue issue) => new()
    {
        Rating = issue.Rating,
        IsFinalIssue = issue.IsFinalIssue,
        StoryArcNumber = NullIfEmpty(issue.StoryArcNumber),
        BookAge = NullIfEmpty(issue.BookAge),
        BookCollectionStatus = NullIfEmpty(issue.BookCollectionStatus),
        BookCondition = NullIfEmpty(issue.BookCondition),
        BookLocation = NullIfEmpty(issue.BookLocation),
        BookNotes = NullIfEmpty(issue.BookNotes),
        BookOwner = NullIfEmpty(issue.BookOwner),
        BookPrice = issue.BookPrice,
        BookStore = NullIfEmpty(issue.BookStore),
        Tags = issue.Tags
            .OrderBy(t => t.Field)
            .ThenBy(t => t.Value, System.StringComparer.OrdinalIgnoreCase)
            .Select(t => new SidecarTag(t.Field.ToString(), t.Value, NullIfEmpty(t.Category), t.Weight.ToString()))
            .ToList(),
    };

    public byte[] ToJsonBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Canonical JSON string - used by <see cref="MetadataFileFieldSnapshot"/> for its change check.</summary>
    public string ToCanonicalString() => JsonSerializer.Serialize(this, JsonOptions);

    public static PaperbunkrSidecar? TryParse(byte[] json)
    {
        try
        {
            return JsonSerializer.Deserialize<PaperbunkrSidecar>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
