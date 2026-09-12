using ClusterLibraryManager.Organizing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClusterLibraryManager.Dialogs;

/// <summary>
/// Backs <see cref="FileConflictDialogView"/> (design doc §6, grilling Q9=B/Q15=B) - a new,
/// purpose-built dialog (not an extension of the host's own <c>ConfirmDialog</c>, which has no
/// checkbox/custom-content slot), shown per collision via
/// <c>INativePluginUiEnvironment.ShowModalAsync</c>. The constructor's <paramref name="resolve"/>
/// callback is the connective tissue that interface's own doc comment describes - built by the host,
/// captured here, invoked directly from whichever button the user picks.
/// </summary>
public sealed partial class FileConflictDialogViewModel : ObservableObject
{
    private readonly Action<(CollisionResolution Resolution, bool ApplyToAllRemaining)> _resolve;

    public string IncomingBookLabel { get; }
    public string ExistingBookLabel { get; }
    public string DestinationPath { get; }

    [ObservableProperty]
    private bool _applyToAllRemaining;

    public FileConflictDialogViewModel(string incomingBookLabel, string existingBookLabel, string destinationPath,
        Action<(CollisionResolution Resolution, bool ApplyToAllRemaining)> resolve)
    {
        IncomingBookLabel = incomingBookLabel;
        ExistingBookLabel = existingBookLabel;
        DestinationPath = destinationPath;
        _resolve = resolve;
    }

    [RelayCommand]
    private void Replace() => _resolve((CollisionResolution.Replace, ApplyToAllRemaining));

    [RelayCommand]
    private void Rename() => _resolve((CollisionResolution.Rename, ApplyToAllRemaining));

    [RelayCommand]
    private void Skip() => _resolve((CollisionResolution.Skip, ApplyToAllRemaining));
}
