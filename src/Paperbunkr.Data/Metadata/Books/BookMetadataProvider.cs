using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Books;

/// <summary>One candidate (or fully fetched) book from a public catalogue (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §5).</summary>
public sealed record BookProviderRecord(
    BookFieldSourceKind Source,
    string Id,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    string? Publisher,
    int? Year,
    string? LanguageISO,
    string? Isbn,
    IReadOnlyList<string> Subjects,
    string? Description,
    string? SeriesName,
    float? SeriesIndex,
    float? CommunityRating,
    int? CommunityRatingCount,
    string? CoverUrl)
{
    public string AuthorLine => string.Join(", ", Authors);

    /// <summary>The values <see cref="BookMetadataApplier"/> understands. Community rating is scaled to our 0-5 range by each provider already.</summary>
    public BookFieldValues ToFieldValues() => new()
    {
        Subtitle = Subtitle,
        Publisher = Publisher,
        LanguageISO = LanguageISO,
        ISBN = Isbn,
        SeriesName = SeriesName,
        SeriesIndex = SeriesIndex,
        Summary = Description,
        CommunityRating = CommunityRating,
        CommunityRatingCount = CommunityRatingCount,
        Genres = Subjects,
    };
}

/// <summary>A public book catalogue the match dialog and bulk job can ask.</summary>
public interface IBookMetadataProvider
{
    BookFieldSourceKind Kind { get; }

    string DisplayName { get; }

    /// <summary>Candidates for an ISBN (when known and valid) or a title + author. Throws <see cref="BookProviderException"/> when the catalogue is unreachable or rate limited.</summary>
    Task<IReadOnlyList<BookProviderRecord>> SearchAsync(string? isbn, string title, string? author, CancellationToken ct);

    /// <summary>The full record for a candidate's <see cref="BookProviderRecord.Id"/> (adds the description / subjects a search leaves out). Null when it no longer exists.</summary>
    Task<BookProviderRecord?> GetAsync(string id, CancellationToken ct);
}

/// <summary>The catalogue could not be reached, answered with an error, or asked us to slow down. Never means "no matches".</summary>
public sealed class BookProviderException : Exception
{
    public BookProviderException(string message, bool isRateLimited = false, Exception? inner = null)
        : base(message, inner)
    {
        IsRateLimited = isRateLimited;
    }

    public bool IsRateLimited { get; }
}

/// <summary>Shared transport for the book catalogues: one polite request at a time, a descriptive User-Agent, 429 handling.</summary>
public abstract class BookProviderBase
{
    private static readonly Lazy<HttpClient> SharedClient = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Paperbunkr/1.0 (+https://github.com/paperbunkr/paperbunkr)");
        return client;
    });

    private readonly HttpClient _http;
    private readonly TimeSpan _minInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _nextAllowedUtc = DateTime.MinValue;

    protected BookProviderBase(HttpClient? http, TimeSpan minInterval)
    {
        _http = http ?? SharedClient.Value;
        _minInterval = minInterval;
    }

    /// <summary>GETs <paramref name="url"/> and parses the JSON body. 404 returns null; other failures throw <see cref="BookProviderException"/>.</summary>
    protected async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            TimeSpan wait = _nextAllowedUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }

            try
            {
                using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                _nextAllowedUtc = DateTime.UtcNow + _minInterval;

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    TimeSpan retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                    _nextAllowedUtc = DateTime.UtcNow + retry;
                    throw new BookProviderException($"{DisplayName} is rate limiting requests. Try again in a minute.", isRateLimited: true);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new BookProviderException($"{DisplayName} answered {(int)response.StatusCode}.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new BookProviderException($"{DisplayName} could not be reached.", inner: ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new BookProviderException($"{DisplayName} timed out.", inner: ex);
            }
            catch (JsonException ex)
            {
                throw new BookProviderException($"{DisplayName} sent a response that could not be read.", inner: ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    protected abstract string DisplayName { get; }

    // ---- JSON helpers shared by both catalogues ----

    protected static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? BookMetadataParsingShim.NullIfBlank(v.GetString())
            : null;

    protected static IReadOnlyList<string> StrList(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return arr.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    protected static double? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)
            ? d
            : null;

    protected static int? Year(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var m = Regex.Match(text, @"\b(1[5-9]\d{2}|20\d{2})\b");
        return m.Success ? int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Maps the common ISO 639-2 three-letter codes Open Library uses to the two-letter codes files use; unknown codes pass through.</summary>
    protected static string? Language2(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return code.Trim().ToLowerInvariant() switch
        {
            "eng" => "en", "fre" or "fra" => "fr", "ger" or "deu" => "de", "spa" => "es", "ita" => "it", "por" => "pt",
            "rus" => "ru", "jpn" => "ja", "chi" or "zho" => "zh", "dut" or "nld" => "nl", "swe" => "sv", "pol" => "pl",
            "kor" => "ko", "lat" => "la", "gre" or "ell" => "el", "tur" => "tr", "ara" => "ar", "heb" => "he", "dan" => "da",
            "nor" => "no", "fin" => "fi", "cze" or "ces" => "cs", "hun" => "hu",
            var other => other,
        };
    }

    protected static string StripHtml(string text) =>
        Regex.Replace(Regex.Replace(text, @"<\s*br\s*/?>|</p>", "\n", RegexOptions.IgnoreCase), "<[^>]+>", string.Empty).Trim();
}

/// <summary>Local alias so the provider base does not depend on the Engine namespace for one trim helper.</summary>
internal static class BookMetadataParsingShim
{
    public static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
