using System;
using System.Globalization;
using System.Linq;

namespace Paperbunkr.App.Models;

/// <summary>
/// The metadata editors present Language as an editable dropdown showing readable culture names
/// (<c>"English — en"</c>) but store the bare ISO code in <c>Issue.LanguageISO</c>
/// (docs/superpowers/specs/2026-09-05-metadata-editor-affordances-design.md §4.3). This maps
/// whatever the user leaves in the box back to what gets persisted:
/// <list type="bullet">
/// <item>a <c>"Name — code"</c> vocab label -&gt; the trailing code;</item>
/// <item>a bare culture display / English name (<c>"English"</c>) -&gt; its two-letter code;</item>
/// <item>anything else (<c>"en-US"</c>, <c>"jp"</c>, a typo) -&gt; stored verbatim - never destroy
/// an existing odd value;</item>
/// <item>empty / whitespace -&gt; <c>null</c>.</item>
/// </list>
/// </summary>
public static class LanguageNormalizer
{
    private static readonly Lazy<CultureInfo[]> Cultures = new(() =>
        CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(c => !string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.TwoLetterISOLanguageName))
            .ToArray());

    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();

        int dash = trimmed.LastIndexOf('—');
        if (dash >= 0)
        {
            string tail = trimmed[(dash + 1)..].Trim();
            if (tail.Length is 2 or 3)
            {
                return tail.ToLowerInvariant();
            }
        }

        var match = Cultures.Value.FirstOrDefault(c =>
            string.Equals(c.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.EnglishName, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.NativeName, trimmed, StringComparison.OrdinalIgnoreCase));

        return match?.TwoLetterISOLanguageName ?? trimmed;
    }
}
