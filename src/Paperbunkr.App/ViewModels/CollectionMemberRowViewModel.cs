using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Collections;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in <see cref="CollectionPropertiesScreenViewModel"/>'s member list - wraps a resolved
/// <see cref="CollectionMember"/> with the Remove command the overlay's row needs. Reordering is
/// via Move Up/Down on the parent (no drag library pulled in for this - see the design doc's own
/// fallback note); removal is buffered like the reading-list overlay's cover pick, applied only on
/// <see cref="CollectionPropertiesScreenViewModel.SaveCommand"/>.
/// </summary>
public sealed partial class CollectionMemberRowViewModel : ObservableObject
{
    private readonly Action<CollectionMemberRowViewModel> _onRemove;

    public CollectionMemberRowViewModel(CollectionMember member, Action<CollectionMemberRowViewModel> onRemove)
    {
        CollectionItemId = member.CollectionItemId;
        DisplayTitle = member.DisplayTitle;
        KindLabel = member.Kind.ToString();
        _onRemove = onRemove;
    }

    public int CollectionItemId { get; }

    public string DisplayTitle { get; }

    public string KindLabel { get; }

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
