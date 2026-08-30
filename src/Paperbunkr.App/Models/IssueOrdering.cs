using System.Collections.Generic;
using System.Linq;
using cYo.Common.Text;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>
/// Orders issues by their actual issue <see cref="Issue.Number"/> rather than database insertion
/// order (<c>Id</c>). <c>Number</c> is a nullable, not-strictly-numeric string (e.g. "1", "10",
/// "0.5", "Annual 1"), so this reuses <see cref="TextNumberFloat"/> - the same numeric-aware
/// comparison already ported into Paperbunkr.Common/used by ComicBookNumberComparer in
/// Paperbunkr.Engine - rather than a naive numeric or ordinal string sort.
/// </summary>
public static class IssueOrdering
{
    public static IOrderedEnumerable<Issue> OrderByNumber(this IEnumerable<Issue> issues) =>
        issues.OrderBy(i => new TextNumberFloat(i.EffectiveNumber() ?? string.Empty));

    /// <summary>
    /// Groups a series' issues into runs for the Detail screen's Issues tab (docs/superpowers/specs/
    /// 2026-08-30-series-detail-run-separator-design.md): primarily by <see cref="Issue.Volume"/>
    /// (issues with no parseable Volume sort first - <see cref="IssueMetadataExtensions.VolumeSortKey"/>
    /// is <see langword="null"/> for them, and nullable-float ordering already puts null first), then
    /// by <see cref="Issue.Number"/> within each run - the exact same key <see cref="OrderByNumber"/>
    /// uses, so a single-run series (the common case) orders identically to today.
    /// </summary>
    public static IOrderedEnumerable<Issue> OrderByRun(this IEnumerable<Issue> issues) =>
        issues
            .OrderBy(i => i.VolumeSortKey())
            .ThenBy(i => new TextNumberFloat(i.EffectiveNumber() ?? string.Empty));
}
