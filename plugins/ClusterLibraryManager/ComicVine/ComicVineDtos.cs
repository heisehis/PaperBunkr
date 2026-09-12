using System.Text.Json.Serialization;

namespace ClusterLibraryManager.ComicVine;

// -- Raw JSON envelope + resource shapes, matching ComicVine's real API field names (format=json,
// a deliberate mechanical deviation from CE's format=xml - see design doc §3). Internal: callers get
// the public DTOs below, mapped from these inside ComicVineService.

internal sealed class ComicVineEnvelope<T>
{
    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("results")]
    public T? Results { get; set; }
}

internal sealed class ComicVineImageRaw
{
    // CE's own priority order for picking one image URL out of several sizes (cvdb.py
    // __parse_image_url, verified): small -> medium -> large -> super -> thumb.
    [JsonPropertyName("small_url")]
    public string? SmallUrl { get; set; }

    [JsonPropertyName("medium_url")]
    public string? MediumUrl { get; set; }

    [JsonPropertyName("large_url")]
    public string? LargeUrl { get; set; }

    [JsonPropertyName("super_url")]
    public string? SuperUrl { get; set; }

    [JsonPropertyName("thumb_url")]
    public string? ThumbUrl { get; set; }

    public string? BestUrl() => SmallUrl ?? MediumUrl ?? LargeUrl ?? SuperUrl ?? ThumbUrl;
}

internal sealed class ComicVinePublisherRaw
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class ComicVineVolumeRaw
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("start_year")]
    public string? StartYear { get; set; }

    [JsonPropertyName("publisher")]
    public ComicVinePublisherRaw? Publisher { get; set; }

    [JsonPropertyName("count_of_issues")]
    public int? CountOfIssues { get; set; }

    [JsonPropertyName("image")]
    public ComicVineImageRaw? Image { get; set; }
}

internal sealed class ComicVineIssueSummaryRaw
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("issue_number")]
    public string? IssueNumber { get; set; }

    [JsonPropertyName("image")]
    public ComicVineImageRaw? Image { get; set; }
}

internal sealed class ComicVineNamedRefRaw
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class ComicVinePersonCreditRaw
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }
}

internal sealed class ComicVineIssueDetailsRaw
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("issue_number")]
    public string? IssueNumber { get; set; }

    [JsonPropertyName("site_detail_url")]
    public string? SiteDetailUrl { get; set; }

    [JsonPropertyName("cover_date")]
    public string? CoverDate { get; set; }

    [JsonPropertyName("store_date")]
    public string? StoreDate { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("volume")]
    public ComicVineNamedRefRaw? Volume { get; set; }

    [JsonPropertyName("story_arc_credits")]
    public List<ComicVineNamedRefRaw>? StoryArcCredits { get; set; }

    [JsonPropertyName("character_credits")]
    public List<ComicVineNamedRefRaw>? CharacterCredits { get; set; }

    [JsonPropertyName("team_credits")]
    public List<ComicVineNamedRefRaw>? TeamCredits { get; set; }

    [JsonPropertyName("location_credits")]
    public List<ComicVineNamedRefRaw>? LocationCredits { get; set; }

    [JsonPropertyName("person_credits")]
    public List<ComicVinePersonCreditRaw>? PersonCredits { get; set; }
}

// -- Public DTOs (design doc §3) - what ComicVineService actually returns to callers.

public sealed record ComicVineVolumeSearchResult(
    int Id,
    string Name,
    string? StartYear,
    string? Publisher,
    int? CountOfIssues,
    string? ImageUrl);

public sealed record ComicVineVolumeDetails(
    int Id,
    string Name,
    string? StartYear,
    string? Publisher,
    int? CountOfIssues,
    string? ImageUrl);

public sealed record ComicVineIssueSummary(
    int Id,
    string? IssueNumber,
    string? Name,
    string? ImageUrl);

/// <summary>Publication-date fields split into year/month/day, matching CE's own
/// `cover_date`/`store_date` -> pub/release y-m-d split (cvdb.py `__issue_parse_simple_stuff`,
/// verified) rather than a single .NET DateTime that can't represent CE's "sometimes only a year is
/// known" partial dates.</summary>
public sealed record ComicVineDatePart(int? Year, int? Month, int? Day);

public sealed record ComicVineIssueDetails(
    int Id,
    int VolumeId,
    string? VolumeName,
    string? IssueNumber,
    string? Title,
    string? SiteDetailUrl,
    ComicVineDatePart PublishedDate,
    ComicVineDatePart ReleasedDate,
    string? Summary,
    IReadOnlyList<string> StoryArcs,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Teams,
    IReadOnlyList<string> Locations,
    IReadOnlyList<ComicVineCredit> Credits);

/// <summary>One person credit, role already mapped through CE's person-role dictionary (cvdb.py
/// person_credits handling, verified) onto Paperbunkr's own credit-field names
/// (Writer/Penciller/Inker/Colorist/Letterer/CoverArtist/Editor) - <see cref="Field"/> is null for a
/// CV role with no Paperbunkr equivalent, which the caller should simply ignore.</summary>
public sealed record ComicVineCredit(string Name, string? Field);
