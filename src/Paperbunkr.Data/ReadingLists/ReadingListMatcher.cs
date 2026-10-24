using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists.Matching;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>
/// Resolves one series/number against the library - the single-entry front of <see cref="ReadingListResolver"/>, kept for the callers
/// that have a bare series and number in hand (the Library's "add a physical book", a canonical check's missing entry). Imports resolve
/// whole lists through the resolver directly, which reads the library once for all entries.
/// <para>The rules live in <see cref="MatchEngine"/> (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.5): every
/// series row with an equivalent name is searched, numbers compare through <see cref="IssueNumberKey"/>, and a comic is only returned
/// when the engine may link it by itself - several volumes the request cannot tell apart, or a lone candidate from another year, are not
/// guessed at.</para>
/// </summary>
public static class ReadingListMatcher
{
    /// <summary>Read-only lookup: the real comic this means, else the placeholder already standing in for it, else null.</summary>
    public static Issue? FindExisting(
        PaperbunkrDbContext context, string seriesName, string number, string? volume = null, int? year = null, string? format = null)
    {
        int? id = ReadingListResolver.FindExisting(context, [new ComicReference(seriesName, number, volume, year, format)])[0];
        return id is null
            ? null
            : context.Issues.Include(i => i.Series).Include(i => i.MetadataProposals).First(i => i.Id == id);
    }

    /// <summary>
    /// <see cref="FindExisting"/>, falling back to creating the <see cref="Series"/> (if needed) and a placeholder <see cref="Issue"/>
    /// (<see cref="Issue.FileIsMissing"/> and <see cref="Issue.IsPlaceholder"/> both true). Saves.
    /// </summary>
    public static Issue ResolveOrCreatePlaceholder(
        PaperbunkrDbContext context, string seriesName, string number, string? volume = null, int? year = null, string? format = null) =>
        ResolveOrCreatePlaceholder(context, seriesName, number, volume, year, format, out _);

    /// <summary>
    /// Additive overload for the manual "add a physical book" Library-screen entry point
    /// (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §3).
    /// <paramref name="wasCreated"/> is <see langword="false"/> when <see cref="FindExisting"/>
    /// matched something already in the library (a real book or an existing placeholder) - callers
    /// must only treat the row as safely deletable when this is <see langword="true"/>.
    /// </summary>
    public static Issue ResolveOrCreatePlaceholder(
        PaperbunkrDbContext context, string seriesName, string number, string? volume, int? year, string? format, out bool wasCreated)
    {
        var existing = FindExisting(context, seriesName, number, volume, year, format);
        if (existing is not null)
        {
            wasCreated = false;
            return existing;
        }

        var resolution = ReadingListResolver.Resolve(context, [new ComicReference(seriesName, number, volume, year, format)]);
        var placeholder = ReadingListResolver.Materialize(context, resolution)[0].Issue;
        context.SaveChanges();
        wasCreated = true;
        return placeholder;
    }
}
