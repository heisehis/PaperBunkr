using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using cYo.Common.Win32;
using Microsoft.Win32;

namespace Paperbunkr.App.Services;

/// <summary>The real, per-user registry behind <see cref="IThumbnailRegistry"/>.</summary>
internal sealed class WindowsThumbnailRegistry : IThumbnailRegistry
{
    private const string ClassesRoot = @"Software\Classes\";
    private const string SavedRoot = @"Software\Paperbunkr\ThumbnailHandlers";
    private const int AssocStrShellExtension = 16;

    public string? GetClassesValue(string keyPath, string? valueName = null)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ClassesRoot + keyPath);
        return key?.GetValue(valueName) as string;
    }

    public void SetClassesValue(string keyPath, string? valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ClassesRoot + keyPath);
        key.SetValue(valueName, value);
    }

    public bool ClassesKeyExists(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ClassesRoot + keyPath);
        return key is not null;
    }

    public void DeleteClassesKeyTree(string keyPath) =>
        Registry.CurrentUser.DeleteSubKeyTree(ClassesRoot + keyPath, throwOnMissingSubKey: false);

    public IReadOnlyDictionary<string, string> GetSaved(string extension)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SavedRoot + @"\" + extension);
        if (key is null)
        {
            return new Dictionary<string, string>();
        }

        return key.GetValueNames().ToDictionary(n => n, n => key.GetValue(n) as string ?? string.Empty);
    }

    public void SetSaved(string extension, string location, string previousValue)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SavedRoot + @"\" + extension);
        key.SetValue(location, previousValue);
    }

    public void DeleteSaved(string extension) =>
        Registry.CurrentUser.DeleteSubKeyTree(SavedRoot + @"\" + extension, throwOnMissingSubKey: false);

    public void DeleteSavedLocation(string extension, string location)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SavedRoot + @"\" + extension, writable: true);
        key?.DeleteValue(location, throwOnMissingValue: false);
    }

    public IReadOnlyList<string> SavedExtensions()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SavedRoot);
        return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    public string? GetEffectiveHandlerClsid(string extension)
    {
        uint size = 64;
        var buffer = new StringBuilder((int)size);
        int hr = AssocQueryStringW(0, AssocStrShellExtension, extension, ThumbnailHandlerService.ThumbnailProviderKey, buffer, ref size);
        return hr == 0 && buffer.Length > 0 ? buffer.ToString() : null;
    }

    public string? DescribeHandler(string clsid)
    {
        using var classKey = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid);
        using var server = classKey?.OpenSubKey("InprocServer32");
        if (server?.GetValue(null) is string dll)
        {
            dll = Environment.ExpandEnvironmentVariables(dll.Trim('"'));
            if (File.Exists(dll))
            {
                var info = FileVersionInfo.GetVersionInfo(dll);
                string? name = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName : info.FileDescription;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }
            }
        }

        return classKey?.GetValue(null) as string is { Length: > 0 } className ? className : null;
    }

    public void RefreshShell() => ShellRegister.RefreshShell();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryStringW(int flags, int str, string assoc, string? extra, StringBuilder? output, ref uint outputSize);
}
