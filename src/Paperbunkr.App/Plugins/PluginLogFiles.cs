using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Owns the per-plugin log files (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md §1):
/// <c>&lt;log folder&gt;\plugins\&lt;key&gt;.log</c>, one line per entry as
/// <c>2026-09-20T13:45:01.123Z [INF] message</c>, an exception's full text indented under its line, capped at
/// <see cref="MaxBytes"/> and rolled to <c>&lt;key&gt;.1.log</c> (one previous file kept, the older one replaced).
/// Thread-safe (one lock per file - plugins log from background hook threads) and <b>never throws</b>: a full
/// disk or a locked file must not turn a plugin's <c>Log.Info</c> into a plugin failure.
/// </summary>
public sealed class PluginLogFiles
{
    public const long MaxBytes = 1024 * 1024;

    private readonly Func<string> _directory;
    private readonly ConcurrentDictionary<string, object> _locks = new();

    public PluginLogFiles(Func<string> directory)
    {
        _directory = directory;
    }

    /// <summary>The real location: <c>%AppData%\Paperbunkr\logs\plugins</c> (follows <see cref="DiagnosticsService.LogDirectory"/>, including its test override).</summary>
    public static PluginLogFiles Default { get; } = new(() => Path.Combine(DiagnosticsService.LogDirectory, "plugins"));

    public string Directory => _directory();

    /// <summary>The log file for <paramref name="pluginKey"/>; the key is made filename-safe, so two odd keys can't escape the folder.</summary>
    public string PathFor(string pluginKey) => Path.Combine(Directory, Sanitize(pluginKey) + ".log");

    public void Write(string pluginKey, string level, string message, Exception? exception = null)
    {
        try
        {
            string path = PathFor(pluginKey);
            object gate = _locks.GetOrAdd(path, _ => new object());
            lock (gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                RollIfFull(path);
                File.AppendAllText(path, Format(level, message, exception), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Deliberately swallowed - see the class comment.
        }
    }

    private static void RollIfFull(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        string rolled = Path.ChangeExtension(path, ".1.log");
        if (File.Exists(rolled))
        {
            File.Delete(rolled);
        }

        File.Move(path, rolled);
    }

    internal static string Format(string level, string message, Exception? exception)
    {
        var text = new StringBuilder();
        text.Append(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")).Append(" [").Append(level).Append("] ");
        text.Append(Indent(message)).AppendLine();
        if (exception is not null)
        {
            text.Append("    ").Append(Indent(exception.ToString())).AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Continuation lines are indented so one entry stays visually one entry.</summary>
    private static string Indent(string text) => text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine + "    ");

    internal static string Sanitize(string pluginKey)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(pluginKey.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        cleaned = cleaned.Trim().Trim('.');
        if (cleaned.Length == 0)
        {
            cleaned = "_";
        }

        return cleaned.Length > 100 ? cleaned[..100] : cleaned;
    }
}

/// <summary>One plugin's <see cref="IPluginLogger"/>, bound to its key. Built per access by the environment (see <c>PaperbunkrPluginEnvironment.Log</c>).</summary>
public sealed class PluginLogger : IPluginLogger
{
    private readonly PluginLogFiles _files;
    private readonly string _pluginKey;

    public PluginLogger(PluginLogFiles files, string pluginKey)
    {
        _files = files;
        _pluginKey = pluginKey;
    }

    public void Debug(string message) => _files.Write(_pluginKey, "DBG", message);

    public void Info(string message) => _files.Write(_pluginKey, "INF", message);

    public void Warn(string message) => _files.Write(_pluginKey, "WRN", message);

    public void Error(string message, Exception? exception = null) => _files.Write(_pluginKey, "ERR", message, exception);
}
