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

    public static void Export(KeyBindingService service, string filePath)
    {
        var bindings = service.GetAllBindings()
            .Select(b => new ExportedBinding(b.Command.Id, b.CurrentKey.ToString()))
            .ToList();

        File.WriteAllText(filePath, JsonSerializer.Serialize(bindings, JsonOptions));
    }

    /// <summary>
    /// Applies every valid entry in <paramref name="filePath"/> and returns how many were applied -
    /// an unknown command id (e.g. exported from a different app version) or an unparseable gesture
    /// is skipped, not treated as a whole-import failure, mirroring <see cref="KeyBindingService.GetKey(Data.PaperbunkrDbContext,string)"/>'s
    /// own per-entry <c>catch (ArgumentException)</c> fallback philosophy, just applied at import time
    /// instead of read time. Only a completely unparseable file (not valid JSON at all) throws.
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
        foreach (var entry in bindings)
        {
            if (!knownIds.Contains(entry.CommandId))
            {
                continue;
            }

            try
            {
                service.SetKey(entry.CommandId, KeyGesture.Parse(entry.Gesture));
                applied++;
            }
            catch (ArgumentException)
            {
                // Corrupt/unparseable single entry - skip it, keep applying the rest.
            }
        }

        return applied;
    }
}
