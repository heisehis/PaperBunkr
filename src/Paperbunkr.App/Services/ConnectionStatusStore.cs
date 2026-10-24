using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services;

/// <summary>
/// Remembers the result of the last explicit connection check per provider (docs/superpowers/specs/2026-10-12-connections-appearance-redesign-design.md,
/// "Persistence of check results"): <c>connection-status.json</c> next to the database, so a database-path override moves it too. A cache, not a
/// source of truth - a file that cannot be read is treated as empty, and writes go through a temporary file so a crash cannot leave a half-written one.
/// No EF migration.
/// </summary>
public sealed class ConnectionStatusStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private readonly string _path;

    public ConnectionStatusStore(string path) => _path = path;

    public string FilePath => _path;

    public static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(Paperbunkr.Data.PaperbunkrDbContext.GetDefaultDatabasePath()) ?? ".", "connection-status.json");

    public IReadOnlyDictionary<string, ConnectionCheck> Load()
    {
        var result = new Dictionary<string, ConnectionCheck>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(_path))
            {
                return result;
            }

            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(_path), JsonOptions);
            if (file?.Checks is null || file.Version != CurrentVersion)
            {
                return result;
            }

            foreach (var (id, entry) in file.Checks)
            {
                if (entry is not null && Enum.TryParse<ConnectionFailure>(entry.Failure, out var failure))
                {
                    result[id] = new ConnectionCheck(entry.Ok, failure, entry.CheckedUtc, entry.Detail);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            result.Clear();
        }

        return result;
    }

    /// <summary>Stores <paramref name="check"/> for <paramref name="providerId"/>, keeping every other provider's last result. Best effort: a failed write is swallowed, since losing a cached status must never break the page.</summary>
    public void Record(string providerId, ConnectionCheck check)
    {
        var all = new Dictionary<string, StoreEntry?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, existing) in Load())
        {
            all[id] = new StoreEntry { Ok = existing.Ok, Failure = existing.Failure.ToString(), CheckedUtc = existing.CheckedUtc, Detail = existing.Detail };
        }

        all[providerId] = new StoreEntry { Ok = check.Ok, Failure = check.Failure.ToString(), CheckedUtc = check.CheckedUtc, Detail = check.Detail };
        Write(all);
    }

    /// <summary>Forgets a provider's last result (a fresh sign-in or a disconnect supersedes it).</summary>
    public void Remove(string providerId)
    {
        var existing = Load();
        if (!existing.ContainsKey(providerId))
        {
            return;
        }

        var all = new Dictionary<string, StoreEntry?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, check) in existing)
        {
            if (!string.Equals(id, providerId, StringComparison.OrdinalIgnoreCase))
            {
                all[id] = new StoreEntry { Ok = check.Ok, Failure = check.Failure.ToString(), CheckedUtc = check.CheckedUtc, Detail = check.Detail };
            }
        }

        Write(all);
    }

    private void Write(Dictionary<string, StoreEntry?> all)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new StoreFile { Version = CurrentVersion, Checks = all }, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A cache - nothing to recover.
        }
    }

    private sealed class StoreFile
    {
        public int Version { get; set; }

        public Dictionary<string, StoreEntry?>? Checks { get; set; }
    }

    private sealed class StoreEntry
    {
        public bool Ok { get; set; }

        public string? Failure { get; set; }

        public DateTimeOffset CheckedUtc { get; set; }

        public string? Detail { get; set; }
    }
}
