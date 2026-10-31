using System.Text.Json;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Books;

/// <summary>
/// Google Books (googleapis.com/books/v1): works without a key at a low daily quota; a key the user pastes into
/// Preferences &gt; Connections lifts it. <see cref="BookProviderRecord.Id"/> is the volume id. Ratings are already 0-5.
/// </summary>
public sealed class GoogleBooksProvider : BookProviderBase, IBookMetadataProvider
{
    private const string Base = "https://www.googleapis.com/books/v1";

    private readonly Func<string?> _apiKey;

    public GoogleBooksProvider(Func<string?>? apiKey = null, HttpClient? http = null)
        : base(http, TimeSpan.FromMilliseconds(800))
    {
        _apiKey = apiKey ?? (() => null);
    }

    public BookFieldSourceKind Kind => BookFieldSourceKind.GoogleBooks;

    string IBookMetadataProvider.DisplayName => DisplayName;

    protected override string DisplayName => "Google Books";

    private string KeyParam()
    {
        string? key = _apiKey();
        return string.IsNullOrWhiteSpace(key) ? string.Empty : $"&key={Uri.EscapeDataString(key.Trim())}";
    }

    public async Task<IReadOnlyList<BookProviderRecord>> SearchAsync(string? isbn, string title, string? author, CancellationToken ct)
    {
        string? cleanIsbn = BookMetadataParsing.ExtractIsbn(isbn);
        string q;
        if (cleanIsbn is not null)
        {
            q = $"isbn:{cleanIsbn}";
        }
        else if (!string.IsNullOrWhiteSpace(title))
        {
            q = $"intitle:\"{title.Replace("\"", string.Empty)}\"" + (string.IsNullOrWhiteSpace(author) ? string.Empty : $"+inauthor:\"{author.Replace("\"", string.Empty)}\"");
        }
        else
        {
            return Array.Empty<BookProviderRecord>();
        }

        using var doc = await GetJsonAsync($"{Base}/volumes?q={Uri.EscapeDataString(q).Replace("%2B", "+")}&maxResults=10&printType=books{KeyParam()}", ct).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BookProviderRecord>();
        }

        return items.EnumerateArray().Select(ToRecord).Where(r => r is not null).Select(r => r!).ToList();
    }

    public async Task<BookProviderRecord?> GetAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        using var doc = await GetJsonAsync($"{Base}/volumes/{Uri.EscapeDataString(id)}?{KeyParam().TrimStart('&')}", ct).ConfigureAwait(false);
        return doc is null ? null : ToRecord(doc.RootElement);
    }

    private BookProviderRecord? ToRecord(JsonElement item)
    {
        string? id = Str(item, "id");
        if (id is null || !item.TryGetProperty("volumeInfo", out var info))
        {
            return null;
        }

        string? title = Str(info, "title");
        if (title is null)
        {
            return null;
        }

        string? isbn = null;
        if (info.TryGetProperty("industryIdentifiers", out var ids) && ids.ValueKind == JsonValueKind.Array)
        {
            var found = ids.EnumerateArray().Select(i => BookMetadataParsing.ExtractIsbn(Str(i, "identifier"))).Where(i => i is not null).ToList();
            isbn = found.FirstOrDefault(i => i!.Length == 13) ?? found.FirstOrDefault();
        }

        double? avg = Num(info, "averageRating");
        int? count = Num(info, "ratingsCount") is double c ? (int)c : null;
        string? cover = info.TryGetProperty("imageLinks", out var links) ? (Str(links, "thumbnail") ?? Str(links, "smallThumbnail")) : null;
        string? description = Str(info, "description");

        return new BookProviderRecord(
            Kind, id, title, Str(info, "subtitle"), StrList(info, "authors"), Str(info, "publisher"),
            Year(Str(info, "publishedDate")), Str(info, "language"), isbn,
            Categories(StrList(info, "categories")),
            description is null ? null : StripHtml(description),
            SeriesName: null, SeriesIndex: null,
            CommunityRating: avg is > 0 ? (float)avg.Value : null,
            CommunityRatingCount: count is > 0 ? count : null,
            CoverUrl: cover?.Replace("http://", "https://", StringComparison.Ordinal));
    }

    /// <summary>Google categories look like "Fiction / Science Fiction / General": split on the slash and keep the distinct parts, dropping "General".</summary>
    private static IReadOnlyList<string> Categories(IReadOnlyList<string> raw) =>
        raw.SelectMany(c => c.Split('/'))
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && !p.Equals("General", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
