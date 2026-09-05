using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.App.Services;

/// <summary>
/// Raises the Activity Center alert for newly-imported files that turn out to duplicate something
/// already in the library (docs/superpowers/specs/2026-09-05-duplicate-files-review-design.md) - one
/// shared helper for the two import call sites (<see cref="LiveFolderWatchService"/> and the manual
/// "Scan Now" path) rather than duplicating the check-and-raise logic in both.
/// </summary>
public static class DuplicateAlertHelper
{
    public static void RaiseIfAny(IActivityService activity, IReadOnlyList<int> addedIssueIds)
    {
        if (addedIssueIds.Count == 0)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var issues = context.Issues.ToList();
        var groups = SmartListQueryBuilder.BuildDuplicateGroups(issues);

        var addedSet = new HashSet<int>(addedIssueIds);
        int matchingGroups = groups.Count(g => g.Any(i => addedSet.Contains(i.Id)));
        if (matchingGroups == 0)
        {
            return;
        }

        activity.RaiseAlert(new ActivityAlert
        {
            Severity = ActivityAlertSeverity.Warning,
            Title = "Possible duplicates found",
            Detail = matchingGroups == 1
                ? "A newly-added file may duplicate something already in your library."
                : $"{matchingGroups} newly-added files may duplicate something already in your library.",
            ActionLabel = "Review",
            ActionLink = new ActivityLink(ActivityLinkKind.MigrationReview),
            DedupeKey = "duplicate-files",
        });
    }
}
