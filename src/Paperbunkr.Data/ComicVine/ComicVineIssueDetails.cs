namespace Paperbunkr.Data.ComicVine;

/// <summary>A ComicVine date split into parts: any of the three can be absent (ComicVine often has only a year, or year and month).</summary>
public sealed record ComicVineDatePart(int? Year, int? Month, int? Day);

/// <summary>One person credit with the ComicVine role already mapped onto Paperbunkr's credit field name; <see cref="Field"/> is null for a role with no equivalent.</summary>
public sealed record ComicVineCredit(string Name, string? Field);

/// <summary>Everything ComicVine knows about one issue that Paperbunkr can store (the per-issue half of the scraper's field matrix).</summary>
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

/// <summary>Fetches one issue's full details by its ComicVine id. Kept apart from <see cref="IComicVineClient"/> so the catalog/search fakes needn't implement it.</summary>
public interface IComicVineIssueDetailsSource
{
    /// <summary>The issue's details, or <c>null</c> when ComicVine has no such issue (its own "object not found", status 101). Other failures throw <see cref="ComicVineException"/>.</summary>
    Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken);
}
