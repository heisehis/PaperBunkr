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
/// <b>Scope note:</b> like <see cref="ComicVine.Scraping.ComicProviderAdopt"/>, this is a real, tested
/// building block with no automatic trigger wired to it in this pass - <c>MetronClient.GetAssociatedSeriesAsync</c>
/// is a separate call from the ordinary issue-detail scrape, and wiring "refresh associated series"
/// into a scheduled task or the series-refresh flow is a smaller follow-up, not attempted here.
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
