using System;
using System.Globalization;
using System.IO;

namespace Paperbunkr.App.Services.Covers;

/// <summary>
/// Cache-file path helper for user-picked (custom) book cover art - the Books counterpart to
/// <see cref="CustomCoverPaths"/>. Own directory, keyed by bare <c>Book.Id</c>, never swept by the
/// orphan GC or the library-rebuild purge.
/// </summary>
public static class CustomBookCoverPaths
{
    /// <summary>Mutable so tests can redirect to a temp folder - never set this outside a test's own setup/teardown.</summary>
    public static string Directory { get; set; } = BuildDefaultDirectory();

    private static string BuildDefaultDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "custom-book-covers");
    }

    public static string GetCachePath(int bookId)
    {
        System.IO.Directory.CreateDirectory(Directory);
        return Path.Combine(Directory, $"{bookId.ToString(CultureInfo.InvariantCulture)}.jpg");
    }

    public static bool Exists(int bookId)
    {
        try
        {
            return File.Exists(GetCachePath(bookId));
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static void Delete(int bookId)
    {
        try
        {
            string path = GetCachePath(bookId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
