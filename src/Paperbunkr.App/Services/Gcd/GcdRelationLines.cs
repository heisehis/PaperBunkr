using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.App.Services.Gcd;

/// <summary>
/// The Related tab's read-only Grand Comics Database lines (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4): bonds from this
/// series to GCD series that aren't in the library ("Continues as The Incredible Hulk (1999)"), each linking to its GCD page. Bonds
/// between two series you have are real relations already (<see cref="GcdBondSync"/>) and aren't repeated here.
/// </summary>
public static class GcdRelationLines
{
    public const string SourceLabel = "GCD";

    public static IReadOnlyList<ExternalRelationPlaceholderSample> For(PaperbunkrDbContext context, int seriesId, Func<GcdDataStore?> openStore)
    {
        int? gcdId = context.Series.Where(s => s.Id == seriesId).Select(s => s.GcdSeriesId).FirstOrDefault();
        if (gcdId is not int id)
        {
            return Array.Empty<ExternalRelationPlaceholderSample>();
        }

        using var store = openStore();
        if (store is null)
        {
            return Array.Empty<ExternalRelationPlaceholderSample>();
        }

        var bonds = store.BondsFor(new[] { id });
        var otherIds = bonds.Select(b => b.OriginId == id ? b.TargetId : b.OriginId).Where(o => o != id).Distinct().ToList();
        var owned = context.Series.Where(s => s.GcdSeriesId != null && otherIds.Contains(s.GcdSeriesId.Value))
            .Select(s => s.GcdSeriesId!.Value).ToHashSet();
        var names = store.GetSeries(otherIds.Where(o => !owned.Contains(o))).ToDictionary(s => s.Id);

        var lines = new List<ExternalRelationPlaceholderSample>();
        var seen = new HashSet<(int, string)>();
        foreach (var bond in bonds)
        {
            bool thisIsOlder = bond.OriginId == id;
            int other = thisIsOlder ? bond.TargetId : bond.OriginId;
            if (!names.TryGetValue(other, out var series) || Label(bond.BondType, thisIsOlder) is not string label || !seen.Add((other, label)))
            {
                continue;
            }

            lines.Add(new ExternalRelationPlaceholderSample
            {
                TargetTitle = series.YearBegan is int year ? $"{series.Name} ({year.ToString(CultureInfo.InvariantCulture)})" : series.Name,
                RelationTypeLabel = label,
                TargetUrl = GcdLinks.Series(other),
                SourceLabel = SourceLabel,
            });
        }

        return lines;
    }

    /// <summary>How the bond reads from this series' side; null for a bond type this version doesn't know.</summary>
    internal static string? Label(string bondType, bool thisIsOlder) => GcdBondSync.RelationFor(bondType) switch
    {
        RelationType.Continuation => thisIsOlder ? "Continues as" : "Continues",
        RelationType.Reboot => thisIsOlder ? "Rebooted as" : "Reboot of",
        RelationType.Related => "Merged with",
        _ => null,
    };
}
