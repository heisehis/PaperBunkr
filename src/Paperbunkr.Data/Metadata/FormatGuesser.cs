using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One starter rule: when <see cref="Pattern"/> is found in a book's series name, title or file name, its format is <see cref="Format"/>.</summary>
public sealed record FormatRule(string Format, Regex Pattern);

/// <summary>
/// Guesses a book's Format from its own names (docs/superpowers/specs/2026-10-11-format-presets-design.md): the generic part of the Data Manager rule set this came from
/// ("Annual" in the series, title or file name means Annual; "Director's Cut" in the series or title means Director's Cut) and the same idea for the other formats its
/// workflow tagged from names (trade paperback, one-shot, black and white). The format names are ComicRack's own default vocabulary
/// (<see cref="FormatSignalCatalog.CeDefaultFormats"/>). It only answers for a book that has no format yet, and never changes a series name.
/// </summary>
public static class FormatGuesser
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>The starter rules, most specific first; the first one that matches wins.</summary>
    public static readonly IReadOnlyList<FormatRule> Rules =
    [
        new("Director's Cut", new Regex(@"\bdirector'?s[\s_\-]*cut\b", Options)),
        new("Trade Paper Back", new Regex(@"\b(?:tpb|trade[\s_\-]*paper[\s_\-]*back)\b", Options)),
        new("One Shot", new Regex(@"\bone[\s_\-]*shot\b", Options)),
        new("Black & White", new Regex(@"\b(?:black[\s_\-]*(?:and|&)[\s_\-]*white|b&w)(?![\p{L}\p{N}])", Options)),
        new("Annual", new Regex(@"\bannual\b", Options)),
    ];

    /// <summary>The format the names point to, or null: when the book already has a format (stored or proposed), or no rule matches. Needs <see cref="Issue.Series"/> and <see cref="Issue.MetadataProposals"/> loaded.</summary>
    public static string? Guess(Issue issue)
    {
        if (!string.IsNullOrWhiteSpace(issue.EffectiveFormat()))
        {
            return null;
        }

        return Guess(issue.Series?.Name, issue.EffectiveTitle(), string.IsNullOrEmpty(issue.FilePath) ? null : Path.GetFileNameWithoutExtension(issue.FilePath));
    }

    /// <summary>The first rule matching any of the given names.</summary>
    public static string? Guess(params string?[] names)
    {
        foreach (FormatRule rule in Rules)
        {
            if (names.Any(n => !string.IsNullOrEmpty(n) && rule.Pattern.IsMatch(n)))
            {
                return rule.Format;
            }
        }

        return null;
    }
}
