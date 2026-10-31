using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Paperbunkr.App.Services.Dictionary;

/// <summary>
/// The optional online fallback (docs/superpowers/specs/2026-10-27-books-slice4b-dictionary-design.md §3): dictionaryapi.dev, asked only when the user
/// turned "Look up unknown words online" on, and it sends nothing but the word.
/// </summary>
public sealed class OnlineDictionaryClient
{
    public const string BaseUrl = "https://api.dictionaryapi.dev/api/v2/entries/en/";

    private readonly Func<HttpClient> _http;

    public OnlineDictionaryClient(Func<HttpClient>? http = null) =>
        _http = http ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });

    public async Task<DictionaryResult?> LookupAsync(string word, CancellationToken ct = default)
    {
        string? normalized = DictionaryLookup.Normalize(word);
        if (normalized is null)
        {
            return null;
        }

        try
        {
            using var client = _http();
            using var response = await client.GetAsync(BaseUrl + Uri.EscapeDataString(normalized), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return Parse(normalized, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses the API's array of entries into the same shape the offline lookup returns (up to three senses per part of speech).</summary>
    public static DictionaryResult? Parse(string word, string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var groups = new List<DictionaryPosGroup>();
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("meanings", out var meanings))
            {
                continue;
            }

            foreach (var meaning in meanings.EnumerateArray())
            {
                string pos = meaning.TryGetProperty("partOfSpeech", out var p) ? p.GetString() ?? "word" : "word";
                var senses = new List<DictionarySense>();
                if (meaning.TryGetProperty("definitions", out var definitions))
                {
                    foreach (var d in definitions.EnumerateArray().Take(DictionaryLookup.MaxSensesPerPos))
                    {
                        string? text = d.TryGetProperty("definition", out var dt) ? dt.GetString() : null;
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        senses.Add(new DictionarySense(text, d.TryGetProperty("example", out var ex) ? ex.GetString() : null));
                    }
                }

                if (senses.Count > 0)
                {
                    groups.Add(new DictionaryPosGroup(pos, senses));
                }
            }
        }

        return groups.Count == 0 ? null : new DictionaryResult(word, word, groups);
    }
}
