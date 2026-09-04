using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Platform;
using FluentIcons.Common;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Resolves a metadata value (external service id, publisher, format, age rating, language ISO,
/// or a derived "special" flag) to a <see cref="MarkSpec"/> the <c>BrandMark</c> control renders
/// (docs/superpowers/specs/2026-08-28-brand-metadata-iconography-design.md). Pure - alias tables and
/// bundled-asset presence are read once from <c>avares://.../Assets/Marks/</c> at construction.
/// </summary>
public sealed class MarkResolver
{
    public static MarkResolver Instance { get; } = new();

    private const string Root = "avares://Paperbunkr.App/Assets/Marks/";

    /// <summary>Single-colour SVGs that should follow the theme text colour rather than render their
    /// own (near-black) fill. <see cref="MarkSpec.Foreground"/> == <see cref="ThemeTint"/> tells
    /// <c>BrandMark</c> to tint with its inherited <c>Foreground</c>.</summary>
    public const string ThemeTint = "$theme";

    private static readonly HashSet<string> MonochromeAssets = new(StringComparer.OrdinalIgnoreCase)
    {
        "anilist", "myanimelist", "kitsu", "mangaupdates",         // Services (Simple Icons, single path)
        "boom", "dynamite", "oni", "seven-seas", "dark-horse", "idw", "square-enix", // Publishers (potrace / wordmark)
    };

    private static readonly Dictionary<string, string> ServiceInitials = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AniList"] = "AL", ["MyAnimeList"] = "MAL", ["MangaDex"] = "MD", ["MangaBaka"] = "MB",
        ["MangaUpdates"] = "MU", ["Kitsu"] = "KT", ["AnimePlanet"] = "AP", ["Anime-Planet"] = "AP",
        ["Shikimori"] = "SK", ["Bangumi"] = "BGM", ["GrandComicsDatabase"] = "GCD",
        ["Grand Comics Database"] = "GCD", ["LeagueOfComicGeeks"] = "LOCG",
        ["League of Comic Geeks"] = "LOCG", ["ComicVine"] = "CV", ["Metron"] = "MT",
        ["ComicBookReadingOrders"] = "CBRO", ["Comic Book Reading Orders"] = "CBRO",
        ["ComicArc"] = "ARC", ["ReadingOrders.net"] = "RON", ["ReadingOrdersNet"] = "RON",
        ["ReadThingsRight"] = "RTR",
    };

    private readonly AliasTable _publishers;
    private readonly AliasTable _formats;
    private readonly AliasTable _ageRatings;
    private readonly Dictionary<string, string> _languageRegions;
    private readonly HashSet<string> _serviceAssets;
    private readonly HashSet<string> _publisherAssets;
    private readonly HashSet<string> _ageRatingAssets;
    private readonly HashSet<string> _formatAssets;
    private readonly HashSet<string> _flagAssets;

    public MarkResolver()
    {
        _publishers = AliasTable.Load(Root + "publisher-aliases.tsv");
        _formats = AliasTable.Load(Root + "format-aliases.tsv");
        _ageRatings = AliasTable.Load(Root + "age-rating-aliases.tsv", NormaliseRatingKey);
        _languageRegions = LoadLanguageRegions(Root + "language-regions.tsv");
        _serviceAssets = ListAssetStems(Root + "Services");
        _publisherAssets = ListAssetStems(Root + "Publishers");
        _ageRatingAssets = ListAssetStems(Root + "AgeRatings");
        _formatAssets = ListAssetStems(Root + "Formats");
        _flagAssets = ListAssetStems(Root + "Flags");
    }

    private static string NormaliseRatingKey(string s) =>
        new(s.Trim().ToLowerInvariant().Where(c => c is not (' ' or '-' or '+' or '_')).ToArray());

    // ---- Services -------------------------------------------------------------------------------

    public MarkSpec ResolveService(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return MarkSpec.None;
        }

        string stem = ServiceStem(id);
        if (_serviceAssets.Contains(stem))
        {
            return Svg("Services", stem);
        }

        string initials = ServiceInitials.TryGetValue(id.Trim(), out var i) ? i : Initials(id);
        return new MarkSpec(MarkKind.LetterMark, Text: initials);
    }

    private static string ServiceStem(string id) => id.Trim().ToLowerInvariant() switch
    {
        "anime-planet" or "animeplanet" => "animeplanet",
        "myanimelist" or "mal" => "myanimelist",
        "grandcomicsdatabase" or "grand comics database" => "gcd",
        "leagueofcomicgeeks" or "league of comic geeks" => "locg",
        "comicbookreadingorders" or "comic book reading orders" => "cbro",
        "readingorders.net" or "readingordersnet" => "readingorders",
        "readthingsright" => "readthingsright",
        var s => new string(s.Where(c => char.IsLetterOrDigit(c)).ToArray()),
    };

    // ---- Publishers --------------------------------------------------------------------------------

    public MarkSpec ResolvePublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return MarkSpec.None;
        }

        if (_publishers.TryResolve(NormalisePublisher(publisher), out AliasRow row))
        {
            string? asset = string.IsNullOrWhiteSpace(row.Col3) ? null : row.Col3;
            if (asset is not null && _publisherAssets.Contains(asset))
            {
                return Svg("Publishers", asset);
            }

            return new MarkSpec(MarkKind.LetterMark, Text: Initials(row.Canonical),
                Background: string.IsNullOrWhiteSpace(row.Col4) ? null : row.Col4);
        }

        return MarkSpec.PlainText(publisher);
    }

    internal static string NormalisePublisher(string value)
    {
        string v = value.Trim().ToLowerInvariant();
        foreach (string suffix in new[] { " comics", " publishing", " entertainment", " press", " inc", " ltd", " llc", " books", " co" })
        {
            if (v.EndsWith(suffix, StringComparison.Ordinal))
            {
                v = v[..^suffix.Length].TrimEnd();
            }
        }

        return v;
    }

    // ---- Format / age rating -----------------------------------------------------------------------

    public MarkSpec ResolveFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return MarkSpec.None;
        }

        if (_formats.TryResolve(format.Trim().ToLowerInvariant(), out AliasRow row))
        {
            // Optional SVG pictogram: an explicit 6th-column stem, or Formats/<canonical-slug>.svg.
            string stem = !string.IsNullOrWhiteSpace(row.Col6)
                ? row.Col6
                : new string(row.Canonical.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (_formatAssets.Contains(stem))
            {
                // Format pictograms carry their own colour (a category-hued palette, see
                // Assets/Marks/SOURCES.md) - rendered as-is, like the age-rating boxes, not tinted.
                return new MarkSpec(MarkKind.SvgAsset, AssetPath: Root + "Formats/" + stem + ".svg");
            }
        }

        return ResolveLabelled(_formats, format);
    }

    /// <summary>Age rating: an ESRB-style SVG box (<c>Assets/Marks/AgeRatings/</c>, rendered as-is -
    /// it is a two-colour glyph, not a tintable mono mark) when the alias row names an asset that
    /// exists; otherwise a colour-coded letter chip.</summary>
    public MarkSpec ResolveAgeRating(string? ageRating)
    {
        if (string.IsNullOrWhiteSpace(ageRating))
        {
            return MarkSpec.None;
        }

        if (!_ageRatings.TryResolve(NormaliseRatingKey(ageRating), out AliasRow row))
        {
            return MarkSpec.PlainText(ageRating);
        }

        if (!string.IsNullOrWhiteSpace(row.Col3) && _ageRatingAssets.Contains(row.Col3))
        {
            return new MarkSpec(MarkKind.SvgAsset, AssetPath: Root + "AgeRatings/" + row.Col3 + ".svg");
        }

        string label = string.IsNullOrWhiteSpace(row.Col4) ? row.Canonical.ToUpperInvariant() : row.Col4;
        string? bg = string.IsNullOrWhiteSpace(row.Col5) ? null : row.Col5;
        return new MarkSpec(MarkKind.LetterMark, Text: label, Background: bg);
    }

    private static MarkSpec ResolveLabelled(AliasTable table, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MarkSpec.None;
        }

        if (!table.TryResolve(value.Trim().ToLowerInvariant(), out AliasRow row))
        {
            return MarkSpec.PlainText(value);
        }

        string label = string.IsNullOrWhiteSpace(row.Col4) ? row.Canonical.ToUpperInvariant() : row.Col4;
        string? bg = string.IsNullOrWhiteSpace(row.Col5) ? null : row.Col5;

        if (!string.IsNullOrWhiteSpace(row.Col3) && Enum.TryParse<Symbol>(row.Col3, out var symbol))
        {
            return new MarkSpec(MarkKind.Glyph, Glyph: symbol, Text: label, Background: bg);
        }

        return new MarkSpec(MarkKind.LetterMark, Text: label, Background: bg);
    }

    // ---- Language ------------------------------------------------------------------------------

    public MarkSpec ResolveLanguage(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return MarkSpec.None;
        }

        string key = iso.Trim().ToLowerInvariant().Replace('_', '-');
        if (!_languageRegions.TryGetValue(key, out var region) && key.Contains('-'))
        {
            _languageRegions.TryGetValue(key[..key.IndexOf('-')], out region);
        }

        if (!string.IsNullOrEmpty(region) && _flagAssets.Contains(region))
        {
            return new MarkSpec(MarkKind.Flag, AssetPath: Root + "Flags/" + region + ".svg");
        }

        return new MarkSpec(MarkKind.Text, Text: iso.Trim().ToUpperInvariant());
    }

    // ---- Special (derived) -------------------------------------------------------------------------

    public IReadOnlyList<MarkSpec> ResolveSpecial(Issue? issue)
    {
        if (issue is null)
        {
            return Array.Empty<MarkSpec>();
        }

        var marks = new List<MarkSpec>();

        ContentType content = issue.Series?.ContentType ?? ContentType.Unknown;
        if (content is ContentType.Manga or ContentType.Manhwa or ContentType.Manhua)
        {
            marks.Add(new MarkSpec(MarkKind.Glyph, Glyph: Symbol.Book, Text: content.ToString().ToUpperInvariant()));
        }

        if (issue.ColorMode is ColorMode.BlackAndWhite or ColorMode.Grayscale)
        {
            marks.Add(new MarkSpec(MarkKind.LetterMark, Text: "B/W"));
        }

        return marks;
    }

    // ---- Reading status / scanlation group (docs/superpowers/specs/2026-09-04-detail-screen-
    //      icons-and-glyphs-design.md §8) - pure glyph marks, no assets/alias tables. ------------

    /// <summary>
    /// A <see cref="ReadingStatus"/> value (its enum name, as every call site produces via
    /// <c>series.ReadingStatus.ToString()</c>) → a colour-coded FluentIcons glyph + friendly
    /// label. <see cref="ReadingStatus.Unknown"/> and anything unparseable → <see cref="MarkSpec.None"/>
    /// (renders nothing). Hex colours mirror the app's semantic tokens - kept as literals here for
    /// the same reason the age-rating chip colours live as hex in <c>age-rating-aliases.tsv</c>:
    /// this resolver is deliberately Avalonia-resource-free.
    /// </summary>
    public MarkSpec ResolveReadingStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse<ReadingStatus>(value.Trim(), ignoreCase: true, out var status))
        {
            return MarkSpec.None;
        }

        var p = ReadingStatusPresentation.For(status);
        return p.HasGlyph ? new MarkSpec(MarkKind.Glyph, Glyph: p.Glyph, Text: p.Label, Foreground: p.Hex) : MarkSpec.None;
    }

    /// <summary>A manga chapter's scanlation-group string → a "people" glyph beside the name.
    /// Blank → <see cref="MarkSpec.None"/>. No colour override (inherits <c>Foreground</c>).</summary>
    public MarkSpec ResolveScanGroup(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? MarkSpec.None
            : new MarkSpec(MarkKind.Glyph, Glyph: Symbol.PeopleTeam, Text: value.Trim());

    // ---- helpers ------------------------------------------------------------------------------------

    private static MarkSpec Svg(string folder, string stem) => new(
        MarkKind.SvgAsset,
        AssetPath: Root + folder + "/" + stem + ".svg",
        Foreground: MonochromeAssets.Contains(stem) ? ThemeTint : null);

    internal static string Initials(string name)
    {
        var words = name.Split(new[] { ' ', '-', '&', '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "?";
        }

        if (words.Length == 1)
        {
            // "MU"/"TPB"-style short tokens keep their letters; a real word becomes a single initial.
            return words[0].Length <= 4 && words[0].ToUpperInvariant() == words[0]
                ? words[0]
                : char.ToUpperInvariant(words[0][0]).ToString();
        }

        return string.Concat(words.Take(3).Select(w => char.ToUpperInvariant(w[0])));
    }

    private static HashSet<string> ListAssetStems(string folderAvares)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (Uri uri in AssetLoader.GetAssets(new Uri(folderAvares + "/"), null))
            {
                string path = uri.AbsolutePath;
                if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(Path.GetFileNameWithoutExtension(path));
                }
            }
        }
        catch (Exception)
        {
            // folder not present yet (e.g. Flags before Step 6) - resolver just falls back.
        }

        return set;
    }

    private static Dictionary<string, string> LoadLanguageRegions(string avares)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string a, string b) in ReadTsv(avares, 2))
        {
            map[a] = b;
        }

        return map;
    }

    private static IEnumerable<(string, string)> ReadTsv(string avares, int minCols)
    {
        using Stream stream = AssetLoader.Open(new Uri(avares));
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length >= minCols)
            {
                yield return (parts[0].Trim(), parts[1].Trim());
            }
        }
    }

    // ---- alias table ---------------------------------------------------------------------------

    internal readonly record struct AliasRow(string Canonical, string Col3, string Col4, string Col5, string Col6);

    internal sealed class AliasTable
    {
        private readonly Dictionary<string, AliasRow> _byKey = new(StringComparer.OrdinalIgnoreCase);

        public static AliasTable Load(string avares, Func<string, string>? normaliseKey = null)
        {
            normaliseKey ??= s => s.Trim().ToLowerInvariant();
            var table = new AliasTable();
            using Stream stream = AssetLoader.Open(new Uri(avares));
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] p = line.Split('\t');
                if (p.Length < 2)
                {
                    continue;
                }

                string canonical = p[0].Trim();
                var row = new AliasRow(
                    canonical,
                    p.Length > 2 ? p[2].Trim() : string.Empty,
                    p.Length > 3 ? p[3].Trim() : string.Empty,
                    p.Length > 4 ? p[4].Trim() : string.Empty,
                    p.Length > 5 ? p[5].Trim() : string.Empty);

                table._byKey[normaliseKey(canonical)] = row;
                foreach (string alias in p[1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    table._byKey[normaliseKey(alias)] = row;
                }
            }

            return table;
        }

        public bool TryResolve(string normalisedKey, out AliasRow row) => _byKey.TryGetValue(normalisedKey, out row);
    }
}
