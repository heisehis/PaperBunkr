using System.Globalization;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Naming;

/// <summary>Resolves one CE token name to a raw field value on an <see cref="Issue"/>. Design doc §5:
/// routed through <c>Effective*</c> extension methods (Paperbunkr's own filename-fallback layer,
/// backed by the auditable <c>MetadataProposals</c> table) rather than raw <see cref="Issue"/>
/// properties, for the fields that have an Effective* equivalent. <paramref name="context"/> is null
/// for a caller with no batch context (e.g. a single-issue evaluation in a test) - every resolver that
/// needs it treats a null context as "no data available" rather than throwing.</summary>
public delegate string? TokenFieldResolver(Issue issue, string args, TemplateContext? context);

/// <summary>
/// Series-level rollup backing the <c>startyear</c>/<c>EndYear</c>/<c>EndMonth</c>(<c>#</c>)/
/// <c>startmonth</c>(<c>#</c>)/<c>firstissuenumber</c>/<c>lastissuenumber</c> tokens - concepts a single
/// <see cref="Issue"/> can't answer alone. Ported verbatim from CE's real <c>locommon.py</c>
/// <c>get_earliest_book</c>/<c>get_last_book</c> (re-verified once the extracted plugin source became
/// available again this session): both scan the ENTIRE library (matched by Publisher + series + volume,
/// our equivalent being <see cref="Issue.SeriesId"/> + <see cref="IssueMetadataExtensions.EffectiveVolume"/>
/// + <see cref="Issue.Publisher"/> - a real FK match, stricter and more reliable than CE's own
/// string-triple match), not just the books in the current organize batch - a prior pass's "batch-scoped
/// only" version was an honest, documented approximation made before this real source was available,
/// now corrected. <c>EndYear</c>/<c>EndMonth</c> deliberately come from the SAME lookup as
/// <c>LastIssueNumber</c> (CE's <c>get_last_book</c>, which selects purely by highest issue number, not
/// by latest date) - "the last book" and "the highest-numbered book" are the same CE concept, not two
/// different queries, even though they could diverge for an oddly-numbered special.
/// </summary>
public sealed record SeriesAggregate(
    int? StartYear,
    int? StartMonth,
    int? EndYear,
    int? EndMonth,
    string? FirstIssueNumber,
    string? LastIssueNumber);

/// <summary>
/// Everything a naming-template evaluation needs beyond the one <see cref="Issue"/> being named -
/// bundles <see cref="Aggregate"/> (series-level lookups) and <see cref="MonthNames"/> (CE's own
/// per-profile localized month-name table, <c>losettings.py</c>'s <c>Months</c> dict, English by
/// default) together, plus <see cref="AdvanceCounter"/>'s mutable running-counter state (CE's
/// <c>PathMaker._counter</c>) - a reference type, not a record, specifically so the SAME counter state
/// is shared and mutated across every <see cref="TemplateEvaluator.Evaluate"/> call made during one
/// organize run (every book, both the folder and file template), matching CE's own single
/// <c>PathMaker</c> instance living for the whole run and never resetting mid-batch.
/// </summary>
public sealed class TemplateContext
{
    private int? _counterValue;

    /// <summary>Mutable (not init-only) deliberately: one <see cref="TemplateContext"/> instance is
    /// shared across an entire organize run (see <see cref="AdvanceCounter"/>'s own doc comment for
    /// why), but which series' aggregate applies changes book-to-book - the caller updates this between
    /// issues rather than constructing a new context per issue, which would also (wrongly) reset the
    /// counter every time.</summary>
    public SeriesAggregate? Aggregate { get; set; }

    public IReadOnlyDictionary<int, string>? MonthNames { get; init; }

    /// <summary>
    /// Values a caller supplies for tokens an <see cref="Issue"/> cannot answer (the acquisition importer's <c>volumeyear</c>, and the downloaded
    /// file's own name for <c>filename</c>). Keys are token names; a missing key resolves to empty like any other absent field.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Extra { get; init; }

