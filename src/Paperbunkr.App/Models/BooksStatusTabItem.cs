using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One tab in the Books screen's status strip (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §2).</summary>
public sealed partial class BooksStatusTabItem : ObservableObject
{
    public BooksStatusTabItem(BooksStatusTab tab, string label)
    {
        Tab = tab;
        Label = label;
    }

    public BooksStatusTab Tab { get; }

    public string Label { get; }

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isActive;
}
