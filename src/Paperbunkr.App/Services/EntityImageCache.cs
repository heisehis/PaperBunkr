using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// On-disk images for characters, teams and creators (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3), the
/// <see cref="StoryEventCoverCache"/> pattern: provider images under a hash of their URL, the user's own image under the entity's kind and id.
/// Downloads go to a temp file, are decoded once to prove they are images, then moved into place. Which image to show is
/// <see cref="EntityImageResolver"/>'s job, not this cache's.
/// </summary>
public static class EntityImageCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };

    /// <summary>Settable so tests point it at a temp folder.</summary>
    public static string CacheDirectory { get; set; } = Paperbunkr.Data.AppDataPaths.Combine("entity-images");

    public static string ProviderPath(string url)
    {
        Directory.CreateDirectory(CacheDirectory);
        return Path.Combine(CacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".jpg");
    }

    /// <summary>The file name stored in <c>CustomImagePath</c> (relative to <see cref="CacheDirectory"/>).</summary>
    public static string CustomFileName(ComicMetadataEntityKind kind, int entityId) => $"{kind.ToString().ToLowerInvariant()}-{entityId}.jpg";

    public static string CustomPath(string customFileName)
    {
        Directory.CreateDirectory(CacheDirectory);
        return Path.Combine(CacheDirectory, Path.GetFileName(customFileName));
    }

    public static bool HasProviderImage(string? url) => url is { Length: > 0 } && File.Exists(ProviderPath(url));

    public static bool HasCustomImage(string? customFileName) => customFileName is { Length: > 0 } && File.Exists(CustomPath(customFileName));

    /// <summary>Decodes an image file, or null when it is missing or unreadable.</summary>
    public static Bitmap? Load(string? path)
    {
        try
        {
            return path is not null && File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public static async Task<bool> DownloadAndCacheAsync(string url, CancellationToken cancellationToken)
    {
        string path = ProviderPath(url);
        if (File.Exists(path))
        {
            return true;
        }

        string temp = path + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            var bytes = await Http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
            using (new Bitmap(temp))
            {
            }

            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Any failure (network, timeout, a bad or undecodable file) is just "not downloaded": the profile is not stamped and is retried later.
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (IOException)
            {
            }

            return false;
        }
    }

    /// <summary>Copies the user's chosen image (scaled to at most 600 px, JPEG) and returns the file name to store, or null on failure.</summary>
    public static string? TrySetCustom(ComicMetadataEntityKind kind, int entityId, string sourcePath)
    {
        string fileName = CustomFileName(kind, entityId);
        string target = CustomPath(fileName);
        string temp = target + ".tmp.jpg";
        try
        {
            using var source = new Bitmap(sourcePath);
            int longest = Math.Max(source.PixelSize.Width, source.PixelSize.Height);
            if (longest <= 0)
            {
                return null;
            }

            double scale = Math.Min(1.0, 600.0 / longest);
            using var resized = source.CreateScaledBitmap(
                new Avalonia.PixelSize(Math.Max(1, (int)(source.PixelSize.Width * scale)), Math.Max(1, (int)(source.PixelSize.Height * scale))),
                BitmapInterpolationMode.HighQuality);
            resized.Save(temp, new JpegBitmapEncoderOptions { Quality = 85 });
            File.Move(temp, target, true);
            return fileName;
        }
        catch
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (IOException)
            {
            }

            return null;
        }
    }

    public static void ClearCustom(string? customFileName)
    {
        if (customFileName is not { Length: > 0 })
        {
            return;
        }

        try
        {
            string path = CustomPath(customFileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
