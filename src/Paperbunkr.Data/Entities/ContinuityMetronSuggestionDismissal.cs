namespace Paperbunkr.Data.Entities;

/// <summary>A persisted dismissal of a specific Metron universe suggestion for one local series.</summary>
public class ContinuityMetronSuggestionDismissal
{
    public int Id { get; set; }
    public int SeriesId { get; set; }
    public Series? Series { get; set; }
    public int MetronUniverseId { get; set; }
    public DateTime DismissedAt { get; set; } = DateTime.UtcNow;
}
