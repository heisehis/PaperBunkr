using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.AdDetection;

/// <summary>The fixed knobs of ad-page detection (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md §5) - constants, not settings.</summary>
public static class AdDetectionLimits
{
    /// <summary>Pages hashed from the front of an issue (ads and house ads sit around the cover).</summary>
    public const int LeadingPages = 3;

    /// <summary>Pages hashed from the back of an issue (where most ads live).</summary>
    public const int TrailingPages = 10;

    /// <summary>A page within this Hamming distance of a known ad is proposed.</summary>
    public const int MatchDistance = 6;

    /// <summary>A newly tagged ad within this distance of one already in the library is not added again.</summary>
    public const int DuplicateDistance = 2;

    /// <summary>The 0-based page indices the scan looks at for an issue of <paramref name="pageCount"/> pages.</summary>
    public static IReadOnlyList<int> WindowFor(int pageCount)
    {
        var pages = new SortedSet<int>();
        for (int i = 0; i < Math.Min(LeadingPages, pageCount); i++)
        {
            pages.Add(i);
        }

        for (int i = Math.Max(0, pageCount - TrailingPages); i < pageCount; i++)
        {
            pages.Add(i);
        }

        return pages.ToList();
    }
}

/// <summary>What one scan did.</summary>
public sealed record AdScanResult(int IssuesScanned, int PagesHashed, int ProposalsCreated, int IssuesFailed);

/// <summary>Ad-page detection as the reader and the scheduler see it (a seam so tests need no real scan).</summary>
public interface IAdHashSeeder
{
    /// <summary>The user tagged this page Advertisement: remember what it looks like.</summary>
    Task SeedAsync(int issueId, int pageNumber);

    /// <summary>The user un-tagged this page: forget the ad it seeded (a wrong tag must not keep producing proposals).</summary>
    Task UnseedAsync(int issueId, int pageNumber);
}

