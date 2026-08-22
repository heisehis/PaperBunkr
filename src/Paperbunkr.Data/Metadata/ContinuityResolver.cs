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
/// </summary>
public static class ContinuityResolver
{
    public static IReadOnlyList<Continuity> GetContinuities(PaperbunkrDbContext context, int seriesId)
    {
        var series = context.Series.Include(s => s.Continuities).FirstOrDefault(s => s.Id == seriesId);
        return series?.Continuities.OrderBy(c => c.Name).ToList() ?? new List<Continuity>();
    }

    public static IReadOnlyList<Series> GetSeriesInContinuity(PaperbunkrDbContext context, int continuityId)
    {
        var continuity = context.Continuities.Include(c => c.Series).FirstOrDefault(c => c.Id == continuityId);
        return continuity?.Series.OrderBy(s => s.Name).ToList() ?? new List<Series>();
    }

    /// <summary>Other series that share at least one continuity with <paramref name="seriesId"/> - deduplicated if more than one continuity is shared.</summary>
    public static IReadOnlyList<Series> GetOtherSeriesSharingContinuity(PaperbunkrDbContext context, int seriesId)
    {
        var continuityIds = context.Series
            .Where(s => s.Id == seriesId)
            .SelectMany(s => s.Continuities.Select(c => c.Id))
            .ToList();

        if (continuityIds.Count == 0)
        {
            return new List<Series>();
        }

        return context.Series
            .Where(s => s.Id != seriesId && s.Continuities.Any(c => continuityIds.Contains(c.Id)))
            .Distinct()
            .OrderBy(s => s.Name)
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

    /// <summary>No-op returning <see langword="false"/> if the series is already a member.</summary>
    public static bool AddSeriesToContinuity(PaperbunkrDbContext context, int seriesId, int continuityId)
    {
        var series = context.Series.Include(s => s.Continuities).FirstOrDefault(s => s.Id == seriesId);
        var continuity = context.Continuities.FirstOrDefault(c => c.Id == continuityId);
        if (series is null || continuity is null)
        {
            return false;
        }

        if (series.Continuities.Any(c => c.Id == continuityId))
        {
            return false;
        }

        series.Continuities.Add(continuity);
        continuity.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();
        return true;
    }

    public static void RemoveSeriesFromContinuity(PaperbunkrDbContext context, int seriesId, int continuityId)
    {
        var series = context.Series.Include(s => s.Continuities).FirstOrDefault(s => s.Id == seriesId);
        var continuity = series?.Continuities.FirstOrDefault(c => c.Id == continuityId);
        if (series is null || continuity is null)
        {
            return;
        }

        series.Continuities.Remove(continuity);
        context.SaveChanges();
    }
}
