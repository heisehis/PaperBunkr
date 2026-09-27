using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services;

/// <summary>
/// One changelog entry as the shared entry view shows it (docs/superpowers/specs/2026-09-26-about-polish-design.md §3): the entry,
/// whether it is the running version's ("Current" badge), whether it opens expanded, and its body already split into tagged groups.
/// </summary>
public sealed record ChangelogRow(ChangelogEntry Entry, bool IsCurrent, bool StartExpanded, IReadOnlyList<ChangelogBodyGroup> Groups)
{
    public static ChangelogRow For(ChangelogEntry entry, bool isCurrent, bool startExpanded)
        => new(entry, isCurrent, startExpanded, ChangelogBodyFormatter.Format(entry.Body));
}

/// <summary>
/// Which changelog entries to show and which one is "current" (docs/superpowers/specs/2026-09-26-about-polish-design.md §3, Q14).
/// Every consumer goes through here instead of taking <c>entries[0]</c>, which is an empty <c>[Unreleased]</c> most of the time.
/// </summary>
public static class ChangelogSelection
{
    /// <summary>Entries with something to show - drops an empty <c>[Unreleased]</c> (or any other heading with no body).</summary>
    public static IReadOnlyList<ChangelogEntry> Visible(IReadOnlyList<ChangelogEntry> entries)
        => entries.Where(e => !string.IsNullOrWhiteSpace(e.Body)).ToList();

    /// <summary>
    /// The newest non-empty entry whose version is not newer than <paramref name="running"/>, so a 0.7.3 build between releases still
    /// highlights 0.7.0 instead of nothing. An entry whose heading isn't a version (<c>Unreleased</c>) is never current. Null if none.
    /// </summary>
    public static ChangelogEntry? Current(IReadOnlyList<ChangelogEntry> entries, Version running)
        => entries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Body)
            && ReleaseVersion.TryParseHeading(e.Version, out var v)
            && !ReleaseVersion.IsNewerThan(v, running));

    /// <summary>About's accordion: every visible entry, with the current one marked and the only one expanded.</summary>
    public static IReadOnlyList<ChangelogRow> BuildRows(IReadOnlyList<ChangelogEntry> entries, Version running)
    {
        var current = Current(entries, running);
        return Visible(entries)
            .Select(e => ChangelogRow.For(e, isCurrent: ReferenceEquals(e, current), startExpanded: ReferenceEquals(e, current)))
            .ToList();
    }
}
