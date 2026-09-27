using System.Text.RegularExpressions;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// Ports CE's <c>matchscore.py</c> formula verbatim (design doc §4) - six independent terms summed,
/// each verified directly against the extracted CE source during the original design pass. Not a
/// single "similarity ratio" - a hand-rolled bag-of-words score plus heuristic bonuses/penalties,
/// exactly as CE has it.
/// </summary>
public static class MatchScoreCalculator
{
    // CE's hardcoded mirror/reprint-publisher list (matchscore.py, verified): substring match for
    // these two, exact match for the rest.
    private static readonly string[] MirrorPublisherSubstrings = { "panini", "deagostina" };
    private static readonly string[] MirrorPublisherExact = { "marvel italia", "marvel uk", "semic_as", "abril" };

    // CE tokenizes on \W+ (any non-word character - matchscore.py:45, verified directly against
    // source), which treats '_' as a word character and does NOT split on it - unlike the earlier
    // curated separator list here, which did. Apostrophes are stripped before tokenizing instead of
    // being a split point (matchscore.py:44: "don't" -> "dont", one word, not "don"/"t" two words).
    private static readonly Regex WordSplitter = new(@"[^\w]+", RegexOptions.Compiled);

    // CE's giant/king-sized and one-shot canonicalization (matchscore.py:46-48, verified), applied
    // before tokenizing so "Giant-Sized X-Men" and "Giant Size X-Men" namescore-match at full weight.
    private static readonly Regex GiantSized = new(@"giant[- ]*sized?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KingSized = new(@"king[- ]*sized?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OneShot = new(@"one[- ]*shot", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="bookSeriesName">The book's parsed/effective series name.</param>
    /// <param name="bookFormat">The book's effective format (e.g. "TPB", "Annual") - CE folds this
    /// into the same word-bag as the series name for namescore.</param>
    /// <param name="bookIssueNumberRaw">The book's raw issue number string (e.g. "5.5", "01"), or null
    /// if unknown. CE stores issue numbers as plain strings everywhere except this one scoring call,
    /// where it parses a float (matchscore.py:87-92, verified directly against source) - this stays a
    /// raw string end-to-end rather than a pre-parsed int, so fractional numbers (annuals, X.5 issues)
    /// score correctly instead of falling back to the neutral case an int-only parse would hit.</param>
    /// <param name="bookYear">The book's effective year, or null if unknown.</param>
    /// <param name="candidate">The ComicVine volume being scored against the book.</param>
    /// <param name="wasPreviouslyChosenForSimilarBook">From <see cref="ComicVineMatchMemory"/> -
    /// true if this exact volume id was chosen before for a book with the same search key.</param>
    /// <param name="currentYear">Injected rather than read from the clock directly, so recency_score
    /// is deterministic in tests.</param>
    public static double Compute(
        string bookSeriesName,
        string? bookFormat,
        string? bookIssueNumberRaw,
        int? bookYear,
        ComicVineVolumeSearchResult candidate,
        bool wasPreviouslyChosenForSimilarBook,
        int currentYear)
    {
        double score = 0;
        score += NameScore(bookSeriesName, bookFormat, candidate.Name);
        score += wasPreviouslyChosenForSimilarBook ? 7 : 0;
        score += PublisherScore(candidate.Publisher);
        score += BookScore(ParseIssueNumberForScoring(bookIssueNumberRaw), candidate.CountOfIssues);
        score += YearScore(bookYear, ParseYear(candidate.StartYear), currentYear);
        score += RecencyScore(ParseYear(candidate.StartYear), currentYear);
        return score;
    }

    /// <summary>+5 per book word matched against the candidate's word bag (each candidate word
    /// consumable only once, so it can't double-count), -1 per unmatched book word, and
    /// -(unmatched candidate words remaining) at the end.</summary>
    private static double NameScore(string bookSeriesName, string? bookFormat, string? candidateName)
    {
        List<string> bookWords = Tokenize(bookSeriesName, bookFormat);
        List<string> candidateWords = Tokenize(candidateName, null);

        double score = 0;
        foreach (string bookWord in bookWords)
        {
            int index = candidateWords.FindIndex(w => string.Equals(w, bookWord, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                score += 5;
                candidateWords.RemoveAt(index);
            }
            else
            {
                score -= 1;
            }
        }

        score -= candidateWords.Count;
        return score;
    }

    private static List<string> Tokenize(string? text, string? extra)
    {
        string combined = string.IsNullOrEmpty(extra) ? text ?? string.Empty : $"{text} {extra}";
        combined = combined.Replace("'", string.Empty);
        combined = GiantSized.Replace(combined, "giant size");
        combined = KingSized.Replace(combined, "king size");
        combined = OneShot.Replace(combined, "one shot");
        return WordSplitter.Split(combined).Where(w => w.Length > 0).ToList();
    }

    private static double PublisherScore(string? publisher)
    {
        if (string.IsNullOrEmpty(publisher))
        {
            return 0;
        }

        string lower = publisher.Trim().ToLowerInvariant();
        if (MirrorPublisherSubstrings.Any(lower.Contains))
        {
            return -6;
        }

        return MirrorPublisherExact.Contains(lower) ? -6 : 0;
    }

    /// <summary>
    /// A series with more than 100 issues is always treated as compatible (CE's own rationale,
    /// verified: long-running series are assumed compatible and CV's issue count is often stale for
    /// them). Otherwise 100 if the book's issue number could plausibly belong (issueNumber - 1 &lt;=
    /// count_of_issues), else -100. <paramref name="bookIssueNumber"/> is a double (not int) so
    /// fractional numbers like 5.5 (annuals, half-numbered issues) score correctly (matchscore.py:87-92,
    /// verified) instead of falling into the neutral unparseable case.
    ///
    /// Null-handling not directly verifiable from source in this pass (see this file's own class doc)
    /// - an unknown book issue number or unknown candidate count_of_issues is treated as neutral
    /// (100, i.e. don't penalize incomplete metadata) rather than guessing at CE's exact behavior for
    /// that case.
    /// </summary>
    private static double BookScore(double? bookIssueNumber, int? countOfIssues)
    {
        if (!bookIssueNumber.HasValue || !countOfIssues.HasValue)
        {
            return 100;
        }

        if (countOfIssues.Value > 100)
        {
            return 100;
        }

        return bookIssueNumber.Value - 1 <= countOfIssues.Value ? 100 : -100;
    }

    /// <summary>CE parses the issue number as a float only for this scoring call, nowhere else
    /// (confirmed directly against source: <c>bookdata.py</c>/<c>dbmodels.py</c>/<c>comicbook.py</c> all
    /// carry it as a plain string; only <c>matchscore.py:87-92</c> ever floats it) - strips everything
    /// but digits/period/minus before parsing, matching CE's own regex-then-float approach exactly.</summary>
    private static double? ParseIssueNumberForScoring(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string stripped = Regex.Replace(raw, @"[^0-9.\-]", string.Empty);
        return double.TryParse(stripped, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n) ? n : null;
    }

    /// <summary>CE validates both years through <c>is_valid_year_b(y) = 1900 &lt; y &lt;= currentYear+1</c>
    /// (matchscore.py:110, verified) before applying the -100/-500 logic - an out-of-range year (e.g.
    /// garbage like 31337, or a plainly-wrong 50) is treated as absent rather than as a real signal.
    /// <paramref name="currentYear"/> is the same injected value <see cref="Compute"/> already threads
    /// into <see cref="RecencyScore"/>, kept deterministic for tests rather than reading the clock here.</summary>
    private static double YearScore(int? bookYear, int? seriesYear, int currentYear)
    {
        bool IsValidYear(int y) => y > 1900 && y <= currentYear + 1;

        int? validBookYear = bookYear is int by && IsValidYear(by) ? by : null;
        int? validSeriesYear = seriesYear is int sy && IsValidYear(sy) ? sy : null;

        if (!validBookYear.HasValue)
        {
            return 0;
        }

        if (!validSeriesYear.HasValue)
        {
            return -100;
        }

        return validSeriesYear.Value > validBookYear.Value ? -500 : 0;
    }

    private static double RecencyScore(int? seriesYear, int currentYear) =>
        seriesYear.HasValue ? -(currentYear - seriesYear.Value) / 100.0 : -1.0;

    /// <summary>CE strips trailing "- " from a volume's start_year (cvdb.py, verified) before
    /// treating it as a plain integer string - mirrored here.</summary>
    private static int? ParseYear(string? startYear)
    {
        if (string.IsNullOrWhiteSpace(startYear))
        {
            return null;
        }

        string trimmed = startYear.TrimEnd('-', ' ');
        return int.TryParse(trimmed, out int year) ? year : null;
    }
}
