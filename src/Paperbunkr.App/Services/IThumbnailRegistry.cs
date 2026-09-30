using System.Collections.Generic;

namespace Paperbunkr.App.Services;

/// <summary>
/// Registry seam for <see cref="ThumbnailHandlerService"/> - same role <see cref="IShellFileAssociation"/> plays
/// for file associations, so tests never touch the real Windows registry. Paths are relative to
/// <c>HKEY_CURRENT_USER\Software\Classes</c> unless a member says otherwise.
/// </summary>
public interface IThumbnailRegistry
{
    /// <summary>A value under HKCU\Software\Classes (null name = the key's default value); null when absent.</summary>
    string? GetClassesValue(string keyPath, string? valueName = null);

    void SetClassesValue(string keyPath, string? valueName, string value);

    bool ClassesKeyExists(string keyPath);

    void DeleteClassesKeyTree(string keyPath);

    /// <summary>Previous handler values Paperbunkr replaced for an extension, keyed by the Classes-relative
    /// location it wrote (stored under HKCU\Software\Paperbunkr\ThumbnailHandlers\&lt;ext&gt;).</summary>
    IReadOnlyDictionary<string, string> GetSaved(string extension);

    void SetSaved(string extension, string location, string previousValue);

    void DeleteSaved(string extension);

    void DeleteSavedLocation(string extension, string location);

    IReadOnlyList<string> SavedExtensions();

    /// <summary>The thumbnail handler CLSID Windows would actually use for <paramref name="extension"/> - the merged
    /// HKCU/HKLM view, ProgID first (AssocQueryString ASSOCSTR_SHELLEXTENSION) - or null when there is none.</summary>
    string? GetEffectiveHandlerClsid(string extension);

    /// <summary>A readable name for a handler CLSID (its DLL's product name, else the class name), or null.</summary>
    string? DescribeHandler(string clsid);

    void RefreshShell();
}
