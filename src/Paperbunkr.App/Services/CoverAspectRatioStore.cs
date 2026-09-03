using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// Process-wide learned cache of each issue's cover aspect ratio (width / height), so the Panorama
/// grid can render every cover at its true shape while virtualized without the panel eagerly
/// decoding every cover just to measure it (the exact eager-decode the 2026-08-22 cover-memory
/// virtualization work removed). A static holder for the same reason as <see cref="CoverImageCache"/>
/// - this app has no DI container and both a static attached-property handler
/// (<see cref="Paperbunkr.App.Views.AsyncCoverImage"/>) and view models need to reach it.
///
/// Two feeds, per the 2026-09-03 design:
/// <list type="bullet">
///   <item><b>At generation</b> - <see cref="CoverThumbnailService"/> writes <c>Issue.CoverAspectRatio</c>
///   straight to the DB when it builds a thumbnail.</item>
///   <item><b>Progressively, on screen</b> - <see cref="Report"/> is called with the real pixel size
///   of each cover bitmap as it decodes during browsing; genuinely new/changed values are batched
///   to <c>Issue.CoverAspectRatio</c> on a short debounce and a (debounced) <see cref="RatiosLearned"/>
///   lets Library re-pack Panorama.</item>
/// </list>
/// Once a library has been scanned once (<see cref="CoverThumbnailService.GenerateAllAsync"/> runs
/// <see cref="CoverThumbnailService.BackfillAspectRatios"/> after generating), every ratio is
/// persisted and this store just serves warm reads with no writes or reflow.
/// </summary>
public static class CoverAspectRatioStore
{
    /// <summary>Two ratios within this are "the same cover" - avoids a pointless DB write / reflow
    /// from sub-pixel rounding differences between the decode path and the stored value.</summary>
    private const double Epsilon = 0.01;

    private const int FlushDebounceMs = 1500;
    private const int EventDebounceMs = 500;

    private static readonly ConcurrentDictionary<int, double> s_ratios = new();
    private static readonly object s_pendingLock = new();
    private static readonly HashSet<int> s_pending = new();

    private static readonly Timer s_flushTimer = new(_ => Flush());
    private static readonly Timer s_eventTimer = new(_ => RatiosLearned?.Invoke(null, EventArgs.Empty));

    /// <summary>
    /// Opens a context for the debounced write-back. <see langword="null"/> until the app wires it
    /// at startup (<c>App.axaml.cs</c>) - which means under test, where it's never wired, the
    /// progressive write-back is inert and a stray <see cref="Report"/> from a decode-path test can
    /// never touch the real per-user database. Tests that specifically exercise <see cref="Flush"/>
    /// set it to a temp-database factory and clear it again via <see cref="ResetForTests"/>.
    /// </summary>
    internal static Func<PaperbunkrDbContext>? ContextFactory { get; set; }

    /// <summary>Raised (thread-pool thread, debounced) after one or more genuinely new or changed
    /// ratios are learned from on-screen covers. Subscribers marshal to their own thread.</summary>
    public static event EventHandler? RatiosLearned;

    /// <summary>Known ratio for <paramref name="issueId"/>, or null if nothing has recorded one yet.</summary>
    public static double? Get(int issueId) => s_ratios.TryGetValue(issueId, out var r) ? r : null;

    /// <summary>
    /// Seeds ratios already persisted in the DB (called from <c>LibraryScreenViewModel</c>'s load)
    /// so a fresh session starts warm - no write-back, no <see cref="RatiosLearned"/>.
    /// </summary>
    public static void Prime(IEnumerable<(int IssueId, double Ratio)> known)
    {
        foreach (var (id, ratio) in known)
        {
            if (ratio > 0 && !double.IsNaN(ratio) && !double.IsInfinity(ratio))
            {
                s_ratios[id] = ratio;
            }
        }
    }

    /// <summary>
    /// Records the aspect ratio observed from a decoded cover bitmap. No-ops when the value is
    /// non-positive/degenerate or already known within <see cref="Epsilon"/>; otherwise stores it
    /// and schedules a debounced DB write plus a debounced <see cref="RatiosLearned"/>.
    /// </summary>
    public static void Report(int issueId, double pixelWidth, double pixelHeight)
    {
        if (pixelWidth > 0 && pixelHeight > 0)
        {
            ReportRatio(issueId, pixelWidth / pixelHeight);
        }
    }

    /// <summary>As <see cref="Report"/>, for a caller that already has the ratio (e.g. cover
    /// generation, which measures the source page directly).</summary>
    public static void ReportRatio(int issueId, double ratio)
    {
        if (ratio <= 0 || double.IsNaN(ratio) || double.IsInfinity(ratio))
        {
            return;
        }

        if (s_ratios.TryGetValue(issueId, out var existing) && Math.Abs(existing - ratio) < Epsilon)
        {
            return;
        }

        s_ratios[issueId] = ratio;
        lock (s_pendingLock)
        {
            s_pending.Add(issueId);
        }

        s_flushTimer.Change(FlushDebounceMs, Timeout.Infinite);
        s_eventTimer.Change(EventDebounceMs, Timeout.Infinite);
    }

    /// <summary>Writes every pending learned ratio to <c>Issue.CoverAspectRatio</c> in one batch.
    /// Swallows all failure - a missed persist just means the value is re-learned next session.</summary>
    private static void Flush()
    {
        var factory = ContextFactory;
        if (factory is null)
        {
            return;
        }

        int[] ids;
        lock (s_pendingLock)
        {
            if (s_pending.Count == 0)
            {
                return;
            }

            ids = s_pending.ToArray();
            s_pending.Clear();
        }

        try
        {
            using var context = factory();
            var rows = context.Issues.Where(i => ids.Contains(i.Id)).ToList();
            foreach (var row in rows)
            {
                if (s_ratios.TryGetValue(row.Id, out var ratio))
                {
                    row.CoverAspectRatio = ratio;
                }
            }

            context.SaveChanges();
        }
        catch (Exception)
        {
            // Best-effort. Re-queue so a later flush retries.
            lock (s_pendingLock)
            {
                foreach (int id in ids)
                {
                    s_pending.Add(id);
                }
            }
        }
    }

    /// <summary>Test-only: drop all learned state and pending writes.</summary>
    internal static void ResetForTests()
    {
        s_ratios.Clear();
        lock (s_pendingLock)
        {
            s_pending.Clear();
        }

        s_flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
        s_eventTimer.Change(Timeout.Infinite, Timeout.Infinite);
        RatiosLearned = null;
        ContextFactory = null;
    }

    /// <summary>Test-only: run the pending DB write synchronously now.</summary>
    internal static void FlushNowForTests() => Flush();
}
