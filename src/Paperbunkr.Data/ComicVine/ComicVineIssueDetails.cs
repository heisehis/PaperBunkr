namespace Paperbunkr.Data.ComicVine;

/// <summary>A ComicVine date split into parts: any of the three can be absent (ComicVine often has only a year, or year and month).</summary>
public sealed record ComicVineDatePart(int? Year, int? Month, int? Day);

/// <summary>
/// One person credit with the ComicVine role already mapped onto Paperbunkr's credit field name;
/// <see cref="Field"/> is null for a role with no equivalent. <see cref="CreatorExternalId"/> and
/// <see cref="RoleExternalId"/> are two genuinely different ids from two different providers, never
/// both populated by the same source: ComicVine's <c>person_credits[].id</c> identifies the creator
/// themself, while Metron's <c>role[].id</c> identifies the *role* on that credit - Metron's own
/// credit objects carry no separate creator id at all (confirmed during design; only the role does).
/// </summary>
public sealed record ComicVineCredit(string Name, string? Field, int? CreatorExternalId = null, int? RoleExternalId = null);

/// <summary>A named thing (arc, character, team, universe, ...) paired with the provider's id for it, when the provider gave one. <see cref="ExternalId"/> is null for a provider that only ever returns bare names.</summary>
public sealed record ComicVineIdName(int? ExternalId, string Name);

/// <summary>One alternate cover Metron lists for an issue (its <c>variants</c> field). No id exists on this object at all (confirmed against Metron's own schema) - <see cref="Name"/>+<see cref="ImageUrl"/> is the only identity a variant has.</summary>
public sealed record ComicVineVariantCover(string? Name, string ImageUrl);

/// <summary>
/// Everything ComicVine or Metron knows about one issue that Paperbunkr can store (the per-issue half
/// of the scraper's field matrix). <see cref="Genres"/> rides along on the nested series object both
/// providers' issue-detail responses carry - a series-level fact arriving with the issue fetch, not
/// separately requested. There is no series-level Status here (confirmed absent from Metron's nested
/// issue.series object, unlike Genres which is confirmed present there) - populating <c>Series.Status</c>
/// from Metron would need a separate call to the standalone series endpoint, not attempted in this pass
/// (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md).
/// </summary>
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
    IReadOnlyList<ComicVineIdName> StoryArcs,
    IReadOnlyList<ComicVineIdName> Characters,
    IReadOnlyList<ComicVineIdName> Teams,
    IReadOnlyList<ComicVineIdName> Locations,
    IReadOnlyList<ComicVineCredit> Credits,
    IReadOnlyList<ComicVineIdName>? Universes = null,
    string? AgeRating = null,
    string? Isbn = null,
    string? Upc = null,
    IReadOnlyList<string>? Genres = null,
    double? AverageRating = null,
    int? RatingCount = null,
    string? Imprint = null,
    IReadOnlyList<ComicVineVariantCover>? Variants = null,
    int? GcdId = null)
{
    public IReadOnlyList<ComicVineIdName> Universes { get; init; } = Universes ?? Array.Empty<ComicVineIdName>();

    public IReadOnlyList<string> Genres { get; init; } = Genres ?? Array.Empty<string>();

    public IReadOnlyList<ComicVineVariantCover> Variants { get; init; } = Variants ?? Array.Empty<ComicVineVariantCover>();

    /// <summary>ComicVine's concept_credits (names): thematic tags such as "Time Travel". Empty for Metron.</summary>
    public IReadOnlyList<string> Concepts { get; init; } = Array.Empty<string>();
}

/// <summary>Fetches one issue's full details by its ComicVine id. Kept apart from <see cref="IComicVineClient"/> so the catalog/search fakes needn't implement it.</summary>
public interface IComicVineIssueDetailsSource
{
    /// <summary>The issue's details, or <c>null</c> when ComicVine has no such issue (its own "object not found", status 101). Other failures throw <see cref="ComicVineException"/>.</summary>
    Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken);
}
