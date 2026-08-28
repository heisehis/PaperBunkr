using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One issue whose comic age is currently only <em>inferred</em> (from <see cref="Issue.Year"/>), reviewable in the Timeline view.</summary>
public sealed record InferredAgeRow(Issue Issue, ComicAge Age, decimal Confidence, string? Reason);

/// <summary>
/// A lightweight bulk "review all inferred ages" surface for the Timeline (docs/superpowers/specs/
/// 2026-08-27-metadata-model-phase4g-age-progression-design.md deferred this as a possible real
/// <c>MetadataProposal</c> integration; this is the lighter version - it lists issues whose age is
/// inferred and, on Accept, writes the CE-style label straight into <see cref="Issue.BookAge"/>).
/// </summary>
public static class BookAgeReviewResolver
{
    /// <summary>Issues in the given series whose <see cref="Issue.BookAge"/> is unset/unrecognized but which <see cref="BookAgeResolver"/> can place from <see cref="Issue.Year"/>.</summary>
    public static IReadOnlyList<InferredAgeRow> GetInferred(PaperbunkrDbContext context, IReadOnlyCollection<int> seriesIds)
    {
        var issues = context.Issues
            .Include(i => i.Series)
            .Where(i => seriesIds.Contains(i.SeriesId))
            .ToList();

        var rows = new List<InferredAgeRow>();
        foreach (var issue in issues)
        {
            var (age, confidence, reason) = BookAgeResolver.Resolve(issue);
            if (age is not ComicAge resolved)
            {
                continue;
            }

            // Only rows the user hasn't already pinned with an explicit label - i.e. the resolver
            // reached step 2 (year inference). An explicit label resolves at exactly 1.0m via step 1,
            // so re-derive whether step 1 fired by checking the stored text.
            bool hasExplicitLabel = !string.IsNullOrWhiteSpace(issue.BookAge)
                && Enum.GetValues<ComicAge>().Any(a =>
                {
                    int paren = issue.BookAge!.IndexOf('(');
                    string label = (paren >= 0 ? issue.BookAge[..paren] : issue.BookAge).Trim();
                    return string.Equals(label, a.ToString(), StringComparison.OrdinalIgnoreCase);
                });

            if (hasExplicitLabel)
            {
                continue;
            }

            rows.Add(new InferredAgeRow(issue, resolved, confidence, reason));
        }

        return rows
            .OrderBy(r => r.Issue.Year ?? int.MaxValue)
            .ThenBy(r => r.Issue.Series?.Name)
            .ToList();
    }

    /// <summary>Writes the CE-style age label for <paramref name="age"/> into the issue's <see cref="Issue.BookAge"/>, making it authoritative (resolver step 1) from then on.</summary>
    public static void Accept(PaperbunkrDbContext context, int issueId, ComicAge age)
    {
        var issue = context.Issues.FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            return;
        }

        issue.BookAge = ComicAgeCatalog.All[age].CeListLabel;
        context.SaveChanges();
    }
}
