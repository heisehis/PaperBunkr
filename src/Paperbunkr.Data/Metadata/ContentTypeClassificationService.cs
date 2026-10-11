using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One provider's lookup for the classifier. A seam so tests never touch the network.</summary>
public interface IContentTypeEvidenceSource
{
    ExternalMetadataProvider Provider { get; }

    /// <summary>
    /// The best-matching entry for the series' titles with its implied type, or null when nothing scored at the review threshold.
    /// Throws <see cref="MetadataProviderUnavailableException"/> when the call itself failed.
    /// </summary>
    Task<ContentTypeEvidenceItem?> GetEvidenceAsync(IReadOnlyList<string> knownTitles, CancellationToken cancellationToken);
}

/// <summary>Adapts an <see cref="IMetadataProvider"/> (MangaBaka, AniList, MangaDex): search by the primary title, score, fetch the best, map its type.</summary>
public sealed class MetadataProviderEvidenceSource : IContentTypeEvidenceSource
{
    private readonly IMetadataProvider _provider;

    public MetadataProviderEvidenceSource(IMetadataProvider provider) => _provider = provider;

    public ExternalMetadataProvider Provider => _provider.ProviderKey;

    public async Task<ContentTypeEvidenceItem?> GetEvidenceAsync(IReadOnlyList<string> knownTitles, CancellationToken cancellationToken)
    {
        if (knownTitles.Count == 0 || string.IsNullOrWhiteSpace(knownTitles[0]))
        {
            return null;
        }

        var results = await _provider.SearchAsync(knownTitles[0], cancellationToken).ConfigureAwait(false);
        var best = results
            .Select(r => (Result: r, Score: TitleMatchScorer.BestScore(knownTitles, r.Title)))
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();
        if (best.Result is null || best.Score < TitleMatchScorer.ReviewThreshold)
        {
            return null;
        }

        var metadata = await _provider.GetAsync(best.Result.ExternalId, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return null;
        }

        var mapped = ProviderContentTypeMapper.Map(_provider.ProviderKey, metadata);
        return new ContentTypeEvidenceItem(_provider.ProviderKey, best.Result.ExternalId, best.Result.Title, best.Score, mapped.Raw, mapped.Type, mapped.QueueOnly);
    }
}

public enum SeriesClassifyKind
{
    /// <summary>Not searched; <see cref="SeriesClassifyResult.SkipReason"/> says why.</summary>
    Skipped,

    /// <summary>Every source failed; nothing was written so the series is retried soon.</summary>
    Unavailable,

    NoMatch,
    Queued,
    Applied,
}

public sealed record SeriesClassifyResult(SeriesClassifyKind Kind, string? SkipReason = null);

public sealed record ContentTypeBatchResult(int Examined, int Applied, int Queued, int NoMatch, int Skipped, int Unavailable, bool BudgetReached);

/// <summary>
/// The tracker-driven content-type classifier (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md): skip gate, then each
/// source in order (MangaBaka, AniList, MangaDex) until the answer is certain, then <see cref="ContentTypeDecision"/>. It creates no
/// tracker link and enqueues no file write-back.
/// </summary>
public sealed class ContentTypeClassificationService
{
    public const int NoMatchRetryDays = 90;

    private readonly IReadOnlyList<IContentTypeEvidenceSource> _sources;
    private readonly Func<DateTime> _utcNow;

    public ContentTypeClassificationService(IReadOnlyList<IContentTypeEvidenceSource> sources, Func<DateTime>? utcNow = null)
    {
        _sources = sources;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Why a series is not worth searching: strong Western evidence a tracker would only contradict with a false match. Needs
    /// <see cref="Series.Issues"/> loaded. Null means "search it".
    /// </summary>
    public static string? SkipReason(Series series) => SkipReason(
        series.GcdSeriesId, series.ContentTypeSource, series.ContentType,
        series.Issues.Select(i => i.Publisher), series.Issues.Any(i => i.MetadataSource is not null));

    /// <summary>The same gate over plain values, so the review queue can evaluate it from a projection without loading every issue.</summary>
    public static string? SkipReason(int? gcdSeriesId, ContentTypeSource source, ContentType type, IEnumerable<string?> publishers, bool scrapedFromComicVineOrMetron)
    {
        if (gcdSeriesId is not null)
        {
            return "Matched in the Grand Comics Database";
        }

        if (publishers.Any(p => PublisherContentTypeClassifier.TryClassify(p, out var publisherType, out _) && publisherType == ContentType.Comic))
        {
            return "Western publisher";
        }

        if (scrapedFromComicVineOrMetron)
        {
            return "Scraped from ComicVine or Metron";
        }

        if (source == ContentTypeSource.Embedded && type == ContentType.Comic)
        {
            return "Marked not-manga in its files";
        }

        return null;
    }

    /// <summary>Whether the automatic sweep should look at the series now (a forced run on a selection ignores this).</summary>
    public static bool IsDue(Series series, DateTime nowUtc)
    {
        if (series.ContentTypeLocked || series.RemoteSourceId is not null || series.ContentTypeCheck == ContentTypeCheck.Skipped)
        {
            return false;
        }

        if (series.ContentType != ContentType.Unknown && !SeriesContentTypeEditor.IsGuess(series.ContentTypeSource))
        {
            return false;
        }

        if (series.ContentTypeSuggestion is not null)
        {
            return false;
        }

        return series.ContentTypeCheck == ContentTypeCheck.None
            || (series.ContentTypeCheck == ContentTypeCheck.NoMatch
                && (series.ContentTypeCheckedUtc is null || (nowUtc - series.ContentTypeCheckedUtc.Value).TotalDays >= NoMatchRetryDays));
    }

    /// <summary>Classifies one series in place (the caller saves). Needs <see cref="Series.Issues"/> and <see cref="Series.Titles"/> loaded.</summary>
    public async Task<SeriesClassifyResult> ClassifyAsync(Series series, bool askBefore, CancellationToken cancellationToken)
    {
        string? skip = SkipReason(series);
        if (skip is not null)
        {
            return new SeriesClassifyResult(SeriesClassifyKind.Skipped, skip);
        }

        var knownTitles = new[] { series.Name }.Concat(series.Titles.Select(t => t.Value)).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var corroboration = CorroboratingTypes(series);

        var evidence = new List<ContentTypeEvidenceItem>();
        int unavailable = 0;
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var item = await source.GetEvidenceAsync(knownTitles, cancellationToken).ConfigureAwait(false);
                if (item is not null)
                {
                    evidence.Add(item);
                }
            }
            catch (MetadataProviderUnavailableException)
            {
                unavailable++;
                continue;
            }

            // Stop as soon as the answer is certain - each further call is a throttled request.
            if (ContentTypeDecision.Decide(evidence, corroboration, askBefore: false).Kind == ContentTypeOutcomeKind.AutoApply)
            {
                break;
            }
        }

