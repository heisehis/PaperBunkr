namespace Paperbunkr.Data.Entities;

public enum EventMetadataField { Name, Description, Cover, StartDate, EndDate, Types, Tags }

/// <summary>Persisted clearing intent and revision; a partial date is never an empty field.</summary>
public sealed class EventMetadataState
{
    public int StoryEventId { get; set; }
    public StoryEvent StoryEvent { get; set; } = null!;
    public EventMetadataField Field { get; set; }
    public string Revision { get; set; } = Guid.NewGuid().ToString("N");
    public bool ProtectedBlank { get; set; }
    public string? EvidenceJson { get; set; }
}
