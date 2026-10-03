using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Turns plugin commands into keyboard actions (Plugin API 4.3, docs/superpowers/specs/2026-10-03-input-service-design.md §14). Every enabled, working <c>Library</c>-hook command gets an action
/// <c>Plugin.{pluginKey}.{commandKey}</c> in the Library scope, listed under "Plugins" in Preferences &gt; Keyboard Shortcuts, so a plugin's command can be bound to a key, a mouse button or a
/// controller button like any built-in one and runs on the current selection. The default binding is the command's manifest <c>shortcut</c> when it parses; otherwise, as in ComicRack CE
/// (<c>PluginEngine</c> assigns Ctrl+Shift+F1 to F12 to the first twelve enabled commands), the next free Ctrl+Shift+F key. A user's remap is stored under the same id, so it survives the plugin
/// being switched off and on again.
/// </summary>
public static class PluginInputActions
{
    public const string Prefix = "Plugin.";

    public const string Group = "Plugins";

    /// <summary>The action id for <paramref name="command"/>.</summary>
    public static string IdFor(Command command) => $"{Prefix}{command.PluginKey}.{command.Key}";

    /// <summary>True for an id produced by <see cref="IdFor"/>.</summary>
    public static bool IsPluginAction(string actionId) => actionId.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Makes the catalog's plugin actions match <paramref name="commands"/> (the currently enabled Library commands, in menu order): registers the new ones, drops the ones that went away,
    /// leaves the rest alone. Safe to call as often as the plugin list changes.
    /// </summary>
    public static void Sync(IInputActionCatalog catalog, IEnumerable<Command> commands)
    {
        var wanted = Build(commands);
        var wantedIds = wanted.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var existing in catalog.All.Where(i => IsPluginAction(i.Id) && !wantedIds.Contains(i.Id)).ToList())
        {
            catalog.Unregister(existing.Id);
        }

        foreach (var info in wanted)
        {
            if (catalog.Find(info.Action) is null)
            {
                catalog.Register(info);
            }
        }
    }

    /// <summary>The action descriptions for <paramref name="commands"/>, with their default bindings worked out.</summary>
    public static IReadOnlyList<InputActionInfo> Build(IEnumerable<Command> commands)
    {
        var list = commands.Where(c => c.Enabled && !c.IsBroken).ToList();
        var used = new HashSet<InputBinding>();
        var infos = new List<InputActionInfo>(list.Count);

        // Declared shortcuts first, so an automatic F-key never takes one a plugin asked for.
        var declared = new Dictionary<Command, InputBinding>();
        foreach (var command in list)
        {
            if (!string.IsNullOrWhiteSpace(command.Shortcut) && InputBinding.TryParse(command.Shortcut, out var binding) && binding.Kind == InputBindingKind.Key && used.Add(binding))
            {
                declared[command] = binding;
            }
        }

        int nextFunctionKey = 0;
        foreach (var command in list)
        {
            IReadOnlyList<InputBinding> defaults = [];
            if (declared.TryGetValue(command, out var own))
            {
                defaults = [own];
            }
            else
            {
                while (nextFunctionKey < 12)
                {
                    var candidate = InputBinding.ForKey(Key.F1 + nextFunctionKey++, KeyModifiers.Control | KeyModifiers.Shift);
                    if (used.Add(candidate))
                    {
                        defaults = [candidate];
                        break;
                    }
                }
            }

            infos.Add(new InputActionInfo(IdFor(command), Group, command.Name, InputScope.Library, InputContext.Always, defaults));
        }

        return infos;
    }
}