/// <summary>
/// Finds probable advertisement pages by comparing perceptual hashes against the ad library (docs/superpowers/
/// specs/2026-09-21-comic-reader-page-intelligence-design.md §5, pitch #9). For each linked issue it hashes the
/// first 3 and last 10 pages (only when the file changed since they were last hashed), skips pages that already
/// carry a tag or a proposal, and creates Pending <see cref="AdPageProposal"/> rows for pages within
/// <see cref="AdDetectionLimits.MatchDistance"/> of a known ad. It never tags anything itself, and a page that has
/// ever had a proposal - Rejected included - is never proposed again. Runs off the UI thread; a file that cannot be
/// read is counted and skipped.
/// </summary>
public sealed class AdPageDetectionService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public AdPageDetectionService(Func<PaperbunkrDbContext>? contextFactory = null)
    {
        _contextFactory = contextFactory ?? (() => PaperbunkrDb.CreateContext());
    }

    public async Task<AdScanResult> ScanAsync(IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken)
    {
        return await Task.Run(() => Scan(progress, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    internal AdScanResult Scan(IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken)
    {
        using var context = _contextFactory();

        var ads = context.AdPageHashes.AsNoTracking().OrderBy(a => a.Id).ToList();
        if (ads.Count == 0)
        {
            // Nothing to match against yet; hashing the library now would be wasted work. The first ad the user tags seeds the library.
            return new AdScanResult(0, 0, 0, 0);
        }

        var issues = context.Issues
            .Where(i => !i.IsPlaceholder && !i.FileIsMissing && i.FilePath != null)
            .Select(i => new { i.Id, i.FilePath })
            .ToList();

        int scanned = 0, hashed = 0, proposals = 0, failed = 0;
        progress?.Report((0, issues.Count));
        foreach (var issue in issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var (pagesHashed, created) = ScanIssue(context, issue.Id, issue.FilePath!, ads, cancellationToken);
                hashed += pagesHashed;
                proposals += created;
                scanned++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                failed++;
                context.ChangeTracker.Clear();
            }

            progress?.Report((scanned + failed, issues.Count));
        }

        return new AdScanResult(scanned, hashed, proposals, failed);
    }

    private static (int PagesHashed, int ProposalsCreated) ScanIssue(
        PaperbunkrDbContext context, int issueId, string filePath, IReadOnlyList<AdPageHash> ads, CancellationToken cancellationToken)
    {
        string? stamp = ContentStampOf(filePath);
        if (stamp is null)
        {
            return (0, 0);
        }

        var taggedPages = context.IssuePages
            .Where(p => p.IssueId == issueId && p.PageType != PageType.Story)
            .Select(p => p.PageNumber)
            .ToHashSet();
        var proposedPages = context.AdPageProposals.Where(p => p.IssueId == issueId).Select(p => p.PageNumber).ToHashSet();
        var cached = context.PageHashes.Where(h => h.IssueId == issueId).ToDictionary(h => h.PageNumber);

        var provider = Paperbunkr.App.Services.PageDecodeCore.TryOpenProvider(filePath);
        if (provider is null)
        {
            throw new IOException($"Cannot open {filePath}");
        }

        int hashedNow = 0, created = 0;
        try
        {
            foreach (int page in AdDetectionLimits.WindowFor(provider.Count))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (taggedPages.Contains(page) || proposedPages.Contains(page))
                {
                    continue;
                }

                long hash;
                if (cached.TryGetValue(page, out var row) && row.ContentStamp == stamp)
                {
                    hash = row.Hash;
                }
                else
                {
                    long? computed = HashPage(provider, page);
                    if (computed is null)
                    {
                        continue;
                    }

                    hash = computed.Value;
                    hashedNow++;
                    if (row is null)
                    {
                        context.PageHashes.Add(new PageHash { IssueId = issueId, PageNumber = page, Hash = hash, ContentStamp = stamp });
                    }
                    else
                    {
                        row.Hash = hash;
                        row.ContentStamp = stamp;
                    }
                }

                var best = NearestAd(ads, hash);
                if (best is { Distance: <= AdDetectionLimits.MatchDistance } match)
                {
                    context.AdPageProposals.Add(new AdPageProposal
                    {
                        IssueId = issueId,
                        PageNumber = page,
                        MatchedAdHashId = match.Ad.Id,
                        Distance = match.Distance,
                        Status = AdPageProposalStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                    });
                    created++;
                }
            }
        }
        finally
        {
            provider.Dispose();
        }

        context.SaveChanges();
        return (hashedNow, created);
    }

    internal static (AdPageHash Ad, int Distance)? NearestAd(IReadOnlyList<AdPageHash> ads, long hash)
    {
        (AdPageHash Ad, int Distance)? best = null;
        foreach (var ad in ads)
        {
            int distance = PageHasher.Distance(ad.Hash, hash);
            if (best is null || distance < best.Value.Distance)
            {
                best = (ad, distance);
            }
        }

        return best;
    }

    /// <summary>Size and modified time of the file, as the marker for "this is the content I hashed". Null when the file is gone.</summary>
    internal static string? ContentStampOf(string filePath)
    {
        var info = new FileInfo(filePath);
        return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : null;
    }

    /// <summary>Hashes one page of an open archive: the raw bytes first (no extra decode), the engine's own decode for formats Skia cannot read.</summary>
    internal static long? HashPage(cYo.Projects.ComicRack.Engine.IO.Provider.ImageProvider provider, int page)
    {
        try
        {
            long? fromBytes = PageHasher.TryCompute(provider.GetByteImage(page));
            if (fromBytes is not null)
            {
                return fromBytes;
            }
        }
        catch
        {
            // Not readable as raw bytes - fall through to the engine decode.
        }

        try
        {
            using var bitmap = Paperbunkr.App.Services.PageDecodeCore.Decode(provider, page);
            return PageHasher.TryCompute(bitmap);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Keeps the ad library in step with what the user tags in the reader (docs/superpowers/specs/2026-09-21-comic-
/// reader-page-intelligence-design.md §5). Tagging a page Advertisement hashes it and adds it (unless a hash within
/// <see cref="AdDetectionLimits.DuplicateDistance"/> is already there); un-tagging removes the hash that page
/// sourced, along with any still-Pending proposals it produced.
/// </summary>
public sealed class AdHashSeeder : IAdHashSeeder
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public AdHashSeeder(Func<PaperbunkrDbContext>? contextFactory = null)
    {
        _contextFactory = contextFactory ?? (() => PaperbunkrDb.CreateContext());
    }

    public Task SeedAsync(int issueId, int pageNumber) => Task.Run(() => Seed(issueId, pageNumber));

    public Task UnseedAsync(int issueId, int pageNumber) => Task.Run(() => Unseed(issueId, pageNumber));

    internal void Seed(int issueId, int pageNumber)
    {
        try
        {
            using var context = _contextFactory();
            string? path = context.Issues.Where(i => i.Id == issueId).Select(i => i.FilePath).FirstOrDefault();
            if (path is null)
            {
                return;
            }

            long? hash = HashFile(path, pageNumber);
            if (hash is null)
            {
                return;
            }

            // The page the user tagged by hand may have a pending proposal in Needs Review: resolve it, do not leave it dangling.
            var pending = context.AdPageProposals.FirstOrDefault(p => p.IssueId == issueId && p.PageNumber == pageNumber && p.Status == AdPageProposalStatus.Pending);
            if (pending is not null)
            {
                pending.Status = AdPageProposalStatus.Accepted;
                pending.ResolvedAt = DateTime.UtcNow;
            }

            bool known = context.AdPageHashes.AsEnumerable().Any(a => PageHasher.Distance(a.Hash, hash.Value) <= AdDetectionLimits.DuplicateDistance);
            if (!known)
            {
                context.AdPageHashes.Add(new AdPageHash { Hash = hash.Value, SourceIssueId = issueId, SourcePageNumber = pageNumber, CreatedAt = DateTime.UtcNow });
            }

            context.SaveChanges();
        }
        catch
        {
            // Best-effort background work: a page that cannot be hashed simply does not seed the library.
        }
    }

    internal void Unseed(int issueId, int pageNumber)
    {
        try
        {
            using var context = _contextFactory();
            var seeded = context.AdPageHashes.Where(a => a.SourceIssueId == issueId && a.SourcePageNumber == pageNumber).ToList();
            if (seeded.Count == 0)
            {
                return;
            }

            var ids = seeded.Select(a => a.Id).ToList();
            context.AdPageProposals.RemoveRange(context.AdPageProposals.Where(p => p.MatchedAdHashId != null && ids.Contains(p.MatchedAdHashId.Value) && p.Status == AdPageProposalStatus.Pending));
            context.AdPageHashes.RemoveRange(seeded);
            context.SaveChanges();
        }
        catch
        {
            // Best-effort, same as Seed.
        }
    }

    private static long? HashFile(string filePath, int pageNumber)
    {
        var provider = Paperbunkr.App.Services.PageDecodeCore.TryOpenProvider(filePath);
        if (provider is null)
        {
            return null;
        }

        try
        {
            return pageNumber >= 0 && pageNumber < provider.Count ? AdPageDetectionService.HashPage(provider, pageNumber) : null;
        }
        finally
        {
            provider.Dispose();
        }
    }
}
