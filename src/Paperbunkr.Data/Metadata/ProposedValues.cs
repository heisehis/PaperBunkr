using System.Globalization;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// "Proposed values" as ComicRack's <c>Proposed Values</c> script and its <c>Commit Proposed Values</c> command mean them
/// (docs/superpowers/specs/2026-10-11-proposed-values-design.md): a value that was worked out (from the file name) and is in effect through
/// <c>Effective*()</c>, but is not written into the book's own field. Here that is an <see cref="MetadataProposalStatus.Accepted"/>
/// <see cref="MetadataProposal"/> for an issue-level field whose stored value is empty. Pending proposals are not counted: they are not in effect, and writing them out
/// would skip the review the user chose.
/// </summary>
public static class ProposedValues
{
    /// <summary>The issue-level fields a proposal can fill, in the order CE's script lists them.</summary>
    public static readonly IReadOnlyList<MetadataProposalField> CommittableFields =
    [
        MetadataProposalField.Title,
        MetadataProposalField.Format,
        MetadataProposalField.Volume,
        MetadataProposalField.Number,
        MetadataProposalField.Count,
        MetadataProposalField.Year,
    ];

    /// <summary>
    /// The fields named in a comma/semicolon list (<c>"number, year"</c>), case-insensitively. Names that are not committable (including CE's <c>series</c>) are ignored, and
    /// a list with no valid name means every field, as the script does.
    /// </summary>
    public static IReadOnlyList<MetadataProposalField> ParseFields(string? list)
    {
        var named = new HashSet<MetadataProposalField>();
        foreach (string token in (list ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse(token, ignoreCase: true, out MetadataProposalField field) && CommittableFields.Contains(field))
            {
                named.Add(field);
            }
        }

        return named.Count == 0 ? CommittableFields : CommittableFields.Where(named.Contains).ToList();
    }

    /// <summary>The (field, value) pairs among <paramref name="fields"/> that <paramref name="issue"/> has a proposed value for. Needs <see cref="Issue.MetadataProposals"/> loaded.</summary>
    public static IReadOnlyList<(MetadataProposalField Field, string Value)> Pending(Issue issue, IReadOnlyCollection<MetadataProposalField> fields)
    {
        var result = new List<(MetadataProposalField, string)>();
        foreach (MetadataProposalField field in fields)
        {
            if (!StoredValueIsEmpty(issue, field))
            {
                continue;
            }

            string? value = issue.MetadataProposals
                .Where(p => p.Field == field && p.Status == MetadataProposalStatus.Accepted && !string.IsNullOrWhiteSpace(p.ProposedValue))
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefault()?.ProposedValue;
            if (value is not null)
            {
                result.Add((field, value));
            }
        }

        return result;
    }

    public static bool Has(Issue issue, IReadOnlyCollection<MetadataProposalField> fields) => Pending(issue, fields).Count > 0;

    /// <summary>Writes every proposed value among <paramref name="fields"/> into the issue's own field and returns how many it set. A Count or Year that is not a whole number is skipped.</summary>
    public static int Commit(Issue issue, IReadOnlyCollection<MetadataProposalField> fields) => Apply(issue, Pending(issue, fields));

    /// <summary>
    /// Writes values already worked out by <see cref="Pending"/> into <paramref name="issue"/>'s own fields. Split from <see cref="Commit"/> because an editor that records an undo step
    /// must load the issue <em>without</em> its proposals (with them loaded, <c>Effective*()</c> already reads the value, so a before/after snapshot would see no change), and so
    /// gets the pending values from a separate read.
    /// </summary>
    public static int Apply(Issue issue, IReadOnlyList<(MetadataProposalField Field, string Value)> pending)
    {
        int set = 0;
        foreach ((MetadataProposalField field, string value) in pending)
        {
            switch (field)
            {
                case MetadataProposalField.Title:
                    issue.Title = value;
                    break;
                case MetadataProposalField.Format:
                    issue.Format = value;
                    break;
                case MetadataProposalField.Volume:
                    issue.Volume = value;
                    break;
                case MetadataProposalField.Number:
                    issue.Number = value;
                    break;
                case MetadataProposalField.Count when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count):
                    issue.Count = count;
                    break;
                case MetadataProposalField.Year when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year):
                    issue.Year = year;
                    break;
                default:
                    continue;
            }

            set++;
        }

        return set;
    }

    private static bool StoredValueIsEmpty(Issue issue, MetadataProposalField field) => field switch
    {
        MetadataProposalField.Title => string.IsNullOrWhiteSpace(issue.Title),
        MetadataProposalField.Format => string.IsNullOrWhiteSpace(issue.Format),
        MetadataProposalField.Volume => string.IsNullOrWhiteSpace(issue.Volume),
        MetadataProposalField.Number => string.IsNullOrWhiteSpace(issue.Number),
        MetadataProposalField.Count => issue.Count is null,
        MetadataProposalField.Year => issue.Year is null,
        _ => false,
    };
}
