using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// Database backup/restore (docs/superpowers/specs/2026-08-07-preferences-advanced-tab-design.md
/// §3) - a from-scratch, right-sized replacement for CE's own multi-file
/// <c>ComicRack.Engine/Backup/BackupManager</c> apparatus, which backs up a sprawling footprint
/// (ini/config/scripts/resources/thumbnails/cache) Paperbunkr doesn't have. Paperbunkr's entire
/// footprint of record is one SQLite file, so this just copies that file. Manual "Backup Now"/
/// "Restore" only - scheduled on-startup/on-shutdown triggers are a deliberately deferred
/// follow-up once this is proven.
/// </summary>
public class BackupService
{
    private const string FilePrefix = "paperbunkr_backup_";

    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public BackupService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal BackupService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public string GetBackupLocation()
    {
        using var context = _contextFactory();
        string? configured = context.GetOrCreateAppSettings().BackupLocation;
        return string.IsNullOrWhiteSpace(configured) ? DefaultBackupLocation() : configured;
    }

    public void SetBackupLocation(string location)
    {
        using var context = _contextFactory();
        context.GetOrCreateAppSettings().BackupLocation = location;
        context.SaveChanges();
    }

    public int GetBackupsToKeep()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().BackupsToKeep;
    }

    public void SetBackupsToKeep(int count)
    {
        using var context = _contextFactory();
        context.GetOrCreateAppSettings().BackupsToKeep = Math.Max(count, 0);
        context.SaveChanges();
    }

    private static string DefaultBackupLocation() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paperbunkr", "backups");

    /// <summary>
    /// Copies the live database file to a timestamped backup, then prunes anything beyond
    /// <see cref="Data.Entities.AppSettings.BackupsToKeep"/>. A plain <see cref="File.Copy(string, string)"/>,
    /// not <c>SqliteConnection.BackupDatabase</c> - every <see cref="PaperbunkrDbContext"/> in this
    /// codebase is already short-lived and closed between operations. <c>ClearAllPools</c> is
    /// still required first though: Microsoft.Data.Sqlite keeps disposed connections in an
    /// internal pool rather than truly releasing the underlying file, and a raw byte copy against
    /// a file a pooled connection still holds open can capture an inconsistent snapshot (confirmed
    /// empirically - a restored backup came back missing tables without this call).
    /// </summary>
    public string BackupNow()
    {
        string location = GetBackupLocation();
        Directory.CreateDirectory(location);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        string dbPath = PaperbunkrDbContext.GetDefaultDatabasePath();
        string backupPath = Path.Combine(location, $"{FilePrefix}{DateTime.UtcNow:yyyyMMdd_HHmmss}.db");
        File.Copy(dbPath, backupPath, overwrite: false);

        PruneOldBackups(location);
        return backupPath;
    }

    private void PruneOldBackups(string location)
    {
        int keep = GetBackupsToKeep();
        var files = Directory.GetFiles(location, $"{FilePrefix}*.db").OrderByDescending(f => f).ToList();

        foreach (string old in files.Skip(keep))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Newest first, matching filename timestamp ordering.</summary>
    public IReadOnlyList<string> GetAvailableBackups()
    {
        string location = GetBackupLocation();
        return Directory.Exists(location)
            ? Directory.GetFiles(location, $"{FilePrefix}*.db").OrderByDescending(f => f).ToList()
            : Array.Empty<string>();
    }

    /// <summary>Overwrites the live database file with <paramref name="backupFilePath"/>'s content. Requires an app restart to take effect - Paperbunkr has no safe way to hot-swap the file underneath an app that's already running against it.</summary>
    public void RestoreBackup(string backupFilePath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Copy(backupFilePath, PaperbunkrDbContext.GetDefaultDatabasePath(), overwrite: true);
    }
}
