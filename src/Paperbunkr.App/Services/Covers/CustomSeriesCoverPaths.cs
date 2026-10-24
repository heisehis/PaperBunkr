using System.Globalization;
using System.IO;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services.Covers;

/// <summary>Durable image files chosen as series artwork, independent from issue cover files.</summary>
public static class CustomSeriesCoverPaths
{
    public static string Directory { get; set; } = AppDataPaths.Combine("custom-series-covers");

    public static string RelativeKey(int seriesId) => $"{seriesId.ToString(CultureInfo.InvariantCulture)}.jpg";

    public static string GetPath(string relativeKey)
    {
        var safeName = Path.GetFileName(relativeKey);
        return Path.Combine(Directory, safeName);
    }

    public static string GetPath(int seriesId) => GetPath(RelativeKey(seriesId));

    public static void Delete(string? relativeKey)
    {
        if (string.IsNullOrWhiteSpace(relativeKey)) return;
        try
        {
            string path = GetPath(relativeKey);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
    }
}
