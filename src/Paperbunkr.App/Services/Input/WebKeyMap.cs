using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Translates between Avalonia keys and the DOM <c>KeyboardEvent.key</c> names an embedded web view reports, so the book reader's JavaScript can forward keys to the input service
/// (docs/superpowers/specs/2026-10-03-input-service-design.md §9) instead of hardcoding which ones turn the page. A key pressed while the web view has focus never reaches Avalonia;
/// the page asks C# which keys are bound (<see cref="NormalizedKeysFor"/>), swallows exactly those, and posts each one back for <see cref="IInputService.ProcessKey"/> to resolve.
/// </summary>
public static class WebKeyMap
{
    private static readonly Dictionary<Key, string> ToJs = BuildToJs();

    private static readonly Dictionary<string, Key> FromJs = ToJs.ToDictionary(p => p.Value, p => p.Key);

    private static Dictionary<Key, string> BuildToJs()
    {
        var map = new Dictionary<Key, string>
        {
            [Key.Left] = "ArrowLeft",
            [Key.Right] = "ArrowRight",
            [Key.Up] = "ArrowUp",
            [Key.Down] = "ArrowDown",
            [Key.PageUp] = "PageUp",
            [Key.PageDown] = "PageDown",
            [Key.Home] = "Home",
            [Key.End] = "End",
            [Key.Space] = " ",
            [Key.Enter] = "Enter",
            [Key.Escape] = "Escape",
            [Key.Tab] = "Tab",
            [Key.Back] = "Backspace",
            [Key.Delete] = "Delete",
            [Key.Insert] = "Insert",
            [Key.OemComma] = ",",
            [Key.OemPeriod] = ".",
            [Key.OemMinus] = "-",
            [Key.OemQuestion] = "/",
            [Key.OemSemicolon] = ";",
        };

        for (char letter = 'A'; letter <= 'Z'; letter++)
        {
            map[(Key)((int)Key.A + (letter - 'A'))] = letter.ToString().ToLowerInvariant();
        }

        for (int digit = 0; digit <= 9; digit++)
        {
            map[(Key)((int)Key.D0 + digit)] = digit.ToString();
        }

        for (int f = 1; f <= 12; f++)
        {
            map[(Key)((int)Key.F1 + (f - 1))] = "F" + f;
        }

        return map;
    }

    /// <summary>The <c>KeyboardEvent.key</c> a web page reports for <paramref name="key"/>, or false when this map does not cover it.</summary>
    public static bool TryToJsKey(Key key, out string jsKey) => ToJs.TryGetValue(key, out jsKey!);

    /// <summary>The Avalonia key for a <c>KeyboardEvent.key</c> (single letters in either case), or false when this map does not cover it.</summary>
    public static bool TryFromJsKey(string jsKey, out Key key)
    {
        key = Key.None;
        return !string.IsNullOrEmpty(jsKey) && FromJs.TryGetValue(jsKey.Length == 1 ? jsKey.ToLowerInvariant() : jsKey, out key);
    }

    /// <summary>
    /// The canonical text both sides compare: modifiers in the order <c>ctrl alt shift meta</c>, each followed by <c>+</c>, then the key (single characters lower-cased). The page builds
    /// the same string from a <c>keydown</c> event to look it up in the bound set.
    /// </summary>
    public static string Normalize(string jsKey, bool ctrl, bool alt, bool shift, bool meta) =>
        (ctrl ? "ctrl+" : string.Empty) + (alt ? "alt+" : string.Empty) + (shift ? "shift+" : string.Empty) + (meta ? "meta+" : string.Empty)
        + (jsKey.Length == 1 ? jsKey.ToLowerInvariant() : jsKey);

    /// <summary>The normalised text of every keyboard binding in <paramref name="bindings"/> this map can express; mouse, wheel and gamepad bindings are skipped.</summary>
    public static IReadOnlyList<string> NormalizedKeysFor(IEnumerable<InputBinding> bindings)
    {
        var keys = new List<string>();
        foreach (var binding in bindings)
        {
            if (binding.Kind == InputBindingKind.Key && TryToJsKey(binding.Key, out var js))
            {
                string normalised = Normalize(
                    js,
                    (binding.Modifiers & KeyModifiers.Control) != 0,
                    (binding.Modifiers & KeyModifiers.Alt) != 0,
                    (binding.Modifiers & KeyModifiers.Shift) != 0,
                    (binding.Modifiers & KeyModifiers.Meta) != 0);
                if (!keys.Contains(normalised))
                {
                    keys.Add(normalised);
                }
            }
        }

        return keys;
    }

    /// <summary>The modifier flags for a forwarded key.</summary>
    public static KeyModifiers ModifiersFor(bool ctrl, bool alt, bool shift, bool meta) =>
        (ctrl ? KeyModifiers.Control : KeyModifiers.None) | (alt ? KeyModifiers.Alt : KeyModifiers.None)
        | (shift ? KeyModifiers.Shift : KeyModifiers.None) | (meta ? KeyModifiers.Meta : KeyModifiers.None);
}
