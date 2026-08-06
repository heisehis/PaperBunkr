namespace Paperbunkr.App.Models;

/// <summary>One row in the Needs Review queue's Content Type section.</summary>
public class SeriesReviewItem
{
    public int SeriesId { get; init; }

    public string SeriesName { get; init; } = string.Empty;
}
