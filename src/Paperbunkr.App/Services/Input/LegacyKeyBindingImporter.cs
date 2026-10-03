using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Input;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// One-time import of the keyboard remaps stored by the old system (the <c>KeyBinding</c> table, or a layout file exported from Preferences) into the new keymap
/// (docs/superpowers/specs/2026-10-03-input-service-design.md §4 Q11). The old semantics carry over exactly: a command with no stored rows was never customised and keeps its
/// defaults; one or more rows are authoritative for that command. The table itself is only ever read, never changed or dropped, because other checkouts share the database.
/// </summary>
public static class LegacyKeyBindingImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record ExportedBinding(string CommandId, string Gesture);

    /// <summary>Reads every stored <c>(CommandId, Key)</c> row from the old table.</summary>
    public static IReadOnlyList<(string CommandId, string Gesture)> ReadDatabaseRows(Func<PaperbunkrDbContext> contextFactory)
    {
        using var context = contextFactory();
        return context.KeyBindings.AsEnumerable().Select(k => (k.CommandId, k.Key)).ToList();
    }

    /// <summary>
    /// Reads a layout file exported by the old Preferences &gt; Keyboard Shortcuts (a JSON list of <c>{CommandId, Gesture}</c> objects, several per command allowed).
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not that shape.</exception>
    public static IReadOnlyList<(string CommandId, string Gesture)> ReadExportedLayout(string json)
    {
        List<ExportedBinding>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<ExportedBinding>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            entries = null;
        }

        return entries is null
            ? throw new InvalidDataException("The file is not a keyboard shortcut layout.")
            : entries.Where(e => e is not null && !string.IsNullOrEmpty(e.CommandId)).Select(e => (e.CommandId, e.Gesture ?? string.Empty)).ToList();
    }

    /// <summary>
    /// Writes the rows into the config's overrides: for each known command, exactly the rows that parse (an entry whose gestures all fail to parse leaves the command at its
    /// defaults, as the old reader did). Rows for commands the catalog does not know are ignored. Returns how many gestures were applied.
    /// </summary>
    public static int Apply(KeymapConfig config, IInputActionCatalog catalog, IEnumerable<(string CommandId, string Gesture)> rows)
    {
        int applied = 0;
        foreach (var group in rows.GroupBy(r => r.CommandId))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || catalog.Find(group.Key) is not { } info)
            {
                continue;
            }

            var bindings = new List<InputBinding>();
            foreach (var (_, gesture) in group)
            {
                try
                {
                    var binding = InputBinding.FromGesture(KeyGesture.Parse(gesture));
                    if (binding.IsDefined && !bindings.Contains(binding))
                    {
                        bindings.Add(binding);
                        applied++;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException)
                {
                    // A stale or corrupt row: skip it and keep the rest of the command's rows, as the old reader did.
                }
            }

            if (bindings.Count == 0)
            {
                continue;
            }

            if (bindings.SequenceEqual(info.Defaults))
            {
                config.Overrides.Remove(info.Id);
            }
            else
            {
                config.Overrides[info.Id] = bindings.Select(b => b.ToString()).ToList();
            }
        }

        return applied;
    }

    /// <summary>
    /// Imports the database table into <paramref name="service"/> once: a no-op when <see cref="KeymapConfig.LegacyBindingsImported"/> is already set. Returns how many
    /// gestures were applied. A database that cannot be read marks nothing and returns 0, so the import retries on the next start.
    /// </summary>
    public static int ImportOnce(InputService service, Func<PaperbunkrDbContext> contextFactory)
    {
        if (service.Config.LegacyBindingsImported)
        {
            return 0;
        }

        IReadOnlyList<(string CommandId, string Gesture)> rows;
        try
        {
            rows = ReadDatabaseRows(contextFactory);
        }
        catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return 0;
        }

        int applied = Apply(service.Config, service.Actions, rows);
        service.Config.LegacyBindingsImported = true;
        service.SaveConfig();
        return applied;
    }
}
