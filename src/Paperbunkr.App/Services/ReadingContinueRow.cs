using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services;

/// <summary>What the Continue row needs to know about one reading list.</summary>
public sealed record ContinueCandidate(int ListId, int ReadCount, int OwnedUnreadCount, DateTime? LastActivityUtc);

/// <summary>
/// The gallery's "Continue reading" strip (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §2, decision Q11): lists
/// you have started (at least one issue read) and can still read (at least one owned, unread issue), most recently read first, at most
/// <see cref="MaxCards"/>. Recency is the latest <c>ReadingEvent</c> on any member issue; a list with reads but no logged event sorts last.
/// </summary>
public static class ReadingContinueRow
{
    public const int MaxCards = 8;

    public static IReadOnlyList<int> Select(IEnumerable<ContinueCandidate> candidates) =>
        candidates
            .Where(c => c.ReadCount > 0 && c.OwnedUnreadCount > 0)
            .OrderByDescending(c => c.LastActivityUtc ?? DateTime.MinValue)
            .ThenBy(c => c.ListId)
            .Take(MaxCards)
            .Select(c => c.ListId)
            .ToList();
}
