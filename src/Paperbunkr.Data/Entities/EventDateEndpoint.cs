namespace Paperbunkr.Data.Entities;

public sealed class EventDateEndpoint
{
    public int StoryEventId { get; set; }
    public StoryEvent StoryEvent { get; set; } = null!;
    public bool IsEnd { get; set; }
    public int Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }
    public bool Uncertain { get; set; }
    public bool Provisional { get; set; }
    public string Basis { get; set; } = "Manual";
    public string? Coverage { get; set; }
}
