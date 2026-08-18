using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Applies an accepted <see cref="MetadataProposalField.Series"/> proposal (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase2b-series-reassignment-design.md) - a write-time reassignment,
/// not a read-time resolver like everything in <see cref="IssueMetadataExtensions"/>. Deliberately
/// its own file rather than folded into that one: this mutates the database, a meaningfully
/// different contract worth keeping visually separate from that file's pure, side-effect-free
/// methods.
/// </summary>
public static class SeriesReassignmentResolver
{
    /// <summary>
    /// Moves <paramref name="proposal"/>'s issue to a series named <paramref name="proposal"/>.<see
    /// cref="MetadataProposal.ProposedValue"/> (found by exact case-insensitive name match, created
    /// if none exists), then deletes the source series if it now has no issues left. Called by
    /// <c>NeedsReviewViewModel</c>'s Accept action on a previously-<see
    /// cref="MetadataProposalStatus.Pending"/> row, and by <c>LibraryFolderScanner</c> for a
    /// <see cref="MetadataResolutionPolicy.Automatic"/>-created proposal - but only <em>after</em>
    /// its own batch <c>SaveChanges</c>, never inline while the issue is still unsaved: this method
    /// resolves the source series via <c>issue.SeriesId</c>, which reads as <c>0</c> (meaningless)
    /// on an entity that hasn't been persisted yet. No duplicate-issue blocking against the target
    /// series' existing issues - deliberately out of scope, see docs/superpowers/specs/2026-08-17-
    /// metadata-model-phase2b-series-reassignment-design.md.
    /// </summary>
    public static void Apply(PaperbunkrDbContext context, MetadataProposal proposal)
    {
        var issue = proposal.Issue ?? context.Issues.Find(proposal.IssueId);
        if (issue is null || string.IsNullOrWhiteSpace(proposal.ProposedValue))
        {
            return;
        }

        var sourceSeries = context.Series.Find(issue.SeriesId);

        var targetSeries = context.Series.FirstOrDefault(s => s.Name.ToLower() == proposal.ProposedValue.ToLower());
        if (targetSeries is null)
        {
            targetSeries = new Series { Name = proposal.ProposedValue };
            context.Series.Add(targetSeries);
        }

        // Both sides of the relationship need updating, not just the Issue.Series reference - when
        // the source series' Issues collection is already loaded/tracked (e.g. LibraryFolderScanner
        // populated it via series.Issues.Add(issue) earlier in the same scan), EF Core's fixup
        // reconciles the reference and collection navigations against each other and the untouched
        // collection side can win, silently no-op'ing the reassignment. Found via a failing test, not
        // assumed - confirmed the same code without this line leaves issue.SeriesId completely
        // unchanged after SaveChanges.
        sourceSeries?.Issues.Remove(issue);
        targetSeries.Issues.Add(issue);
        issue.Series = targetSeries;

        // Two SaveChanges calls, deliberately - the first alone would leave both "does the target
        // already have a real Id" and "has the database's own SeriesId column for this issue
        // actually moved yet" ambiguous when targetSeries is brand new, which the "delete an empty
        // source series" check below needs answered for real, not inferred from in-memory state
        // that hasn't round-tripped through SaveChanges yet.
        context.SaveChanges();

        if (sourceSeries is not null && sourceSeries.Id != targetSeries.Id)
        {
            bool sourceHasOtherIssues = context.Issues.Any(i => i.SeriesId == sourceSeries.Id);
            if (!sourceHasOtherIssues)
            {
                context.Series.Remove(sourceSeries);
                context.SaveChanges();
            }
        }
    }
}
