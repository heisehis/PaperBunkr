using System;
using System.Globalization;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Local, network-free content-type inference from embedded <c>LanguageISO</c>
/// (docs/superpowers/specs/2026-08-23-language-iso-content-type-heuristic-design.md) - step 3 of
/// the docs/onboarding.md §7 pipeline ("no network match -&gt; local heuristic using LanguageISO").
/// Confirmed against CE's own <c>ComicBook.GetIsoCulture</c> (_reference/ComicRackCE/
/// ComicRack.Engine/ComicBook.cs) that <c>LanguageISO</c> is a free-form culture string, not a
/// fixed enum - matching normalizes via <see cref="CultureInfo"/> rather than exact comparison so
/// culture variants (<c>ja-JP</c>, <c>zh-Hant</c>, <c>zh-CN</c>) resolve the same as their bare
/// two-letter code.
/// </summary>
public static class LanguageIsoClassifier
{
    /// <summary>
    /// Maps a two-letter ISO language code to a <see cref="ContentType"/>/<see cref="ReadingMode"/>
    /// pair. <c>ja</c>-&gt;Manga/RightToLeft (paged, right-to-left convention). <c>ko</c>-&gt;
    /// Manhwa/Webtoon and <c>zh</c>-&gt;Manhua/Webtoon - per user direction, Korean/Chinese comics
    /// are predominantly left-to-right/Western-paged in convention (unlike manga), and modern
    /// digital manhwa/manhua specifically is overwhelmingly published in edge-to-edge vertical-scroll
    /// (webtoon) format, so neither gets manga's RightToLeft default. Returns false (leaving the out
    /// parameters at their defaults) for any other code, or input <see cref="CultureInfo"/> can't parse.
    /// </summary>
    public static bool TryClassify(string? languageIso, out ContentType contentType, out ReadingMode readingMode)
    {
        contentType = default;
        readingMode = default;

        if (string.IsNullOrWhiteSpace(languageIso))
        {
            return false;
        }

        string twoLetterCode;
        try
        {
            twoLetterCode = CultureInfo.GetCultureInfo(languageIso).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }

        switch (twoLetterCode)
        {
            case "ja":
                contentType = ContentType.Manga;
                readingMode = ReadingMode.RightToLeft;
                return true;
            case "ko":
                contentType = ContentType.Manhwa;
                readingMode = ReadingMode.Webtoon;
                return true;
            case "zh":
                contentType = ContentType.Manhua;
                readingMode = ReadingMode.Webtoon;
                return true;
            default:
                return false;
        }
    }
}
