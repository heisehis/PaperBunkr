using System.Globalization;
using System.Linq;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Upserts <see cref="SeriesAssociation"/> rows for one series from Metron's <c>associated</c> field
/// (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md). Idempotent - re-running with
/// the same data changes nothing.
///
/// Called by the series connector's weekly fetch (<see cref="SeriesConnector.MetronAssociationFetch"/>;
/// docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §5). Rows that stay are updated in place, so the
/// ComicVine/GCD ids the connector looked up for them survive a re-sync.
/// </summary>
public static class SeriesAssociationSync
{
    public static void Sync(PaperbunkrDbContext context, int seriesId, IReadOnlyList<ComicVineIdName> associated)
    {
        var existing = context.SeriesAssociations.Where(a => a.SeriesId == seriesId && a.Provider == ComicProvider.Metron).ToList();
        var wantedIds = associated.Where(a => a.ExternalId is not null).Select(a => a.ExternalId!.Value.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        foreach (var stale in existing.Where(a => !wantedIds.Contains(a.ExternalSeriesId)))
        {
            context.SeriesAssociations.Remove(stale);
        }

        foreach (var associate in associated.Where(a => a.ExternalId is not null))
        {
            string externalId = associate.ExternalId!.Value.ToString(CultureInfo.InvariantCulture);
            var row = existing.FirstOrDefault(a => a.ExternalSeriesId == externalId);
            if (row is null)
            {
                context.SeriesAssociations.Add(new SeriesAssociation
                {
                    SeriesId = seriesId,
                    Provider = ComicProvider.Metron,
                    ExternalSeriesId = externalId,
                    Name = associate.Name,
                });
            }
            else if (row.Name != associate.Name)
            {
                row.Name = associate.Name;
            }
        }

        context.SaveChanges();
    }
}
