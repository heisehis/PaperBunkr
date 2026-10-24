namespace Paperbunkr.Data.Entities;

/// <summary>A user-selected structured type for one StoryEvent.</summary>
public sealed class StoryEventTypeAssignment
{
    public int StoryEventId { get; set; }
    public StoryEvent? StoryEvent { get; set; }
    public StoryEventType Type { get; set; }
}
