using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Paperbunkr.App.Services;

/// <summary>
/// Startup-milestone breadcrumbs and crash capture, written to plain-text files under
/// <c>%AppData%\Paperbunkr\logs</c>. Mirrors ComicRackCE's own diagnostic report shape
/// (<c>cYo.Common.Runtime.Diagnostic.WriteProgramInfo</c> + <c>CrashDialog.OnBark</c>'s
/// program-info/exception-chain/timestamp layout) rather than inventing a new one, but as a
/// file sink instead of CE's WinForms crash dialog - no direct Avalonia equivalent exists yet,
/// and the immediate problem this solves is "a startup failure produces zero durable evidence,"
/// not "the user needs an in-app crash reporter." Built after a live session where a genuinely
/// silent Database.Migrate() interruption took an hour of forensic SQL/EF probing to diagnose
/// with no log to consult - see docs/superpowers/specs (crash diagnostics infra) for context.
/// </summary>
public static class DiagnosticsService
{
    private const int MaxCrashLogsRetained = 20;

    private static readonly object WriteLock = new();

    /// <summary>
    /// Test-only redirect for <see cref="LogDirectory"/>, mirroring
    /// <c>PaperbunkrDbContext.DatabasePathOverride</c> - mutable so tests can point every write at
    /// a temp folder instead of the real per-user log directory. Never set this outside a test's
    /// own constructor/teardown.
    /// </summary>
    internal static string? LogDirectoryOverride { get; set; }

    /// <summary>Test-only override for <see cref="MaxCrashLogsRetained"/>, same rationale as <see cref="LogDirectoryOverride"/>.</summary>
    internal static int? MaxCrashLogsRetainedOverride { get; set; }

    public static string LogDirectory => LogDirectoryOverride ?? GetDefaultLogDirectory();

    private static string StartupLogPath => Path.Combine(LogDirectory, "startup.log");

    /// <summary>
    /// Hooks every unhandled-exception surface this process actually has (background threads via
    /// <see cref="AppDomain"/>, unobserved async faults, and the Avalonia UI thread) so a crash
    /// anywhere gets a durable report before the process goes down. Call once, as the very first
    /// statement in <c>Program.Main</c> - before <c>BuildAvaloniaApp()</c> - so startup failures
    /// inside Avalonia's own bootstrap are covered too.
    /// </summary>
    public static void Install()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch
        {
            // If the log directory itself can't be created, every write below no-ops (each write
            // has its own try/catch) - diagnostics must never be the reason the app fails to start.
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception, e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception, isTerminating: false);
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            // Not marking e.Handled: Avalonia has no equivalent to WinForms' "swallow and keep
            // running" for UI-thread exceptions, and pretending otherwise would leave the app in
            // an unknown state. This only guarantees the crash is logged before the process exits.
            LogCrash("Dispatcher.UIThread.UnhandledException", e.Exception, isTerminating: true);
        };

        LogMilestone("Diagnostics installed.");
    }

    /// <summary>Appends a timestamped breadcrumb to startup.log - cheap enough to call liberally around startup phases.</summary>
    public static void LogMilestone(string message)
    {
        AppendLine(StartupLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");
    }

    /// <summary>
    /// Writes a full crash report (program info + exception chain, same shape as CE's own crash
    /// dialog content) to its own timestamped file, and a one-line pointer to startup.log so the
    /// breadcrumb trail and the crash detail are easy to correlate.
    /// </summary>
    public static void LogCrash(string context, Exception? exception, bool isTerminating)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            string fileName = $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log";
            string path = Path.Combine(LogDirectory, fileName);

            var sb = new StringBuilder();
            WriteProgramInfo(sb);
            sb.AppendLine(new string('-', 20));
            sb.AppendLine($"Context      : {context}");
            sb.AppendLine($"Terminating  : {isTerminating}");
            AppendException(sb, exception);
            sb.AppendLine(new string('-', 20));
            sb.AppendLine($"Report generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

            lock (WriteLock)
            {
                File.WriteAllText(path, sb.ToString());
            }

            LogMilestone($"CRASH [{context}] terminating={isTerminating} -> {fileName}");
            PruneOldCrashLogs();
        }
        catch
        {
            // A failure while logging a crash must never mask or replace the original crash.
        }
    }

    private static void WriteProgramInfo(StringBuilder sb)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        sb.AppendLine("Application  : Paperbunkr");
        sb.AppendLine($"Assembly     : {entryAssembly?.GetName().Version}");
        sb.AppendLine($"OS           : {Environment.OSVersion} ({(Environment.Is64BitProcess ? "64" : "32")}-bit)");
        sb.AppendLine($".NET         : {Environment.Version}");
        sb.AppendLine($"Processors   : {Environment.ProcessorCount}");
        sb.AppendLine($"Working set  : {Environment.WorkingSet / 1024 / 1024} MB");
    }

    private static void AppendException(StringBuilder sb, Exception? exception)
    {
        int depth = 0;
        while (exception is not null)
        {
            sb.AppendLine(new string('-', 20));
            sb.AppendLine(depth == 0 ? exception.GetType().FullName : $"Inner ({depth}): {exception.GetType().FullName}");
            if (exception.TargetSite is not null)
            {
                sb.AppendLine(exception.TargetSite.ToString());
            }

            sb.AppendLine(exception.Message);
            sb.AppendLine(exception.StackTrace);
            exception = exception.InnerException;
            depth++;
        }
    }

    private static void AppendLine(string path, string line)
    {
        try
        {
            lock (WriteLock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never throw into the code path they're observing.
        }
    }

    private static void PruneOldCrashLogs()
    {
        try
        {
            var stale = new DirectoryInfo(LogDirectory)
                .GetFiles("crash-*.log")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(MaxCrashLogsRetainedOverride ?? MaxCrashLogsRetained);

            foreach (var file in stale)
            {
                file.Delete();
            }
        }
        catch
        {
            // Retention is best-effort - never let cleanup itself become a crash source.
        }
    }

    private static string GetDefaultLogDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "logs");
    }
}
