using System;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// CE-parity port of <c>ComicInfo.SeriesEquals</c>/<c>rxVolume</c>/<c>rxSpecial</c>
/// (<c>_reference/ComicRackCE/ComicRack.Engine/ComicInfo.cs:123-125,1579-1592</c>), used by CBL
/// reading-list import as a tiered cascade (docs/superpowers/specs/2026-09-17-series-name-matching-
/// and-empty-row-cleanup-design.md). Confirmed against the real CE source rather than an invented
/// rule - CE already treats <c>"X: Y"</c> and <c>"X - Y"</c> as the same name once tier 3 strips
/// every non-alphanumeric character (including <c>:</c>, <c>-</c>, and spaces) plus the whole words
/// "the"/"and".
/// </summary>
public static class TitleNormalizer
{
    private static readonly Regex RxVolume = new(@"\bv(ol(ume)?)?\.?\s?\d+\b\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxSpecial = new(@"[^a-z0-9]|\bthe\b|\band\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Tiered match, loosening only on a miss: exact (case-insensitive), then - if
    /// <paramref name="ignoreVolume"/> - with a trailing/embedded volume marker stripped, then with
    /// <see cref="StripDown"/> applied to both sides. An exact match never pays the false-positive
    /// risk of the looser tiers.
    /// </summary>
    public static bool NamesMatch(string a, string b, bool ignoreVolume = true)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ignoreVolume)
        {
            string av = RxVolume.Replace(a, string.Empty).Trim();
            string bv = RxVolume.Replace(b, string.Empty).Trim();
            if (string.Equals(av, bv, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return string.Equals(StripDown(a), StripDown(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CE's tier-3 <c>rxSpecial</c> strip - every non-alphanumeric character plus the whole words
    /// "the"/"and" - as a canonical fold-everything key. Used directly as a grouping key (e.g. story
    /// arc candidate grouping) and as the final tier inside <see cref="NamesMatch"/>.
    /// </summary>
    public static string StripDown(string s) => RxSpecial.Replace(s, string.Empty);
}
