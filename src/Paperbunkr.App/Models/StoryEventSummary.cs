using System.Globalization;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Models;

/// <summary>Sidebar row for one <c>StoryEvent</c> (docs/superpowers/specs/2026-08-17-metadata-model-phase4b-story-events-design.md) - name,
/// member count, start year, and whether it's the currently open event. Mirrors the Reading Lists gallery tile (<see cref="ReadingGalleryTile"/>).</summary>
public class StoryEventSummary
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int MemberCount { get; init; }

    /// <summary>The event's start date year, else its earliest member's cover year; null when nothing is dated.</summary>
    public int? StartYear { get; init; }

    public bool IsActive { get; init; }

    /// <summary>The faint "2007 · 32" on the right of the row.</summary>
    public string MetaLabel => StartYear is int year
        ? $"{year.ToString(CultureInfo.InvariantCulture)} · {MemberCount.ToString("N0", CultureInfo.CurrentCulture)}"
        : MemberCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Deletes the whole event (docs/superpowers/specs/2026-08-22-delete-functionality-design.md).</summary>
    public required TwoStepConfirm DeleteConfirm { get; init; }
}
