namespace Paperbunkr.Data.Entities;

/// <summary>A user-curated tag owned by a Story Event. Comic-derived tags are projected at read time.</summary>
public class StoryEventTag
{
    public int Id { get; set; }
    public int StoryEventId { get; set; }
    public StoryEvent? StoryEvent { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Category { get; set; } = "Event";
    public IssueTagWeight Weight { get; set; }
}
