using System.Diagnostics;
using System.Drawing;
using cYo.Projects.ComicRack.Engine.IO.Provider;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Builds a small multi-page (bundled <c>FORM:DJVM</c>) .djvu through the engine's own writer path -
/// <see cref="DjVuImage.SaveDjVu"/> (c44.exe) per page, then <c>djvm.exe -c</c>/<c>-i</c> to bundle
/// them, the same two steps <c>DjVuStorageProvider.OnStore</c> runs. Multi-page because
/// <c>DjvuComicProvider</c> lists pages with <c>djvm -l</c>, which rejects a single-page
/// <c>FORM:DJVU</c> file outright (<c>DjVmDoc.no_form_djvm2</c>) - same as CE.
/// </summary>
internal static class DjvuFixture
{
    public const int Width = 300;
    public const int Height = 400;

    private static string DjvmExe =>
        Path.Combine(Path.GetDirectoryName(typeof(DjVuImage).Assembly.Location)!, "Resources", "djvm.exe");

    public static string Create(string path, int pageCount)
    {
        var colors = new[] { Color.Firebrick, Color.SteelBlue, Color.Goldenrod, Color.SeaGreen, Color.Orchid };
        for (int i = 0; i < pageCount; i++)
        {
            string pagePath = Path.GetTempFileName();
            try
            {
                using (var bitmap = new Bitmap(Width, Height))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(colors[i % colors.Length]);
                    }
                    DjVuImage.SaveDjVu(bitmap, pagePath);
                }

                RunDjvm($"-{(i == 0 ? "c" : "i")} \"{path}\" \"{pagePath}\"");
            }
            finally
            {
                File.Delete(pagePath);
            }
        }
        return path;
    }

    private static void RunDjvm(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(DjvmExe, arguments) { CreateNoWindow = true, UseShellExecute = false })!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"djvm {arguments} exited with {process.ExitCode}");
        }
    }
}
