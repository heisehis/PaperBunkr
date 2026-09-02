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
    /// <remarks>
    /// Corruption severe enough to break the schema b-tree itself (page 1) can make SQLite throw
    /// <see cref="Microsoft.Data.Sqlite.SqliteException"/> (SQLITE_CORRUPT) straight out of
    /// <c>ExecuteScalar</c> instead of letting <c>integrity_check</c> return a descriptive string -
    /// confirmed via a deliberately-corrupted page 1 fixture. That's still corruption, just a
    /// louder flavor of it, and must be caught here rather than propagate: this method exists
    /// specifically so a corrupt file gets the recovery dialog (design doc §3) instead of
    /// bubbling up to <c>DiagnosticsService.LogCrash(isTerminating: true)</c>'s one-way crash
    /// dialog - the exact failure mode this whole feature was built to replace.
    /// </remarks>
    public static bool CheckIntegrity(out string? detail)
    {
        string dbPath = PaperbunkrDbContext.GetDefaultDatabasePath();
        if (!File.Exists(dbPath))
        {
            detail = null;
            return true;
        }

        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            string result = (string)cmd.ExecuteScalar()!;
            detail = result;
            return result == "ok";
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            detail = ex.Message;
            return false;
        }
    }
}
