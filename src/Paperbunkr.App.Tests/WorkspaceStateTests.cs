using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Round-trip and defensive-deserialization coverage for <see cref="WorkspaceStateJson"/>
/// (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md).
/// </summary>
public class WorkspaceStateTests
{
    [Fact]
    public void LibraryState_RoundTripsEveryField()
    {
        var original = new LibraryWorkspaceState(
            Granularity: LibraryContentGranularity.Series,
            IssueListSortField: IssueListSortField.Opened,
            IssueListSortDirection: SortDirection.Ascending,
            IssueListGroupField: IssueListGroupField.Series,
            ViewMode: LibraryViewMode.Details,
            GridDensity: 1.35,
            ShowTileTitles: false,
            ShowUnreadBadge: false,
            ShowPublisherBadge: true,
            ShowLanguageBadge: true,
            UseLanguageIcon: true,
            ShowContinueReadingButton: true,
            SearchQuery: "spider, man",
            SearchMode: SearchMode.Writer,
            ActiveContentType: ContentType.Manga,
            ActiveCollectionId: 42,
            FilterUnreadOnly: true,
            FilterMissingIssues: true,
            FilterTrackedOnly: true,
            DetailsColumns: "Title,Series,Number");

        var restored = WorkspaceStateJson.DeserializeLibrary(WorkspaceStateJson.Serialize(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void BooksState_RoundTrips()
    {
        var original = new BooksWorkspaceState(BooksSortField.RecentlyAdded, SortDirection.Descending, BooksGroupField.Series);
        Assert.Equal(original, WorkspaceStateJson.DeserializeBooks(WorkspaceStateJson.Serialize(original)));
    }

    [Fact]
    public void EnumsAreWrittenAsStrings_NotNumbers()
    {
        string json = WorkspaceStateJson.Serialize(new LibraryWorkspaceState(ViewMode: LibraryViewMode.List));
        Assert.Contains("\"List\"", json);
        Assert.DoesNotContain("\"ViewMode\":2", json);
    }

    [Fact]
    public void MissingKey_FallsBackToAppDefault_ForThatFieldOnly()
    {
        // Only ViewMode present - every other field must land on its default.
        var state = WorkspaceStateJson.DeserializeLibrary("{\"ViewMode\":\"Tiles\"}");

        Assert.Equal(LibraryViewMode.Tiles, state.ViewMode);
        Assert.Equal(LibraryContentGranularity.Issue, state.Granularity);
        Assert.Equal(IssueListSortField.Added, state.IssueListSortField);
        Assert.Equal(SearchMode.All, state.SearchMode);
        Assert.Null(state.SearchQuery);
        Assert.False(state.FilterUnreadOnly);
    }

    [Fact]
    public void UnknownKey_IsIgnored()
    {
        var state = WorkspaceStateJson.DeserializeLibrary("{\"ViewMode\":\"List\",\"SomethingRemovedLater\":true}");
        Assert.Equal(LibraryViewMode.List, state.ViewMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"ViewMode\":\"NoSuchMode\"}")]
    [InlineData("[1,2,3]")]
    public void GarbageOrUnparseable_YieldsAllDefaults_NeverThrows(string json)
    {
        var state = WorkspaceStateJson.DeserializeLibrary(json);
        Assert.Equal(new LibraryWorkspaceState(), state);
    }
}
