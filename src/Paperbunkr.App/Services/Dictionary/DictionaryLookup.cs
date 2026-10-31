using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services.Dictionary;

public sealed record DictionarySense(string Definition, string? Example);

public sealed record DictionaryPosGroup(string Pos, IReadOnlyList<DictionarySense> Senses);

/// <summary>A word's definitions, grouped by part of speech; <see cref="Lemma"/> differs from <see cref="Word"/> when an inflected form was reduced.</summary>
public sealed record DictionaryResult(string Word, string Lemma, IReadOnlyList<DictionaryPosGroup> Groups);

/// <summary>
/// Finds a word's definitions (docs/superpowers/specs/2026-10-27-books-slice4b-dictionary-design.md §2): the word itself, then WordNet's irregular
/// forms, then its suffix-stripping rules ("morphy"). The data sources are functions so this is tested without a database.
/// </summary>
public sealed class DictionaryLookup
{
    public const int MaxSensesPerPos = 3;
    public const int MaxWordLength = 40;

    private readonly Func<string, IReadOnlyList<DictionaryEntry>> _entries;
    private readonly Func<string, IReadOnlyList<string>> _exceptions;

    public DictionaryLookup(Func<string, IReadOnlyList<DictionaryEntry>> entries, Func<string, IReadOnlyList<string>> exceptions)
    {
        _entries = entries;
        _exceptions = exceptions;
    }

    private static readonly (string Suffix, string Replacement)[] Rules =
    [
        ("ies", "y"), ("ses", "s"), ("xes", "x"), ("zes", "z"), ("ches", "ch"), ("shes", "sh"), ("men", "man"),
        ("es", "e"), ("es", ""), ("s", ""),
        ("ing", "e"), ("ing", ""), ("ed", "e"), ("ed", ""),
        ("er", "e"), ("er", ""), ("est", "e"), ("est", ""),
    ];

    private static readonly char[] EdgePunctuation = ".,;:!?\"'()[]{}“”‘’—–-…«»".ToCharArray();

    /// <summary>Lower-cases and trims punctuation; null when the text is not a single plausible word.</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string word = text.Trim().Trim(EdgePunctuation).ToLowerInvariant();
        // A word may be "mother-in-law" or "o'clock" but never contain whitespace.
        return word.Length is > 0 and <= MaxWordLength && !word.Any(char.IsWhiteSpace) && word.Any(char.IsLetter) ? word : null;
    }

    /// <summary>Candidate base forms, in the order they are tried.</summary>
    public IReadOnlyList<string> Candidates(string word)
    {
        var list = new List<string> { word };
        list.AddRange(_exceptions(word));
        foreach (var (suffix, replacement) in Rules)
        {
            if (word.Length > suffix.Length + 1 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                string stem = word[..^suffix.Length] + replacement;
                list.Add(stem);
                // "running" → "runn" → "run": a doubled final consonant before -ing / -ed / -er / -est.
                if (replacement.Length == 0 && suffix is "ing" or "ed" or "er" or "est" && stem.Length > 2 && stem[^1] == stem[^2] && !"aeiou".Contains(stem[^1]))
                {
                    list.Add(stem[..^1]);
                }
            }
        }

        return list.Distinct(StringComparer.Ordinal).ToList();
    }

    public DictionaryResult? Lookup(string? text)
    {
        string? word = Normalize(text);
        if (word is null)
        {
            return null;
        }

        foreach (string candidate in Candidates(word))
        {
            var entries = _entries(candidate);
            if (entries.Count == 0)
            {
                continue;
            }

            var groups = entries
                .GroupBy(e => e.Pos)
                .Select(g => new DictionaryPosGroup(g.Key, g.Take(MaxSensesPerPos).Select(e => new DictionarySense(e.Definition, e.Example)).ToList()))
                .ToList();
            return new DictionaryResult(word, candidate, groups);
        }

        return null;
    }
}