    /// <summary>CE's <c>insert_counter</c> (`lobookmover.py:1633`, verified): the FIRST call in this
    /// context's lifetime seeds the counter at <paramref name="start"/> and returns it as-is; every
    /// call after that adds <paramref name="increment"/> first, then returns the new value - padded
    /// left with zeros to <paramref name="pad"/> digits (0 = no padding).</summary>
    public string AdvanceCounter(int start, int increment, int pad)
    {
        _counterValue = _counterValue is null ? start : _counterValue.Value + increment;
        return pad > 0
            ? _counterValue.Value.ToString(CultureInfo.InvariantCulture).PadLeft(pad, '0')
            : _counterValue.Value.ToString(CultureInfo.InvariantCulture);
    }
}

public static class FieldResolvers
{
    /// <summary>CE's own default <c>Months</c> table (`losettings.py:70`, verified) - English month
    /// names 1-12 PLUS four season entries 13-16 (CE lets a book's own Month field carry a season
    /// instead of a calendar month, e.g. a "Winter 2020" annual) - user-editable per profile in CE
    /// (`configureform.py`'s month-name editor); Paperbunkr doesn't yet expose a per-profile editor for
    /// this pass, so every profile gets this same English default
    /// (<see cref="Paperbunkr.Data.Naming.TemplateContext.MonthNames"/>).</summary>
    public static readonly IReadOnlyDictionary<int, string> DefaultMonthNames = new Dictionary<int, string>
    {
        [1] = "January", [2] = "February", [3] = "March", [4] = "April", [5] = "May", [6] = "June",
        [7] = "July", [8] = "August", [9] = "September", [10] = "October", [11] = "November", [12] = "December",
        [13] = "Spring", [14] = "Summer", [15] = "Fall", [16] = "Winter",
    };

