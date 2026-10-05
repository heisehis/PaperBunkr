using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// The resolved keymap (docs/superpowers/specs/2026-10-03-input-service-design.md §5.3): each action's <em>effective</em> bindings (the user's override if there is one,
/// otherwise the action's defaults) plus a reverse index from a physical <see cref="InputBinding"/> to the actions it can trigger. Pure logic with no Avalonia event
/// types, so it is tested directly. It owns the <see cref="KeymapConfig"/> it edits; the service saves it after each change.
/// </summary>
public sealed class InputKeymap
{
    private static readonly IReadOnlyList<InputAction> NoActions = [];

    private readonly IInputActionCatalog _catalog;
    private readonly Dictionary<string, IReadOnlyList<InputBinding>> _effective = new(StringComparer.Ordinal);
    private readonly Dictionary<InputBinding, List<InputAction>> _index = [];

    public InputKeymap(IInputActionCatalog catalog, KeymapConfig config)
    {
        _catalog = catalog;
        Config = config;
        Rekeyed = Rebuild();
    }

    /// <summary>True when constructing the keymap moved overrides from former ids to current ones, so the config differs from what was loaded and should be saved.</summary>
    public bool Rekeyed { get; }

    /// <summary>The config being edited; save this after a change.</summary>
    public KeymapConfig Config { get; }

    /// <summary>Raised after any change to the effective bindings.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Recomputes the effective bindings and the index from the catalog and the config. First re-keys overrides saved under an action's
    /// <see cref="InputActionInfo.FormerIds"/> to its current id (the new id's own override wins if both exist). Call after the catalog gains actions.
    /// Returns true when it moved any override, so the caller knows the config should be saved.
    /// </summary>
    public bool Rebuild()
    {
        bool rekeyed = false;
        foreach (var info in _catalog.All)
        {
            if (info.FormerIds is not { Count: > 0 } former)
            {
                continue;
            }

            foreach (string old in former)
            {
                if (Config.Overrides.Remove(old, out var list))
                {
                    rekeyed = true;
                    Config.Overrides.TryAdd(info.Id, list);
                }
            }
        }

        _effective.Clear();
        _index.Clear();
        foreach (var info in _catalog.All)
        {
            var bindings = Config.Overrides.TryGetValue(info.Id, out var texts) ? ParseAll(texts) ?? info.Defaults : info.Defaults;
            _effective[info.Id] = bindings;
            foreach (var binding in bindings)
            {
                if (!_index.TryGetValue(binding, out var actions))
                {
                    _index[binding] = actions = [];
                }

                actions.Add(info.Action);
            }
        }

        return rekeyed;
    }

    /// <summary>The action's effective bindings; empty when it is unbound or unknown.</summary>
    public IReadOnlyList<InputBinding> GetBindings(InputAction action) =>
        action.Id is { } id && _effective.TryGetValue(id, out var bindings) ? bindings : [];

    /// <summary>True when the user has replaced this action's default bindings.</summary>
    public bool IsCustomised(InputAction action) => action.Id is { } id && Config.Overrides.ContainsKey(id);

    /// <summary>Every action the binding can trigger, in catalog order; empty when it is unbound.</summary>
    public IReadOnlyList<InputAction> Resolve(InputBinding binding) => _index.TryGetValue(binding, out var actions) ? actions : NoActions;

    /// <summary>
    /// Replaces the action's bindings. Duplicates are dropped; a list identical to the defaults removes the override instead (so a default changed in a later release
    /// still reaches this action). An empty list unbinds the action.
    /// </summary>
    public void Set(InputAction action, IReadOnlyList<InputBinding> bindings)
    {
        var info = _catalog.Find(action) ?? throw new ArgumentException($"'{action}' is not a registered action.", nameof(action));
        var distinct = bindings.Where(b => b.IsDefined).Distinct().ToList();
        if (distinct.SequenceEqual(info.Defaults))
        {
            Config.Overrides.Remove(info.Id);
        }
        else
        {
            Config.Overrides[info.Id] = distinct.Select(b => b.ToString()).ToList();
        }

        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops the action's override, restoring its defaults.</summary>
    public void Reset(InputAction action)
    {
        if (action.Id is { } id && Config.Overrides.Remove(id))
        {
            Rebuild();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Drops every override.</summary>
    public void ResetAll()
    {
        if (Config.Overrides.Count > 0)
        {
            Config.Overrides.Clear();
            Rebuild();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Other actions that already use <paramref name="binding"/> in the same scope with an overlapping <see cref="InputContext"/>, so the user's press would be ambiguous.
    /// <see cref="InputContext.Always"/> overlaps everything. Sharing a binding across <em>different</em> scopes is deliberate layering (the inner scope is asked first and
    /// the press falls through to the outer one when unclaimed: the thumb button turns pages in the reader and navigates back elsewhere), so it is not a conflict.
    /// </summary>
    public IReadOnlyList<InputConflict> FindConflicts(InputAction action, InputBinding binding)
    {
        var info = _catalog.Find(action);
        if (info is null || !_index.TryGetValue(binding, out var sharing))
        {
            return [];
        }

        var conflicts = new List<InputConflict>();
        foreach (var other in sharing)
        {
            if (other.Equals(action) || _catalog.Find(other) is not { } otherInfo)
            {
                continue;
            }

            if (otherInfo.Scope.Name == info.Scope.Name && otherInfo.Context.Overlaps(info.Context))
            {
                conflicts.Add(new InputConflict(other, binding));
            }
        }

        return conflicts;
    }

    /// <summary>
    /// The parsed bindings; an empty list when the override is empty (deliberately unbound); <see langword="null"/> when every entry failed to parse, so the caller can
    /// fall back to the defaults instead of silently unbinding the action.
    /// </summary>
    private static IReadOnlyList<InputBinding>? ParseAll(List<string>? texts)
    {
        if (texts is null || texts.Count == 0)
        {
            return [];
        }

        var parsed = new List<InputBinding>(texts.Count);
        foreach (string text in texts)
        {
            // An unparseable entry (hand-edited, or from a newer version's syntax) is skipped rather than discarding the rest of the action's bindings.
            if (InputBinding.TryParse(text, out var binding) && !parsed.Contains(binding))
            {
                parsed.Add(binding);
            }
        }

        return parsed.Count > 0 ? parsed : null;
    }
}
