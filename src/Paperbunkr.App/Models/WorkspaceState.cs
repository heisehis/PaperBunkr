using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// The exact set of <see cref="AppSettings"/> fields the Library screen persists
/// (<c>LibraryScreenViewModel.LoadLibrarySettings</c> / <c>SaveLibrarySettings</c>), captured as
/// one value for a saved <see cref="Workspace"/> (docs/superpowers/specs/2026-09-03-library-saved-
/// workspaces-design.md). Every parameter has a default equal to the app's own out-of-box default,
/// so an old <c>StateJson</c> blob written before a field existed deserializes cleanly to that
/// field's default rather than throwing.
/// </summary>
public sealed record LibraryWorkspaceState(
    LibraryContentGranularity Granularity = LibraryContentGranularity.Issue,
    LibrarySortField SortField = LibrarySortField.DateAdded,
    SortDirection SortDirection = SortDirection.Descending,
    LibraryGroupField GroupField = LibraryGroupField.None,
    IssueListSortField IssueListSortField = IssueListSortField.Added,
    SortDirection IssueListSortDirection = SortDirection.Descending,
    IssueListGroupField IssueListGroupField = IssueListGroupField.None,
    LibraryViewMode ViewMode = LibraryViewMode.PosterGrid,
    double GridDensity = 1.0,
    bool ShowTileTitles = true,
    bool ShowUnreadBadge = true,
    bool ShowPublisherBadge = false,
    bool ShowLanguageBadge = false,
    bool UseLanguageIcon = false,
    bool ShowContinueReadingButton = false,
    string? SearchQuery = null,
    SearchMode SearchMode = SearchMode.All,
    ContentType? ActiveContentType = null,
    int? ActiveCollectionId = null,
    bool FilterUnreadOnly = false,
    bool FilterMissingIssues = false,
    bool FilterTrackedOnly = false,
    string? DetailsColumns = null);

/// <summary>
/// The Books screen's persisted sort/group state - its whole three-field slice of
/// <see cref="AppSettings"/> (<c>BooksScreenViewModel.LoadBooksSettings</c>). Same defaulting
/// contract as <see cref="LibraryWorkspaceState"/>.
/// </summary>
public sealed record BooksWorkspaceState(
    BooksSortField SortField = BooksSortField.Title,
    SortDirection SortDirection = SortDirection.Ascending,
    BooksGroupField GroupField = BooksGroupField.None);

/// <summary>
/// Serialization for <see cref="Workspace.StateJson"/>. Enums are written as strings (readable,
/// and stable against re-ordering an enum). Deserialization is defensive: a corrupt or
/// unparseable blob yields the all-defaults record, never throws - the same silent-fallback
/// posture <c>LibraryScreenViewModel.DeserializeRecentSearches</c> takes.
/// </summary>
public static class WorkspaceStateJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T state) => JsonSerializer.Serialize(state, Options);

    public static LibraryWorkspaceState DeserializeLibrary(string? json) =>
        Deserialize(json, static () => new LibraryWorkspaceState());

    public static BooksWorkspaceState DeserializeBooks(string? json) =>
        Deserialize(json, static () => new BooksWorkspaceState());

    /// <summary>Never throws. On any failure returns the all-app-default record and logs.</summary>
    private static T Deserialize<T>(string? json, Func<T> fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) is { } value ? value : fallback();
        }
        catch (JsonException)
        {
            return fallback();
        }
    }
}
