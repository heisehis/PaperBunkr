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
        var existing = context.StoryEvents.FirstOrDefault(e => e.Name.ToLower() == trimmed.ToLower());
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
