using System.Diagnostics;
using Paperbunkr.ShellThumbnails.Interop;

namespace Paperbunkr.ShellThumbnails;

/// <summary>
/// DjVu page 1 via DjVuLibre's <c>ddjvu.exe</c> - the same tool and arguments the engine's
/// <c>DjVuImage.GetBitmap</c> uses - converted to TIFF and decoded with WIC. Only available when
/// <c>Resources\ddjvu.exe</c> is installed next to the handler; <see cref="IsAvailable"/> is what the app checks
/// before offering the DjVu thumbnail toggle.
/// </summary>
internal static class DjvuRenderer
{
    public static string ToolPath => Path.Combine(ModuleLocation.Directory, "Resources", "ddjvu.exe");

    public static bool IsAvailable => File.Exists(ToolPath);

    public static BgraImage? RenderFirstPage(Stream stream, int maxSide, Deadline deadline)
    {
        if (!IsAvailable)
        {
            return null;
        }

        string input = Path.Combine(Path.GetTempPath(), $"pb-thumb-{Guid.NewGuid():N}.djvu");
        string output = Path.ChangeExtension(input, ".tif");
        try
        {
            using (var file = File.Create(input))
            {
                stream.CopyTo(file); // stream is a DeadlineStream - a slow copy stops at the deadline
            }

            var start = new ProcessStartInfo(ToolPath, $"-format=tiff -page=1 -size={maxSide}x{maxSide} \"{input}\" \"{output}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(deadline.Remaining))
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                }

                return null;
            }

            return process.ExitCode == 0 && File.Exists(output) ? Wic.DecodeScaled(File.ReadAllBytes(output), maxSide) : null;
        }
        finally
        {
            TryDelete(input);
            TryDelete(output);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
