using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;

namespace Paperbunkr.App.Services;

/// <summary>
/// The no-editor authoring path (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 6, decision D9): a theme
/// is exported exactly as it is installed (a starting point for writing another one), and the user's own customizations are exported and
/// imported as a separate small JSON file. Neither writes anything into an installed theme.
/// </summary>
public partial class ThemeService
{
    /// <summary>The largest customizations file that will be read. A real one is a few kilobytes.</summary>
    private const long MaxCustomizationsBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions ExportJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Writes the theme family <paramref name="themeKey"/> belongs to as a <c>.crpck</c> at <paramref name="destinationPath"/>: every file of
    /// the package, unchanged. Works for a built-in theme too, which makes any of them a template. The user's overrides are not included.
    /// </summary>
    public bool ExportThemePackage(string themeKey, string destinationPath, out string? error)
    {
        error = null;
        if (!_catalog.TryFind(themeKey, out var document, out _))
        {
            error = "That theme could not be found.";
            return false;
        }

        var files = _catalog.ListFiles(document.Key);
        if (!files.Contains("theme.json", StringComparer.OrdinalIgnoreCase))
        {
            error = "That theme has no theme.json to export.";
            return false;
        }

        string temp = destinationPath + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var output = File.Create(temp))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (string file in files)
                {
                    using var source = _catalog.OpenAsset(document.Key, file);
                    if (source is null)
                    {
                        continue;
                    }

                    using var entry = archive.CreateEntry(file, CompressionLevel.Optimal).Open();
                    source.CopyTo(entry);
                }
            }

            File.Move(temp, destinationPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        {
            TryDelete(temp);
            error = "The theme could not be exported: " + ex.Message;
            return false;
        }
    }

    /// <summary>Writes everything the user has customized (all scopes, layout adjustments, contrast boost, effect quality) as one JSON file.</summary>
    public bool ExportCustomizations(string destinationPath, out string? error)
    {
        error = null;
        try
        {
            File.WriteAllText(destinationPath, ThemeOverrideSerializer.Serialize(_overrideStore.Load()).ToJsonString(ExportJsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = "Your customizations could not be exported: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Replaces the user's customizations with the ones in a file written by <see cref="ExportCustomizations"/> and applies them live. The file
    /// is data only; a value in it that is not valid for its token is ignored when the theme is resolved, exactly like one typed by hand.
    /// </summary>
    public bool ImportCustomizations(string sourcePath, out string? error)
    {
        error = null;
        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists)
            {
                error = "That file could not be found.";
                return false;
            }

            if (info.Length > MaxCustomizationsBytes)
            {
                error = "That file is too large to be a customizations file.";
                return false;
            }

            if (JsonNode.Parse(File.ReadAllText(sourcePath)) is not JsonObject root || ThemeOverrideSerializer.Deserialize(root) is not { } overrides)
            {
                error = "That file is not a Paperbunkr customizations file, or it was written by a newer version.";
                return false;
            }

            _overrideStore.Save(overrides);
            RefreshAfterOverrideChange();
            return true;
        }
        catch (JsonException)
        {
            error = "That file is not valid JSON.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = "Your customizations could not be imported: " + ex.Message;
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
