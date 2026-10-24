using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Refreshes a Metron-tagged continuity's name, designation, publisher, and description from Metron's universe record
/// (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §3). The scrape learns membership from an issue's universe ID;
/// this sweep repairs details when the targeted scrape-time lookup was unavailable.
/// </summary>
public static class ContinuityUniverseSweep
{
    public const int DefaultBatchSize = 20;

    /// <summary>How many continuities were updated.</summary>
    public static async Task<int> RunAsync(Func<PaperbunkrDbContext> contextFactory, IMetronUniverses universes, int batchSize, CancellationToken cancellationToken)
    {
        System.Collections.Generic.List<(int Id, int MetronId)> due;
        using (var context = contextFactory())
        {
            due = context.Continuities.Where(c => c.MetronId != null
                    && (c.Designation == null || c.Publisher == null || c.Description == null))
                .OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.MetronId })
                .AsEnumerable()
                .Where(c => int.TryParse(c.MetronId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                .Select(c => (c.Id, int.Parse(c.MetronId!, System.Globalization.CultureInfo.InvariantCulture)))
                .Take(Math.Max(1, batchSize))
                .ToList();
        }

        int updated = 0;
        foreach (var (id, metronId) in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetronUniverseInfo? info;
            try
            {
                info = await universes.GetUniverseAsync(metronId, cancellationToken).ConfigureAwait(false);
            }
            catch (ComicVineException)
            {
                break;      // rate limit, quota or login hold: the rest waits for the next run
            }

            using var write = contextFactory();
            var continuity = write.Continuities.FirstOrDefault(c => c.Id == id);
            if (continuity is null || (continuity.Designation is not null && continuity.Publisher is not null && continuity.Description is not null))
            {
                continue;
            }

            // Empty optional fields mean "Metron was checked and has no value", so a missing
            // provider value does not make this continuity eligible for every later sweep.
            if (info is null)
            {
                continuity.Designation ??= string.Empty;
                continuity.Publisher ??= string.Empty;
                continuity.Description ??= string.Empty;
                write.SaveChanges();
                continue;
            }

            bool changed = continuity.Name != info.Name
                || continuity.Designation != (info.Designation ?? string.Empty)
                || continuity.Publisher != (info.Publisher ?? string.Empty)
                || continuity.Description != (info.Description ?? string.Empty);
            continuity.Name = info.Name;
            continuity.Designation = info.Designation ?? string.Empty;
            continuity.Publisher = info.Publisher ?? string.Empty;
            continuity.Description = info.Description ?? string.Empty;
            if (changed) updated++;
            write.SaveChanges();
        }

        return updated;
    }
}
