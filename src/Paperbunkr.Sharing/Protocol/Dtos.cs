namespace Paperbunkr.Sharing.Protocol;

// Wire DTOs for protocol v1 (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md §5).
//
// The catalog DTOs carry display metadata only. Anything personal to the host - file paths, file
// size/times, open count, last page read, ratings, reviews, notes, the Book* ownership fields,
// per-issue reader overrides, bookmarks, missing/duplicate flags - is deliberately absent, and
// CatalogDtoTests fails if one is ever added. Enums travel as their names so the two sides never
// depend on each other's numeric values.

/// <summary>Unauthenticated <c>GET /v1/hello</c> - the only thing a stranger can learn about a host.</summary>
public sealed record HelloResponse(int ProtocolVersion, string InstanceId, string DisplayName, bool RequiresPassword);

public sealed record SessionRequest(string Password);

public sealed record SessionResponse(string Token, DateTimeOffset ExpiresAt);

/// <summary>Generic error body. Never carries exception text.</summary>
public sealed record ErrorResponse(string Error);

public sealed record SharedListDto(int Id, string Name, string Kind);

public sealed record IssueTagDto(string Field, string Category, string Weight, string Value);

public sealed record CatalogSeriesDto(
    int Id,
    string Name,
    string? SortName,
    string ContentType,
    string ReadingMode,
    string Status,
    string? Publisher,
    string? Genre,
    string? Summary,
    string? Creator);

public sealed record CatalogIssueDto(
    int Id,
    int SeriesId,
    string? Title,
    string? Number,
    int? Count,
    string? Volume,
    string? AlternateSeries,
    string? AlternateNumber,
    int? AlternateCount,
    string? StoryArc,
    string? StoryArcNumber,
    string? SeriesGroup,
    bool? IsFinalIssue,
    string? Summary,
    int? Year,
    int? Month,
    int? Day,
    string? Writer,
    string? Penciller,
    string? Inker,
    string? Colorist,
    string? Letterer,
    string? CoverArtist,
    string? Editor,
    string? Translator,
    string? Publisher,
    string? Imprint,
    string? Web,
    int? PageCount,
    string? LanguageISO,
    string? Format,
    string? AgeRating,
    string? Characters,
    string? Teams,
    string? Locations,
    string? MainCharacterOrTeam,
    float? CommunityRating,
    string? ISBN,
    string ColorMode,
    DateTime? ReleasedTime,
    double? CoverAspectRatio,
    IReadOnlyList<IssueTagDto> Tags,
    string? ContentStamp = null);

/// <summary>One page of the shared catalog. <see cref="NextCursor"/> is opaque; null on the last page.</summary>
public sealed record CatalogPage(
    string CatalogVersion,
    IReadOnlyList<CatalogSeriesDto> Series,
    IReadOnlyList<CatalogIssueDto> Issues,
    string? NextCursor);

public sealed record PageInfoDto(int Index, int? Width, int? Height, string? Type);

public sealed record PagesResponse(int PageCount, IReadOnlyList<PageInfoDto> Pages);
