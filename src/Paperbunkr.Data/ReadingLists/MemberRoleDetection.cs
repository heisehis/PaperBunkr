using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>What a role-detection run did: roles written automatically, and suggestions held for review.</summary>
public sealed record RoleDetectionSummary(int Applied, int Suggested)
{
    public static RoleDetectionSummary Empty { get; } = new(0, 0);

    public static RoleDetectionSummary operator +(RoleDetectionSummary a, RoleDetectionSummary b) => new(a.Applied + b.Applied, a.Suggested + b.Suggested);

    public override string ToString() => Applied == 0 && Suggested == 0
        ? "No roles detected."
        : $"{Applied} role{(Applied == 1 ? string.Empty : "s")} detected, {Suggested} need{(Suggested == 1 ? "s" : string.Empty)} review.";
}

/// <summary>
/// Runs <see cref="MemberRoleDetector"/> over a whole reading list or story event and records the results with
/// <see cref="MemberRoleApplier"/> (docs/superpowers/specs/2026-09-25-reading-list-role-detection-design.md, section 4). Saves once at the end.
/// </summary>
public static class MemberRoleDetection
{
    /// <summary>The detector's answer for one issue in an arc of <paramref name="total"/> members. <paramref name="seriesName"/> overrides the
    /// issue's own series (a reading-order source names the series itself, and a placeholder's series row may not exist yet).</summary>
    public static RoleSuggestion? Suggest(
        Issue issue, string? seriesName, string? annotation, string? dominantSeries, int position, int total,
        string? sourceTitle = null, string? sourceSummary = null) =>
        MemberRoleDetector.Detect(new MemberRoleFacts(
            issue.EffectiveFormat(),
            // The library's own title wins; a provider's story title stands in for an issue the library does not own (a placeholder has none).
            issue.EffectiveTitle() ?? sourceTitle,
            seriesName ?? issue.Series?.Name,
            issue.EffectiveNumber(),
            annotation,
            dominantSeries,
            position,
            total,
            sourceSummary));

    /// <summary>The "Detect roles" action on a reading list. Items the user has given a role only ever get a suggestion.</summary>
    public static RoleDetectionSummary DetectForList(PaperbunkrDbContext context, int readingListId)
    {
        List<ReadingListItem> items = context.ReadingListItems
            .Include(i => i.Issue).ThenInclude(i => i!.Series)
            .Include(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
            .Where(i => i.ReadingListId == readingListId)
            .OrderBy(i => i.SortOrder)
            .ToList();

        string? dominant = MemberRoleDetector.DominantSeries(items.Select(i => i.Issue?.Series?.Name));
        var summary = RoleDetectionSummary.Empty;
        for (int position = 0; position < items.Count; position++)
        {
            ReadingListItem item = items[position];
            if (item.Issue is null)
            {
                continue;
            }

            summary += Tally(MemberRoleApplier.Apply(item, Suggest(item.Issue, null, null, dominant, position, items.Count)));
        }

        context.SaveChanges();
        return summary;
    }

    /// <summary>The "Detect roles" action on a story event. Existing members (whose role is never empty) only ever get a suggestion,
    /// unless their role was itself set automatically.</summary>
    public static RoleDetectionSummary DetectForEvent(PaperbunkrDbContext context, int storyEventId)
    {
        List<EventMembership> members = context.EventMemberships
            .Include(m => m.Issue).ThenInclude(i => i!.Series)
            .Include(m => m.Issue).ThenInclude(i => i!.MetadataProposals)
            .Where(m => m.StoryEventId == storyEventId)
            .OrderBy(m => m.Position)
            .ToList();

        string? dominant = MemberRoleDetector.DominantSeries(members.Select(m => m.Issue?.Series?.Name));
        var summary = RoleDetectionSummary.Empty;
        for (int position = 0; position < members.Count; position++)
        {
            EventMembership member = members[position];
            if (member.Issue is null)
            {
                continue;
            }

            summary += Tally(MemberRoleApplier.Apply(member, Suggest(member.Issue, null, null, dominant, position, members.Count)));
        }

        context.SaveChanges();
        return summary;
    }

    /// <summary>Adds <paramref name="orderedIssueIds"/> to a story event in order, giving each new member the role the detector is sure
    /// of (else Core, the previous default) and holding a weaker guess as a suggestion. Used where a whole arc becomes an event.</summary>
    public static RoleDetectionSummary AddDetectedMembers(PaperbunkrDbContext context, int storyEventId, IReadOnlyList<int> orderedIssueIds)
    {
        Dictionary<int, Issue> issues = context.Issues
            .Include(i => i.Series)
            .Include(i => i.MetadataProposals)
            .Where(i => orderedIssueIds.Contains(i.Id))
            .ToDictionary(i => i.Id);

        string? dominant = MemberRoleDetector.DominantSeries(orderedIssueIds.Select(id => issues.TryGetValue(id, out Issue? i) ? i.Series?.Name : null));
        var summary = RoleDetectionSummary.Empty;
        for (int position = 0; position < orderedIssueIds.Count; position++)
        {
            int issueId = orderedIssueIds[position];
            if (!EventMembershipResolver.AddMember(context, storyEventId, issueId, EventMembershipRole.Core))
            {
                continue;       // already a member
            }

            if (!issues.TryGetValue(issueId, out Issue? issue))
            {
                continue;
            }

            EventMembership member = context.EventMemberships.First(m => m.StoryEventId == storyEventId && m.IssueId == issueId);
            summary += Tally(MemberRoleApplier.Apply(member, Suggest(issue, null, null, dominant, position, orderedIssueIds.Count), isNew: true));
        }

        context.SaveChanges();
        return summary;
    }

    internal static RoleDetectionSummary Tally(RoleApplyResult result) => result switch
    {
        RoleApplyResult.Applied => new RoleDetectionSummary(1, 0),
        RoleApplyResult.Suggested => new RoleDetectionSummary(0, 1),
        _ => RoleDetectionSummary.Empty,
    };
}
