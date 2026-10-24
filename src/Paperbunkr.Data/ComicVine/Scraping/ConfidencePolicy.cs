namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// One automatic cover check, as measured: the book's cover against the candidate (<see cref="Similarity"/>, null when it couldn't be measured),
/// the closest runner-up's art against the candidate's (<see cref="RunnerUp"/>, only for first or unnumbered issues), and Metron's published-hash
/// verdict when that decided instead (<see cref="PublishedHashMatch"/>).
/// </summary>
public sealed record CoverCheck(double? Similarity, double? RunnerUp, bool? PublishedHashMatch);

/// <summary>
/// What each Strictness level means for the unattended matcher (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md
/// section 1). Balanced is CE's own automatcher: the book's cover must be more than 0.87 similar to the candidate's, and the candidate must not
/// look more than 0.77 (0.87 less CE's 0.10 margin) like a runner-up. Lenient and Strict move both by 0.05, each in the cautious or the trusting direction.
/// </summary>
public static class ConfidencePolicy
{
    public const double RunnerUpMargin = 0.10;

    public static double CoverMatch(ScrapeStrictness strictness) => strictness switch
    {
        ScrapeStrictness.Lenient => 0.82,
        ScrapeStrictness.Strict => 0.92,
        _ => 0.87,
    };

    /// <summary>
    /// How alike the top candidate and a runner-up may look before the match is refused. Stricter means a lower limit (refuse sooner), so it moves
    /// opposite to <see cref="CoverMatch"/>; only Balanced is CE's own 0.87 - 0.10. Spelled out, since 0.87 - 0.10 is 0.7699999... in floating point.
    /// </summary>
    public static double RunnerUpLimit(ScrapeStrictness strictness) => strictness switch
    {
        ScrapeStrictness.Lenient => 0.82,
        ScrapeStrictness.Strict => 0.72,
        _ => 0.77,
    };

    /// <summary>
    /// The automatcher's verdict at <paramref name="strictness"/>: the top candidate must not look too much like a runner-up, and the book's cover
    /// must match it - by Metron's published hash when there is one, else by similarity. Nothing measurable (no cover) never passes.
    /// </summary>
    public static bool Passes(ScrapeStrictness strictness, CoverCheck check)
    {
        if (check.RunnerUp is double runnerUp && runnerUp > RunnerUpLimit(strictness))
        {
            return false;
        }

        if (check.PublishedHashMatch is bool published)
        {
            return published;
        }

        return check.Similarity is double similarity && similarity > CoverMatch(strictness);
    }

    public static string Describe(ScrapeStrictness strictness) => strictness switch
    {
        ScrapeStrictness.Lenient => "Applies more matches on its own; a few may need correcting.",
        ScrapeStrictness.Strict => "Applies only near-certain matches; more comics wait for review.",
        _ => "ComicRack's own rule: applies a match when the covers agree.",
    };
}
