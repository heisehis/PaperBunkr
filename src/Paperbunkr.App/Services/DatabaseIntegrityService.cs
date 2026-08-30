using System.IO;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// Startup corruption detection (docs/superpowers/specs/2026-08-29-db-corruption-safeguards-
/// design.md §3), called from <c>App.axaml.cs</c> before <see cref="PaperbunkrDb.HasAnySeries"/>/
/// <see cref="PaperbunkrDb.EnsureCreated"/> ever touch the live database, so a genuinely corrupt
/// file is caught before EF/migrations attempt to open it.
/// </summary>
public static class DatabaseIntegrityService
{
    /// <summary>
    /// Runs <c>PRAGMA integrity_check</c> against the live db file via a throwaway connection.
    /// Returns true (with <paramref name="detail"/> null) if the file doesn't exist yet - nothing
    /// to check, this is the first-launch case - or if the check passes. Returns false only on a
    /// genuine structural problem, with the raw pragma result (never just "ok") in
    /// <paramref name="detail"/>.
    /// </summary>
    public static bool CheckIntegrity(out string? detail)
    {
        string dbPath = PaperbunkrDbContext.GetDefaultDatabasePath();
        if (!File.Exists(dbPath))
        {
            detail = null;
            return true;
        }

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        string result = (string)cmd.ExecuteScalar()!;
        detail = result;
        return result == "ok";
    }
}
