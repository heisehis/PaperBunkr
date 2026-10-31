using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Models;

/// <summary>A row in the Books "Shelves" dropdown; <see cref="IsActive"/> marks the chosen shelf.</summary>
public sealed partial class BookShelfRow : ObservableObject
{
    public BookShelfRow(BookShelf shelf) => Shelf = shelf;

    public BookShelf Shelf { get; }

    public string Name => Shelf.Name;

    [ObservableProperty]
    private bool _isActive;
}
