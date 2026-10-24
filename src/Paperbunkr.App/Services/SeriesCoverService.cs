using System.IO;
using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Covers;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>Persists user-selected series artwork separately from issue covers.</summary>
public sealed class SeriesCoverService
{
    public bool TrySetCustomCover(int seriesId, string sourcePath)
    {
        try
        {
            using var source = new Bitmap(sourcePath);
            return Save(seriesId, source);
        }
        catch { return false; }
    }

    public bool TrySetCustomCoverFromBytes(int seriesId, byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var source = new Bitmap(stream);
            return Save(seriesId, source);
        }
        catch { return false; }
    }

    private static bool Save(int seriesId, Bitmap source)
    {
        string key = CustomSeriesCoverPaths.RelativeKey(seriesId);
        string path = CustomSeriesCoverPaths.GetPath(key);
        try
        {
            Directory.CreateDirectory(CustomSeriesCoverPaths.Directory);
            var size = source.PixelSize;
            int longest = Math.Max(size.Width, size.Height);
            if (longest <= 0) return false;
            double scale = Math.Min(1.0, 400d / longest);
            var target = new PixelSize(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
            using var scaled = source.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
            scaled.Save(path, new JpegBitmapEncoderOptions { Quality = 85 });

            using var context = PaperbunkrDb.CreateContext();
            var series = context.Series.Find(seriesId);
            if (series is null) return false;
            series.CustomCoverPath = key;
            context.SaveChanges();
            CoverImageCache.InvalidateMemoryOnly($"series:{seriesId}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Reset(int seriesId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Find(seriesId);
        if (series is null) return;
        string? oldPath = series.CustomCoverPath;
        series.CustomCoverPath = null;
        context.SaveChanges();
        CustomSeriesCoverPaths.Delete(oldPath);
        CoverImageCache.InvalidateMemoryOnly($"series:{seriesId}");
    }
}
