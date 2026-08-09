namespace Paperbunkr.App.Services;

/// <summary>
/// Thin seam over <see cref="cYo.Common.Win32.ShellRegister"/> (docs/superpowers/specs/
/// 2026-08-07-preferences-advanced-tab-design.md §2) - unlike this Preferences screen's other
/// services, this one is abstracted behind an interface rather than a redirected-but-still-real
/// path/context factory, because the real implementation writes to the actual Windows registry.
/// Tests use an in-memory fake, never the real <c>HKEY_CLASSES_ROOT</c>.
/// </summary>
public interface IShellFileAssociation
{
    bool IsRegistered(string typeId, string extension);

    void Register(string typeId, string extension, string displayName, string appPath);

    void Unregister(string typeId, string extension);

    void RefreshShell();
}
