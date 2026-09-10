using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Paperbunkr.App.Services;

/// <summary>
/// How the current launch should proceed, decided from the on-disk bootstrap sentinel.
/// </summary>
public enum BootstrapDecision
{
    /// <summary>No unresolved prior failure - start normally.</summary>
    Normal,

    /// <summary>The previous launch never finished bootstrapping - retry with software rendering forced on.</summary>
    SafeMode,

    /// <summary>A software-rendering retry also failed - stop trying to start Avalonia; show a native notice.</summary>
    GiveUp,
}

/// <summary>
/// Crash-recovery sentinel for the pre-window startup phase (docs/superpowers/specs/
/// 2026-09-10-bootstrap-crash-sentinel-safe-mode-design.md). A crash between
/// <c>StartWithClassicDesktopLifetime</c> and the splash window rendering - a native access
/// violation from a bad GPU, a broken driver, or (historically) a bad ReadyToRun image - is not a
/// catchable managed exception and leaves no <c>crash-*.log</c>. The only state that survives is a
/// file written to disk <em>before</em> the crash: <see cref="Record"/> stamps one right before
/// Avalonia starts, <see cref="Clear"/> removes it the instant the splash renders, and
/// <see cref="Evaluate"/> reads it on the next launch to decide whether to force software rendering
/// (<see cref="BootstrapDecision.SafeMode"/>) or give up (<see cref="BootstrapDecision.GiveUp"/>).
///
/// <para>
/// Every method is best-effort: an unwritable <c>%AppData%</c> makes all of them no-ops and the
/// app behaves exactly as it did before this class existed. Path-overridable for tests, mirroring
/// <see cref="GraphicsBootstrap"/> / <see cref="DiagnosticsService"/>.
/// </para>
/// </summary>
public static class BootstrapSentinel
{
    /// <summary>Test-only redirect for <see cref="StatePath"/>. Never set outside a test's own ctor/teardown.</summary>
    internal static string? StatePathOverride { get; set; }

    public static string StatePath => StatePathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Paperbunkr",
        "bootstrap.state");

    /// <summary>
    /// Read the sentinel and decide how this launch should proceed. Never throws - any missing
    /// file, I/O error, or parse failure resolves to <see cref="BootstrapDecision.Normal"/>.
    /// </summary>
    public static BootstrapDecision Evaluate(Version currentVersion)
    {
        var state = TryRead();
        if (state is null)
        {
            return BootstrapDecision.Normal;
        }

        // A fresh install or a version upgrade earns one clean normal-path attempt regardless of
        // what the previous version left behind.
        if (state.AppVersion != currentVersion.ToString())
        {
            return BootstrapDecision.Normal;
        }

        return state.Attempts <= 1 ? BootstrapDecision.SafeMode : BootstrapDecision.GiveUp;
    }

    /// <summary>
    /// Record that a bootstrap attempt is starting. Creates the file at <c>attempts = 1</c>, or
    /// increments <c>attempts</c> on an existing file for the same app version (stamping
    /// <c>firstFailUtc</c> the first time it finds one). A version mismatch resets to
    /// <c>attempts = 1</c>, so this agrees with <see cref="Evaluate"/>'s upgrade reset. Best-effort.
    /// Call in <c>Program.Main</c> immediately before <c>StartWithClassicDesktopLifetime</c>.
    /// </summary>
    public static void Record(Version currentVersion)
    {
        try
        {
            var existing = TryRead();
            State next;
            if (existing is null || existing.AppVersion != currentVersion.ToString())
            {
                next = new State
                {
                    Attempts = 1,
                    AppVersion = currentVersion.ToString(),
                    FirstFailUtc = null,
                };
            }
            else
            {
                next = new State
                {
                    Attempts = existing.Attempts + 1,
                    AppVersion = existing.AppVersion,
                    FirstFailUtc = existing.FirstFailUtc ?? DateTime.UtcNow,
                };
            }

            Write(next);
            DiagnosticsService.LogMilestone($"Bootstrap sentinel recorded: attempts={next.Attempts}");
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Bootstrap sentinel write failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>
    /// Bootstrap reached a rendered window - delete the sentinel. Called once, immediately after
    /// <c>splash.Show()</c> returns in <c>App.RunDesktopStartupAsync</c>. Best-effort; a missing
    /// file is not an error.
    /// </summary>
    public static void Clear() => Delete("Bootstrap sentinel cleared.");

    /// <summary>
    /// Delete the sentinel after the <see cref="BootstrapDecision.GiveUp"/> notice has been shown,
    /// so a later manual launch (post driver-fix / reinstall) starts clean at
    /// <see cref="BootstrapDecision.Normal"/> instead of looping straight back to give-up.
    /// </summary>
    public static void ResetAfterGiveUp() => Delete("Bootstrap give-up: shown native notice, reset sentinel.");

    private static void Delete(string milestone)
    {
        try
        {
            File.Delete(StatePath);
            DiagnosticsService.LogMilestone(milestone);
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Bootstrap sentinel delete failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    private static State? TryRead()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath), SerializerOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Write(State state)
    {
        string? dir = Path.GetDirectoryName(StatePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, SerializerOptions));
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class State
    {
        [JsonPropertyName("attempts")]
        public int Attempts { get; set; }

        [JsonPropertyName("firstFailUtc")]
        public DateTime? FirstFailUtc { get; set; }

        [JsonPropertyName("appVersion")]
        public string AppVersion { get; set; } = string.Empty;
    }
}
