using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services;

/// <summary>
/// Keyboard shortcut layout import/export (docs/superpowers/specs/2026-08-25-reader-chrome-design.md)
/// - a genuine gap, not a restyle: confirmed via grep that neither <see cref="KeyBindingService"/> nor
/// anywhere else in this codebase had import/export before this. No CE precedent to port (unlike
/// <c>CblReadingListIO</c>'s XML container) - a plain JSON list of command id/gesture pairs is the
/// simplest format that round-trips cleanly, matching this codebase's existing JSON usage elsewhere
/// (<see cref="SkinService"/>'s theme.json).
/// </summary>
public static class KeyBindingIO
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private sealed record ExportedBinding(string CommandId, string Gesture);

    /// <summary>
    /// One JSON object per bound gesture (docs/superpowers/specs/2026-09-07-keyboard-shortcuts-
    /// redesign-design.md §5) - a command with 2 bindings exports 2 objects sharing the same
    /// CommandId. No format/schema change from the single-binding-per-command shape this shipped
    /// with; a legacy export (at most one object per CommandId) is just a special case of this.
    /// </summary>
    public static void Export(KeyBindingService service, string filePath)
    {
        var bindings = service.GetAllBindings()
            .SelectMany(b => b.Keys.Select(k => new ExportedBinding(b.Command.Id, k.ToString())))
            .ToList();

        File.WriteAllText(filePath, JsonSerializer.Serialize(bindings, JsonOptions));
    }

    /// <summary>
    /// Applies every valid entry in <paramref name="filePath"/> and returns how many were applied -
    /// an unknown command id (e.g. exported from a different app version) or an unparseable gesture
    /// is skipped, not treated as a whole-import failure, mirroring <see cref="KeyBindingService.GetKeys(Data.PaperbunkrDbContext,string)"/>'s
    /// own per-entry <c>catch (ArgumentException)</c> fallback philosophy, just applied at import time
    /// instead of read time. Only a completely unparseable file (not valid JSON at all) throws.
    /// Entries are grouped by CommandId and applied via <see cref="KeyBindingService.ReplaceKeys"/> -
    /// a re-import is idempotent (replaces that command's whole binding set) rather than additive,
    /// so importing the same file twice in a row doesn't pile up duplicate/stale rows.
    /// </summary>
    public static int Import(KeyBindingService service, string filePath)
    {
        List<ExportedBinding>? bindings;
        try
        {
            bindings = JsonSerializer.Deserialize<List<ExportedBinding>>(File.ReadAllText(filePath), JsonOptions);
        }
        catch (JsonException)
        {
            bindings = null;
        }

        if (bindings is null)
        {
            throw new InvalidDataException($"'{filePath}' is not a valid keyboard shortcut layout.");
        }

        var knownIds = KeyboardCommandRegistry.Commands.Select(c => c.Id).ToHashSet();
        int applied = 0;
        foreach (var group in bindings.Where(e => knownIds.Contains(e.CommandId)).GroupBy(e => e.CommandId))
        {
            var gestures = new List<KeyGesture>();
            foreach (var entry in group)
            {
                try
                {
                    gestures.Add(KeyGesture.Parse(entry.Gesture));
                    applied++;
                }
                catch (ArgumentException)
                {
                    // Corrupt/unparseable single entry - skip it, keep applying the rest of the group.
                }
            }

            if (gestures.Count > 0)
            {
                service.ReplaceKeys(group.Key, gestures);
            }
        }

        return applied;
    }
}
