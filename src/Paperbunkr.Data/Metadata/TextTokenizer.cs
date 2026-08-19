using System;
using System.Collections.Generic;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Shared comma-separated-list tokenizer for <c>Paperbunkr.Data</c> resolvers - same convention as
/// <c>Paperbunkr.App.Models.ListFieldTokens</c>, reimplemented here since <c>Paperbunkr.Data</c>
/// doesn't depend on <c>Paperbunkr.App</c>. Extracted from <see cref="RecommendationResolver"/>'s own
/// former private copy so <see cref="HomeFeedResolver"/> (docs/superpowers/specs/
/// 2026-08-18-home-screen-design.md) can reuse the identical genre/tag-splitting behavior instead of
/// a third near-duplicate.
/// </summary>
public static class TextTokenizer
{
    public static HashSet<string> Tokenize(string? value)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return tokens;
        }

        foreach (string token in value.Split(','))
        {
            string trimmed = token.Trim();
            if (trimmed.Length > 0)
            {
                tokens.Add(trimmed);
            }
        }

        return tokens;
    }
}
