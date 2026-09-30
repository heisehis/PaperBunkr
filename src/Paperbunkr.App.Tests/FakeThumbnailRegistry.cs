using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// In-memory <see cref="IThumbnailRegistry"/>: <see cref="Classes"/> plays HKCU\Software\Classes (key path →
/// value name ("" = default) → value), <see cref="Machine"/> plays what other programs registered machine-wide.
/// The effective handler follows Windows' lookup - the extension's ProgID first, then the extension key, each
/// with HKCU winning over the machine view.
/// </summary>
internal sealed class FakeThumbnailRegistry : IThumbnailRegistry
{
    public Dictionary<string, Dictionary<string, string>> Classes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Machine { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Descriptions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, string>> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int RefreshCount { get; private set; }

    public string? GetClassesValue(string keyPath, string? valueName = null) =>
        Classes.TryGetValue(keyPath, out var values) && values.TryGetValue(valueName ?? string.Empty, out var v) ? v : null;

    public void SetClassesValue(string keyPath, string? valueName, string value)
    {
        if (!Classes.TryGetValue(keyPath, out var values))
        {
            Classes[keyPath] = values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        values[valueName ?? string.Empty] = value;
    }

    public bool ClassesKeyExists(string keyPath) =>
        Classes.Keys.Any(k => k.Equals(keyPath, StringComparison.OrdinalIgnoreCase) || k.StartsWith(keyPath + @"\", StringComparison.OrdinalIgnoreCase));

    public void DeleteClassesKeyTree(string keyPath)
    {
        foreach (var key in Classes.Keys.Where(k => k.Equals(keyPath, StringComparison.OrdinalIgnoreCase) || k.StartsWith(keyPath + @"\", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Classes.Remove(key);
        }
    }

    public IReadOnlyDictionary<string, string> GetSaved(string extension) =>
        Saved.TryGetValue(extension, out var s) ? new Dictionary<string, string>(s) : new Dictionary<string, string>();

    public void SetSaved(string extension, string location, string previousValue)
    {
        if (!Saved.TryGetValue(extension, out var s))
        {
            Saved[extension] = s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        s[location] = previousValue;
    }

    public void DeleteSaved(string extension) => Saved.Remove(extension);

    public void DeleteSavedLocation(string extension, string location)
    {
        if (Saved.TryGetValue(extension, out var s))
        {
            s.Remove(location);
        }
    }

    public IReadOnlyList<string> SavedExtensions() => Saved.Keys.ToList();

    public string? GetEffectiveHandlerClsid(string extension)
    {
        string? progId = GetClassesValue(extension) is { Length: > 0 } user ? user : Machine.GetValueOrDefault(extension);
        if (progId is { Length: > 0 })
        {
            string key = progId + @"\ShellEx\" + ThumbnailHandlerService.ThumbnailProviderKey;
            return GetClassesValue(key) ?? Machine.GetValueOrDefault(key);
        }

        string extKey = extension + @"\ShellEx\" + ThumbnailHandlerService.ThumbnailProviderKey;
        return GetClassesValue(extKey) ?? Machine.GetValueOrDefault(extKey);
    }

    public string? DescribeHandler(string clsid) => Descriptions.GetValueOrDefault(clsid);

    public void RefreshShell() => RefreshCount++;

    /// <summary>A temp "app folder" with a stand-in handler DLL (and optionally DjVuLibre's ddjvu.exe).</summary>
    public static string CreateAppDirectory(bool withHandler = true, bool withDdjvu = false)
    {
        string dir = Path.Combine(Path.GetTempPath(), "pb-thumbs-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Resources"));
        if (withHandler)
        {
            File.WriteAllBytes(Path.Combine(dir, ThumbnailHandlerService.HandlerDllName), new byte[] { 0 });
        }

        if (withDdjvu)
        {
            File.WriteAllBytes(Path.Combine(dir, "Resources", "ddjvu.exe"), new byte[] { 0 });
        }

        return dir;
    }
}
