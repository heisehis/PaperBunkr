using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The tiny "name this workspace" overlay (docs/superpowers/specs/2026-09-03-library-saved-
/// workspaces-design.md) - one validated text field, Save / Cancel. Shared by both the Library
/// and Books workspace switchers for "Save current view as…" and "Rename", via
/// <see cref="MainViewModel"/>'s <c>PromptWorkspaceName</c> delegate.
/// </summary>
public partial class WorkspaceNameViewModel : ViewModelBase
{
    private readonly Action _onCancel;
    private Action<string>? _onConfirm;

    public WorkspaceNameViewModel(Action onCancel)
    {
        _onCancel = onCancel;
    }

    /// <summary>Opens the overlay's state for a fresh prompt. <paramref name="initial"/> pre-fills the field (a rename); null for a new name.</summary>
    public void Begin(string? initial, Action<string> onConfirm, string heading = WorkspaceHeading, string hint = WorkspaceHint)
    {
        Name = initial ?? string.Empty;
        _onConfirm = onConfirm;
        Heading = heading;
        Hint = hint;
    }

    public const string WorkspaceHeading = "NAME THIS WORKSPACE";
    public const string WorkspaceHint = "Reusing an existing name overwrites that workspace.";

    /// <summary>The overlay's title; the same prompt also names list layouts (docs/superpowers/specs/2026-10-04-list-layouts-design.md).</summary>
    [ObservableProperty]
    private string _heading = WorkspaceHeading;

    [ObservableProperty]
    private string _hint = WorkspaceHint;

    [ObservableProperty]
    private string _name = string.Empty;

    partial void OnNameChanged(string value) => SaveCommand.NotifyCanExecuteChanged();

    private bool CanSave() => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => _onConfirm?.Invoke(Name.Trim());

    [RelayCommand]
    private void Cancel() => _onCancel();
}
