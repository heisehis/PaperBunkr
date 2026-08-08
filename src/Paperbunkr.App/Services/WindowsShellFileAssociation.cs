using cYo.Common.Win32;

namespace Paperbunkr.App.Services;

/// <summary>Real <see cref="IShellFileAssociation"/> - wraps the already-ported, already-working <see cref="ShellRegister"/> directly.</summary>
public class WindowsShellFileAssociation : IShellFileAssociation
{
    public bool IsRegistered(string typeId, string extension) => ShellRegister.IsFileOpenRegistered(typeId, extension);

    public void Register(string typeId, string extension, string displayName, string appPath) =>
        ShellRegister.RegisterFileOpen(typeId, extension, displayName, appPath, appPath);

    public void Unregister(string typeId, string extension) => ShellRegister.UnregisterFileOpen(typeId, extension);

    public void RefreshShell() => ShellRegister.RefreshShell();
}
