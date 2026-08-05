using System.Collections.ObjectModel;

namespace Paperbunkr.App.Models;

/// <summary>Sample data for a Reading List's story-arc grouping.</summary>
public sealed class ReadingArcSample
{
    public required string Title { get; init; }
    public required ObservableCollection<ReadingArcIssueSample> Issues { get; init; }
}

public sealed class ReadingArcIssueSample
{
    public required string Num { get; init; }
    public required string Name { get; init; }
    public bool IsOwned { get; init; }
    public bool IsMissing => !IsOwned;
}
