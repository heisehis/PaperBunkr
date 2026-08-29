using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data;

/// <summary>
/// The single shared definition of CE's "search across a curated field bundle" concept
/// (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md §4). One dictionary keyed by
/// <see cref="SearchMode"/>, each value the exact per-<see cref="Issue"/> field list that mode
/// searches — transcribed field-for-field from <c>LibraryScreenViewModel.MatchesSearch</c>'s
/// <c>s.Issues.Any(i =&gt; ...)</c> clauses, which in turn match CE's
/// <c>ComicBookAllPropertiesMatcher.MatcherOption</c> field sets.
///
/// Two call sites use it, both <see cref="SearchMode"/>-driven:
/// <list type="bullet">
/// <item><c>LibraryScreenViewModel.MatchesSearch</c> — its per-issue clauses (the Series-level
/// <c>s.Name</c>/<c>s.Publisher</c>/<c>s.Genre</c>/title checks stay hand-written, they have no
/// per-Issue equivalent).</item>
/// <item><see cref="SmartLists.SmartListQueryBuilder"/> — <see cref="SmartListField.AllProperties"/>
/// conditions, scoped by <see cref="SmartListCondition.SearchMode"/> (null ⇒ <see cref="SearchMode.All"/>).</item>
/// </list>
/// Editing one call site's field list without the other is caught by
/// <c>SearchFieldBundleCatalogParityTests</c>.
/// </summary>
public static class SearchFieldBundleCatalog
{
    /// <summary>
    /// Per-<see cref="Issue"/> field selectors, one entry per <see cref="SearchMode"/>. A selector
    /// yields the raw field values (nulls kept — callers apply their own null/empty handling) that
    /// mode matches the query against.
    /// </summary>
    public static readonly IReadOnlyDictionary<SearchMode, Func<Issue, IEnumerable<string?>>> IssueFieldSelectors =
        new Dictionary<SearchMode, Func<Issue, IEnumerable<string?>>>
        {
            [SearchMode.All] = i => new[]
            {
                i.AlternateSeries, i.EffectiveTitle(), i.SeriesGroup, i.StoryArc,
                i.Writer, i.Penciller, i.Inker, i.Colorist, i.Letterer, i.Editor,
                i.Translator, i.CoverArtist, i.Summary, i.Notes, i.Review,
                i.FilePath, i.JoinedGenre(), i.Publisher, i.Imprint, i.Volume,
                i.Number, i.AlternateNumber, i.Format, i.AgeRating, i.JoinedTags(),
                i.MainCharacterOrTeam, i.Teams, i.Locations, i.BookAge, i.BookCollectionStatus,
                i.BookNotes, i.BookOwner, i.BookStore, i.BookLocation, i.ISBN, i.ScanInformation,
            },

            [SearchMode.Series] = i => new[]
            {
                i.AlternateSeries, i.Format, i.SeriesGroup, i.StoryArc,
            },

            [SearchMode.Writer] = i => new[]
            {
                i.Writer,
            },

            [SearchMode.Artists] = i => new[]
            {
                i.Writer, i.Penciller, i.Inker, i.Colorist, i.Editor, i.Translator, i.Letterer, i.CoverArtist,
            },

            [SearchMode.Descriptive] = i => new[]
            {
                i.Notes, i.Summary, i.Review, i.JoinedTags(), i.MainCharacterOrTeam, i.Teams, i.Locations, i.ScanInformation,
            },

            [SearchMode.File] = i => new[]
            {
                i.FilePath,
            },

            [SearchMode.Catalog] = i => new[]
            {
                i.BookAge, i.BookCollectionStatus, i.BookNotes, i.BookOwner, i.BookStore, i.BookLocation, i.ISBN,
            },
        };

    /// <summary>Selector for <paramref name="mode"/>, falling back to <see cref="SearchMode.All"/> for a null/unknown mode.</summary>
    public static Func<Issue, IEnumerable<string?>> For(SearchMode? mode) =>
        mode is { } m && IssueFieldSelectors.TryGetValue(m, out var selector) ? selector : IssueFieldSelectors[SearchMode.All];
}
