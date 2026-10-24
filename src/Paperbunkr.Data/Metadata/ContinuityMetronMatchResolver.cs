using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Tags a series' <see cref="Continuity"/> membership directly from Metron's <c>universes</c> field
/// (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md), which is already present in
/// the issue-detail response every scrape fetches. Universe details are refreshed separately by
/// their exact Metron ID. Same visibility/namespace as <see cref="ContinuityResolver"/>
/// (both internal) so it can call <see cref="ContinuityResolver.GetOrCreate"/>/
/// <see cref="ContinuityResolver.AddSeriesToContinuity"/> directly.
/// </summary>
internal static class ContinuityMetronMatchResolver
{
    /// <summary>No-op for anything but a Metron-sourced scrape, or one with no universe tags - Metron is the only source that ever populates <see cref="ComicVineIssueDetails.Universes"/>.</summary>
    public static void SyncFromIssueDetails(PaperbunkrDbContext context, int seriesId, ComicProvider provider, IReadOnlyList<ComicVineIdName> universes)
    {
        if (provider != ComicProvider.Metron || universes.Count == 0)
        {
            return;
        }

        foreach (var universe in universes.Where(u => !string.IsNullOrWhiteSpace(u.Name)))
        {
            Continuity? continuity = null;
            if (universe.ExternalId is int externalId)
            {
                string metronId = externalId.ToString(CultureInfo.InvariantCulture);
                continuity = context.Continuities.FirstOrDefault(item => item.MetronId == metronId)
                    ?? context.Continuities.Where(item => item.MetronId == null).AsEnumerable()
                        .FirstOrDefault(item => string.Equals(item.Name, universe.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (continuity is null)
                {
                    continuity = ContinuityResolver.GetOrCreate(context, universe.Name);
                }

                if (continuity.MetronId is null)
                {
                    continuity.MetronId = metronId;
                    context.SaveChanges();
                }
            }
            else
            {
                continuity = ContinuityResolver.GetOrCreate(context, universe.Name);
            }

            if (continuity is not null && (universe.ExternalId is null || continuity.MetronId == universe.ExternalId.Value.ToString(CultureInfo.InvariantCulture)))
            {
                ContinuityResolver.AddSeriesToContinuity(context, seriesId, continuity.Id);
            }
        }
    }

    /// <summary>Refreshes a continuity's four descriptive fields from Metron's canonical universe record after its ID is attached from an issue scrape.</summary>
    public static async Task<bool> RefreshUniverseDetailsAsync(PaperbunkrDbContext context, IMetronUniverses universes, int metronUniverseId, CancellationToken cancellationToken)
    {
        string externalId = metronUniverseId.ToString(CultureInfo.InvariantCulture);
        var continuity = context.Continuities.FirstOrDefault(item => item.MetronId == externalId);
        if (continuity is null)
        {
            return false;
        }

        MetronUniverseInfo? info = await universes.GetUniverseAsync(metronUniverseId, cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            return false;
        }

        string designation = info.Designation ?? string.Empty;
        string publisher = info.Publisher ?? string.Empty;
        string description = info.Description ?? string.Empty;
        if (continuity.Name == info.Name && continuity.Designation == designation
            && continuity.Publisher == publisher && continuity.Description == description)
        {
            return false;
        }

        continuity.Name = info.Name;
        continuity.Designation = designation;
        continuity.Publisher = publisher;
        continuity.Description = description;
        continuity.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();
        return true;
    }
}
