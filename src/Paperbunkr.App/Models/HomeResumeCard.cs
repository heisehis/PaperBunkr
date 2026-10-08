using Avalonia.Media;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Models;

/// <summary>
/// One card in Home's merged Continue Reading row (docs/superpowers/specs/2026-09-28-home-improvements-design.md I2): wraps
/// either a comic (<see cref="Comic"/>) or a book (<see cref="Book"/>) so one row can hold both in last-read order.
/// </summary>
public sealed class HomeResumeCard
{
    public HomeContinueReadingCard? Comic { get; init; }

    public HomeBookCard? Book { get; init; }

    public bool IsBook => Book is not null;

    public string Title => Book?.Title ?? Comic?.Series.Name ?? string.Empty;

    /// <summary>Issue number badge for comics; books show their author line instead.</summary>
    public string? Badge => Comic?.ResumeIssueBadge;

    /// <summary>A book's author line; a comic's time-left estimate, when there is one.</summary>
    public string? Meta => Book is not null ? Book.Author : Comic?.TimeLeft;

    public bool ShowProgress => Comic is not null || Book?.ShowProgress == true;

    public double ProgressFraction => Comic?.ResumeProgressFraction ?? Book?.ProgressFraction ?? 0;

    /// <summary>Kept for the comic side's existing callers/tests - 0 for a book.</summary>
    public int ResumeIssueId => Comic?.ResumeIssueId ?? 0;

    public double ResumeProgressFraction => ProgressFraction;

    /// <summary>Resolved on read, the same way the two cover converters do - comics and books use separate caches.</summary>
    public IImage? CoverImage => Book is { } book
        ? (book.CoverKey is string bookKey ? BookCoverImageCache.Get(bookKey) : null)
        : (Comic?.Series.CoverKey is string key ? CoverImageCache.Get(key) : null);
}
