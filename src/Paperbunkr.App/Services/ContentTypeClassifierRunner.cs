using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>
/// Wires the tracker-driven content-type classifier (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md) to the real providers
/// and the user's settings. The scheduled task, Preferences' "Classify library" and the Library's "Classify selected" all run through here.
/// </summary>
public static class ContentTypeClassifierRunner
{
    /// <summary>Searches per scheduled run. AniList is throttled to one request per two seconds, so this keeps a run to a few minutes; the next run resumes where it stopped.</summary>
    public const int ScheduledBudget = 150;

    /// <summary>Searches for a "Classify library" press or a "Classify selected" - larger than a scheduled run since a person asked for it.</summary>
    public const int ManualBudget = 400;

    // One AniList instance for the process: its 30-requests-a-minute throttle is per instance (see MangaBakaMetadataProvider.Shared).
    private static readonly AniListMetadataProvider AniList = new(AniListHttpClient.Shared);

    public static ContentTypeClassificationService CreateService() => new(new IContentTypeEvidenceSource[]
    {
        new MetadataProviderEvidenceSource(MangaBakaMetadataProvider.Shared),
        new MetadataProviderEvidenceSource(AniList),
        new MetadataProviderEvidenceSource(MangaDexMetadataProvider.Shared),
    });

    public static async Task<ContentTypeBatchResult> RunAsync(
        int budget,
        Action<int, int>? report,
        CancellationToken cancellationToken,
        IReadOnlyCollection<int>? onlySeriesIds = null,
        Func<PaperbunkrDbContext>? contextFactory = null,
        ContentTypeClassificationService? service = null)
    {
        contextFactory ??= PaperbunkrDb.CreateContext;
        bool askBefore;
        using (var context = contextFactory())
        {
            askBefore = context.GetOrCreateAppSettings().AskBeforeClassifying;
        }

        return await (service ?? CreateService())
            .ClassifyBatchAsync(contextFactory, askBefore, budget, report, cancellationToken, onlySeriesIds)
            .ConfigureAwait(false);
    }

    /// <summary>One line for the Activity Center and the scheduler's history.</summary>
    public static string Summarize(ContentTypeBatchResult result)
    {
        if (result.Applied + result.Queued + result.NoMatch + result.Skipped == 0)
        {
            return result.Unavailable > 0 ? "The tracker sites didn't respond, so nothing was checked" : "Nothing to classify";
        }

        var parts = new List<string>();
        if (result.Applied > 0) parts.Add($"{result.Applied} classified");
        if (result.Queued > 0) parts.Add($"{result.Queued} waiting for your review");
        if (result.NoMatch > 0) parts.Add($"{result.NoMatch} with no match");
        if (result.Skipped > 0) parts.Add($"{result.Skipped} skipped as Western");
        if (result.Unavailable > 0) parts.Add($"{result.Unavailable} couldn't be reached");
        string text = string.Join(", ", parts);
        return result.BudgetReached ? text + " (stopped at this run's limit; the next run continues)" : text;
    }
}
