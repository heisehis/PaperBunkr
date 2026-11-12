using System;
using System.IO;
using Avalonia.Media.Imaging;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

public static class StoryEventCoverPaths
{
    public static string CacheDirectory { get; set; } = Paperbunkr.Data.AppDataPaths.Combine("story-event-covers");
    public static string GetPath(int eventId) { Directory.CreateDirectory(CacheDirectory); return Path.Combine(CacheDirectory, $"{eventId}.jpg"); }
}

public static class StoryEventCoverCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    public static Bitmap? Get(int eventId)
    {
        var path = StoryEventCoverPaths.GetPath(eventId);
        try
        {
            using var db = PaperbunkrDb.CreateContext();
            var row = db.StoryEvents.Find(eventId);
            if (row is not null && row.CoverSource == Paperbunkr.Data.Entities.StoryEventCoverSource.None && string.IsNullOrWhiteSpace(row.CoverImageUrl)) return null;
            if (row is not null && row.CoverSource != Paperbunkr.Data.Entities.StoryEventCoverSource.Manual && row.CoverImageUrl is { Length: > 0 } url)
            {
                string candidate = ProviderPath(url);
                if (File.Exists(candidate)) path = candidate;
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException) { }
        try { return File.Exists(path) ? new Bitmap(path) : null; }
        catch (Exception ex) when (ex is IOException or NotSupportedException) { return null; }
    }

    public static async Task<bool> DownloadAndCacheAsync(int eventId, string url, CancellationToken cancellationToken)
    {
        string path = ProviderPath(url);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            var bytes = await Http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
            using var check = new Bitmap(temp);
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException or TaskCanceledException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
            return false;
        }
    }

    private static string ProviderPath(string url)
    {
        Directory.CreateDirectory(StoryEventCoverPaths.CacheDirectory);
        return Path.Combine(StoryEventCoverPaths.CacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".jpg");
    }

    public static Bitmap? GetCandidate(string url)
    {
        try { string path = ProviderPath(url); return File.Exists(path) ? new Bitmap(path) : null; }
        catch (Exception ex) when (ex is IOException or NotSupportedException) { return null; }
    }

    public static bool TrySetCustomCover(int eventId, string sourcePath)
    {
        try
        {
            using var source = new Bitmap(sourcePath);
            int longest = Math.Max(source.PixelSize.Width, source.PixelSize.Height);
            if (longest <= 0) return false;
            double scale = Math.Min(1.0, 400.0 / longest);
            using var resized = source.CreateScaledBitmap(new Avalonia.PixelSize(Math.Max(1, (int)(source.PixelSize.Width * scale)), Math.Max(1, (int)(source.PixelSize.Height * scale))), BitmapInterpolationMode.HighQuality);
            resized.Save(GetTempPath(eventId), new JpegBitmapEncoderOptions { Quality = 85 });
            File.Move(GetTempPath(eventId), StoryEventCoverPaths.GetPath(eventId), true);
            return true;
        }
        catch { return false; }
    }

    private static string GetTempPath(int id) => StoryEventCoverPaths.GetPath(id) + ".tmp.jpg";
    public static void Invalidate(int id) { try { var p = StoryEventCoverPaths.GetPath(id); if (File.Exists(p)) File.Delete(p); } catch (IOException) { } }
}
