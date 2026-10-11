using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>A genre read out of a synopsis, with how sure the keyword match is (0.6-0.8) and the words that matched.</summary>
public sealed record GenreSuggestion(string Genre, decimal Confidence, IReadOnlyList<string> MatchedKeywords);

/// <summary>What one run of <see cref="SynopsisGenreInferrer.SuggestForLibrary"/> did.</summary>
public sealed record GenreSuggestRunResult(int Checked, int Suggested, int Remaining)
{
    public string Summary => Suggested == 0
        ? (Checked == 0 ? "No series without a genre to look at." : $"Looked at {Checked:N0} series; no genre could be read from their synopses.")
        : $"Suggested a genre for {Suggested:N0} of {Checked:N0} series. Review them in Library Health."
          + (Remaining > 0 ? $" {Remaining:N0} more to look at next run." : string.Empty);
}

/// <summary>
/// Suggests a genre from a series' synopsis by keyword (docs/superpowers/specs/2026-10-06-smart-features-design.md §6.2). No model and
/// no network: a curated table of genre words, and a genre is only suggested when at least two different words for it appear. Only for
/// series that have a synopsis but no genre from anywhere else, and only ever as a Pending proposal for the user to accept - it never
/// applies itself, whatever the auto-apply policy says. Deliberately no content warnings: a keyword match is far too weak a basis.
/// </summary>
public static class SynopsisGenreInferrer
{
    /// <summary>
    /// The source these proposals are filed under. Not <see cref="MetadataProposalSource.AI"/>: this is a keyword table, and the label
    /// is shown to the user in the review queue. A synopsis suggestion is recognised by <see cref="IsSynopsisSuggestion"/>.
    /// </summary>
    public const MetadataProposalSource Source = MetadataProposalSource.Other;

    public const int MinDistinctKeywords = 2;

    /// <summary>Series looked at per scheduled run.</summary>
    public const int ScheduledBudget = 200;