    /// <summary>CE's own leading-article list for <c>FirstLetter</c> (`lobookmover.py:1625`, verified) -
    /// English plus Dutch/German/French/Spanish/Portuguese/Italian articles, exactly as CE ships it.</summary>
    private static readonly Regex FirstLetterRegex = new(
        @"^(?:(?:the|a|an|de|het|een|die|der|das|des|dem|der|ein|eines|einer|einen|la|le|l'|les|un|une|el|las|los|las|un|una|unos|unas|o|os|um|uma|uns|umas|en|et|il|lo|uno|gli)\s+)?(?<letter>.).+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// CE's real `template_to_field` has ~50 entries (`lobookmover.py:1199`, verified against the
    /// extracted plugin source). Only the fields confirmed to have a real Paperbunkr equivalent are
    /// implemented here - an unsupported token name throws <see cref="NotSupportedException"/> at
    /// evaluation time (see <see cref="TemplateEvaluator"/>) rather than silently resolving to an empty
    /// string, so a template author discovers an unsupported token immediately instead of getting a
    /// silently wrong path - a deliberate Paperbunkr deviation from CE's own "leave the raw token text
    /// in place" behavior for an unrecognized name, not an oversight.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, TokenFieldResolver> ByName = Build();

    private static Dictionary<string, TokenFieldResolver> Build()
    {
        var map = new Dictionary<string, TokenFieldResolver>
        {
            ["series"] = (issue, _, _) => issue.Series?.Name,

            // Host-supplied (TemplateContext.Extra): not derivable from an Issue. "volumeyear" is the series' start year - constant across a
            // series' issues, so one series never splits across folders by each issue's own year; "filename" is the downloaded file's name
            // without its extension (the acquisition importer's only use).
            ["volumeyear"] = (_, _, context) => ExtraValue(context, "volumeyear"),
            ["filename"] = (_, _, context) => ExtraValue(context, "filename"),
            ["number"] = (issue, args, _) => PadNumeric(issue.EffectiveNumber(), args),
            ["count"] = (issue, args, _) => PadNumeric(issue.EffectiveCount()?.ToString(CultureInfo.InvariantCulture), args),
            ["year"] = (issue, args, _) => PadNumeric(issue.EffectiveYear()?.ToString(CultureInfo.InvariantCulture), args),

            // CE's real split (`lobookmover.py:1521`, verified): the UNPADDED "month" token is actually
            // the LOCALIZED MONTH NAME (`insert_month_as_name`, via the profile's own Months table), not
            // a number - a prior pass had this backwards (numeric "month", no "month#" at all). "month#"
            // is the one that's the plain/padded numeric month.
            ["month"] = (issue, _, context) => MonthName(issue.Month, context),
            ["month#"] = (issue, args, _) => PadNumeric(issue.Month?.ToString(CultureInfo.InvariantCulture), args),

            ["Day"] = (issue, args, _) => PadNumeric(issue.Day?.ToString(CultureInfo.InvariantCulture), args),
            ["volume"] = (issue, _, _) => issue.EffectiveVolume(),
            ["title"] = (issue, _, _) => issue.EffectiveTitle(),
            ["format"] = (issue, _, _) => issue.EffectiveFormat(),
            ["publisher"] = (issue, _, _) => issue.Publisher,
            ["imprint"] = (issue, _, _) => issue.Imprint,
            ["ageRating"] = (issue, _, _) => issue.AgeRating,
            ["language"] = (issue, _, _) => issue.LanguageISO,
            ["writer"] = (issue, _, _) => issue.Writer,
            ["penciller"] = (issue, _, _) => issue.Penciller,
            ["inker"] = (issue, _, _) => issue.Inker,
            ["colorist"] = (issue, _, _) => issue.Colorist,
            ["letterer"] = (issue, _, _) => issue.Letterer,
            ["coverartist"] = (issue, _, _) => issue.CoverArtist,
            ["editor"] = (issue, _, _) => issue.Editor,
            ["characters"] = (issue, _, _) => issue.Characters,
            ["teams"] = (issue, _, _) => issue.Teams,
            ["locations"] = (issue, _, _) => issue.Locations,
            ["storyarc"] = (issue, _, _) => issue.StoryArc,
            ["seriesgroup"] = (issue, _, _) => issue.SeriesGroup,
            ["maincharacter"] = (issue, _, _) => issue.MainCharacterOrTeam,
            ["scaninfo"] = (issue, _, _) => issue.ScanInformation,
            ["altSeries"] = (issue, _, _) => issue.AlternateSeries,
            ["altNumber"] = (issue, _, _) => issue.AlternateNumber,
            ["altCount"] = (issue, args, _) => PadNumeric(issue.AlternateCount?.ToString(CultureInfo.InvariantCulture), args),
            ["Rating"] = (issue, _, _) => issue.Rating?.ToString("0.##", CultureInfo.InvariantCulture),
            ["CommunityRating"] = (issue, _, _) => issue.CommunityRating?.ToString("0.##", CultureInfo.InvariantCulture),
            ["manga"] = (issue, args, _) => YesNo(issue.Series?.ContentType == ContentType.Manga, args),
            ["seriesComplete"] = (issue, args, _) => YesNo(issue.Series?.IsComplete ?? false, args),
            ["genre"] = (issue, args, _) => JoinTags(issue, IssueTagField.Genre, args),
            ["tags"] = (issue, args, _) => JoinTags(issue, IssueTagField.Tags, args),

            // Real Issue.ReleasedTime/AddedTime DateTime? fields. CE's own exact rendering format for a
            // bare date token isn't verified from source this session (insert_formated_datetime takes a
            // caller-supplied .NET format string as its OWN arg, which our args-string already threads
            // through the same way), so this formats as a plain ISO "yyyy-MM-dd" when no format arg is
            // given - a defensible, documented default, not a claimed CE-verified default.
            ["ReleasedDate"] = (issue, args, _) => FormatDate(issue.ReleasedTime, args),
            ["AddedDate"] = (issue, args, _) => FormatDate(issue.AddedTime, args),

            // Series-level (see SeriesAggregate's own doc comment - a real full-library lookup, ported
            // from CE's get_earliest_book/get_last_book).
            ["startyear"] = (_, _, context) => context?.Aggregate?.StartYear?.ToString(CultureInfo.InvariantCulture),
            ["startmonth"] = (_, _, context) => MonthName(context?.Aggregate?.StartMonth, context),
            ["startmonth#"] = (_, args, context) => PadNumeric(context?.Aggregate?.StartMonth?.ToString(CultureInfo.InvariantCulture), args),
            ["EndYear"] = (_, _, context) => context?.Aggregate?.EndYear?.ToString(CultureInfo.InvariantCulture),
            ["EndMonth"] = (_, _, context) => MonthName(context?.Aggregate?.EndMonth, context),
            ["EndMonth#"] = (_, args, context) => PadNumeric(context?.Aggregate?.EndMonth?.ToString(CultureInfo.InvariantCulture), args),
            ["firstissuenumber"] = (_, args, context) => PadNumeric(context?.Aggregate?.FirstIssueNumber, args),
            ["lastissuenumber"] = (_, args, context) => PadNumeric(context?.Aggregate?.LastIssueNumber, args),

            // CE's `FirstLetter(FieldName)` (`lobookmover.py:1612`, verified) - strips one of CE's own
            // leading articles (English/Dutch/German/French/Spanish/Portuguese/Italian) before taking
            // the first letter, capitalized. Resolves FieldName recursively through this same dictionary
            // (a Paperbunkr generalization - CE calls `getattr` directly on a fixed field, we reuse the
            // full resolver so `first` composes with every other token, not just a hardcoded subset).
            ["first"] = (issue, args, context) =>
            {
                string innerField = ExtractParenSegments(args).FirstOrDefault() ?? string.Empty;
                if (!ByName.TryGetValue(innerField, out TokenFieldResolver? innerResolver))
                {
                    throw new NotSupportedException($"<first(...)>'s inner field '{innerField}' is not a supported naming template token.");
                }

                string? innerValue = innerResolver(issue, string.Empty, context);
                if (string.IsNullOrEmpty(innerValue))
                {
                    return null;
                }

                Match match = FirstLetterRegex.Match(innerValue);
                return match.Success ? match.Groups["letter"].Value.ToUpperInvariant() : null;
            },

            // CE's `ReadPercentage` (`lobookmover.py:1584`, verified) - args are (text)(operator)(percent);
            // operator is exactly one of < > =. Paperbunkr has no stored ReadPercentage field - computed
            // from the real LastPageRead/PageCount fields the reader already maintains, rounded to the
            // nearest whole percent, matching the 0-100 int scale CE's own ComicBook.ReadPercentage uses.
            ["read"] = (issue, args, _) =>
            {
                List<string> segments = ExtractParenSegments(args);
                if (segments.Count != 3)
                {
                    return null;
                }

                int? percentage = ReadPercentage(issue);
                if (percentage is null || !int.TryParse(segments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int threshold))
                {
                    return null;
                }

                bool matches = segments[1] switch
                {
                    "=" => percentage == threshold,
                    ">" => percentage > threshold,
                    "<" => percentage < threshold,
                    _ => false,
                };

                return matches ? segments[0] : null;
            },

            // CE's `Counter` (`lobookmover.py:1633`, verified) - args are (start)(increment)(pad). No
            // batch context (e.g. a bare single-issue test) means no shared counter state exists to
            // advance, so this resolves to null rather than fabricating a fresh one-off counter every
            // call, which would defeat the whole point of it being a running count.
            ["counter"] = (issue, args, context) =>
            {
                List<string> segments = ExtractParenSegments(args);
                if (context is null || segments.Count != 3
                    || !int.TryParse(segments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)
                    || !int.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int increment)
                    || (segments[2].Length > 0 && !int.TryParse(segments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int _)))
                {
                    return null;
                }

                int pad = segments[2].Length > 0 ? int.Parse(segments[2], CultureInfo.InvariantCulture) : 0;
                return context.AdvanceCounter(start, increment, pad);
            },
        };

        return map;
    }

