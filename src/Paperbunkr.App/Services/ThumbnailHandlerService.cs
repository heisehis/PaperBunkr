using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Paperbunkr.App.Services;

/// <summary>
/// Turns Paperbunkr's File Explorer cover thumbnails on and off per file type
/// (docs/superpowers/specs/2026-09-30-explorer-cover-thumbnails-design.md §2). Registration is per-user
/// (HKCU\Software\Classes, no elevation - same as <see cref="FileAssociationService"/>):
/// <list type="bullet">
/// <item>the handler's COM class, pointing at <c>Paperbunkr.ShellThumbnails.dll</c> next to the app;</item>
/// <item><c>&lt;ext&gt;\ShellEx\{e357fccd-…}</c>, plus the same key under Paperbunkr's own ProgID when Paperbunkr's file
/// association is active for that extension - Windows consults the ProgID first, so a handler only on the
/// extension key would be skipped.</item>
/// </list>
/// Whatever value sat at each location before is saved and put back on disable/uninstall (decisions 6-7). The
/// "who owns it now" label (decision 22) comes from the handler Windows would really use, not from HKCU alone.
/// </summary>
public class ThumbnailHandlerService
{
    public const string ThumbnailProviderKey = "{e357fccd-a995-4576-b01f-234630154e96}";

    /// <summary>Must match <c>Paperbunkr.ShellThumbnails.Com.ThumbnailProvider.ClsidString</c> (a test pins the two together).</summary>
    public const string HandlerClsid = "{7fd4d2e6-ec36-4ed3-88b8-9360ce98535b}";

    public const string HandlerDllName = "Paperbunkr.ShellThumbnails.dll";

    /// <summary>Paperbunkr's own ProgIDs (<see cref="FileAssociationService"/>'s type ids) start with this.</summary>
    private const string PaperbunkrProgIdPrefix = "Paperbunkr.";

    /// <summary>Saved-value sentinel: there was nothing at that location before Paperbunkr wrote it.</summary>
    internal const string NothingBefore = "<none>";

    /// <summary>Every extension the handler can draw (decisions 2, 11), before the DjVu tool check.</summary>
    public static readonly IReadOnlyList<string> AllThumbnailExtensions = new[]
    {
        ".cbz", ".cbr", ".cb7", ".cbt", ".pdf", ".djvu", ".epub", ".mobi", ".azw", ".azw3",
    };

    private readonly IThumbnailRegistry _registry;
    private readonly string _appDirectory;

    public ThumbnailHandlerService()
        : this(new WindowsThumbnailRegistry(), AppContext.BaseDirectory)
    {
    }

    internal ThumbnailHandlerService(IThumbnailRegistry registry, string appDirectory)
    {
        _registry = registry;
        _appDirectory = appDirectory;
    }

    public string HandlerDllPath => Path.Combine(_appDirectory, HandlerDllName);

    /// <summary>False in a plain dev build - the handler DLL only exists after the Native AOT publish in BuildInstaller.ps1.</summary>
    public bool IsHandlerInstalled => File.Exists(HandlerDllPath);

    /// <summary>The extensions offered in Preferences. DjVu needs DjVuLibre's <c>Resources\ddjvu.exe</c>, which isn't always
    /// shipped, so it's only offered when that tool is actually there.</summary>
    public IReadOnlyList<string> SupportedExtensions =>
        File.Exists(Path.Combine(_appDirectory, "Resources", "ddjvu.exe"))
            ? AllThumbnailExtensions
            : AllThumbnailExtensions.Where(e => e != ".djvu").ToList();

