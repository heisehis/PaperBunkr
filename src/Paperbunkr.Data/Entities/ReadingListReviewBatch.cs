namespace Paperbunkr.Data.Entities;

/// <summary>Database-local link history. Saved in the transaction that changes the links.</summary>
public sealed class ReadingListReviewBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Label { get; set; } = string.Empty;
    public string Payload { get; set; } = "[]";
}
