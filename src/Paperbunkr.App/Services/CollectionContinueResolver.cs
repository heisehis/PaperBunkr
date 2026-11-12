using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>The collection the Home hero card is about, and where its "Continue" chip goes (null issue = no chip).</summary>
public sealed record CollectionHero(int CollectionId, int? ContinueIssueId, string? ContinueLabel);

/// <summary>
/// Which collection you read from most recently and its next unread issue (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md, Home hero). Recency is the
/// newest <see cref="Issue.OpenedTime"/> among a collection's members (a series member's issues, or a standalone issue member). The next unread issue is the first
/// manual member in collection order that has one (a series' first unread issue by number; a standalone issue when unread); books and rule-matched members are skipped.
/// Unread uses the same test the series cards use: <c>LastPageRead</c> null or 0. One pass over the manual items, no per-row queries.
/// </summary>
public static class CollectionContinueResolver
{
    public static CollectionHero? GetHero(PaperbunkrDbContext context, IReadOnlyList<int> collectionIds)
    {
        if (collectionIds.Count == 0)
        {
            return null;
        }

        var items = context.CollectionItems
            .AsNoTracking()
            .Where(ci => collectionIds.Contains(ci.CollectionId))
            .Include(ci => ci.Series).ThenInclude(s => s!.Issues)
            .Include(ci => ci.Issue).ThenInclude(i => i!.Series)
            .OrderBy(ci => ci.SortOrder).ThenBy(ci => ci.Id)
            .ToList();

        int? bestCollection = null;
        DateTime best = DateTime.MinValue;
        foreach (int collectionId in collectionIds) // the caller's order breaks ties
        {
            DateTime newest = DateTime.MinValue;
            foreach (var item in items.Where(i => i.CollectionId == collectionId))
            {
                if (item.Series is { } series)
                {
                    foreach (var issue in series.Issues)
                    {
                        if (issue.OpenedTime is DateTime opened && opened > newest)
                        {
                            newest = opened;
                        }
                    }
                }
                else if (item.Issue?.OpenedTime is DateTime openedIssue && openedIssue > newest)
                {
                    newest = openedIssue;
                }
            }

            if (newest > best)
            {
                best = newest;
                bestCollection = collectionId;
            }
        }

        if (bestCollection is not int chosen)
        {
            return null;
        }

        foreach (var item in items.Where(i => i.CollectionId == chosen))
        {
            Issue? next = null;
            if (item.Series is { } series)
            {
                next = series.Issues.OrderByNumber().FirstOrDefault(IsUnread);
            }
            else if (item.Issue is { } issue && IsUnread(issue))
            {
                next = issue;
            }

            if (next is not null)
            {
                string seriesName = next.Series?.Name ?? item.Series?.Name ?? "Issue";
                string number = string.IsNullOrWhiteSpace(next.EffectiveNumber()) ? string.Empty : $" #{next.EffectiveNumber()}";
                return new CollectionHero(chosen, next.Id, $"{seriesName}{number}");
            }
        }

        return new CollectionHero(chosen, null, null);
    }

    private static bool IsUnread(Issue issue) => issue.LastPageRead is null or 0;
}
