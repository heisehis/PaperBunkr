using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SkiaSharp;

namespace Paperbunkr.App.Theming.Runtime;

/// <summary>The outcome of installing a <c>.crpck</c>: the key it was installed under, or why it was refused, plus every finding.</summary>
public sealed record ThemeInstallResult(bool Success, string? Key, string? Error, IReadOnlyList<ThemeDiagnostic> Diagnostics)
{
    public IEnumerable<ThemeDiagnostic> Warnings => Diagnostics.Where(d => d.Severity == ThemeDiagnosticSeverity.Warning);
}

/// <summary>Bounds on a theme package (design doc decision D8). Generous for an honest theme, small enough that a hostile archive cannot fill a disk.</summary>
public sealed record ThemePackageLimits
{
    public int MaxEntries { get; init; } = 512;

    public long MaxTotalBytes { get; init; } = 64L * 1024 * 1024;

    public long MaxEntryBytes { get; init; } = 16L * 1024 * 1024;

    public int MaxImageDimension { get; init; } = 8192;

    public static ThemePackageLimits Default { get; } = new();
}

/// <summary>
/// Installs a <c>.crpck</c> theme package (a ZIP with a <c>theme.json</c>) safely (design doc sections 5.5 and 13.3). The archive is extracted
/// to a staging folder under the limits, validated there (schema, references, assets), and only then swapped into place - so a package that
/// fails for any reason leaves a previously installed theme of the same name exactly as it was. Nothing in a package is ever executed, and
/// nothing is fetched from the network.
/// </summary>
public static class ThemePackageInstaller
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase) { ".json", ".txt", ".md" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };
    private static readonly HashSet<string> VectorExtensions = new(StringComparer.OrdinalIgnoreCase) { ".svg" };
    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf" };

    /// <summary>Never allowed inside a theme, even though nothing would run them: their presence means the archive is not a theme.</summary>
    private static readonly HashSet<string> ForbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".js", ".jse", ".wsf", ".msi", ".scr", ".lnk", ".sys", ".jar", ".sh", ".py", ".pbpck",
    };

    public static ThemeInstallResult Install(string crpckPath, string installedDirectory, string extractedDirectory, ThemePackageLimits? limits = null)
    {
        limits ??= ThemePackageLimits.Default;
        var diagnostics = new List<ThemeDiagnostic>();
        string key = Path.GetFileNameWithoutExtension(crpckPath);

        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('.') || key.Contains(':') || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Fail(null, "The package's file name cannot be used as a theme name.", diagnostics, ThemeDiagnosticCodes.UnsafePackage);
        }

        string staging = Path.Combine(extractedDirectory, $".staging-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);

            string? extractError = ExtractTo(crpckPath, staging, limits, diagnostics);
            if (extractError is not null)
            {
                return Fail(key, extractError, diagnostics, ThemeDiagnosticCodes.UnsafePackage);
            }

            string themePath = Path.Combine(staging, "theme.json");
            if (!File.Exists(themePath))
            {
                return Fail(key, "This file doesn't contain a theme.json.", diagnostics, ThemeDiagnosticCodes.InvalidJson);
            }

            var document = ThemeDocumentParser.Parse(File.ReadAllText(themePath), key, diagnostics, relative => ReadStaged(staging, relative));
            // A broken optional detail (a missing layout file, say) is recorded and installs with its fallback; only an unusable or unsafe file is refused.
            if (document is null || diagnostics.Any(d => d.Severity == ThemeDiagnosticSeverity.Error && IsBlocking(d.Code)))
            {
                string reason = diagnostics.FirstOrDefault(d => d.Severity == ThemeDiagnosticSeverity.Error)?.Message ?? "This file doesn't contain a valid theme.json.";
                return new ThemeInstallResult(false, key, reason, diagnostics);
            }

            ValidateAssets(staging, limits, diagnostics);
            ValidateReferences(document, staging, diagnostics);
            foreach (var variant in document.Variants)
            {
                // Resolving surfaces reference cycles, type mismatches and contrast findings before the theme is ever applied.
                diagnostics.AddRange(ThemeResolver.Resolve(document, variant.Id).Diagnostics);
            }

            if (diagnostics.FirstOrDefault(d => d.Severity == ThemeDiagnosticSeverity.Error && IsBlocking(d.Code)) is { } blocking)
            {
                return new ThemeInstallResult(false, key, blocking.Message, diagnostics);
            }

            Swap(staging, Path.Combine(extractedDirectory, key));
            staging = string.Empty; // moved into place - nothing left to clean up

            // Keeping the original archive is a convenience (re-export, "open themes folder"); failing to copy it does not undo the install.
            try
            {
                Directory.CreateDirectory(installedDirectory);
                string destination = Path.Combine(installedDirectory, key + ".crpck");
                if (!string.Equals(Path.GetFullPath(crpckPath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(crpckPath, destination, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Information, ThemeDiagnosticCodes.FallbackUsed, $"The theme is installed, but the original package could not be kept: {ex.Message}", ThemeId: key));
            }

            return new ThemeInstallResult(true, key, null, diagnostics);
        }
        catch (InvalidDataException ex)
        {
            return Fail(key, $"This file is not a readable theme package: {ex.Message}", diagnostics, ThemeDiagnosticCodes.UnsafePackage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Fail(key, ex.Message, diagnostics, ThemeDiagnosticCodes.UnsafePackage);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>Errors that mean "do not install". A broken optional detail (a missing icon, a bad layout) installs with the finding recorded and its fallback used.</summary>
    private static bool IsBlocking(string code) => code is ThemeDiagnosticCodes.InvalidJson or ThemeDiagnosticCodes.UnsupportedSchema or ThemeDiagnosticCodes.UnsafePackage
        or ThemeDiagnosticCodes.LimitExceeded or ThemeDiagnosticCodes.ReferenceCycle;

    private static ThemeInstallResult Fail(string? key, string message, List<ThemeDiagnostic> diagnostics, string code)
    {
        diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, code, message, ThemeId: key));
        return new ThemeInstallResult(false, key, message, diagnostics);
    }

    /// <summary>Extracts under the limits, entry by entry, counting real bytes (an archive can lie about its sizes). Returns an error message, or null.</summary>
    private static string? ExtractTo(string crpckPath, string staging, ThemePackageLimits limits, List<ThemeDiagnostic> diagnostics)
    {
        using var archive = ZipFile.OpenRead(crpckPath);
        if (archive.Entries.Count > limits.MaxEntries)
        {
            return $"The package has {archive.Entries.Count} files; the limit is {limits.MaxEntries}.";
        }

        long total = 0;
        string stagingRoot = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        var buffer = new byte[81920];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue; // a directory entry
            }

            if (!ThemePackagePaths.TryNormalize(entry.FullName, out string relative))
            {
                return $"The package contains an unsafe path ('{entry.FullName}').";
            }

            string extension = Path.GetExtension(relative);
            if (ForbiddenExtensions.Contains(extension))
            {
                return $"The package contains a program or script ('{relative}'). Themes are data only.";
            }

            if (!seen.Add(relative))
            {
                return $"The package contains '{relative}' more than once.";
            }

            bool known = TextExtensions.Contains(extension) || ImageExtensions.Contains(extension) || VectorExtensions.Contains(extension) || FontExtensions.Contains(extension)
                || string.Equals(Path.GetFileName(relative), "LICENSE", StringComparison.OrdinalIgnoreCase);
            if (!known)
            {
                diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.InvalidAsset, $"'{relative}' is not a file type themes use and was skipped.", relative));
                continue;
            }

            string destination = Path.GetFullPath(Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
            {
                return $"The package contains an unsafe path ('{entry.FullName}').";
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            long entryBytes = 0;
            using (var source = entry.Open())
            using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    entryBytes += read;
                    total += read;
                    if (entryBytes > limits.MaxEntryBytes)
                    {
                        return $"'{relative}' is larger than the {limits.MaxEntryBytes / (1024 * 1024)} MB limit for one file.";
                    }

                    if (total > limits.MaxTotalBytes)
                    {
                        return $"The package unpacks to more than the {limits.MaxTotalBytes / (1024 * 1024)} MB limit.";
                    }

                    target.Write(buffer, 0, read);
                }
            }
        }

        return null;
    }

    /// <summary>Checks that every image decodes within the size limit and every font really is one. A bad asset is removed and reported; the theme still installs with its fallback.</summary>
    private static void ValidateAssets(string staging, ThemePackageLimits limits, List<ThemeDiagnostic> diagnostics)
    {
        foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(staging, file).Replace('\\', '/');
            string extension = Path.GetExtension(file);
            string? problem = null;

            if (ImageExtensions.Contains(extension))
            {
                try
                {
                    using var codec = SKCodec.Create(file);
                    if (codec is null)
                    {
                        problem = "is not a readable image";
                    }
                    else if (codec.Info.Width > limits.MaxImageDimension || codec.Info.Height > limits.MaxImageDimension)
                    {
                        problem = $"is {codec.Info.Width}x{codec.Info.Height}; the limit is {limits.MaxImageDimension} pixels a side";
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    problem = "could not be read";
                }
            }
            else if (FontExtensions.Contains(extension))
            {
                if (!LooksLikeFont(file))
                {
                    problem = "is not a TrueType or OpenType font";
                }
            }
            else if (VectorExtensions.Contains(extension))
            {
                // An SVG is only ever read as path data by the icon loader; scripts and external references inside it are never evaluated.
                string text = File.ReadAllText(file);
                if (!text.Contains("<svg", StringComparison.OrdinalIgnoreCase))
                {
                    problem = "is not an SVG";
                }
            }

            if (problem is not null)
            {
                diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.InvalidAsset, $"'{relative}' {problem} and was left out.", relative, FallbackUsed: true));
                TryDeleteFile(file);
            }
        }
    }

    /// <summary>Reports icon, texture and font references that point at nothing (after bad assets were removed).</summary>
    private static void ValidateReferences(ThemeDocument document, string staging, List<ThemeDiagnostic> diagnostics)
    {
        foreach (var layer in document.Variants.Select(v => v.Layer).Prepend(document.Base))
        {
            foreach (var (path, entry) in layer.Tokens)
            {
                bool isAssetPath = path.StartsWith("icons.", StringComparison.Ordinal) || path.EndsWith(".source", StringComparison.Ordinal) || path.EndsWith(".file", StringComparison.Ordinal);
                if (!isAssetPath || ThemeValue.FromLiteral(entry.Node) is not { Kind: ThemeValueKind.Text, Text: { } reference })
                {
                    continue;
                }

                if (!LooksLikeFileReference(reference))
                {
                    continue; // "symbol:Home", "path:M0 0..." and similar inline forms
                }

                if (!ThemePackagePaths.TryNormalize(reference, out string normalized))
                {
                    diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.UnsafePackage, $"'{reference}' is not a path inside the theme package.", entry.JsonPath, reference, ThemeId: document.Id));
                }
                else if (!File.Exists(Path.Combine(staging, normalized.Replace('/', Path.DirectorySeparatorChar))))
                {
                    diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.InvalidAsset, $"'{normalized}' is referenced but is not in the package; the built-in fallback is used.", entry.JsonPath, reference, ThemeId: document.Id, FallbackUsed: true));
                }
            }

            foreach (var (name, material) in layer.Materials)
            {
                if (material["source"] is System.Text.Json.Nodes.JsonValue source && source.TryGetValue(out string? reference) && reference is not null)
                {
                    if (!ThemePackagePaths.TryNormalize(reference, out string normalized))
                    {
                        diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.UnsafePackage, $"Material '{name}' points outside the theme package ('{reference}').", $"materials.{name}.source", reference, ThemeId: document.Id));
                    }
                    else if (!File.Exists(Path.Combine(staging, normalized.Replace('/', Path.DirectorySeparatorChar))))
                    {
                        diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.InvalidAsset, $"Material '{name}' uses '{normalized}', which is not in the package; its fallback colour is used.", $"materials.{name}.source", reference, ThemeId: document.Id, FallbackUsed: true));
                    }
                }
            }
        }
    }

    /// <summary>A value is a file reference when it ends in a known asset extension; anything with a scheme ("http:", "symbol:") is not.</summary>
    internal static bool LooksLikeFileReference(string value)
    {
        string extension = Path.GetExtension(value);
        return !value.Contains("://", StringComparison.Ordinal)
            && (ImageExtensions.Contains(extension) || VectorExtensions.Contains(extension) || FontExtensions.Contains(extension))
            && !value.StartsWith("symbol:", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("path:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeFont(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            Span<byte> header = stackalloc byte[4];
            if (stream.Read(header) < 4)
            {
                return false;
            }

            // TrueType (00 01 00 00 or 'true'), OpenType/CFF ('OTTO'), collection ('ttcf').
            return (header[0] == 0x00 && header[1] == 0x01 && header[2] == 0x00 && header[3] == 0x00)
                || (header[0] == (byte)'O' && header[1] == (byte)'T' && header[2] == (byte)'T' && header[3] == (byte)'O')
                || (header[0] == (byte)'t' && header[1] == (byte)'r' && header[2] == (byte)'u' && header[3] == (byte)'e')
                || (header[0] == (byte)'t' && header[1] == (byte)'t' && header[2] == (byte)'c' && header[3] == (byte)'f');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Puts the staged folder where the theme lives. The previous version is moved aside first and restored if the swap fails, so there is never a moment with no usable copy.</summary>
    private static void Swap(string staging, string target)
    {
        string? previous = null;
        if (Directory.Exists(target))
        {
            previous = target + $".old-{Guid.NewGuid():N}";
            Directory.Move(target, previous);
        }

        try
        {
            Directory.Move(staging, target);
        }
        catch
        {
            if (previous is not null && !Directory.Exists(target))
            {
                Directory.Move(previous, target);
            }

            throw;
        }

        if (previous is not null)
        {
            TryDeleteDirectory(previous);
        }
    }

    private static string? ReadStaged(string staging, string relative)
    {
        string path = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
