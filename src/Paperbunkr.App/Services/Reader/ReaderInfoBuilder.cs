using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.App.Services.Reader;

/// <summary>One credit line of the info panel: a role and the people who did it.</summary>
public sealed record InfoCredit(string Role, string Names);

/// <summary>What the reader's info panel shows for an issue (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #28). Plain data, built by <see cref="ReaderInfoBuilder"/>.</summary>
public sealed record ReaderInfoModel(
    string SeriesLine,
    string? Title,
    string? Summary,
    IReadOnlyList<InfoCredit> Credits,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Teams,
    IReadOnlyList<string> Locations,
    string? StoryArc,
    string? ContextLine,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Tags,
    string? RatingLine,
    string? PublisherLine)
{
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public bool HasCredits => Credits.Count > 0;

    public bool HasPeopleAndPlaces => Characters.Count > 0 || Teams.Count > 0 || Locations.Count > 0;

    public bool HasCharacters => Characters.Count > 0;

    public bool HasTeams => Teams.Count > 0;

    public bool HasLocations => Locations.Count > 0;

    public bool HasGenres => Genres.Count > 0;

    public bool HasTags => Tags.Count > 0;
}

/// <summary>Builds a <see cref="ReaderInfoModel"/> from an issue's own fields; everything is optional and an absent field simply produces no section.</summary>
public static class ReaderInfoBuilder
{
    public static ReaderInfoModel Build(Issue issue, string seriesName, ReadingContext? context, IReadOnlyList<string>? genres = null, IReadOnlyList<string>? tags = null)
    {
        string number = issue.EffectiveNumber() ?? string.Empty;
        string line = string.IsNullOrWhiteSpace(number) ? seriesName : $"{seriesName} #{number}";
        if (issue.Count is > 0 && !string.IsNullOrWhiteSpace(number))
        {
            line += $" of {issue.Count}";
        }

        var credits = new List<InfoCredit>();
        AddCredit(credits, "Writer", issue.Writer);
        AddCredit(credits, "Penciller", issue.Penciller);
        AddCredit(credits, "Inker", issue.Inker);
        AddCredit(credits, "Colorist", issue.Colorist);
        AddCredit(credits, "Letterer", issue.Letterer);
        AddCredit(credits, "Cover artist", issue.CoverArtist);
        AddCredit(credits, "Editor", issue.Editor);
        AddCredit(credits, "Translator", issue.Translator);

        return new ReaderInfoModel(
            line,
            string.IsNullOrWhiteSpace(issue.Title) ? null : issue.Title.Trim(),
            string.IsNullOrWhiteSpace(issue.Summary) ? null : issue.Summary.Trim(),
            credits,
            Split(issue.Characters),
            Split(issue.Teams),
            Split(issue.Locations),
            ArcLine(issue),
            context is null ? null : $"{context.Label} · {context.Position} of {context.Total}",
            genres ?? [],
            tags ?? [],
            RatingLine(issue),
            PublisherLine(issue));
    }

    /// <summary>Splits a comma or semicolon separated metadata field into trimmed, distinct names, keeping their order.</summary>
    public static IReadOnlyList<string> Split(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? [] : SmartListLeafEvaluator.SplitValues(raw).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static void AddCredit(List<InfoCredit> credits, string role, string? raw)
    {
        var names = Split(raw);
        if (names.Count > 0)
        {
            credits.Add(new InfoCredit(role, string.Join(", ", names)));
        }
    }

    private static string? ArcLine(Issue issue)
    {
        if (string.IsNullOrWhiteSpace(issue.StoryArc))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(issue.StoryArcNumber) ? issue.StoryArc.Trim() : $"{issue.StoryArc.Trim()} (part {issue.StoryArcNumber.Trim()})";
    }

    private static string? RatingLine(Issue issue)
    {
        var parts = new List<string>();
        if (issue.Rating is > 0)
        {
            parts.Add($"★ {issue.Rating.Value:0.#}");
        }

        if (!string.IsNullOrWhiteSpace(issue.AgeRating))
        {
            parts.Add(issue.AgeRating.Trim());
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string? PublisherLine(Issue issue)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(issue.Publisher))
        {
            parts.Add(issue.Publisher.Trim());
        }

        if (!string.IsNullOrWhiteSpace(issue.Imprint))
        {
            parts.Add(issue.Imprint.Trim());
        }

        if (issue.Year is > 0)
        {
            parts.Add(issue.Year.Value.ToString());
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
