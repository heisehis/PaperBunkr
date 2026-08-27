using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// Chapter-progress computation for a tracker push (docs/superpowers/specs/2026-08-23-tracker-
/// write-back-sync-design.md) - "the series' highest <c>Issue.EffectiveNumber()</c> among issues
/// with <c>IsInProgress()</c>/<c>HasBeenRead()</c>," parsed as best-effort integer chapter number via
/// the same <c>NumberSortKey</c>-based parsing <see cref="IssueMetadataExtensions.NumberSortKey"/>
/// already uses elsewhere. Volume progress is out of scope this pass.
/// </summary>
public static class TrackerProgressCalculator
{
    public static int? ComputeChapterProgress(IEnumerable<Issue> issues)
    {
        float? highest = issues
            .Where(i => i.HasBeenRead() || i.IsInProgress())
            .Select(i => i.NumberSortKey())
            .Max();

        return highest is float value ? (int)value : null;
    }
}
