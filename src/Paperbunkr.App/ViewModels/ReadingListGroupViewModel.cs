using System.Collections.ObjectModel;

namespace Paperbunkr.App.ViewModels;

/// <summary>A sub-arc grouping of consecutive rows sharing the same GroupLabel (blank for ungrouped items).</summary>
public class ReadingListGroupViewModel
{
    public required string Label { get; init; }

    public bool HasLabel => !string.IsNullOrEmpty(Label);

    public required ObservableCollection<ReadingListItemRowViewModel> Rows { get; init; }
}
