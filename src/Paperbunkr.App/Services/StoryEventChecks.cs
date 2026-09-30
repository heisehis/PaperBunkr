using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>
/// "Check story events" - the weekly task and the Story Events sidebar button run exactly this: the Story Event resolver (ids, merges;
/// docs/superpowers/specs/2026-09-27-story-event-resolver-design.md) and then the smart connector (docs/superpowers/specs/
/// 2026-09-27-continuity-map-design.md §2), in that order so two halves of a duplicate are never linked.
/// </summary>
public static class StoryEventChecks
{
    public sealed record Result(StoryEventIdentitySweepSummary Identity, EventConnectorSummary Connector)
    {
        public string Text => Identity.Merges.Count > 0
            ? $"{Identity.Text} · {Connector.Text}\n{Identity.MergeDetails}"
            : $"{Identity.Text} · {Connector.Text}";
    }

    public static async Task<string> RunAsync(IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken) =>
        (await RunDetailedAsync(null, null, progress, cancellationToken).ConfigureAwait(false)).Text;

    /// <param name="sources">Provider sources for id completion; null = the saved credentials.</param>
    /// <param name="wikidata">Wikidata lookup for the connector; null = the live Wikidata client.</param>
    public static async Task<Result> RunDetailedAsync(
        IReadOnlyDictionary<ComicProvider, IArcIdentitySource>? sources,
        IWikidataLookup? wikidata,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        if (sources is null)
        {
            using var context = PaperbunkrDb.CreateContext();
            sources = ProviderArcIdentitySource.CreateAvailable(context);
        }

        var identity = await StoryEventIdentitySweep.RunAsync(
            () => PaperbunkrDb.CreateContext(), sources, null, StoryEventIdentitySweep.DefaultBatchSize, progress, cancellationToken).ConfigureAwait(false);
        // Matched issues are dated by the Grand Comics Database when its data is installed (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4).
        using var gcd = Paperbunkr.Data.Gcd.GcdDataStore.TryOpen();
        var connector = await EventConnectorSweep.RunAsync(
            () => PaperbunkrDb.CreateContext(), wikidata ?? new WikidataClient(WikidataClient.CreateClient()),
            WikidataEventLinks.DefaultBatchSize, cancellationToken, gcd).ConfigureAwait(false);
        return new Result(identity, connector);
    }
}
