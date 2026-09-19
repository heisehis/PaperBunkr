using System;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Get-or-create for <see cref="Entities.StoryEvent"/>, the dedup-by-name path
/// <see cref="EventMembershipResolver"/> never had (docs/superpowers/specs/2026-09-17-storyevent-
/// continuity-autopopulate-design.md) - needed so accepting two separate
/// <see cref="StoryArcGroupingResolver"/> candidates for the same arc name doesn't create duplicate
/// <see cref="Entities.StoryEvent"/> rows. Same case-insensitive shape as
/// <see cref="ContinuityResolver.GetOrCreate"/>.
/// </summary>
internal static class StoryEventResolver
{
    /// <summary>Case-insensitive name match before inserting, so retyping ("Civil War" vs "civil war") doesn't create a near-duplicate event.</summary>
    public static StoryEvent GetOrCreate(PaperbunkrDbContext context, string name)
    {
        string trimmed = name.Trim();
        var events = context.StoryEvents.ToList();
        var existing = events.FirstOrDefault(e => string.Equals(e.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            // Punctuation-variant fold (docs/superpowers/specs/2026-09-17-series-name-matching-and-
            // empty-row-cleanup-design.md) - "Cataclysm: The Ultimates" vs "Cataclysm - The
            // Ultimates" shouldn't spawn a second StoryEvent for the same crossover.
            ?? events.FirstOrDefault(e => TitleNormalizer.NamesMatch(e.Name, trimmed, ignoreVolume: false));
        if (existing is not null)
        {
            return existing;
        }

        var storyEvent = new StoryEvent { Name = trimmed, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        return storyEvent;
    }
}
