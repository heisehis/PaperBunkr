using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services.LibrarySearch;

/// <summary>
/// One search mode's distinct "value match" suggestion pool, prepared so ranking a query costs
/// well under a millisecond however large the pool gets (docs/superpowers/specs/
/// 2026-09-19-library-search-perf-design.md §1). The old code ran <c>Contains(OrdinalIgnoreCase)</c>
/// over every value on every keystroke, then sorted every match to keep six.
///
/// The pool is sorted once by its upper-cased text (ordinal), with a parallel array of the
/// upper-cased text. <see cref="Rank"/>:
/// <list type="number">
///   <item>finds the prefix-match range by binary search and takes up to <c>max</c> values from it;</item>
///   <item>only if the cap is not yet reached, scans the sorted pool for substring matches outside that
///   range and stops as soon as the cap is filled.</item>
/// </list>
/// Output order is identical to the old
/// <c>OrderByDescending(startsWith).ThenBy(alphabetical OrdinalIgnoreCase).Take(max)</c>: prefix
/// matches first, then the remaining substring matches, each group alphabetical.
/// Values must already be distinct under <see cref="StringComparison.OrdinalIgnoreCase"/> (the
/// suggestion index builder guarantees it), so the sort has no ties.
/// </summary>
internal sealed class SuggestionCandidateList
{
    private readonly string[] _values;
    private readonly string[] _normalized;

    // All normalized values in sorted order, each preceded by LibrarySearchIndex.Separator, in one string,
    // with the start offset of every value. The substring pass does a handful of vectorized IndexOf calls
    // over this instead of a per-value Contains loop: a value's text never contains the separator, and
    // neither can a query, so a hit can never span two values.
    private readonly string _blob;
    private readonly int[] _offsets;

    public static readonly SuggestionCandidateList Empty = new(Array.Empty<string>());

    public SuggestionCandidateList(IEnumerable<string> values)
    {
        var list = new List<string>(values);
        var normalized = new string[list.Count];
        for (int i = 0; i < normalized.Length; i++)
        {
            normalized[i] = LibrarySearchIndex.Normalize(list[i]);
        }

        var valueArray = list.ToArray();
        Array.Sort(normalized, valueArray, StringComparer.Ordinal);
        _values = valueArray;
        _normalized = normalized;

        var builder = new System.Text.StringBuilder(normalized.Sum(n => n.Length + 1));
        _offsets = new int[normalized.Length];
        for (int i = 0; i < normalized.Length; i++)
        {
            builder.Append(LibrarySearchIndex.Separator);
            _offsets[i] = builder.Length;
            builder.Append(normalized[i]);
        }

        _blob = builder.ToString();
    }

    public int Count => _values.Length;

    /// <summary>Up to <paramref name="max"/> values matching <paramref name="trimmedQuery"/> (non-empty,
    /// already trimmed), prefix matches first. Empty for an empty query.</summary>
    public IReadOnlyList<string> Rank(string trimmedQuery, int max)
    {
        var results = new List<string>(Math.Min(max, 16));
        if (trimmedQuery.Length == 0 || max <= 0 || _values.Length == 0)
        {
            return results;
        }

        string query = LibrarySearchIndex.Normalize(trimmedQuery);

        // First index whose normalized text is >= query; every prefix match sits contiguously from here.
        int low = 0;
        int high = _normalized.Length;
        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (string.CompareOrdinal(_normalized[mid], query) < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        int prefixStart = low;
        int prefixEnd = prefixStart;
        while (prefixEnd < _normalized.Length && _normalized[prefixEnd].StartsWith(query, StringComparison.Ordinal))
        {
            if (results.Count < max)
            {
                results.Add(_values[prefixEnd]);
            }

            prefixEnd++;
            if (results.Count >= max)
            {
                return results;
            }
        }

        // Substring matches outside the prefix range, in sorted order, stopping as soon as the cap is filled.
        if (query.Contains(LibrarySearchIndex.Separator))
        {
            return results;
        }

        int position = 0;
        while (results.Count < max && position < _blob.Length)
        {
            int hit = _blob.IndexOf(query, position, StringComparison.Ordinal);
            if (hit < 0)
            {
                break;
            }

            // The value containing this hit: the last one whose start offset is <= hit.
            int index = Array.BinarySearch(_offsets, hit);
            if (index < 0)
            {
                index = ~index - 1;
            }

            if (index < prefixStart || index >= prefixEnd)
            {
                results.Add(_values[index]);
            }

            position = index + 1 < _offsets.Length ? _offsets[index + 1] : _blob.Length;
        }

        return results;
    }
}