        if (evidence.Count == 0 && unavailable == _sources.Count && _sources.Count > 0)
        {
            return new SeriesClassifyResult(SeriesClassifyKind.Unavailable);
        }

        var outcome = ContentTypeDecision.Decide(evidence, corroboration, askBefore);
        var now = _utcNow();
        string? json = ContentTypeEvidence.Serialize(evidence);
        switch (outcome.Kind)
        {
            case ContentTypeOutcomeKind.AutoApply:
                SeriesContentTypeEditor.ApplyAuto(series, outcome.Type!.Value, outcome.Confidence, json, now);
                return new SeriesClassifyResult(SeriesClassifyKind.Applied);
            case ContentTypeOutcomeKind.Queue:
                SeriesContentTypeEditor.Queue(series, outcome.Type!.Value, outcome.Confidence, json, now);
                return new SeriesClassifyResult(SeriesClassifyKind.Queued);
            default:
                SeriesContentTypeEditor.MarkNoMatch(series, now);
                return new SeriesClassifyResult(SeriesClassifyKind.NoMatch);
        }
    }

    /// <summary>
    /// Walks the due series (never-checked first, then oldest check) spending at most <paramref name="searchBudget"/> searches, saving after each so an
    /// interrupted run keeps its progress. <paramref name="onlySeriesIds"/> forces exactly those series regardless of due-ness (a user's "Classify
    /// selected"); locked series are still never touched.
    /// </summary>
    public async Task<ContentTypeBatchResult> ClassifyBatchAsync(
        Func<PaperbunkrDbContext> contextFactory,
        bool askBefore,
        int searchBudget,
        Action<int, int>? report,
        CancellationToken cancellationToken,
        IReadOnlyCollection<int>? onlySeriesIds = null)
    {
        using var context = contextFactory();
        var query = context.Series
            .Where(s => !s.ContentTypeLocked && s.RemoteSourceId == null && s.ContentTypeCheck != ContentTypeCheck.Skipped && s.Issues.Any());
        if (onlySeriesIds is not null)
        {
            var ids = onlySeriesIds.ToList();
            query = query.Where(s => ids.Contains(s.Id));
        }

        var candidateIds = query
            .OrderBy(s => s.ContentTypeCheckedUtc == null ? 0 : 1)
            .ThenBy(s => s.ContentTypeCheckedUtc)
            .ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToList();

        int examined = 0, applied = 0, queued = 0, noMatch = 0, skipped = 0, unavailable = 0, searches = 0, consecutiveUnavailable = 0;
        bool budgetReached = false;
        var now = _utcNow();

        foreach (int id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (searches >= searchBudget)
            {
                budgetReached = true;
                break;
            }

            var series = context.Series.Include(s => s.Issues).Include(s => s.Titles).First(s => s.Id == id);
            if (onlySeriesIds is null && !IsDue(series, now))
            {
                continue;
            }

            examined++;
            report?.Invoke(examined, candidateIds.Count);
            var result = await ClassifyAsync(series, askBefore, cancellationToken).ConfigureAwait(false);
            switch (result.Kind)
            {
                case SeriesClassifyKind.Skipped:
                    skipped++;
                    continue;
                case SeriesClassifyKind.Unavailable:
                    unavailable++;
                    searches++;
                    // A provider outage: stop rather than burn the budget failing on every series.
                    if (++consecutiveUnavailable >= 5)
                    {
                        return new ContentTypeBatchResult(examined, applied, queued, noMatch, skipped, unavailable, false);
                    }

                    continue;
                case SeriesClassifyKind.Applied:
                    applied++;
                    break;
                case SeriesClassifyKind.Queued:
                    queued++;
                    break;
                default:
                    noMatch++;
                    break;
            }

            consecutiveUnavailable = 0;
            searches++;
            context.SaveChanges();
        }

        return new ContentTypeBatchResult(examined, applied, queued, noMatch, skipped, unavailable, budgetReached);
    }

    private static IReadOnlyCollection<ContentType> CorroboratingTypes(Series series)
    {
        var types = new HashSet<ContentType>();
        foreach (var issue in series.Issues)
        {
            if (PublisherContentTypeClassifier.TryClassify(issue.Publisher, out var type, out _))
            {
                types.Add(type);
            }
        }

        if (series.ContentType != ContentType.Unknown && SeriesContentTypeEditor.IsGuess(series.ContentTypeSource))
        {
            types.Add(series.ContentType);
        }

        return types;
    }
}
