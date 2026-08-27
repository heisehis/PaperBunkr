using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Resolved rendering configuration - a value mirror of <see cref="AppSettings.RenderingBackend"/>
/// and <see cref="AppSettings.PreferNativeOpenGl"/>.
/// </summary>
public sealed record GraphicsConfig(RenderBackend Backend, bool PreferNativeOpenGl)
{
    public static GraphicsConfig Default { get; } = new(RenderBackend.Auto, false);
}

/// <summary>
/// Pre-UI rendering-backend resolution (docs/superpowers/specs/2026-08-27-hardware-accelerated-
/// rendering-design.md). The graphics stack is chosen inside <c>Program.BuildAvaloniaApp()</c>,
/// long before the SQLite database is opened/migrated in
/// <c>App.OnFrameworkInitializationCompleted</c>, so the persisted <see cref="AppSettings"/>
/// value can't be read there directly. Instead it is mirrored to a tiny standalone
/// <c>%AppData%\Paperbunkr\graphics.json</c> file - read here with no EF/SQLite dependency - and
/// reconciled back to the DB value once it becomes available (<see cref="SyncCache"/>).
///
/// Nothing here logs directly; callers (<c>Program</c>, <c>App</c>) emit the milestones so this
/// stays free of <see cref="DiagnosticsService"/> coupling and trivially unit-testable.
/// </summary>
public static class GraphicsBootstrap
{
    internal const string EnvVarName = "PAPERBUNKR_RENDER";

    /// <summary>Test-only redirect for <see cref="CachePath"/>, same pattern as <c>DiagnosticsService.LogDirectoryOverride</c>.</summary>
    internal static string? CachePathOverride { get; set; }

    /// <summary>Test-only override for environment-variable reads, so tests never touch the real process environment.</summary>
    internal static Func<string, string?>? EnvReaderOverride { get; set; }

    public static string CachePath => CachePathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Paperbunkr",
        "graphics.json");

    /// <summary>
    /// Bootstrap read, called from <c>Program.Main</c> before Avalonia starts and before the
    /// database is available. Precedence: <c>PAPERBUNKR_RENDER</c> env var (if a recognized value)
    /// overrides only <see cref="GraphicsConfig.Backend"/> → <c>graphics.json</c> →
    /// <see cref="GraphicsConfig.Default"/>. Never throws.
    /// </summary>
    /// <returns>
    /// The resolved config plus a short human-readable source string for the startup breadcrumb
    /// ("env", "graphics.json", "graphics.json + env", "default (no cache)",
    /// "default (graphics.json unreadable)").
    /// </returns>
    public static (GraphicsConfig Config, string Source) Resolve()
    {
        var (fileConfig, fileSource) = ReadCache();

        RenderBackend? envBackend = ParseBackend(ReadEnv(EnvVarName));
        if (envBackend is { } backend)
        {
            string source = fileSource is "graphics.json" ? "graphics.json + env" : "env";
            return (fileConfig with { Backend = backend }, source);
        }

        return (fileConfig, fileSource);
    }

    /// <summary>
    /// Maps a resolved config to the priority-ordered <see cref="Win32RenderingMode"/> fallback
    /// chain (spec §4). The first element that initializes wins.
    /// </summary>
    public static Win32RenderingMode[] ToRenderingModes(GraphicsConfig config) => config.Backend switch
    {
        RenderBackend.Software => new[] { Win32RenderingMode.Software },
        RenderBackend.Gpu => config.PreferNativeOpenGl
            ? new[] { Win32RenderingMode.Wgl, Win32RenderingMode.AngleEgl }
            : new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl },
        _ => config.PreferNativeOpenGl
            ? new[] { Win32RenderingMode.Wgl, Win32RenderingMode.AngleEgl, Win32RenderingMode.Software }
            : new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl, Win32RenderingMode.Software },
    };

    /// <summary>
    /// Post-DB reconciliation, called from <c>App.OnFrameworkInitializationCompleted</c> once the
    /// persisted <see cref="AppSettings"/> values are readable. Rewrites <c>graphics.json</c> to
    /// match iff it currently differs (or is missing/unreadable). Never throws - a failed write
    /// (read-only <c>%AppData%</c>) just means the stale cache is used next launch.
    /// </summary>
    /// <returns><see langword="true"/> if the file was (re)written.</returns>
    public static bool SyncCache(RenderBackend backend, bool preferNativeOpenGl)
    {
        var desired = new GraphicsConfig(backend, preferNativeOpenGl);
        var (current, source) = ReadCache();
        if (source is "graphics.json" && current == desired)
        {
            return false;
        }

        try
        {
            string? dir = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(
                new CacheDto
                {
                    Backend = backend.ToString().ToLowerInvariant(),
                    PreferNativeOpenGl = preferNativeOpenGl,
                },
                SerializerOptions);
            File.WriteAllText(CachePath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (GraphicsConfig Config, string Source) ReadCache()
    {
        string path = CachePath;
        if (!File.Exists(path))
        {
            return (GraphicsConfig.Default, "default (no cache)");
        }

        try
        {
            CacheDto? dto = JsonSerializer.Deserialize<CacheDto>(File.ReadAllText(path), SerializerOptions);
            if (dto is null)
            {
                return (GraphicsConfig.Default, "default (graphics.json unreadable)");
            }

            bool preferWgl = dto.PreferNativeOpenGl;
            RenderBackend? parsed = ParseBackend(dto.Backend);
            return (new GraphicsConfig(parsed ?? RenderBackend.Auto, preferWgl), "graphics.json");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return (GraphicsConfig.Default, "default (graphics.json unreadable)");
        }
    }

    private static string? ReadEnv(string name) =>
        (EnvReaderOverride ?? Environment.GetEnvironmentVariable)(name);

    private static RenderBackend? ParseBackend(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out RenderBackend parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class CacheDto
    {
        [JsonPropertyName("backend")]
        public string? Backend { get; set; }

        [JsonPropertyName("preferNativeOpenGl")]
        public bool PreferNativeOpenGl { get; set; }
    }
}
