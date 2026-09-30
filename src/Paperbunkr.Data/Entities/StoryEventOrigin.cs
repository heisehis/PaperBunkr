namespace Paperbunkr.Data.Entities;

/// <summary>Who made a <see cref="StoryEvent"/> (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §1).</summary>
public enum StoryEventOrigin
{
    /// <summary>Made by hand (New Event dialog, "+ New"), or renamed by the user. Never merged without asking.</summary>
    User = 0,

    /// <summary>Created from provider data (an accepted story-arc suggestion or an Issue Properties look-up).</summary>
    Provider = 1,
}