    private static string? ExtraValue(TemplateContext? context, string name) =>
        context?.Extra is { } extra && extra.TryGetValue(name, out string? value) ? value : null;

    private static string? MonthName(int? month, TemplateContext? context)
    {
        if (month is not int m)
        {
            return null;
        }

        IReadOnlyDictionary<int, string> names = context?.MonthNames ?? DefaultMonthNames;
        return names.TryGetValue(m, out string? name) ? name : null;
    }

    /// <summary>CE's `insert_formated_datetime` (`lobookmover.py:1728`, verified) takes an optional
    /// .NET-style format-string arg; falls back to a plain ISO date when none is given (see this
    /// dictionary's own `ReleasedDate`/`AddedDate` entries for why ISO, not a CE-verified default).</summary>
    private static string? FormatDate(DateTime? value, string args)
    {
        if (value is not DateTime dt)
        {
            return null;
        }

        string? format = ExtractParenSegments(args).FirstOrDefault();
        return string.IsNullOrEmpty(format) ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dt.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Real Issue.LastPageRead/PageCount fields - Paperbunkr's closest equivalent to CE's own
    /// ComicBook.ReadPercentage (a core-engine-computed 0-100 int this plugin has no direct access to).
    /// Null when either field is unknown, so `read`'s operator comparisons simply never match rather
    /// than guessing at a percentage from incomplete data.</summary>
    private static int? ReadPercentage(Issue issue)
    {
        if (issue.PageCount is not int pageCount || pageCount <= 0)
        {
            return null;
        }

        int lastPageRead = issue.LastPageRead ?? 0;
        return (int)Math.Round(lastPageRead * 100.0 / pageCount, MidpointRounding.AwayFromZero);
    }

    /// <summary>`Custom(key)` (design doc §5) - looks up <see cref="Issue.CustomValues"/> by name,
    /// Paperbunkr's real analog of CE's packed `Custom` token.</summary>
    public static string? ResolveCustom(Issue issue, string key) =>
        issue.CustomValues.FirstOrDefault(c => string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>`&lt;number2&gt;` = left-pad to 2 digits (design doc §5: "a bare digit-count arg, not
    /// printf-style `{Issue:000}`"). Only pads when the value parses as a plain non-negative integer;
    /// otherwise (e.g. an annotated issue number like "1A") returns it unchanged rather than guessing
    /// at partial-numeric padding rules CE itself handles with more nuance (auto-detecting width from
    /// the series' last issue) that this pass doesn't replicate - a per-issue resolver has no access
    /// to "the rest of the series" to auto-detect a width from.</summary>
    private static string? PadNumeric(string? value, string args)
    {
        if (string.IsNullOrEmpty(value) || !int.TryParse(args, out int width) || width <= 0)
        {
            return value;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')
            : value;
    }

    /// <summary>CE's yes/no token args: `(text)` for the "Yes" case, `(text)(!)` for the "No" case
    /// (design doc §5) - args here is the raw paren-args string as captured by the parser (e.g.
    /// `(Yes)` or `(Yes)(!)`), parsed minimally: the first parenthesised segment is the "true" text
    /// (default "Yes"), a second literal `(!)` segment is the "false" text (default "No").</summary>
    private static string YesNo(bool value, string args)
    {
        List<string> segments = ExtractParenSegments(args);
        string trueText = segments.Count > 0 ? segments[0] : "Yes";
        string falseText = segments.Count > 1 ? segments[1] : "No";
        return value ? trueText : falseText;
    }

    private static List<string> ExtractParenSegments(string args)
    {
        var segments = new List<string>();
        int depth = 0;
        int start = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == '(')
            {
                if (depth == 0)
                {
                    start = i + 1;
                }

                depth++;
            }
            else if (args[i] == ')')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    segments.Add(args[start..i]);
                    start = -1;
                }
            }
        }

        return segments;
    }

    /// <summary>Multi-value join for a real Paperbunkr collection field (<see cref="Issue.Tags"/>,
    /// filtered by <see cref="IssueTagField"/>) - CE's grammar carries `(separator)(series|issue)`
    /// args on these tokens (design doc §5); the separator is the first paren segment, default ", "
    /// if none given. The second arg (series-vs-issue scope) has no meaning for a per-issue resolver
    /// evaluating one book at a time, so it's accepted but ignored.</summary>
    private static string JoinTags(Issue issue, IssueTagField field, string args)
    {
        List<string> segments = ExtractParenSegments(args);
        string separator = segments.Count > 0 ? segments[0] : ", ";
        return string.Join(separator, issue.Tags.Where(t => t.Field == field).Select(t => t.Value));
    }
}
