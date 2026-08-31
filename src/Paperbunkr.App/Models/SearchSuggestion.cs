namespace Paperbunkr.App.Models;

/// <summary>Which of the four suggestion sources a <see cref="SearchSuggestion"/> row came from
/// (docs/superpowers/specs/2026-08-31-library-search-suggestions-design.md) - drives both the
/// icon/section header in the popup and, for <see cref="SavedSearch"/>, the selection behavior.</summary>
public enum SearchSuggestionKind
{
    Recent,
    Value,
    SavedSearch,
    FieldHint,
}

/// <summary>One row in the Library search box's suggestions popup.</summary>
public class SearchSuggestion
{
    public required SearchSuggestionKind Kind { get; init; }

    public required string DisplayText { get; init; }

    /// <summary>What <c>SearchQuery</c> becomes on selection - used for <see cref="SearchSuggestionKind.Recent"/>,
    /// <see cref="SearchSuggestionKind.Value"/>, and <see cref="SearchSuggestionKind.FieldHint"/>. Null for
    /// <see cref="SearchSuggestionKind.SavedSearch"/>, which clears the search box instead
    /// (see <see cref="CollectionId"/>).</summary>
    public string? InsertText { get; init; }

    /// <summary>Set only for <see cref="SearchSuggestionKind.SavedSearch"/> - the <c>Collection.Id</c> to select via <c>SelectCollectionById</c>.</summary>
    public int? CollectionId { get; init; }
}
