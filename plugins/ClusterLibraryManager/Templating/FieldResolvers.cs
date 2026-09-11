using System.Globalization;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace ClusterLibraryManager.Templating;

/// <summary>Resolves one CE token name to a raw field value on an <see cref="Issue"/>. Design doc §5:
/// routed through <c>Effective*</c> extension methods (Paperbunkr's own filename-fallback layer,
/// backed by the auditable <c>MetadataProposals</c> table) rather than raw <see cref="Issue"/>
/// properties, for the fields that have an Effective* equivalent.</summary>
public delegate string? TokenFieldResolver(Issue issue, string args);

public static class FieldResolvers
{
    /// <summary>
    /// CE's real `template_to_field` has ~50 entries (design doc §5). Only the fields confirmed to
    /// have a real Paperbunkr equivalent are implemented here - an unsupported token name throws
    /// <see cref="NotSupportedException"/> at evaluation time (see <see cref="TemplateEvaluator"/>)
    /// rather than silently resolving to an empty string, so a template author discovers an
    /// unsupported token immediately instead of getting a silently wrong path. Not implemented in
    /// this pass, each for a specific reason:
    /// - `altCount`, `firstissuenumber`, `lastissuenumber` - no confirmed Issue field.
    /// - `first` (CE's `FirstLetter(FieldName)`) - needs a second field-name argument resolved
    ///   recursively; a real feature, just not built yet.
    /// - `read` (CE's read-percentage-as-conditional-text) - needs its own operator/threshold parsing.
    /// - `counter` - needs batch-wide state (a running count across an organize run), which a
    ///   per-issue field resolver can't provide; belongs at `LibraryOrganizerService` level.
    /// - `ReleasedDate`, `AddedDate`, `EndYear`, `EndMonth`(`#`), `startmonth`(`#`) - CE's own
    ///   date-part tokens beyond plain `year`/`month`/`Day` (which ARE implemented, against the real
    ///   confirmed `Issue.Month`/`Issue.Day` int fields - CE's own default template needs `month`
    ///   directly); "release date" and "added date" as distinct concepts weren't confirmed against a
    ///   real Issue field in this pass.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, TokenFieldResolver> ByName = Build();

    private static Dictionary<string, TokenFieldResolver> Build()
    {
        var map = new Dictionary<string, TokenFieldResolver>
        {
            ["series"] = (issue, _) => issue.Series?.Name,
            ["number"] = (issue, args) => PadNumeric(issue.EffectiveNumber(), args),
            ["count"] = (issue, args) => PadNumeric(issue.EffectiveCount()?.ToString(CultureInfo.InvariantCulture), args),
            ["year"] = (issue, _) => issue.EffectiveYear()?.ToString(CultureInfo.InvariantCulture),
            ["month"] = (issue, _) => issue.Month?.ToString(CultureInfo.InvariantCulture),
            ["Day"] = (issue, _) => issue.Day?.ToString(CultureInfo.InvariantCulture),
            // Approximation, not a verified CE-equivalent mapping: Paperbunkr's Series entity has no
            // stored start-year field (confirmed during the design pass) - a true "series' earliest
            // publication year" needs a cross-issue query this per-issue resolver can't do alone.
            // Falls back to this issue's own effective year instead of throwing, since a template
            // using {<startyear>} on a single book still wants a plausible year in the path.
            ["startyear"] = (issue, _) => issue.EffectiveYear()?.ToString(CultureInfo.InvariantCulture),
            ["volume"] = (issue, _) => issue.EffectiveVolume(),
            ["title"] = (issue, _) => issue.EffectiveTitle(),
            ["format"] = (issue, _) => issue.EffectiveFormat(),
            ["publisher"] = (issue, _) => issue.Publisher,
            ["imprint"] = (issue, _) => issue.Imprint,
            ["ageRating"] = (issue, _) => issue.AgeRating,
            ["language"] = (issue, _) => issue.LanguageISO,
            ["writer"] = (issue, _) => issue.Writer,
            ["penciller"] = (issue, _) => issue.Penciller,
            ["inker"] = (issue, _) => issue.Inker,
            ["colorist"] = (issue, _) => issue.Colorist,
            ["letterer"] = (issue, _) => issue.Letterer,
            ["coverartist"] = (issue, _) => issue.CoverArtist,
            ["editor"] = (issue, _) => issue.Editor,
            ["characters"] = (issue, _) => issue.Characters,
            ["teams"] = (issue, _) => issue.Teams,
            ["locations"] = (issue, _) => issue.Locations,
            ["storyarc"] = (issue, _) => issue.StoryArc,
            ["seriesgroup"] = (issue, _) => issue.SeriesGroup,
            ["maincharacter"] = (issue, _) => issue.MainCharacterOrTeam,
            ["scaninfo"] = (issue, _) => issue.ScanInformation,
            ["altSeries"] = (issue, _) => issue.AlternateSeries,
            ["altNumber"] = (issue, _) => issue.AlternateNumber,
            ["Rating"] = (issue, _) => issue.Rating?.ToString("0.##", CultureInfo.InvariantCulture),
            ["CommunityRating"] = (issue, _) => issue.CommunityRating?.ToString("0.##", CultureInfo.InvariantCulture),
            ["manga"] = (issue, args) => YesNo(issue.Series?.ContentType == ContentType.Manga, args),
            ["seriesComplete"] = (issue, args) => YesNo(issue.Series?.IsComplete ?? false, args),
            ["genre"] = (issue, args) => JoinTags(issue, IssueTagField.Genre, args),
            ["tags"] = (issue, args) => JoinTags(issue, IssueTagField.Tags, args),
        };

        return map;
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
