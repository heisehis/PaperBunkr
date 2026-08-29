using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Golden-list guard for <see cref="SearchFieldBundleCatalog"/>
/// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §4/§5). The expected sets below
/// are transcribed field-for-field from the pre-extraction <c>LibraryScreenViewModel.MatchesSearch</c>
/// <c>s.Issues.Any(i =&gt; ...)</c> clauses. <c>MatchesSearch</c> now delegates to the catalog, so the
/// two stay in lockstep by construction; this test pins the catalog so a later silent edit to
/// either call site's field list fails here.
/// </summary>
public class SearchFieldBundleCatalogParityTests
{
    /// <summary>An <see cref="Issue"/> whose every searchable field carries its own name as its value.</summary>
    private static Issue Probe()
    {
        var issue = new Issue
        {
            AlternateSeries = "AlternateSeries",
            Title = "EffectiveTitle",
            SeriesGroup = "SeriesGroup",
            StoryArc = "StoryArc",
            Writer = "Writer",
            Penciller = "Penciller",
            Inker = "Inker",
            Colorist = "Colorist",
            Letterer = "Letterer",
            Editor = "Editor",
            Translator = "Translator",
            CoverArtist = "CoverArtist",
            Summary = "Summary",
            Notes = "Notes",
            Review = "Review",
            FilePath = "FilePath",
            Publisher = "Publisher",
            Imprint = "Imprint",
            Volume = "Volume",
            Number = "Number",
            AlternateNumber = "AlternateNumber",
            Format = "Format",
            AgeRating = "AgeRating",
            MainCharacterOrTeam = "MainCharacterOrTeam",
            Teams = "Teams",
            Locations = "Locations",
            BookAge = "BookAge",
            BookCollectionStatus = "BookCollectionStatus",
            BookNotes = "BookNotes",
            BookOwner = "BookOwner",
            BookStore = "BookStore",
            BookLocation = "BookLocation",
            ISBN = "ISBN",
            ScanInformation = "ScanInformation",
        };
        issue.MergeFrom(IssueTagField.Genre, new[] { "JoinedGenre" });
        issue.MergeFrom(IssueTagField.Tags, new[] { "JoinedTags" });
        return issue;
    }

    private static readonly IReadOnlyDictionary<SearchMode, string[]> Golden = new Dictionary<SearchMode, string[]>
    {
        [SearchMode.All] = new[]
        {
            "AlternateSeries", "EffectiveTitle", "SeriesGroup", "StoryArc", "Writer", "Penciller",
            "Inker", "Colorist", "Letterer", "Editor", "Translator", "CoverArtist", "Summary",
            "Notes", "Review", "FilePath", "JoinedGenre", "Publisher", "Imprint", "Volume", "Number",
            "AlternateNumber", "Format", "AgeRating", "JoinedTags", "MainCharacterOrTeam", "Teams",
            "Locations", "BookAge", "BookCollectionStatus", "BookNotes", "BookOwner", "BookStore",
            "BookLocation", "ISBN", "ScanInformation",
        },
        [SearchMode.Series] = new[] { "AlternateSeries", "Format", "SeriesGroup", "StoryArc" },
        [SearchMode.Writer] = new[] { "Writer" },
        [SearchMode.Artists] = new[] { "Writer", "Penciller", "Inker", "Colorist", "Editor", "Translator", "Letterer", "CoverArtist" },
        [SearchMode.Descriptive] = new[] { "Notes", "Summary", "Review", "JoinedTags", "MainCharacterOrTeam", "Teams", "Locations", "ScanInformation" },
        [SearchMode.File] = new[] { "FilePath" },
        [SearchMode.Catalog] = new[] { "BookAge", "BookCollectionStatus", "BookNotes", "BookOwner", "BookStore", "BookLocation", "ISBN" },
    };

    [Theory]
    [InlineData(SearchMode.All)]
    [InlineData(SearchMode.Series)]
    [InlineData(SearchMode.Writer)]
    [InlineData(SearchMode.Artists)]
    [InlineData(SearchMode.Descriptive)]
    [InlineData(SearchMode.File)]
    [InlineData(SearchMode.Catalog)]
    public void Selector_ReadsExactlyTheGoldenFieldSet(SearchMode mode)
    {
        var actual = SearchFieldBundleCatalog.IssueFieldSelectors[mode](Probe())
            .Where(v => !string.IsNullOrEmpty(v))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Golden[mode].OrderBy(v => v, StringComparer.Ordinal).ToArray(), actual);
    }

    [Fact]
    public void EverySearchMode_HasASelector()
    {
        Assert.Equal(
            Enum.GetValues<SearchMode>().OrderBy(m => m).ToArray(),
            SearchFieldBundleCatalog.IssueFieldSelectors.Keys.OrderBy(m => m).ToArray());
    }

    [Fact]
    public void For_FallsBackToAll_ForNull()
    {
        Assert.Same(SearchFieldBundleCatalog.IssueFieldSelectors[SearchMode.All], SearchFieldBundleCatalog.For(null));
    }
}
