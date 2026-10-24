using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.Services;

/// <summary>
/// "Check story events" - the weekly task and the Story Events sidebar button run exactly this: the Story Event resolver (ids, merges;
/// docs/superpowers/specs/2026-09-27-story-event-resolver-design.md), then the outside reading orders (Comic Book Reading Orders and Metron's
/// EVENT lists; docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4), then the smart connector (which turns what they said into
/// relations), in that order so two halves of a duplicate are never linked and a link is never made from stale ids.
/// </summary>
public static class StoryEventChecks
{
    /// <summary>The reading-order sources one run asks. A null source is skipped; <see cref="Live"/> builds the real ones.</summary>
    public sealed record EventOrderInputs(ICbroTimeline? Cbro, IMetronEventLists? Metron, IMetronUniverses? Universes = null)
    {
        public static EventOrderInputs None { get; } = new(null, null);

        /// <summary>CBRO always (no credentials); Metron only when its login is saved.</summary>
        public static EventOrderInputs Live()
        {
            using var context = PaperbunkrDb.CreateContext();
            var metron = ComicProviderFactory.Create(context, ComicProvider.Metron, ComicVineRequestPriority.Low);
            return new EventOrderInputs(new ComicBookReadingOrdersSource(), metron as IMetronEventLists, metron as IMetronUniverses);
        }
    }

    public sealed record Result(StoryEventIdentitySweepSummary Identity, EventOrderSummary Order, EventConnectorSummary Connector, int UniversesUpdated = 0)
    {
        public string Text => Identity.Merges.Count > 0
            ? $"{Identity.Text} · {Order.Text} · {Connector.Text}\n{Identity.MergeDetails}"
            : $"{Identity.Text} · {Order.Text} · {Connector.Text}";
    }

    public static async Task<string> RunAsync(IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken) =>
        (await RunDetailedAsync(null, null, progress, cancellationToken).ConfigureAwait(false)).Text;

    /// <param name="sources">Provider sources for id completion; null = the saved credentials.</param>
    /// <param name="orderInputs">Reading-order sources; null = the live ones.</param>
    public static async Task<Result> RunDetailedAsync(
        IReadOnlyDictionary<ComicProvider, IArcIdentitySource>? sources,
        EventOrderInputs? orderInputs,
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

        orderInputs ??= EventOrderInputs.Live();
        var order = await EventOrderSweep.RunAsync(
            () => PaperbunkrDb.CreateContext(), orderInputs.Cbro, orderInputs.Metron, EventOrderSweep.DefaultMetronBatchSize, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);

        // Matched issues are dated by the Grand Comics Database when its data is installed (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4).
        using var gcd = Paperbunkr.Data.Gcd.GcdDataStore.TryOpen();
        var connector = await EventConnectorSweep.RunAsync(() => PaperbunkrDb.CreateContext(), cancellationToken, gcd).ConfigureAwait(false);

        int universes = orderInputs.Universes is null
            ? 0
            : await ContinuityUniverseSweep.RunAsync(() => PaperbunkrDb.CreateContext(), orderInputs.Universes, ContinuityUniverseSweep.DefaultBatchSize, cancellationToken).ConfigureAwait(false);
        return new Result(identity, order, connector, universes);
    }
}
