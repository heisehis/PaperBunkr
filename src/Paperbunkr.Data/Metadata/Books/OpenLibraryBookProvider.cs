using System.Text.Json;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Books;

/// <summary>
/// Open Library (openlibrary.org): no account, no key. Search via <c>/search.json</c> (ISBN, or title + author); the work record
/// (<c>/works/OL…W.json</c>) adds the description. <see cref="BookProviderRecord.Id"/> is the work key, e.g. "/works/OL45804W".
/// </summary>
public sealed class OpenLibraryBookProvider : BookProviderBase, IBookMetadataProvider
{
    private const string Base = "https://openlibrary.org";

    private const string SearchFields = "key,title,subtitle,author_name,first_publish_year,publisher,isbn,language,subject,ratings_average,ratings_count,cover_i";

    public OpenLibraryBookProvider(HttpClient? http = null)
        : base(http, TimeSpan.FromMilliseconds(600))
    {
    }

    public static OpenLibraryBookProvider Shared { get; } = new();

    public BookFieldSourceKind Kind => BookFieldSourceKind.OpenLibrary;

    string IBookMetadataProvider.DisplayName => DisplayName;

    protected override string DisplayName => "Open Library";

    public async Task<IReadOnlyList<BookProviderRecord>> SearchAsync(string? isbn, string title, string? author, CancellationToken ct)
    {
        string? cleanIsbn = BookMetadataParsing.ExtractIsbn(isbn);
        string query = cleanIsbn is not null
            ? $"isbn={Uri.EscapeDataString(cleanIsbn)}"
            : $"title={Uri.EscapeDataString(title)}" + (string.IsNullOrWhiteSpace(author) ? string.Empty : $"&author={Uri.EscapeDataString(author)}");
        if (cleanIsbn is null && string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<BookProviderRecord>();
        }

        using var doc = await GetJsonAsync($"{Base}/search.json?{query}&limit=10&fields={SearchFields}", ct).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("docs", out var docs) || docs.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BookProviderRecord>();
        }

        var results = new List<BookProviderRecord>();
        foreach (var d in docs.EnumerateArray())
        {
            string? key = Str(d, "key");
            string? name = Str(d, "title");
            if (key is null || name is null)
            {
                continue;
            }

            string? isbnFound = cleanIsbn ?? StrList(d, "isbn").Select(BookMetadataParsing.ExtractIsbn).FirstOrDefault(i => i is { Length: 13 }) ?? StrList(d, "isbn").Select(BookMetadataParsing.ExtractIsbn).FirstOrDefault(i => i is not null);
            double? avg = Num(d, "ratings_average");
            int? count = Num(d, "ratings_count") is double c ? (int)c : null;
            string? cover = Num(d, "cover_i") is double coverId ? $"https://covers.openlibrary.org/b/id/{(long)coverId}-L.jpg" : null;

            results.Add(new BookProviderRecord(
                Kind, key, name, Str(d, "subtitle"), StrList(d, "author_name"),
                StrList(d, "publisher").FirstOrDefault(),
                Num(d, "first_publish_year") is double y ? (int)y : null,
                Language2(StrList(d, "language").FirstOrDefault()),
                isbnFound,
                Subjects(StrList(d, "subject")),
                Description: null, SeriesName: null, SeriesIndex: null,
                CommunityRating: avg is > 0 ? (float)Math.Round(avg.Value, 2) : null,
                CommunityRatingCount: count is > 0 ? count : null,
                CoverUrl: cover));
        }

        return results;
    }

    public async Task<BookProviderRecord?> GetAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("/works/", StringComparison.Ordinal))
        {
            return null;
        }

        using var doc = await GetJsonAsync($"{Base}{id}.json", ct).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        var root = doc.RootElement;
        string? description = null;
        if (root.TryGetProperty("description", out var desc))
        {
            description = desc.ValueKind switch
            {
                JsonValueKind.String => desc.GetString(),
                JsonValueKind.Object => Str(desc, "value"),
                _ => null,
            };
        }

        return new BookProviderRecord(
            Kind, id, Str(root, "title") ?? string.Empty, Str(root, "subtitle"), Array.Empty<string>(),
            Publisher: null, Year: null, LanguageISO: null, Isbn: null,
            Subjects(StrList(root, "subjects")),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            SeriesName: null, SeriesIndex: null, CommunityRating: null, CommunityRatingCount: null, CoverUrl: null);
    }

    /// <summary>Open Library subjects are long-tailed ("Fiction, science fiction, general"); keep the first few so tags stay useful.</summary>
    private static IReadOnlyList<string> Subjects(IReadOnlyList<string> all) => all.Take(8).ToList();
}
