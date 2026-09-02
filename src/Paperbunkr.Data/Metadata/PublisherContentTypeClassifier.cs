using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Local, network-free content-type inference from <see cref="Issue.Publisher"/>
/// (docs/superpowers/specs/2026-08-30-publisher-content-type-classification-design.md) - a third
/// signal alongside the embedded <c>Manga</c> field and <see cref="LanguageIsoClassifier"/>.
/// Matching is case-insensitive <c>Contains</c> against a starter, hand-curated key list (real
/// publisher strings vary - "Viz Media", "VIZ Media LLC", "Shueisha Inc." - and a reasonably
/// specific key like "Viz Media" catches all of them without listing every literal variant).
/// Deliberately excludes publishers whose output spans more than one <see cref="ContentType"/>
/// (Dark Horse publishes both Western comics and licensed manga; Tapas/Kakao Piccoma are
/// mixed-content platforms) - a confident-looking wrong answer is worse than no answer here.
/// </summary>
public static class PublisherContentTypeClassifier
{
    private static readonly (string Key, ContentType ContentType, ReadingMode ReadingMode)[] Entries =
    {
        ("Marvel", ContentType.Comic, ReadingMode.LeftToRight),
        ("DC Comics", ContentType.Comic, ReadingMode.LeftToRight),
        ("Image Comics", ContentType.Comic, ReadingMode.LeftToRight),
        ("Boom! Studios", ContentType.Comic, ReadingMode.LeftToRight),
        ("IDW", ContentType.Comic, ReadingMode.LeftToRight),
        ("Valiant", ContentType.Comic, ReadingMode.LeftToRight),
        ("Dynamite", ContentType.Comic, ReadingMode.LeftToRight),
        ("Archie Comics", ContentType.Comic, ReadingMode.LeftToRight),
        ("Oni Press", ContentType.Comic, ReadingMode.LeftToRight),
        ("Vertigo", ContentType.Comic, ReadingMode.LeftToRight),
        ("WildStorm", ContentType.Comic, ReadingMode.LeftToRight),
        ("AfterShock", ContentType.Comic, ReadingMode.LeftToRight),
        ("Black Mask", ContentType.Comic, ReadingMode.LeftToRight),
        ("Top Cow", ContentType.Comic, ReadingMode.LeftToRight),

        ("Viz", ContentType.Manga, ReadingMode.RightToLeft),
        ("Shueisha", ContentType.Manga, ReadingMode.RightToLeft),
        ("Shogakukan", ContentType.Manga, ReadingMode.RightToLeft),
        ("Kodansha", ContentType.Manga, ReadingMode.RightToLeft),
        ("Square Enix", ContentType.Manga, ReadingMode.RightToLeft),
        ("Kadokawa", ContentType.Manga, ReadingMode.RightToLeft),
        ("Seven Seas", ContentType.Manga, ReadingMode.RightToLeft),
        ("Vertical Comics", ContentType.Manga, ReadingMode.RightToLeft),
        ("Yen Press", ContentType.Manga, ReadingMode.RightToLeft),
        ("Denpa", ContentType.Manga, ReadingMode.RightToLeft),
        ("One Peace Books", ContentType.Manga, ReadingMode.RightToLeft),

        ("WEBTOON", ContentType.Manhwa, ReadingMode.Webtoon),
        ("LINE Webtoon", ContentType.Manhwa, ReadingMode.Webtoon),
        ("Lezhin", ContentType.Manhwa, ReadingMode.Webtoon),
        ("Ize Press", ContentType.Manhwa, ReadingMode.Webtoon),
        ("D&C Media", ContentType.Manhwa, ReadingMode.Webtoon),
        ("Redice Studio", ContentType.Manhwa, ReadingMode.Webtoon),

        ("Kuaikan Manhua", ContentType.Manhua, ReadingMode.Webtoon),
        ("Bilibili Comics", ContentType.Manhua, ReadingMode.Webtoon),
    };

    /// <summary>
    /// Maps a free-form <see cref="Issue.Publisher"/> string to a <see cref="ContentType"/>/
    /// <see cref="ReadingMode"/> pair via case-insensitive substring match against
    /// <see cref="Entries"/>. Returns <see langword="false"/> (leaving the out parameters at their
    /// defaults) for null/blank input or no match.
    /// </summary>
    public static bool TryClassify(string? publisher, out ContentType contentType, out ReadingMode readingMode)
    {
        contentType = default;
        readingMode = default;

        if (string.IsNullOrWhiteSpace(publisher))
        {
            return false;
        }

        foreach (var entry in Entries)
        {
            if (publisher.Contains(entry.Key, StringComparison.OrdinalIgnoreCase))
            {
                contentType = entry.ContentType;
                readingMode = entry.ReadingMode;
                return true;
            }
        }

        return false;
    }
}
