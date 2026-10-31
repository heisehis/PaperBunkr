using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.SeriesConnector;

namespace Paperbunkr.App.Services;

/// <summary>
/// Runs the series connector (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §5.2). The full run, used by the weekly
/// "Find related series" task and the Related tab's "Find relations" button: save the local rules, ask Metron about associated series
/// (when Metron is connected), then save again so what Metron said lands. The local run, used after a scan or a scrape: no network, seconds.
/// Both honour Preferences › Library › "Find related series automatically".
/// </summary>
public static class SeriesConnectorRunner
{
    public const string TurnedOff = "Turned off in Preferences";

    public static bool IsEnabled(Func<PaperbunkrDbContext> contextFactory)
    {
        using var context = contextFactory();
        return IsEnabled(context);
    }

    /// <summary>Reads the switch without creating the settings row (a background reader must not race the UI to create it). No row = the default, on.</summary>
    public static bool IsEnabled(PaperbunkrDbContext context) =>
        context.AppSettings.AsNoTracking().Select(a => (bool?)a.FindRelatedSeriesAutomatically).FirstOrDefault() ?? true;

    /// <param name="metron">Null = the saved Metron login, if any; tests pass a fake.</param>
    public static async Task<string> RunFullAsync(
        Func<PaperbunkrDbContext> contextFactory,
        IActivityJobHandle? job,
        CancellationToken cancellationToken,
        IMetronSeriesLinks? metron = null,
        DateTime? nowUtc = null)
    {
        if (!IsEnabled(contextFactory))
        {
            return TurnedOff;
        }

        job?.Report("Linking volumes, annuals, team-ups and event tie-ins…");
        var local = await Task.Run(() => SeriesConnectorSweep.Run(contextFactory), cancellationToken).ConfigureAwait(false);

        if (metron is null)
        {
            using var context = contextFactory();
            metron = ComicProviderFactory.Create(context, ComicProvider.Metron, ComicVineRequestPriority.Low) as IMetronSeriesLinks;
        }

        if (metron is null)
        {
            return local.Text;
        }

        job?.Report("Asking Metron about related series…");
        var progress = job is null ? null : new Progress<(int Done, int Total)>(p => job.Report(p.Done, p.Total, "Asking Metron about related series"));
        var fetch = await MetronAssociationFetch.RunAsync(contextFactory, metron, nowUtc ?? DateTime.UtcNow, cancellationToken, progress).ConfigureAwait(false);
        var withMetron = await Task.Run(() => SeriesConnectorSweep.Run(contextFactory), cancellationToken).ConfigureAwait(false);

        var total = new SeriesConnectorSummary(local.Created + withMetron.Created, local.Updated + withMetron.Updated, local.Removed + withMetron.Removed);
        return fetch.Failed > 0 ? $"{total.Text} · Metron didn't answer for {fetch.Failed} series (retried next run)" : total.Text;
    }

    /// <summary>The no-network part, for after a scan or a scrape. Null when turned off.</summary>
    public static SeriesConnectorSummary? RunLocal(Func<PaperbunkrDbContext> contextFactory) =>
        IsEnabled(contextFactory) ? SeriesConnectorSweep.Run(contextFactory) : null;
}
