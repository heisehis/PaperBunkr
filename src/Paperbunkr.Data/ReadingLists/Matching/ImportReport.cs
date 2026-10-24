namespace Paperbunkr.Data.ReadingLists.Matching;

/// <summary>
/// What became of every entry of an import or refresh (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.7). Nothing is
/// dropped without being counted: <see cref="Parsed"/> always equals the five outcome counts added up, and anything the source could not
/// deliver is named in <see cref="Skipped"/>.
/// </summary>
/// <param name="Reported">How many entries the source itself says there are, when it says.</param>
/// <param name="Parsed">Entries read from the file or source.</param>
/// <param name="MatchedById">Linked through a provider id the source supplied.</param>
/// <param name="MatchedByMetadata">Linked by series, number and whatever else was given.</param>
/// <param name="NeedsChoice">The library has candidates but the source did not say enough to pick; a placeholder stands in until the user does.</param>
/// <param name="Missing">Nothing in the library fits; a placeholder stands in.</param>
/// <param name="Duplicates">Entries that resolved to a comic an earlier entry already took (a list holds a comic once).</param>
/// <param name="Skipped">Lines or ids the source could not turn into an entry, as text.</param>
public sealed record ImportReport(
    int? Reported,
    int Parsed,
    int MatchedById,
    int MatchedByMetadata,
    int NeedsChoice,
    int Missing,
    IReadOnlyList<string> Duplicates,
    IReadOnlyList<string> Skipped)
{
    public int Matched => MatchedById + MatchedByMetadata;

    /// <summary>The source says it has more entries than were read or accounted for as skipped.</summary>
    public bool Incomplete => Reported is int reported && reported > Parsed + Skipped.Count;

    /// <summary>"42 matched (30 by id), 3 need a choice, 5 missing, 1 duplicate, 2 skipped." Zero counts are left out.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>
            {
                MatchedById > 0 && MatchedByMetadata > 0 ? $"{Matched} matched ({MatchedById} by id)"
                    : MatchedById > 0 ? $"{Matched} matched by id"
                    : $"{Matched} matched",
            };
            Add(parts, NeedsChoice, NeedsChoice == 1 ? "needs a choice" : "need a choice");
            Add(parts, Missing, "missing");
            Add(parts, Duplicates.Count, Duplicates.Count == 1 ? "duplicate" : "duplicates");
            Add(parts, Skipped.Count, "skipped");

            string text = string.Join(", ", parts) + ".";
            if (Incomplete)
            {
                text += $" Incomplete: the source lists {Reported} entries and {Parsed + Skipped.Count} were received.";
            }

            return text;
        }
    }

    /// <summary>The names behind the duplicate and skipped counts, a few of each - for a status line or an alert.</summary>
    public string? Details(int most = 3)
    {
        var lines = new List<string>();
        Name(lines, "Duplicate", Duplicates, most);
        Name(lines, "Skipped", Skipped, most);
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static void Add(List<string> parts, int count, string what)
    {
        if (count > 0)
        {
            parts.Add($"{count} {what}");
        }
    }

    private static void Name(List<string> lines, string label, IReadOnlyList<string> names, int most)
    {
        lines.AddRange(names.Take(most).Select(n => $"{label}: {n}"));
        if (names.Count > most)
        {
            lines.Add($"…and {names.Count - most} more {label.ToLowerInvariant()}.");
        }
    }
}