    private static readonly IReadOnlyDictionary<string, string[]> Keywords = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Superhero"] = ["superhero", "superheroes", "superpowers", "supervillain", "vigilante", "mutant", "mutants", "caped", "secret identity", "masked hero"],
        ["Horror"] = ["horror", "haunted", "ghost", "ghosts", "zombie", "zombies", "vampire", "vampires", "demon", "demons", "terrifying", "nightmare", "undead", "monster", "monsters"],
        ["Science Fiction"] = ["spaceship", "starship", "galaxy", "alien", "aliens", "robot", "robots", "android", "cyborg", "interstellar", "dystopian", "time travel", "futuristic", "space station", "planet"],
        ["Fantasy"] = ["magic", "wizard", "wizards", "sorcerer", "sorceress", "dragon", "dragons", "kingdom", "elf", "elves", "sword", "enchanted", "realm", "mage", "prophecy"],
        ["Crime"] = ["detective", "murder", "heist", "gangster", "mafia", "mob", "criminal", "crime", "police", "investigation", "noir", "killer", "serial killer"],
        ["Mystery"] = ["mystery", "mysterious", "whodunit", "clue", "clues", "disappearance", "unsolved", "secret", "secrets", "investigates", "puzzle"],
        ["Romance"] = ["romance", "romantic", "love story", "falls in love", "falling in love", "heartbreak", "lovers", "crush", "wedding", "boyfriend", "girlfriend"],
        ["Comedy"] = ["comedy", "hilarious", "funny", "humor", "humour", "gag", "slapstick", "antics", "misadventures", "satire", "parody"],
        ["Action"] = ["battle", "battles", "fight", "fights", "fighting", "explosive", "assassin", "mercenary", "showdown", "combat", "martial arts"],
        ["Adventure"] = ["adventure", "adventures", "quest", "journey", "expedition", "treasure", "explorer", "explorers", "voyage", "uncharted"],
        ["Western"] = ["cowboy", "cowboys", "gunslinger", "outlaw", "outlaws", "sheriff", "frontier", "wild west", "saloon"],
        ["War"] = ["war", "soldier", "soldiers", "battlefield", "platoon", "trenches", "world war", "army", "invasion", "military"],
        ["Historical"] = ["historical", "medieval", "victorian", "ancient rome", "samurai", "feudal", "dynasty", "century", "empire"],
        ["Sports"] = ["tournament", "championship", "team", "coach", "baseball", "basketball", "soccer", "football", "boxing", "volleyball", "athlete"],
        ["Slice of Life"] = ["everyday life", "daily life", "slice of life", "high school", "classmates", "friendship", "coming of age", "neighbours", "neighbors"],
        ["Thriller"] = ["thriller", "conspiracy", "hunted", "on the run", "deadly", "espionage", "spy", "spies", "hostage", "betrayal"],
        ["Supernatural"] = ["supernatural", "spirit", "spirits", "occult", "curse", "cursed", "exorcist", "paranormal", "possessed", "witch", "witches"],
        ["Post-Apocalyptic"] = ["apocalypse", "post-apocalyptic", "wasteland", "survivors", "outbreak", "collapse of civilization", "ruins", "fallout"],
        ["Mecha"] = ["mecha", "giant robot", "giant robots", "pilot", "pilots", "mobile suit"],
        ["Isekai"] = ["another world", "reincarnated", "reincarnation", "transported to", "summoned to", "isekai"],
    };

    private static readonly IReadOnlyDictionary<string, Regex[]> Patterns = Keywords.ToDictionary(
        kv => kv.Key,
        kv => kv.Value.Select(w => new Regex($@"\b{Regex.Escape(w)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)).ToArray(),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>The best-matching genre, or null when no genre has <see cref="MinDistinctKeywords"/> different words in the text.</summary>
    public static GenreSuggestion? Infer(string? synopsis)
    {
        if (string.IsNullOrWhiteSpace(synopsis))
        {
            return null;
        }

        GenreSuggestion? best = null;
        foreach (var (genre, patterns) in Patterns)
        {
            var matched = new List<string>();
            for (int i = 0; i < patterns.Length; i++)
            {
                if (patterns[i].IsMatch(synopsis))
                {
                    matched.Add(Keywords[genre][i]);
                }
            }

            if (matched.Count >= MinDistinctKeywords && (best is null || matched.Count > best.MatchedKeywords.Count))
            {
                best = new GenreSuggestion(genre, ConfidenceFor(matched.Count), matched);
            }
        }

        return best;
    }

    /// <summary>Two words 0.6, three 0.7, four or more 0.8 - never higher: it is still only a keyword match.</summary>
    internal static decimal ConfidenceFor(int distinctKeywords) => Math.Clamp(0.4m + 0.1m * distinctKeywords, 0.6m, 0.8m);

    /// <summary>
    /// Looks at up to <paramref name="budget"/> series that have a synopsis, no genre of their own, no genre on any of their issues, and
    /// no genre proposal already (in any status - a rejected suggestion is not made again), and adds a Pending series-scoped
    /// <see cref="MetadataProposalField.Genre"/> proposal for each one a genre can be read from.
    /// </summary>
    public static GenreSuggestRunResult SuggestForLibrary(PaperbunkrDbContext context, int budget = ScheduledBudget)
    {
        var alreadyProposed = context.MetadataProposals
            .Where(p => p.SeriesId != null && p.Field == MetadataProposalField.Genre)
            .Select(p => p.SeriesId!.Value)
            .Distinct()
            .ToHashSet();
        var seriesWithIssueGenre = context.Set<IssueTag>()
            .Where(t => t.Field == IssueTagField.Genre)
            .Select(t => t.Issue!.SeriesId)
            .Distinct()
            .ToHashSet();

        var candidates = context.Series
            .Where(s => (s.Genre == null || s.Genre == "") && s.Summary != null && s.Summary != "")
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.Summary })
            .ToList()
            .Where(s => !alreadyProposed.Contains(s.Id) && !seriesWithIssueGenre.Contains(s.Id))
            .ToList();

        int suggested = 0;
        var now = DateTime.UtcNow;
        foreach (var candidate in candidates.Take(budget))
        {
            if (Infer(candidate.Summary) is not { } suggestion)
            {
                continue;
            }

            context.MetadataProposals.Add(new MetadataProposal
            {
                SeriesId = candidate.Id,
                Field = MetadataProposalField.Genre,
                CurrentValue = null,
                ProposedValue = suggestion.Genre,
                Source = Source,
                Confidence = suggestion.Confidence,
                Status = MetadataProposalStatus.Pending,
                CreatedAt = now,
            });
            suggested++;
        }

        context.SaveChanges();
        int looked = Math.Min(budget, candidates.Count);
        // A series nothing could be read from has no proposal row to mark it as looked at, so it is looked at again next run; the
        // lookup is a few regexes over one string, so that costs nothing worth a column.
        return new GenreSuggestRunResult(looked, suggested, Math.Max(0, candidates.Count - looked));
    }

    /// <summary>True for a proposal this class created (a pending or resolved synopsis genre suggestion).</summary>
    public static bool IsSynopsisSuggestion(MetadataProposal proposal) =>
        proposal.SeriesId is not null && proposal.Field == MetadataProposalField.Genre && proposal.Source == Source;
}
