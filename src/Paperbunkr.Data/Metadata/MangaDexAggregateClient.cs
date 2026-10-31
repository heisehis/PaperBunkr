using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Paperbunkr.Data.Metadata;

/// <summary>One chapter MangaDex lists for a series. <see cref="Volume"/> is null for MangaDex's "none" bucket.</summary>
public sealed record MangaDexAggregateChapter(string? Volume, string Chapter);

/// <summary>
/// The result of <c>GET /manga/{id}/aggregate</c> (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md
/// Part 1). Shape verified live 2026-10-26: <c>{result, volumes:{&lt;vol|"none"&gt;:{volume, count, chapters:{&lt;ch&gt;:{chapter, id, isUnavailable, others[], count}}}}}</c>.
/// </summary>
public sealed record MangaDexAggregate(IReadOnlyList<MangaDexAggregateChapter> Chapters)
{
    public int ChapterCount => Chapters.Count;

    /// <summary>Distinct volume numbers MangaDex knows (the "none" bucket excluded).</summary>
    public IReadOnlyList<string> Volumes { get; } = Distinct(Chapters);

    private static IReadOnlyList<string> Distinct(IReadOnlyList<MangaDexAggregateChapter> chapters)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var chapter in chapters)
        {
            if (chapter.Volume is not null && seen.Add(chapter.Volume))
            {
                list.Add(chapter.Volume);
            }
        }

        return list;
    }
}

/// <summary>
/// Fetches and parses MangaDex's per-series chapter list. Read-only and metadata-only, like <see cref="MangaDexMetadataProvider"/>:
/// it shares that provider's shared HttpClient (the User-Agent MangaDex insists on) and its conservative request spacing.
/// Throws <see cref="MetadataProviderUnavailableException"/> on a failed call, same contract as the provider.
/// </summary>
public sealed class MangaDexAggregateClient
{
    private const string BaseUrl = "https://api.mangadex.org/";

    /// <summary>Same ~400 ms spacing as <see cref="MangaDexMetadataProvider"/> (MangaDex allows ~5 req/s/IP); static so every call site shares one limiter.</summary>
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(400);

    private static readonly object RateLimitLock = new();
    private static DateTime _nextAllowedRequestUtc = DateTime.MinValue;

    private readonly HttpClient _httpClient;

    public MangaDexAggregateClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static readonly MangaDexAggregateClient Shared = new(MangaDexHttpClient.Shared);

    /// <param name="language">An ISO 639-1 code to filter by (MangaDex's <c>translatedLanguage[]</c>); null asks for every language.</param>
    public async Task<MangaDexAggregate> FetchAsync(string mangaId, string? language, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mangaId))
        {
            throw new MetadataProviderUnavailableException();
        }

        string url = $"{BaseUrl}manga/{Uri.EscapeDataString(mangaId)}/aggregate";
        if (!string.IsNullOrWhiteSpace(language))
        {
            url += $"?translatedLanguage%5B%5D={Uri.EscapeDataString(language.Trim().ToLowerInvariant())}";
        }

        await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new MetadataProviderUnavailableException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MetadataProviderUnavailableException();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                ObserveRetryAfter(response);
                throw new MetadataProviderUnavailableException();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MetadataProviderUnavailableException();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Parse(json) ?? throw new MetadataProviderUnavailableException();
        }
    }

    /// <summary>Parses the aggregate JSON; null when it isn't an <c>ok</c> response of the expected shape. Public so a cached payload can be re-read without a call.</summary>
    public static MangaDexAggregate? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("volumes", out var volumes))
            {
                return null;
            }

            var chapters = new List<MangaDexAggregateChapter>();
            // MangaDex sends an empty array rather than an empty object when a series has no chapters.
            if (volumes.ValueKind == JsonValueKind.Object)
            {
                foreach (var volume in volumes.EnumerateObject())
                {
                    string? volumeName = string.Equals(volume.Name, "none", StringComparison.OrdinalIgnoreCase) ? null : volume.Name;
                    if (!volume.Value.TryGetProperty("chapters", out var chapterMap) || chapterMap.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var chapter in chapterMap.EnumerateObject())
                    {
                        bool unavailable = chapter.Value.TryGetProperty("isUnavailable", out var flag) && flag.ValueKind == JsonValueKind.True;
                        if (unavailable)
                        {
                            continue;
                        }

                        string number = chapter.Value.TryGetProperty("chapter", out var named) && named.ValueKind == JsonValueKind.String
                            ? named.GetString() ?? chapter.Name
                            : chapter.Name;
                        // MangaDex files an unnumbered oneshot under chapter "none"; there is no number to compare against.
                        if (string.Equals(number, "none", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        chapters.Add(new MangaDexAggregateChapter(volumeName, number));
                    }
                }
            }

            return new MangaDexAggregate(chapters);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses a chapter or volume label into a number the way the rest of the app compares them (invariant, so "10.5" is ten and a half).</summary>
    public static bool TryParseNumber(string? text, out float number) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        TimeSpan delay;
        lock (RateLimitLock)
        {
            var now = DateTime.UtcNow;
            var slot = _nextAllowedRequestUtc > now ? _nextAllowedRequestUtc : now;
            _nextAllowedRequestUtc = slot + MinRequestInterval;
            delay = slot - now;
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ObserveRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
        lock (RateLimitLock)
        {
            var until = DateTime.UtcNow + retryAfter;
            if (until > _nextAllowedRequestUtc)
            {
                _nextAllowedRequestUtc = until;
            }
        }
    }
}
