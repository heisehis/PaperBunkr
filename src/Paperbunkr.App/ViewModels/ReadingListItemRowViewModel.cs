using System;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>One row in a Reading List's item grid, wrapping a real <see cref="ReadingListItem"/>.</summary>
public partial class ReadingListItemRowViewModel : ViewModelBase
{
    private readonly Action<ReadingListItemRowViewModel> _onMoveUp;
    private readonly Action<ReadingListItemRowViewModel> _onMoveDown;
    private readonly Action<ReadingListItemRowViewModel> _onRemove;

    public ReadingListItemRowViewModel(
        ReadingListItem item,
        Action<ReadingListItemRowViewModel> onMoveUp,
        Action<ReadingListItemRowViewModel> onMoveDown,
        Action<ReadingListItemRowViewModel> onRemove)
    {
        Item = item;
        _onMoveUp = onMoveUp;
        _onMoveDown = onMoveDown;
        _onRemove = onRemove;
    }

    public ReadingListItem Item { get; }

    public string Number => Item.Issue?.Number ?? "?";

    public string Name => Item.Issue?.Title ?? Item.Issue?.Series?.Name ?? "Unknown";

    public bool IsOwned => Item.Issue is { FileIsMissing: false };

    public bool IsMissing => !IsOwned;

    [RelayCommand]
    private void MoveUp() => _onMoveUp(this);

    [RelayCommand]
    private void MoveDown() => _onMoveDown(this);

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
