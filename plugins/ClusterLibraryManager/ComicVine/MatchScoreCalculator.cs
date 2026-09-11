namespace ClusterLibraryManager.ComicVine;

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

    private static readonly char[] WordSeparators = { ' ', '-', '_', ':', ',', '.', '\'', '(', ')', '[', ']' };

    /// <param name="bookSeriesName">The book's parsed/effective series name.</param>
    /// <param name="bookFormat">The book's effective format (e.g. "TPB", "Annual") - CE folds this
    /// into the same word-bag as the series name for namescore.</param>
    /// <param name="bookIssueNumber">The book's parsed issue number, or null if unparseable.</param>
    /// <param name="bookYear">The book's effective year, or null if unknown.</param>
    /// <param name="candidate">The ComicVine volume being scored against the book.</param>
    /// <param name="wasPreviouslyChosenForSimilarBook">From <see cref="ComicVineMatchMemory"/> -
    /// true if this exact volume id was chosen before for a book with the same search key.</param>
    /// <param name="currentYear">Injected rather than read from the clock directly, so recency_score
    /// is deterministic in tests.</param>
    public static double Compute(
        string bookSeriesName,
        string? bookFormat,
        int? bookIssueNumber,
        int? bookYear,
        ComicVineVolumeSearchResult candidate,
        bool wasPreviouslyChosenForSimilarBook,
        int currentYear)
    {
        double score = 0;
        score += NameScore(bookSeriesName, bookFormat, candidate.Name);
        score += wasPreviouslyChosenForSimilarBook ? 7 : 0;
        score += PublisherScore(candidate.Publisher);
        score += BookScore(bookIssueNumber, candidate.CountOfIssues);
        score += YearScore(bookYear, ParseYear(candidate.StartYear));
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
        return combined.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).ToList();
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
    /// count_of_issues), else -100.
    ///
    /// Null-handling not directly verifiable from source in this pass (see this file's own class doc)
    /// - an unknown book issue number or unknown candidate count_of_issues is treated as neutral
    /// (100, i.e. don't penalize incomplete metadata) rather than guessing at CE's exact behavior for
    /// that case.
    /// </summary>
    private static double BookScore(int? bookIssueNumber, int? countOfIssues)
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

    private static double YearScore(int? bookYear, int? seriesYear)
    {
        if (!bookYear.HasValue)
        {
            return 0;
        }

        if (!seriesYear.HasValue)
        {
            return -100;
        }

        return seriesYear.Value > bookYear.Value ? -500 : 0;
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
