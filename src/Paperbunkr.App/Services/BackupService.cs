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

    /// <summary>
    /// The folder backups live in - the user's <see cref="Data.Entities.AppSettings.BackupLocation"/>
    /// override, else <see cref="DefaultBackupLocation"/>.
    /// </summary>
    /// <remarks>
    /// Must never throw. This is on the database-recovery path (<c>App.HandleDatabaseRecovery</c> →
    /// <see cref="GetAvailableBackups"/> → here), which runs <b>precisely when the database is
    /// unreadable</b> - so opening it to read the override can fail with
    /// <c>SqliteException: database disk image is malformed</c>. Before this guard that crashed the
    /// process before <c>DatabaseRecoveryWindow</c> could even appear, making the built-in
    /// backup-restore recovery flow unreachable exactly when it's needed (real incident 2026-09-03).
    /// A corrupt DB falls back to the default backups folder.
    /// </remarks>
    public string GetBackupLocation()
    {
        try
        {
            using var context = _contextFactory();
            string? configured = context.GetOrCreateAppSettings().BackupLocation;
            return string.IsNullOrWhiteSpace(configured) ? DefaultBackupLocation() : configured;
        }
        catch (Exception)
        {
            return DefaultBackupLocation();
        }
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

    public bool GetAutoBackupEnabled()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().AutoBackupEnabled;
    }

    public void SetAutoBackupEnabled(bool enabled)
    {
        using var context = _contextFactory();
        context.GetOrCreateAppSettings().AutoBackupEnabled = enabled;
        context.SaveChanges();
    }

    public int GetAutoBackupMinIntervalHours()
    {
        using var context = _contextFactory();
        return context.GetOrCreateAppSettings().AutoBackupMinIntervalHours;
    }

    public void SetAutoBackupMinIntervalHours(int hours)
    {
        using var context = _contextFactory();
        context.GetOrCreateAppSettings().AutoBackupMinIntervalHours = Math.Max(hours, 1);
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

        string dbPath = PaperbunkrDbContext.GetDefaultDatabasePath();
        CheckpointWal(dbPath);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        string backupPath = Path.Combine(location, $"{FilePrefix}{DateTime.UtcNow:yyyyMMdd_HHmmss}.db");
        File.Copy(dbPath, backupPath, overwrite: false);

        PruneOldBackups(location);
        return backupPath;
    }

    /// <summary>
    /// Flushes WAL-mode's <c>-wal</c> file into the main <c>.db</c> file before a plain byte copy
    /// (docs/superpowers/specs/2026-08-29-db-corruption-safeguards-design.md §2) - since
    /// <see cref="PaperbunkrDb.CreateContext"/> now sets <c>journal_mode=WAL</c>, recently committed
    /// data can live only in the <c>-wal</c> sidecar until checkpointed, and a raw copy of just
    /// <c>paperbunkr.db</c> would silently miss it. <c>TRUNCATE</c> (not <c>PASSIVE</c>/<c>FULL</c>)
    /// both flushes and truncates the <c>-wal</c> file back to zero bytes, so this keeps
    /// <see cref="BackupNow"/> a single-file copy rather than needing to also copy the sidecars.
    /// No-op (harmless) if the file doesn't exist yet or isn't in WAL mode.
    /// </summary>
    private static void CheckpointWal(string dbPath)
    {
        if (!File.Exists(dbPath))
        {
            return;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Automatic backup trigger (docs/superpowers/specs/2026-08-29-db-corruption-safeguards-
    /// design.md §2), called from <c>App.axaml.cs</c> on startup and clean shutdown. No-ops if
    /// <see cref="Data.Entities.AppSettings.AutoBackupEnabled"/> is off, or if the newest existing
    /// backup is younger than <see cref="Data.Entities.AppSettings.AutoBackupMinIntervalHours"/> -
    /// otherwise every restart in a short session would add its own backup and
    /// <c>BackupsToKeep</c>'s rotation window would fill with startup noise instead of history.
    /// Best-effort: any failure (missing file, locked file, etc.) is swallowed, since both call
    /// sites treat this as a background nicety, never something that should block startup or delay
    /// shutdown.
    /// </summary>
    public void RunAutoBackupIfDue()
    {
        try
        {
            if (!GetAutoBackupEnabled())
            {
                return;
            }

            var newest = GetAvailableBackups().FirstOrDefault();
            if (newest is not null && TryParseBackupTimestamp(newest, out DateTime newestUtc))
            {
                double ageHours = (DateTime.UtcNow - newestUtc).TotalHours;
                if (ageHours < GetAutoBackupMinIntervalHours())
                {
                    return;
                }
            }

            BackupNow();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Parses the UTC timestamp out of a <c>paperbunkr_backup_yyyyMMdd_HHmmss.db</c> filename, as written by <see cref="BackupNow"/>.</summary>
    private static bool TryParseBackupTimestamp(string backupPath, out DateTime utc)
    {
        string name = Path.GetFileNameWithoutExtension(backupPath);
        string stamp = name.StartsWith(FilePrefix, StringComparison.Ordinal) ? name[FilePrefix.Length..] : name;
        return DateTime.TryParseExact(
            stamp, "yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out utc);
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
