using System.Text.RegularExpressions;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>The publishing format a comic (or a candidate series) is, as far as names can tell.</summary>
public enum ComicFormatKind
{
    Regular,
    Annual,
    TradePaperback,
    Hardcover,
    Omnibus,
    OneShot,
    GraphicNovel,
    Special,
    FreeComicBookDay,
}

/// <summary>
/// Format and filename hints for matching (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 5, with the
/// formats the user asked for on 2026-10-21). CE only lets a format count when the comic's Format field is filled in, and then only as one
/// more word in the name comparison; most files have no Format, so an annual or a trade was scored like a regular issue. Here the format is
/// read from the Format field, else the file name, else its folder, and a candidate series of the same format is preferred
/// (<see cref="Agreement"/>) - a deliberate deviation from CE's scorer, which this adds to rather than changes. The same hints clean a raw
/// file-style series name ("Batman_Beyond_Neo-Gothic_(2023-)") into a query and give a year when the comic has none.
/// </summary>
public static class ComicFormatHints
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly (ComicFormatKind Kind, Regex Pattern)[] Patterns =
    {
        (ComicFormatKind.FreeComicBookDay, new Regex(@"\b(fcbd|free[ _.-]*comic[ _.-]*book[ _.-]*day)\b", Options)),
        (ComicFormatKind.Annual, new Regex(@"\bannual\b", Options)),
        (ComicFormatKind.Omnibus, new Regex(@"\bomnibus\b", Options)),
        (ComicFormatKind.TradePaperback, new Regex(@"\b(tpb|trade[ _.-]*paper[ _.-]*back)\b", Options)),
        (ComicFormatKind.Hardcover, new Regex(@"\b(hc|hard[ _.-]*cover|deluxe[ _.-]*edition)\b", Options)),
        (ComicFormatKind.GraphicNovel, new Regex(@"\b(o?gn|graphic[ _.-]*novel)\b", Options)),
        (ComicFormatKind.OneShot, new Regex(@"\bone[ _.-]*shot\b", Options)),
        (ComicFormatKind.Special, new Regex(@"\bspecial\b", Options)),
    };

    private static readonly Regex Bracketed = new(@"[\(\[\{][^\)\]\}]*[\)\]\}]", Options);
    private static readonly Regex YearGroup = new(@"\((?<year>(19|20)\d{2})(-|\s*-\s*(19|20)?\d{0,2})?\)", Options);
    private static readonly Regex IssueTail = new(@"\s*#\s*[\w.]+\s*$", Options);
    private static readonly Regex Spaces = new(@"\s{2,}", Options);

    /// <summary>The comic's format: its Format field when that names one, else its file name, else the folder it is in.</summary>
    public static ComicFormatKind Detect(string? format, string? filePath)
    {
        var fromField = FromText(format);
        if (fromField != ComicFormatKind.Regular || !string.IsNullOrWhiteSpace(format) && IsRegularWord(format))
        {
            return fromField;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ComicFormatKind.Regular;
        }

        var fromFile = FromText(Path.GetFileNameWithoutExtension(filePath));
        if (fromFile != ComicFormatKind.Regular)
        {
            return fromFile;
        }

        // A folder named "Lobo Annual" or "Trades" says it for every file in it.
        return FromText(Path.GetFileName(Path.GetDirectoryName(filePath)));
    }

    /// <summary>The format a name spells out ("Lobo Annual", "Saga TPB"), or Regular.</summary>
    public static ComicFormatKind FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ComicFormatKind.Regular;
        }

        string normalized = text.Replace('_', ' ');
        foreach (var (kind, pattern) in Patterns)
        {
            if (pattern.IsMatch(normalized))
            {
                return kind;
            }
        }

        return ComicFormatKind.Regular;
    }

    /// <summary>
    /// Added to a candidate's score: the same non-regular format as the comic +15; an annual or other special format for a regular comic, or a
    /// regular series for an annual, -15. A trade, hardcover or omnibus matched against a series without the word in its name gets only -5,
    /// because sources often name a collected edition exactly like its series.
    /// </summary>
    public static double Agreement(ComicFormatKind book, string? candidateName)
    {
        var candidate = FromText(candidateName);
        if (book == candidate)
        {
            return book == ComicFormatKind.Regular ? 0 : 15;
        }

        if (book == ComicFormatKind.Regular)
        {
            return -15;
        }

        return book is ComicFormatKind.TradePaperback or ComicFormatKind.Hardcover or ComicFormatKind.Omnibus or ComicFormatKind.GraphicNovel
            && candidate == ComicFormatKind.Regular
            ? -5
            : -15;
    }

    /// <summary>
    /// A series name fit to search with: underscores to spaces, bracketed scanner tags and years ("(2023-)", "[Digital]") and a trailing
    /// "#1" removed. A clean name comes back unchanged.
    /// </summary>
    public static string CleanQuery(string seriesName)
    {
        string cleaned = seriesName.Replace('_', ' ');
        cleaned = Bracketed.Replace(cleaned, " ");
        cleaned = IssueTail.Replace(cleaned, string.Empty);
        cleaned = Spaces.Replace(cleaned, " ").Trim();
        return cleaned.Length == 0 ? seriesName.Trim() : cleaned;
    }

    /// <summary>The start year a file-style name carries ("(2023-)", "(1990)"), from the series name or else the file name.</summary>
    public static int? YearHint(string? seriesName, string? filePath)
    {
        foreach (var text in new[] { seriesName, string.IsNullOrEmpty(filePath) ? null : Path.GetFileNameWithoutExtension(filePath) })
        {
            if (text is not null && YearGroup.Match(text) is { Success: true } m && int.TryParse(m.Groups["year"].Value, out int year))
            {
                return year;
            }
        }

        return null;
    }

    private static readonly Regex AfterHash = new(@"#\s*(?<designation>[^#]+)$", Options);
    private static readonly Regex TrailingYear = new(@"\s*\b(19|20)\d{2}\b\s*$", Options);
    private static readonly Regex NumberLike = new(@"^[\d.,/½-]+[a-z]?$", Options);

    /// <summary>
    /// What a file name calls the issue when it is words, not a number: "The_Amazing_Spider-Man_(2025-)_#Halloween_Special_2026.cbz" -> "Halloween
    /// Special" (reported 2026-10-22: such a special is its own series at the source, which a search for the main series buries). Null for a number
    /// ("#37", "#001", "#1.5") or when there is no "#".
    /// </summary>
    public static string? IssueDesignation(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(filePath).Replace('_', ' ');
        var m = AfterHash.Match(name);
        if (!m.Success)
        {
            return null;
        }

        string designation = Bracketed.Replace(m.Groups["designation"].Value, " ");
        designation = TrailingYear.Replace(Spaces.Replace(designation, " ").Trim(), string.Empty).Trim();
        return designation.Length < 2 || NumberLike.IsMatch(designation) ? null : designation;
    }

    private static readonly Regex TrailingIssueNumber = new(@"\s+(no\.?\s*)?\d+(\.\d+)?[a-z]?\s*$", Options);

    /// <summary>
    /// The series title a comic's file name gives, without the issue number, year and bracketed tags: "Secret_Wars_2099_001 (2015).cbz" -> "Secret
    /// Wars 2099". Null when there is no file or nothing is left. Used where a library series holds comics of more than one source series
    /// (reported 2026-10-22: "Secret Wars" holding "Secret Wars 2099").
    /// </summary>
    public static string? FileTitle(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(filePath).Replace('_', ' ');
        int hash = name.IndexOf('#');
        if (hash >= 0)
        {
            name = name[..hash];
        }

        name = Bracketed.Replace(name, " ");
        name = Spaces.Replace(name, " ").Trim(' ', '-', '–', ':');
        name = TrailingIssueNumber.Replace(name, string.Empty).Trim(' ', '-', '–', ':');
        return name.Length < 2 ? null : name;
    }

    /// <summary>Two titles name the same series, ignoring case, punctuation, "The"/"and", a volume marker and a "(year)".</summary>
    public static bool SameTitle(string a, string b) =>
        Metadata.TitleNormalizer.NamesMatch(StripYearSuffix(CleanQuery(a)), StripYearSuffix(CleanQuery(b)));

    /// <summary>
    /// The name to search and score with: the file's title when it extends the library series name ("Secret Wars 2099" under "Secret Wars"),
    /// else the series name. A file title that merely restates the series, or says something unrelated, changes nothing.
    /// </summary>
    public static string SearchName(string seriesName, string? filePath)
    {
        string? fileTitle = FileTitle(filePath);
        if (fileTitle is null)
        {
            return seriesName;
        }

        string series = Metadata.TitleNormalizer.StripDown(StripYearSuffix(CleanQuery(seriesName)).ToLowerInvariant());
        string file = Metadata.TitleNormalizer.StripDown(StripYearSuffix(fileTitle).ToLowerInvariant());
        return series.Length > 0 && file.Length > series.Length && file.StartsWith(series, StringComparison.Ordinal) ? fileTitle : seriesName;
    }

    private static readonly Regex YearSuffix = new(@"\s*\(\s*(19|20)\d{2}\s*-?\s*\)\s*$", Options);

    private static string StripYearSuffix(string name) => YearSuffix.Replace(name, string.Empty).Trim();

    private static bool IsRegularWord(string format) =>
        format.Trim().ToLowerInvariant() is "series" or "ongoing" or "regular" or "comic" or "issue" or "limited series" or "mini-series" or "maxi-series";
}
