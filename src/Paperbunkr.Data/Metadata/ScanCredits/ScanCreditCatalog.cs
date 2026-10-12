using System.Globalization;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.Metadata.ScanCredits;

/// <summary>
/// What the scanner-credit detector knows (docs/superpowers/specs/2026-10-11-scan-credits-design.md): the names of scan and release groups, and the words that make a bracketed
/// piece of a file name <em>not</em> a scanner (a format, a rating, a month, a language, "of 4", a variant...). A built-in set plus the user's own additions
/// (<see cref="ScanCreditSettings"/>). Written for this app from the behaviour of ComicRack's "Scan Information From Filename" script: the group names are plain data, the
/// word list and matching rules are our own.
/// </summary>
public sealed class ScanCreditCatalog
{
    private static readonly Regex SeparatorOrSymbol = new(@"[\s_\-|/,.&]+", RegexOptions.Compiled);

    // A token such as "1280px", "17+", "MA15+", "T+": a count or rating, never a scanner.
    private static readonly Regex NumericTokenRx = new(@"^(?:\d+(?:px)?|[a-z]*\d+\+|[a-z]\+|\d+(?:st|nd|rd|th))$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly HashSet<string> _ignoredTokens;

    /// <summary>The known scanner and release-group names, longest first, so "Digital-Empire" is tried before "Empire".</summary>
    public IReadOnlyList<string> Scanners { get; }

    private ScanCreditCatalog(IEnumerable<string> scanners, IEnumerable<string> ignored)
    {
        Scanners = scanners
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(s => s.Length)
            .ThenBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _ignoredTokens = ignored.SelectMany(Tokens).Select(t => t.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The built-in lists alone.</summary>
    public static ScanCreditCatalog Default => DefaultCatalog.Value;

    // Lazy: the word lists below are static fields initialised after this one, so the catalog must not be built during type initialisation.
    private static readonly Lazy<ScanCreditCatalog> DefaultCatalog = new(() => Create(null));

    /// <summary>The built-in lists plus the user's additions (null = none).</summary>
    public static ScanCreditCatalog Create(ScanCreditSettings? user) => new(
        BuiltInScanners.Concat(user?.Scanners ?? []),
        BuiltInIgnoredWords.Concat(MonthWords()).Concat(LanguageWords).Concat(user?.Ignored ?? []));

    /// <summary>A word or number that carries no scanner information.</summary>
    public bool IsNoiseToken(string token)
    {
        if (token.Length == 0 || NumericTokenRx.IsMatch(token))
        {
            return true;
        }

        return _ignoredTokens.Contains(token.ToLowerInvariant());
    }

    /// <summary>The words of a phrase, split on spaces, underscores, dashes, slashes, commas, dots and ampersands.</summary>
    public static IEnumerable<string> Tokens(string text) => SeparatorOrSymbol.Split(text).Where(t => t.Length > 0);

    private static IEnumerable<string> MonthWords()
    {
        var format = CultureInfo.InvariantCulture.DateTimeFormat;
        return format.MonthNames.Concat(format.AbbreviatedMonthNames).Where(m => m.Length > 0).Append("sept");
    }

    // Names of groups that tag their releases; plain data.
    private static readonly string[] BuiltInScanners =
    [
        "A-Team", "Agent Orange-Superman", "AnEvilScan", "Antscan", "Archangel", "bakhos", "Bat Dinger", "Batgrrl-cr", "Bchry", "Blasty-dcp", "Brent-NW", "BTX",
        "C-3PO Scan", "cape1736", "CaptainMarvel", "Clickwheel", "ComicHost + A-Team", "comradejoe", "Crypt Preist", "Cypher", "DangerAngel", "DarthDMT", "Digimortal",
        "digital-Empire", "digital-Empire-CheGuevera-TheGroup", "digital-Perestroika-TheGroup", "digital-TheGroup", "Dragonz", "Empire-Lizzy", "Empire-Megan",
        "Empire-Perestroika-TheGroup", "Evilmage", "Extremest", "FBScan", "Filecatchers", "Guardian of Asgard", "hh-webrip", "HHshark", "Hydrocardiac", "Imbie", "Invisikid",
        "IsThisRealLife", "Jojo", "kabamarou", "KaiserSoze", "KFRONE HAMPTA", "KidShenlongScans", "KingofKingz", "KingPowers", "Lizard", "Mal32-Gambit-Twobyfour",
        "MinuteMen-Syl3ntBob", "mmdsx&TerminatedToons", "Nastynat", "Nile", "NSD", "Oroboros-Danger CPS", "Overkill", "PeteThePIPster", "PhaRO", "Pikmin", "Pir8s 4 Kids",
        "Pyramid", "raynor_ni", "Re-em", "Rip-Wash", "Scalliwags", "ScanDog", "shinter", "Spintriae", "StormDrake", "TAPSniper-Comicscans", "Team-RIP", "Team Mal-DREGS",
        "tenchi", "Teth Adam", "TheRedStar", "TokerTheKid", "Tripfag", "tsarrack", "WebRip-Empire", "webrip-Temujin", "webrip-TheGroup", "webrip - Empire",
        "webrip - TheUndisclosed", "WhobutDrew!!", "WildBlueZero", "Woodman-Mal32", "Youngstar", "YoungStr", "YZ1", "Zeg",
        "Zone-Empire", "Minutemen-Slayer", "Mephisto-Empire", "Kileko-Empire", "Son of Ultron-Empire", "Oracle-Empire", "GreenGiant-DCP", "DCP", "CPS", "TheGroup",
    ];

    // Words and short phrases, split into tokens, that mean "not a scanner". Our own list.
    private static readonly string[] BuiltInIgnoredWords =
    [
        // counts and parts
        "of", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "both", "only", "shot", "oneshot", "cover", "covers", "variant", "variants",
        "printing", "print", "px",
        // kinds of book
        "series", "limited", "annual", "anthology", "crossover", "complete", "event", "special", "preview", "prologue", "epilogue", "sketch", "reference", "review", "reviewed",
        "script", "graphic", "novel", "magazine", "manga", "flyer", "giant", "king", "size", "trade", "paper", "back", "tpb", "hardcover", "hard", "box", "set", "pack", "in",
        "annotation", "annotations", "c2c", "cover2cover", "year", "minus", "point",
        // how it was released
        "digital", "webrip", "web", "comic", "scanlation", "fixed", "noads", "ads", "ad", "no", "black", "white", "and",
        // audience and rating
        "adults", "adult", "mature", "teen", "teens", "everyone", "kids", "early", "childhood", "rating", "pending", "nsfw", "sfw", "not", "safe", "for", "work", "up", "pg",
        // publishers and scene tags that appear as plain labels
        "dc", "dcuo", "mdcu", "mgc", "fcdb", "image",
    ];

    // Language names and the two- and three-letter codes release names use.
    private static readonly string[] LanguageWords =
    [
        "english", "en", "eng", "german", "de", "ger", "deu", "french", "fr", "fra", "fre", "spanish", "castilian", "es", "spa", "italian", "it", "ita", "portuguese", "pt", "por",
        "dutch", "nl", "dut", "nld", "polish", "pl", "pol", "russian", "ru", "rus", "japanese", "ja", "jp", "jpn", "korean", "ko", "kor", "chinese", "mandarin", "zh", "chi", "zho",
        "cn", "czech", "czeck", "cs", "cze", "ces", "greek", "el", "gre", "ell", "finnish", "fi", "fin", "croatian", "hr", "hrv", "hungarian", "hu", "hun", "turkish", "tr", "tur",
        "slovak", "sk", "slo", "belarusian", "be", "bel", "breton", "br", "bre", "simplified", "traditional", "hans",
    ];
}
