using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>One usable image in the "My icons" folder; <see cref="Label"/> is its file name without the extension.</summary>
public sealed record MyIconFile(string FileName, string Label);

/// <summary>What a scan of the "My icons" folder found: the usable images (by label) and how many files were skipped.</summary>
public sealed record MyIconScan(IReadOnlyList<MyIconFile> Files, int Skipped);

/// <summary>
/// Reads and caches the user's own collection icons (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md): <c>.svg</c> and <c>.png</c>
/// up to 2 MB, each rendered once per file version and size so its longest side fits the icon square. Everything here may run off the UI thread; a file that
/// cannot be read is skipped (in a scan) or gives null (when drawn), never an exception.
/// </summary>
public static class CollectionIconImages
{
    /// <summary>Files larger than this are skipped: a real icon is a few kilobytes.</summary>
    public const long MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>The size a scan renders each file at to prove it loads (the picker's button size, so the result is reused).</summary>
    public const int ScanBox = 40;

    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True for the extensions the folder accepts.</summary>
    public static bool IsSupported(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".svg" or ".png";

    /// <summary>Lists the usable images in <paramref name="folder"/> (default: <see cref="CollectionIconFolder.Path"/>). A missing folder is an empty result.</summary>
    public static MyIconScan Scan(string? folder = null)
    {
        folder ??= CollectionIconFolder.Path;
        if (!Directory.Exists(folder))
        {
            return new MyIconScan(Array.Empty<MyIconFile>(), 0);
        }

        var files = new List<MyIconFile>();
        int skipped = 0;
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(folder).Where(IsSupported).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MyIconScan(Array.Empty<MyIconFile>(), 0);
        }

        foreach (string path in paths)
        {
            string name = Path.GetFileName(path);
            if (Get(name, ScanBox, folder) is null)
            {
                skipped++;
                continue;
            }

            files.Add(new MyIconFile(name, Path.GetFileNameWithoutExtension(name)));
        }

        return new MyIconScan(files.OrderBy(f => f.Label, StringComparer.CurrentCultureIgnoreCase).ToList(), skipped);
    }

    /// <summary>The image <paramref name="fileName"/> fitted into a <paramref name="box"/>-pixel square, or null when it is missing, too large or unreadable.</summary>
    public static Bitmap? Get(string fileName, int box, string? folder = null)
    {
        folder ??= CollectionIconFolder.Path;
        if (box <= 0 || string.IsNullOrWhiteSpace(fileName) || !IsSupported(fileName) || Path.GetFileName(fileName) != fileName)
        {
            return null;
        }

        string path = Path.Combine(folder, fileName);
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        string key = $"{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}|{box}";
        return Cache.GetOrAdd(key, _ => Load(path, box));
    }

    /// <summary><see cref="Get"/> on a worker thread, for a control that should not wait for a first render.</summary>
    public static Task<Bitmap?> GetAsync(string fileName, int box, string? folder = null) => Task.Run(() => Get(fileName, box, folder));

    private static Bitmap? Load(string path, int box)
    {
        if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return SvgMarkRenderer.RenderFile(path, box);
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var full = new Bitmap(stream);
            var size = full.PixelSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }

            double fit = Math.Min(1.0, (double)box / Math.Max(size.Width, size.Height));
            var target = new PixelSize(Math.Max(1, (int)Math.Round(size.Width * fit)), Math.Max(1, (int)Math.Round(size.Height * fit)));
            return full.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
