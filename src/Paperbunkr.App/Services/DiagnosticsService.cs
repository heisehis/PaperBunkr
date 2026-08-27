using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Services;

/// <summary>
/// Startup-milestone breadcrumbs and crash capture, written to plain-text files under
/// <c>%AppData%\Paperbunkr\logs</c>. Mirrors ComicRackCE's own diagnostic report shape
/// (<c>cYo.Common.Runtime.Diagnostic.WriteProgramInfo</c> + <c>CrashDialog.OnBark</c>'s
/// program-info/exception-chain/timestamp layout) rather than inventing a new one. Originally built
/// as a pure file sink (a live session had a genuinely silent <c>Database.Migrate()</c> interruption
/// take an hour of forensic SQL/EF probing to diagnose with no log to consult) with no in-app crash
/// dialog - <see cref="Views.CrashReportWindow"/> (docs/superpowers/specs/
/// 2026-08-23-app-chrome-crash-reporter-and-tray-design.md) added that on top of the same capture
/// hooks, still with zero change to what gets logged or where.
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
        {
            var exception = e.ExceptionObject as Exception;
            LogCrash("AppDomain.UnhandledException", exception, e.IsTerminating);
            // allowContinue: false - the CLR terminates the process the instant this handler
            // returns regardless of anything we do here, so offering a Continue button would
            // misrepresent what's about to happen. Always resolves to a clean, explicit exit
            // instead of letting the runtime's own unhandled-exception termination run (which on
            // Windows can surface an OS-level "stopped working" prompt).
            ShowCrashDialogAndExit("AppDomain.UnhandledException", exception, allowContinue: false);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // isTerminating: false - the app keeps running with no visible break (the fault was
            // already swallowed by the time this fires), so no dialog: showing a crash-style
            // interruption for something the user never actually experienced would be a new,
            // unwarranted regression, not a feature. Log only.
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception, isTerminating: false);
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            LogCrash("Dispatcher.UIThread.UnhandledException", e.Exception, isTerminating: true);
            // Unlike AppDomain.UnhandledException, this source genuinely supports swallow-and-
            // continue: DispatcherUnhandledExceptionEventArgs.Handled (verified against Avalonia's
            // own Avalonia.Base.xml docs - correcting this comment's own prior claim that no such
            // thing existed) suppresses the crash exactly like WinForms' own
            // ThreadExceptionEventArgs.Handled, which CE's Abort button relied on.
            var outcome = ShowCrashDialog("Dispatcher.UIThread.UnhandledException", e.Exception, allowContinue: true);
            if (outcome == CrashOutcome.Continue)
            {
                e.Handled = true;
                return;
            }

            ActOnCrashOutcome(outcome);
        };

        LogMilestone("Diagnostics installed.");
    }

    /// <summary>
    /// Shows <see cref="CrashReportWindow"/> for a source that never offers Continue, then always
    /// exits (Restart relaunches first) - the shared tail end of the
    /// <see cref="AppDomain.UnhandledException"/> path. If no outcome could be obtained (dialog
    /// unavailable - see <see cref="ShowCrashDialog"/>), still exits explicitly rather than falling
    /// through to the CLR's own termination.
    /// </summary>
    private static void ShowCrashDialogAndExit(string context, Exception? exception, bool allowContinue)
    {
        var outcome = ShowCrashDialog(context, exception, allowContinue);
        ActOnCrashOutcome(outcome);
        Environment.Exit(1);
    }

    private static void ActOnCrashOutcome(CrashOutcome? outcome)
    {
        if (outcome != CrashOutcome.Restart)
        {
            return;
        }

        try
        {
            string? exePath = Environment.ProcessPath;
            if (exePath is not null)
            {
                Process.Start(exePath);
            }
        }
        catch
        {
            // A failed relaunch must not prevent the exit that's about to happen anyway.
        }

        Environment.Exit(0);
    }

    /// <summary>
    /// Blocks the calling thread (any thread - marshals to the UI thread if needed) until the user
    /// responds to <see cref="CrashReportWindow"/>, or returns <c>null</c> if the dialog couldn't be
    /// shown at all (Avalonia not yet running - an early-startup crash, or the dialog itself threw).
    /// Never lets a failure here mask the original crash, which is already durably logged by the
    /// time this runs.
    /// </summary>
    private static CrashOutcome? ShowCrashDialog(string context, Exception? exception, bool allowContinue)
    {
        try
        {
            if (Application.Current is null)
            {
                return null;
            }

            string report = BuildReport(context, exception, isTerminating: allowContinue is false);
            return Dispatcher.UIThread.CheckAccess()
                ? CrashReportWindow.ShowModal(report, allowContinue)
                : Dispatcher.UIThread.Invoke(() => CrashReportWindow.ShowModal(report, allowContinue));
        }
        catch
        {
            return null;
        }
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

            lock (WriteLock)
            {
                File.WriteAllText(path, BuildReport(context, exception, isTerminating));
            }

            LogMilestone($"CRASH [{context}] terminating={isTerminating} -> {fileName}");
            PruneOldCrashLogs();
        }
        catch
        {
            // A failure while logging a crash must never mask or replace the original crash.
        }
    }

    /// <summary>
    /// Builds the full report text (program info + exception chain, same shape as CE's own crash
    /// dialog content) - shared by <see cref="LogCrash"/>'s file write and
    /// <see cref="CrashReportWindow"/>'s displayed text, so the two are always identical.
    /// </summary>
    private static string BuildReport(string context, Exception? exception, bool isTerminating)
    {
        var sb = new StringBuilder();
        WriteProgramInfo(sb);
        sb.AppendLine(new string('-', 20));
        sb.AppendLine($"Context      : {context}");
        sb.AppendLine($"Terminating  : {isTerminating}");
        AppendException(sb, exception);
        sb.AppendLine(new string('-', 20));
        sb.AppendLine($"Report generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        return sb.ToString();
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
