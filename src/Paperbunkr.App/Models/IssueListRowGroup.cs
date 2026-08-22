using System.Collections.ObjectModel;

namespace Paperbunkr.App.Models;

/// <summary>One group section in the Comic List screen when grouping is active - same shape as <see cref="SeriesCardGroup"/>.</summary>
public class IssueListRowGroup
{
    public required string Header { get; init; }

    public required ObservableCollection<IssueListRow> Items { get; init; }
}
