using System.Collections.ObjectModel;

namespace Paperbunkr.App.Models;

/// <summary>Sample data for a Duplicate Finder plugin group.</summary>
public sealed class DuplicateGroupSample
{
    public required string Title { get; init; }
    public required string Note { get; init; }
    public required ObservableCollection<DuplicateItemSample> Items { get; init; }
}

public sealed class DuplicateItemSample
{
    public required string FileName { get; init; }
    public required string Info { get; init; }
    public bool Keep { get; init; }
}
