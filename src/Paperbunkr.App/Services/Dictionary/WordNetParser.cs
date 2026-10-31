using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Paperbunkr.App.Services.Dictionary;

/// <summary>One sense of a word: its lemma, part of speech (noun, verb, adjective, adverb), definition and an optional example.</summary>
public sealed record DictionaryEntry(string Lemma, string Pos, string Definition, string? Example);

/// <summary>
/// Reads Princeton WordNet 3.0's database files (<c>data.*</c>, <c>index.*</c>, <c>*.exc</c>) into dictionary rows
/// (docs/superpowers/specs/2026-10-27-books-slice4b-dictionary-design.md §2). Pure text in, rows out, so it is tested with small hand-written samples.
/// </summary>
public static class WordNetParser
{
    public static string PosName(char code) => code switch
    {
        'n' => "noun",
        'v' => "verb",
        'a' or 's' => "adjective",
        'r' => "adverb",
        _ => "word",
    };

    /// <summary>Parses one POS's <c>data</c> and <c>index</c> text. <paramref name="pos"/> is n, v, a or r.</summary>
    public static IEnumerable<DictionaryEntry> ParsePos(char pos, string dataText, string indexText)
    {
        var glosses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in dataText.Split('\n'))
        {
            if (line.Length == 0 || line[0] == ' ')
            {
                continue; // the licence header lines start with spaces
            }

            int bar = line.IndexOf('|');
            if (bar < 0)
            {
                continue;
            }

            string[] head = line[..bar].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (head.Length < 4)
            {
                continue;
            }

            glosses[head[0]] = line[(bar + 1)..].Trim();
        }

        foreach (string line in indexText.Split('\n'))
        {
            if (line.Length == 0 || line[0] == ' ')
            {
                continue;
            }

            string[] tokens = line.TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 7 || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int synsetCount) || tokens.Length < synsetCount)
            {
                continue;
            }

            string lemma = tokens[0].Replace('_', ' ');
            foreach (string offset in tokens.Skip(tokens.Length - synsetCount))
            {
                if (glosses.TryGetValue(offset, out string? gloss))
                {
                    var (definition, example) = SplitGloss(gloss);
                    yield return new DictionaryEntry(lemma, PosName(pos), definition, example);
                }
            }
        }
    }

    /// <summary>"a; b; \"ex\"; \"ex2\"" → definition "a; b", example "ex".</summary>
    public static (string Definition, string? Example) SplitGloss(string gloss)
    {
        var parts = gloss.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var definition = string.Join("; ", parts.TakeWhile(p => !p.StartsWith('"')));
        string? example = parts.FirstOrDefault(p => p.StartsWith('"'))?.Trim('"').Trim();
        return (definition.Length == 0 ? gloss.Trim() : definition, string.IsNullOrEmpty(example) ? null : example);
    }

    /// <summary>Irregular forms: each line is "inflected base [base...]".</summary>
    public static IEnumerable<(string Form, string Lemma)> ParseExceptions(string excText)
    {
        foreach (string line in excText.Split('\n'))
        {
            string[] tokens = line.TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < tokens.Length; i++)
            {
                yield return (tokens[0].Replace('_', ' '), tokens[i].Replace('_', ' '));
            }
        }
    }
}
