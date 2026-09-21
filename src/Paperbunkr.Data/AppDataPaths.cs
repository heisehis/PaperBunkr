using System;
using System.IO;

namespace Paperbunkr.Data;

/// <summary>
/// The one place every per-user Paperbunkr data path is rooted (docs/superpowers/specs/
/// 2026-09-19-remote-library-sharing-design.md §10/§11 P0). By default that is
/// <c>%AppData%\Paperbunkr</c>, exactly where every path lived before this helper existed; setting the
/// <c>PAPERBUNKR_DATA_DIR</c> environment variable relocates <em>all</em> of it - database, covers,
/// backups, plugins, logs, graphics/bootstrap state - so a second instance (or a UI test) can run
/// fully isolated from the real profile.
/// </summary>
/// <remarks>
/// <c>PAPERBUNKR_DB_PATH</c> (see <see cref="PaperbunkrDbContext.DatabasePathOverride"/>) still wins
/// for the database file alone; the data dir only decides where the DB goes when that is unset.
/// Set the environment variable on the child process before launch - like
/// <c>DatabasePathOverride</c>, the static is seeded once at type initialization.
/// </remarks>
public static class AppDataPaths
{
    public const string EnvironmentVariableName = "PAPERBUNKR_DATA_DIR";

    /// <summary>Mutable so tests can redirect; never set outside a test's own constructor/teardown. Null means "use the per-user default".</summary>
    public static string? RootOverride { get; set; } = ReadEnvironmentOverride();

    /// <summary>The roaming per-user Paperbunkr folder (or the override). Not created here - callers create what they use.</summary>
    public static string Root => RootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paperbunkr");

    /// <summary>The non-roaming per-user Paperbunkr folder (WebView2 profile). Same override as <see cref="Root"/> when one is set, so an isolated instance keeps everything under one directory.</summary>
    public static string LocalRoot => RootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paperbunkr");

    /// <summary><see cref="Root"/> joined with <paramref name="segments"/>.</summary>
    public static string Combine(params string[] segments) => Path.Combine(Root, Path.Combine(segments));

    private static string? ReadEnvironmentOverride()
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.IsNullOrWhiteSpace(raw) ? null : Path.GetFullPath(raw.Trim());
    }
}
