using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read/write resolver for <see cref="Continuity"/> membership (docs/superpowers/specs/2026-08-17-
/// metadata-model-phase4a-continuity-design.md) - the single tested home for the case-insensitive
/// get-or-create + idempotent membership rules, same shape as <see cref="MediaRelationResolver"/>.
/// Since docs/superpowers/specs/2026-08-28-continuity-editing-design.md membership runs through the
/// explicit <see cref="ContinuityMembership"/> join entity, which also carries a per-membership
/// note and sort order.
/// </summary>
// internal - see MediaRelationResolver (Plugin API v3 §7). Plugins read through IMetadataGraph.
internal static class ContinuityResolver
{
    public static IReadOnlyList<Continuity> GetContinuities(PaperbunkrDbContext context, int seriesId)
    {
        return context.ContinuityMemberships
            .Where(m => m.SeriesId == seriesId)
            .Select(m => m.Continuity)
            .OrderBy(c => c.Name)
            .ToList();
    }

    /// <summary>Member series of a continuity, in <see cref="ContinuityMembership.SortOrder"/> order then by name.</summary>
    public static IReadOnlyList<Series> GetSeriesInContinuity(PaperbunkrDbContext context, int continuityId)
    {
        return context.ContinuityMemberships
            .Where(m => m.ContinuityId == continuityId)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Series.Name)
            .Select(m => m.Series)
            .ToList();
    }

    /// <summary>The membership rows for a continuity, ordered - use when the caller needs the note / sort order, not just the series.</summary>
    public static IReadOnlyList<ContinuityMembership> GetMemberships(PaperbunkrDbContext context, int continuityId)
    {
        return context.ContinuityMemberships
            .Include(m => m.Series)
            .Where(m => m.ContinuityId == continuityId)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Series.Name)
            .ToList();
    }

    /// <summary>Other series that share at least one continuity with <paramref name="seriesId"/> - deduplicated if more than one continuity is shared.</summary>
    public static IReadOnlyList<Series> GetOtherSeriesSharingContinuity(PaperbunkrDbContext context, int seriesId)
    {
        var continuityIds = context.ContinuityMemberships
            .Where(m => m.SeriesId == seriesId)
            .Select(m => m.ContinuityId)
            .ToList();

        if (continuityIds.Count == 0)
        {
            return new List<Series>();
        }

        var otherSeriesIds = context.ContinuityMemberships
            .Where(m => m.SeriesId != seriesId && continuityIds.Contains(m.ContinuityId))
            .Select(m => m.SeriesId)
            .Distinct()
            .ToList();

        return context.Series
            .Where(s => otherSeriesIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToList();
    }

    /// <summary>
    /// Series that belong to <em>both</em> continuities (docs/superpowers/specs/2026-08-27-metadata-
    /// model-phase4f-continuity-browse-design.md's deferred cross-continuity comparison). Ordered by
    /// name.
    /// </summary>
    public static IReadOnlyList<Series> GetSeriesInBothContinuities(PaperbunkrDbContext context, int continuityAId, int continuityBId)
    {
        var inA = context.ContinuityMemberships.Where(m => m.ContinuityId == continuityAId).Select(m => m.SeriesId).ToHashSet();
        var inBoth = context.ContinuityMemberships
            .Where(m => m.ContinuityId == continuityBId)
            .Select(m => m.SeriesId)
            .AsEnumerable()
            .Where(inA.Contains)
            .ToList();

        return context.Series
            .Include(s => s.Issues)
            .Where(s => inBoth.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToList();
    }

    /// <summary>Other continuities that share at least one series with <paramref name="continuityId"/>, each with the shared-series count - the candidate list for a "compare with…" picker.</summary>
    public static IReadOnlyList<(Continuity Continuity, int SharedSeriesCount)> GetOverlappingContinuities(PaperbunkrDbContext context, int continuityId)
    {
        var mySeriesIds = context.ContinuityMemberships.Where(m => m.ContinuityId == continuityId).Select(m => m.SeriesId).ToHashSet();
        if (mySeriesIds.Count == 0)
        {
            return new List<(Continuity, int)>();
        }

        var sharedCounts = context.ContinuityMemberships
            .Where(m => m.ContinuityId != continuityId && mySeriesIds.Contains(m.SeriesId))
            .GroupBy(m => m.ContinuityId)
            .Select(g => new { ContinuityId = g.Key, Shared = g.Count() })
            .ToList();

        if (sharedCounts.Count == 0)
        {
            return new List<(Continuity, int)>();
        }

        var otherIds = sharedCounts.Select(x => x.ContinuityId).ToList();
        var continuities = context.Continuities.Where(c => otherIds.Contains(c.Id)).ToDictionary(c => c.Id);

        return sharedCounts
            .Select(x => (Continuity: continuities[x.ContinuityId], SharedSeriesCount: x.Shared))
            .OrderByDescending(t => t.SharedSeriesCount)
            .ThenBy(t => t.Continuity.Name)
            .ToList();
    }

    /// <summary>Case-insensitive name match before inserting, so careless retyping ("Earth-616" vs "earth-616") doesn't create near-duplicate continuities.</summary>
    public static Continuity GetOrCreate(PaperbunkrDbContext context, string name)
    {
        string trimmed = name.Trim();
        var existing = context.Continuities.FirstOrDefault(c => c.Name.ToLower() == trimmed.ToLower());
        if (existing is not null)
        {
            return existing;
        }

        var continuity = new Continuity { Name = trimmed, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Continuities.Add(continuity);
        context.SaveChanges();
        return continuity;
    }

    /// <summary>No-op returning <see langword="false"/> if the series is already a member. New members append at <c>SortOrder = max + 1</c>.</summary>
    public static bool AddSeriesToContinuity(PaperbunkrDbContext context, int seriesId, int continuityId)
    {
        var series = context.Series.FirstOrDefault(s => s.Id == seriesId);
        var continuity = context.Continuities.FirstOrDefault(c => c.Id == continuityId);
        if (series is null || continuity is null)
        {
            return false;
        }

        if (context.ContinuityMemberships.Any(m => m.ContinuityId == continuityId && m.SeriesId == seriesId))
        {
            return false;
        }

        int nextOrder = context.ContinuityMemberships
            .Where(m => m.ContinuityId == continuityId)
            .Select(m => (int?)m.SortOrder)
            .Max() ?? -1;

        context.ContinuityMemberships.Add(new ContinuityMembership
        {
            ContinuityId = continuityId,
            SeriesId = seriesId,
            SortOrder = nextOrder + 1,
        });
        continuity.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();
        return true;
    }

    public static void RemoveSeriesFromContinuity(PaperbunkrDbContext context, int seriesId, int continuityId)
    {
        var membership = context.ContinuityMemberships
            .FirstOrDefault(m => m.ContinuityId == continuityId && m.SeriesId == seriesId);
        if (membership is null)
        {
            return;
        }

        context.ContinuityMemberships.Remove(membership);
        context.SaveChanges();
    }

    /// <summary>Sets (or clears, with null/blank) the free-text note on one membership.</summary>
    public static void SetMembershipNote(PaperbunkrDbContext context, int continuityId, int seriesId, string? note)
    {
        var membership = context.ContinuityMemberships
            .FirstOrDefault(m => m.ContinuityId == continuityId && m.SeriesId == seriesId);
        if (membership is null)
        {
            return;
        }

        membership.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        context.SaveChanges();
    }

    /// <summary>
    /// Rewrites the continuity's membership order to match <paramref name="orderedSeriesIds"/>
    /// (0, 1, 2, …). Ids not currently members are ignored; members missing from the list keep
    /// their relative order after the listed ones.
    /// </summary>
    public static void SetMembershipOrder(PaperbunkrDbContext context, int continuityId, IReadOnlyList<int> orderedSeriesIds)
    {
        var memberships = context.ContinuityMemberships
            .Where(m => m.ContinuityId == continuityId)
            .ToList();
        if (memberships.Count == 0)
        {
            return;
        }

        int next = 0;
        foreach (int seriesId in orderedSeriesIds)
        {
            var match = memberships.FirstOrDefault(m => m.SeriesId == seriesId);
            if (match is not null)
            {
                match.SortOrder = next++;
            }
        }

        foreach (var leftover in memberships.Where(m => !orderedSeriesIds.Contains(m.SeriesId)).OrderBy(m => m.SortOrder))
        {
            leftover.SortOrder = next++;
        }

        context.SaveChanges();
    }

    /// <summary>
    /// Folds <paramref name="sourceId"/> into <paramref name="targetId"/>
    /// (docs/superpowers/specs/2026-08-28-continuity-editing-design.md): every series in the source
    /// that isn't already in the target is added to the target (carrying its note; appended after
    /// the target's existing members), then the source continuity is deleted. Series themselves are
    /// never touched. No-op if either id is missing or they're the same. Returns the number of
    /// series carried over.
    /// </summary>
    public static int Merge(PaperbunkrDbContext context, int sourceId, int targetId)
    {
        if (sourceId == targetId)
        {
            return 0;
        }

        var source = context.Continuities.FirstOrDefault(c => c.Id == sourceId);
        var target = context.Continuities.FirstOrDefault(c => c.Id == targetId);
        if (source is null || target is null)
        {
            return 0;
        }

        var sourceMemberships = context.ContinuityMemberships.Where(m => m.ContinuityId == sourceId).ToList();
        var targetSeriesIds = context.ContinuityMemberships.Where(m => m.ContinuityId == targetId).Select(m => m.SeriesId).ToHashSet();
        int nextOrder = context.ContinuityMemberships
            .Where(m => m.ContinuityId == targetId)
            .Select(m => (int?)m.SortOrder)
            .Max() ?? -1;

        int carried = 0;
        foreach (var membership in sourceMemberships)
        {
            if (targetSeriesIds.Add(membership.SeriesId))
            {
                context.ContinuityMemberships.Add(new ContinuityMembership
                {
                    ContinuityId = targetId,
                    SeriesId = membership.SeriesId,
                    Note = membership.Note,
                    SortOrder = ++nextOrder,
                });
                carried++;
            }
        }

        context.Continuities.Remove(source); // cascade drops the source's own membership rows
        target.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();
        return carried;
    }
}