    public bool IsSupported(string extension) => SupportedExtensions.Contains(Normalize(extension), StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(string extension) =>
        string.Equals(_registry.GetEffectiveHandlerClsid(Normalize(extension)), HandlerClsid, StringComparison.OrdinalIgnoreCase);

    /// <summary>The name of the other program whose thumbnails Windows shows for this type, or null when none (or Paperbunkr).</summary>
    public string? GetOtherOwner(string extension)
    {
        string? clsid = _registry.GetEffectiveHandlerClsid(Normalize(extension));
        if (clsid is null || string.Equals(clsid, HandlerClsid, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return _registry.DescribeHandler(clsid) ?? "another program";
    }

    public void SetEnabled(IEnumerable<string> extensions, bool enabled)
    {
        var requested = extensions.Select(Normalize).Where(IsSupported).Distinct().ToList();
        if (requested.Count == 0)
        {
            return;
        }

        if (enabled && !IsHandlerInstalled)
        {
            throw new FileNotFoundException("Paperbunkr's thumbnail handler isn't installed with this build.", HandlerDllPath);
        }

        foreach (string ext in requested)
        {
            if (enabled)
            {
                Enable(ext);
            }
            else
            {
                Disable(ext);
            }
        }

        RemoveClassIfUnused();
        _registry.RefreshShell();
    }

    /// <summary>Every supported extension no other program currently draws thumbnails for (decision 13) - what the
    /// installer checkbox turns on.</summary>
    public IReadOnlyList<string> UnownedExtensions() =>
        SupportedExtensions.Where(ext => IsEnabled(ext) || GetOtherOwner(ext) is null).ToList();

    /// <summary>Removes every registration Paperbunkr made and restores what was there before (uninstall).</summary>
    public void DisableAll()
    {
        foreach (string ext in _registry.SavedExtensions().ToList())
        {
            Disable(ext);
        }

        RemoveClassIfUnused();
        _registry.RefreshShell();
    }

    /// <summary>
    /// Re-writes the enabled extensions after a file association changed: turning Paperbunkr's association on
    /// gives the extension a ProgID that Windows now checks first, so the handler must be registered there too;
    /// turning it off removes that ProgID.
    /// </summary>
    public void ReapplyAfterAssociationChange()
    {
        foreach (string ext in _registry.SavedExtensions().ToList())
        {
            Enable(ext);
        }

        _registry.RefreshShell();
    }

    private void Enable(string ext)
    {
        EnsureClassRegistered();
        var saved = _registry.GetSaved(ext);

        // Drop saved ProgID locations whose ProgID has since gone away (association turned off).
        foreach (var location in saved.Keys.Where(l => !IsExtensionLocation(ext, l) && !_registry.ClassesKeyExists(ParentProgId(l))).ToList())
        {
            _registry.DeleteSavedLocation(ext, location);
        }

        saved = _registry.GetSaved(ext);

        foreach (string location in LocationsFor(ext))
        {
            if (!saved.ContainsKey(location))
            {
                _registry.SetSaved(ext, location, _registry.GetClassesValue(location) ?? NothingBefore);
            }

            _registry.SetClassesValue(location, null, HandlerClsid);
        }
    }

    private void Disable(string ext)
    {
        foreach (var (location, previous) in _registry.GetSaved(ext))
        {
            bool isExtensionKey = IsExtensionLocation(ext, location);
            if (previous == NothingBefore)
            {
                _registry.DeleteClassesKeyTree(location);
            }
            else if (isExtensionKey || _registry.ClassesKeyExists(ParentProgId(location)))
            {
                _registry.SetClassesValue(location, null, previous);
            }
        }

        _registry.DeleteSaved(ext);
    }

    private IEnumerable<string> LocationsFor(string ext)
    {
        yield return ext + @"\ShellEx\" + ThumbnailProviderKey;

        string? progId = _registry.GetClassesValue(ext);
        if (progId is not null && progId.StartsWith(PaperbunkrProgIdPrefix, StringComparison.Ordinal))
        {
            yield return progId + @"\ShellEx\" + ThumbnailProviderKey;
        }
    }

    private static bool IsExtensionLocation(string ext, string location) =>
        location.StartsWith(ext + @"\", StringComparison.OrdinalIgnoreCase);

    private static string ParentProgId(string location) => location[..location.IndexOf('\\')];

    private void EnsureClassRegistered()
    {
        string classKey = @"CLSID\" + HandlerClsid;
        _registry.SetClassesValue(classKey, null, "Paperbunkr cover thumbnails");
        _registry.SetClassesValue(classKey + @"\InprocServer32", null, HandlerDllPath);
        _registry.SetClassesValue(classKey + @"\InprocServer32", "ThreadingModel", "Apartment");
    }

    private void RemoveClassIfUnused()
    {
        if (_registry.SavedExtensions().Count == 0)
        {
            _registry.DeleteClassesKeyTree(@"CLSID\" + HandlerClsid);
        }
    }

    private static string Normalize(string extension) =>
        (extension.StartsWith('.') ? extension : "." + extension).ToLowerInvariant();
}
