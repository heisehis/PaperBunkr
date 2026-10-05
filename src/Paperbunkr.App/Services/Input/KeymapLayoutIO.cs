using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Input;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Keyboard, mouse and gamepad layout import and export for Preferences &gt; Keyboard Shortcuts (replaces the old <c>KeyBindingIO</c>). The file is a plain JSON list of
/// <c>{ "CommandId": "...", "Gesture": "..." }</c> objects, one per bound input, several allowed per command: the same shape the previous system wrote, so layouts exported before the
/// input service existed still import. Export writes every action's <em>effective</em> bindings (defaults included), so the file is a complete layout, not a diff.
/// </summary>
public static class KeymapLayoutIO
{
    // Relaxed escaping so "Ctrl+WheelUp" is written as such, not as "Ctrl\u002BWheelUp".
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record Entry(string CommandId, string Gesture);

    /// <summary>Writes the complete current layout to <paramref name="filePath"/>.</summary>
    public static void Export(IInputService input, string filePath)
    {
        var entries = input.Actions.All
            .SelectMany(info => input.GetBindings(info.Action).Select(binding => new Entry(info.Id, binding.ToString())))
            .ToList();
        File.WriteAllText(filePath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    /// <summary>
    /// Applies <paramref name="filePath"/> and returns how many bindings it applied. Entries are grouped by command and each command's whole binding list is <em>replaced</em> (so
    /// importing the same file twice changes nothing); a command the app does not know, an unparseable gesture, or a command whose entries all fail to parse is skipped and the rest
    /// still applied. Gestures are read in the current text form and, failing that, as a legacy Avalonia key gesture.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a layout at all.</exception>
    public static int Import(IInputService input, string filePath)
    {
        var rows = LegacyKeyBindingImporter.ReadExportedLayout(File.ReadAllText(filePath));
        int applied = 0;
        foreach (var group in rows.GroupBy(r => r.CommandId))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || input.Actions.Find(group.Key) is not { } info)
            {
                continue;
            }

            var bindings = new List<InputBinding>();
            foreach (var (_, gesture) in group)
            {
                if (TryParseGesture(gesture, out var binding) && !bindings.Contains(binding))
                {
                    bindings.Add(binding);
                }
            }

            if (bindings.Count == 0)
            {
                continue;
            }

            input.SetBindings(info.Action, bindings);
            applied += bindings.Count;
        }

        return applied;
    }

    private static bool TryParseGesture(string text, out InputBinding binding)
    {
        if (InputBinding.TryParse(text, out binding))
        {
            return true;
        }

        try
        {
            binding = InputBinding.FromGesture(KeyGesture.Parse(text));
            return binding.IsDefined;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            binding = default;
            return false;
        }
    }
}
