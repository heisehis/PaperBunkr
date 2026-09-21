namespace Paperbunkr.Plugins;

/// <summary>
/// A plugin's own log file (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md §1), so an
/// author isn't left polluting the app's main log or guessing why something failed. Lines go to
/// <c>%AppData%\Paperbunkr\logs\plugins\&lt;plugin key&gt;.log</c>, each with a UTC timestamp and a level; the file is
/// capped at 1 MB and rolls to <c>&lt;key&gt;.1.log</c>. The host also writes this plugin's own failures, timeouts and
/// dropped-event reports here, so it's the one place to look.
/// <para>
/// Logging never throws into the plugin. Don't log secrets - the log is plain text.
/// </para>
/// </summary>
public interface IPluginLogger
{
    void Debug(string message);

    void Info(string message);

    void Warn(string message);

    /// <summary>Logs an error; a supplied <paramref name="exception"/>'s full text is written under the message.</summary>
    void Error(string message, Exception? exception = null);
}
