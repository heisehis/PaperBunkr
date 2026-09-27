using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>How much a detected role can be trusted: <see cref="High"/> is applied on its own (into a slot the user has not filled),
/// <see cref="Low"/> is only ever offered as a suggestion the user accepts or dismisses.</summary>
public enum RoleConfidence
{
    Low,
    High,
}

/// <summary>A role the detector thinks an issue plays in an arc or event, why, and how sure it is.</summary>
public sealed record RoleSuggestion(EventMembershipRole Role, RoleConfidence Confidence, string Reason);

/// <summary>
/// What the detector may look at for one member. <paramref name="Annotation"/> is a section heading or note a reading-order source carried
/// with the issue. <paramref name="DominantSeries"/> is the series most of the arc's members belong to (null when no series has a clear
/// majority); <paramref name="Position"/> / <paramref name="Total"/> place the member inside the arc.
/// </summary>
public sealed record MemberRoleFacts(
    string? Format,
    string? IssueTitle,
    string? SeriesName,
    string? Number,
    string? Annotation,
    string? DominantSeries,
    int Position,
    int Total,
    string? Summary = null);

/// <summary>
/// Suggests an <see cref="EventMembershipRole"/> for an issue in a reading list or story event
/// (docs/superpowers/specs/2026-09-25-reading-list-role-detection-design.md). No provider (ComicVine, Metron, the reading-order sites,
/// Wikidata) exposes a role, so this is inference from the words an issue and its source carry, plus one structural rule. Pure: no I/O.
/// <para>Precedence, strongest first: a source annotation or heading, the Format field, the issue's own title, the series name, then
/// the structural rule. Whole-word, case-insensitive matching ("Aftermath" does not match "Aftermathematics"). <c>Optional</c> is never
/// suggested - only the user marks an issue optional.</para>
/// </summary>
public static class MemberRoleDetector
{
    /// <summary>An arc needs at least this many members before "first/last of a different series" reads as a prologue/epilogue.</summary>
    private const int LongArcThreshold = 6;

    private static readonly (EventMembershipRole Role, Regex Pattern, string Word)[] Keywords =
    {
        (EventMembershipRole.Prologue, new Regex(@"\b(prologue|prelude)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "prologue/prelude"),
        (EventMembershipRole.Epilogue, new Regex(@"\bepilogue\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "epilogue"),
        (EventMembershipRole.Aftermath, new Regex(@"\baftermath\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "aftermath"),
        (EventMembershipRole.TieIn, new Regex(@"\btie[\s-]?ins?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "tie-in"),
    };

    private static readonly Regex MinusOne = new(@"\bminus[\s-]?1\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static RoleSuggestion? Detect(MemberRoleFacts facts)
    {
        // 1. A source annotation / section heading ("Tie-Ins", "Prelude") is the source's own statement about this issue.
        if (MatchKeyword(facts.Annotation) is { } annotationRole)
        {
            return new RoleSuggestion(annotationRole.Role, RoleConfidence.High, $"Source note: {Trim(facts.Annotation)}");
        }

        // 2. The Format field (the existing Prologue / Minus 1 / Epilogue catalog, plus the other role words).
        if (MatchKeyword(facts.Format) is { } formatRole)
        {
            return new RoleSuggestion(formatRole.Role, RoleConfidence.High, $"Format: {Trim(facts.Format)}");
        }

        if (facts.Format is not null && MinusOne.IsMatch(facts.Format))
        {
            return new RoleSuggestion(EventMembershipRole.Prologue, RoleConfidence.High, $"Format: {Trim(facts.Format)}");
        }

        // 3. The issue's own title, or an issue number of 0 / -1.
        if (MatchKeyword(facts.IssueTitle) is { } titleRole)
        {
            return new RoleSuggestion(titleRole.Role, RoleConfidence.High, $"Title contains \"{titleRole.Word}\"");
        }

        if (facts.Number is "0" or "-1")
        {
            return new RoleSuggestion(EventMembershipRole.Prologue, RoleConfidence.High, $"Issue number {facts.Number}");
        }

        // 3b. A provider's one-line summary of the issue (ComicVine's deck). It is prose about the issue, not a label - "leads into the epilogue"
        //     is not an epilogue - so a match is only ever a suggestion.
        if (MatchKeyword(facts.Summary) is { } summaryRole)
        {
            return new RoleSuggestion(summaryRole.Role, RoleConfidence.Low, $"Summary mentions \"{summaryRole.Word}\"");
        }

        // 4. The same words only in the series name: weaker, since unrelated series are called "Aftermath".
        if (MatchKeyword(facts.SeriesName) is { } seriesRole)
        {
            return new RoleSuggestion(seriesRole.Role, RoleConfidence.Low, $"Series name contains \"{seriesRole.Word}\"");
        }

        // 5. Structural: an issue from a different series than the rest of the arc is a tie-in; the first or last such issue of a
        //    long arc reads as a prologue or epilogue instead.
        if (facts.DominantSeries is { Length: > 0 } dominant
            && facts.SeriesName is { Length: > 0 } series
            && !string.Equals(series, dominant, StringComparison.OrdinalIgnoreCase))
        {
            if (facts.Total >= LongArcThreshold && facts.Position == 0)
            {
                return new RoleSuggestion(EventMembershipRole.Prologue, RoleConfidence.Low, $"First issue of the arc, from {series} rather than {dominant}");
            }

            if (facts.Total >= LongArcThreshold && facts.Position == facts.Total - 1)
            {
                return new RoleSuggestion(EventMembershipRole.Epilogue, RoleConfidence.Low, $"Last issue of the arc, from {series} rather than {dominant}");
            }

            return new RoleSuggestion(EventMembershipRole.TieIn, RoleConfidence.Low, $"From {series}, while most of the arc is {dominant}");
        }

        return null;
    }

    /// <summary>The series most members belong to, or null when no series has more than half of them (so a mixed arc never invents a "main" series).</summary>
    public static string? DominantSeries(IEnumerable<string?> seriesNames)
    {
        var names = seriesNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).ToList();
        if (names.Count == 0)
        {
            return null;
        }

        var top = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).First();
        return top.Count() * 2 > names.Count ? top.Key : null;
    }

    private static (EventMembershipRole Role, string Word)? MatchKeyword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach ((EventMembershipRole role, Regex pattern, string word) in Keywords)
        {
            if (pattern.IsMatch(text))
            {
                return (role, word);
            }
        }

        return null;
    }

    private static string Trim(string? text) => text is { Length: > 60 } ? text[..60] + "…" : text ?? string.Empty;
}
