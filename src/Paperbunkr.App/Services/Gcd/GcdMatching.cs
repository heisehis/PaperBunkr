using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.App.Services.Gcd;

/// <summary>
/// "Match series to GCD" - the weekly task, and what runs right after the data is installed or updated (docs/superpowers/specs/
/// 2026-09-27-gcd-data-design.md §3-§4): <see cref="GcdMatcher"/> then <see cref="GcdBondSync"/>.
/// </summary>
public static class GcdMatching
{
    public const string NotInstalled = "Grand Comics Database data isn't installed - download it under Preferences → Connections.";

    /// <param name="extractPath">The extract to use; null = the installed one.</param>
    /// <param name="metronSeriesGcdId">Metron series → GCD id lookup; null = from the saved Metron login (none saved = no lookups).</param>
    public static async Task<string> RunAsync(
        Func<PaperbunkrDbContext> contextFactory,
        IActivityJobHandle? handle,
        CancellationToken cancellationToken,
        string? extractPath = null,
        Func<int, CancellationToken, Task<int?>>? metronSeriesGcdId = null)
    {
        using var store = GcdDataStore.TryOpen(extractPath);
        if (store is null)
        {
            return NotInstalled;
        }

        handle?.Report("Matching series to the Grand Comics Database…");
        metronSeriesGcdId ??= MetronLookup(contextFactory);
        var match = await GcdMatcher.RunAsync(contextFactory, store, metronSeriesGcdId, cancellationToken: cancellationToken).ConfigureAwait(false);
        handle?.Report("Linking continued series…");
        var bonds = GcdBondSync.Run(contextFactory, store);
        return Summary(match, bonds);
    }

    internal static string Summary(GcdMatchResult match, GcdBondSyncResult bonds)
    {
        var parts = new List<string>();
        parts.Add(match.SeriesMatched == 0 ? "No new series matched" : $"{match.SeriesMatched} series matched");
        if (match.IssuesMatched > 0)
        {
            parts.Add($"{match.IssuesMatched} issue{(match.IssuesMatched == 1 ? "" : "s")} dated");
        }

        if (bonds.Added > 0)
        {
            parts.Add($"{bonds.Added} series link{(bonds.Added == 1 ? "" : "s")} added");
        }

        if (bonds.Removed > 0)
        {
            parts.Add($"{bonds.Removed} removed");
        }

        return string.Join(" · ", parts);
    }

    private static Func<int, CancellationToken, Task<int?>>? MetronLookup(Func<PaperbunkrDbContext> contextFactory)
    {
        using var context = contextFactory();
        if (ComicProviderFactory.Create(context, ComicProvider.Metron, ComicVineRequestPriority.Low) is not MetronClient metron)
        {
            return null;
        }

        return async (seriesId, ct) => (await metron.GetSeriesInfoAsync(seriesId, ct).ConfigureAwait(false))?.GcdId;
    }
}
