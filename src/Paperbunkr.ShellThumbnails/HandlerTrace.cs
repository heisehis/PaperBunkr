namespace Paperbunkr.ShellThumbnails;

/// <summary>
/// Opt-in diagnostics for a handler that runs invisibly inside Windows' thumbnail surrogate (dllhost.exe): when
/// <c>%TEMP%\Paperbunkr\thumbnails.trace</c> exists, each request appends a line to <c>thumbnails.log</c> next to it.
/// Off (and free) otherwise - create the empty file to turn it on for a bug report. The Temp folder, because it's
/// what the surrogate can reliably reach: its known-folder lookup for AppData came back empty when verified on
/// 2026-09-30, while <see cref="Path.GetTempPath"/> resolved to the user's own Temp.
/// </summary>
internal static class HandlerTrace
{
    public static readonly string Folder = Path.Combine(Path.GetTempPath(), "Paperbunkr");

    private static readonly bool Enabled = File.Exists(Path.Combine(Folder, "thumbnails.trace"));
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(Folder, "thumbnails.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
