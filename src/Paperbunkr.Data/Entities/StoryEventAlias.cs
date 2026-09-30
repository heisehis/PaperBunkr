namespace Paperbunkr.Data.Entities;

/// <summary>
/// Another name a <see cref="StoryEvent"/> is known by (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §1) -
/// a merged-away duplicate's name, or the other provider's spelling of the arc ("Hulk: Planet Hulk" on a "Planet Hulk" event).
/// Creating, grouping and verifying match on <see cref="Key"/>, so a spelling that was merged away is never re-created.
/// </summary>
public class StoryEventAlias
{
    public int Id { get; set; }

    public int StoryEventId { get; set; }

    public StoryEvent? StoryEvent { get; set; }

    /// <summary>The name as written.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The lower-cased <c>TitleNormalizer.StripDown</c> form used for matching.</summary>
    public string Key { get; set; } = string.Empty;

    public StoryEventAliasSource Source { get; set; }
}

public enum StoryEventAliasSource
{
    /// <summary>The name of an event merged into this one.</summary>
    Merge = 0,

    /// <summary>The other provider's spelling, found during id completion.</summary>
    Provider = 1,
}
