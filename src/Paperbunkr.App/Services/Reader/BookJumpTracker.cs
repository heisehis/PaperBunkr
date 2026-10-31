using System;
using System.Globalization;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// Decides whether a navigation inside a book is a "large" jump worth offering a jump-back chip for
/// (docs/superpowers/specs/2026-10-27-books-slice3a-reader-jumpback-session-design.md §2). A different
/// chapter always is; inside one chapter it is when the block ids are numeric (<c>pb-p&lt;n&gt;</c>) and
/// at least <see cref="BlockThreshold"/> blocks apart, otherwise when the scroll fraction moved by more
/// than <see cref="FractionThreshold"/>.
/// </summary>
public static class BookJumpTracker
{
    public const double FractionThreshold = 0.15;
    public const int BlockThreshold = 10;

    public static bool IsLargeJump(BookPosition from, BookPosition to)
    {
        if (from.ChapterIndex != to.ChapterIndex)
            return true;

        if (TryBlockNumber(from.BlockId, out var a) && TryBlockNumber(to.BlockId, out var b))
            return Math.Abs(a - b) >= BlockThreshold;

        return Math.Abs(from.ProgressionFraction - to.ProgressionFraction) > FractionThreshold;
    }

    private static bool TryBlockNumber(string? blockId, out int number)
    {
        number = 0;
        const string prefix = "pb-p";
        if (string.IsNullOrEmpty(blockId) || !blockId.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        return int.TryParse(blockId.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
