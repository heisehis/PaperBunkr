using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Models;

/// <summary>
/// One swatch in Preferences → Reader → Background texture (docs/superpowers/specs/2026-09-25-
/// publisher-icons-and-reader-textures-design.md §B): built from <c>ReaderBackgroundTextures.All</c>,
/// so adding a texture to the catalog adds a swatch with no XAML change. <see cref="IsActive"/> is
/// pushed by the owning view-model whenever the selected texture changes.
/// </summary>
public sealed partial class ReaderTextureChoice : ObservableObject
{
    public ReaderTextureChoice(string id, string displayName, Action<string> select)
    {
        Id = id;
        DisplayName = displayName;
        SelectCommand = new RelayCommand(() => select(id));
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string AutomationName => DisplayName + " texture";
    public ICommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isActive;
}
