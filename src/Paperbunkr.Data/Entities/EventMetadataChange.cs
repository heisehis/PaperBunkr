namespace Paperbunkr.Data.Entities;

public sealed class EventMetadataChange
{
    public long Id { get; set; }
    public Guid OperationId { get; set; }
    public int StoryEventId { get; set; }
    public StoryEvent StoryEvent { get; set; } = null!;
    public EventMetadataField Field { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }
    public bool BeforeProtected { get; set; }
    public string AppliedRevision { get; set; } = string.Empty;
    public string? EvidenceJson { get; set; }
    public bool Undone { get; set; }
    public bool MergeInvalidated { get; set; }
}
