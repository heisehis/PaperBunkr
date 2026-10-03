using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// The default <see cref="IInputActionCatalog"/>: an ordered list of <see cref="InputActionInfo"/> with a lookup by id. <see cref="CreateWithCoreActions"/> seeds it
/// with the built-in table (<see cref="InputActions.Core"/>); plugins and later features add to it with <see cref="Register"/>. Not thread-safe: like the rest of
/// the input service it is used from the UI thread only.
/// </summary>
public sealed class InputActionCatalog : IInputActionCatalog
{
    private readonly List<InputActionInfo> _all = [];
    private readonly Dictionary<string, InputActionInfo> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _formerIds = new(StringComparer.Ordinal);

    public IReadOnlyList<InputActionInfo> All => _all;

    /// <summary>A catalog holding every built-in action.</summary>
    public static InputActionCatalog CreateWithCoreActions()
    {
        var catalog = new InputActionCatalog();
        foreach (var info in InputActions.Core)
        {
            catalog.Register(info);
        }

        return catalog;
    }

    public InputActionInfo? Find(InputAction action) => action.Id is { } id && _byId.TryGetValue(id, out var info) ? info : null;

    /// <summary>The current id of the action that replaced <paramref name="formerId"/>, or <see langword="null"/> when no registered action lists it.</summary>
    public string? ResolveFormerId(string formerId) => _formerIds.TryGetValue(formerId, out var current) ? current : null;

    public void Register(InputActionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!info.Action.IsDefined)
        {
            throw new ArgumentException("An action needs an id.", nameof(info));
        }

        string id = info.Id;
        if (_byId.ContainsKey(id))
        {
            throw new ArgumentException($"An action with id '{id}' is already registered.", nameof(info));
        }

        if (_formerIds.ContainsKey(id))
        {
            throw new ArgumentException($"'{id}' is listed as a former id of '{_formerIds[id]}'.", nameof(info));
        }

        var former = info.FormerIds ?? [];
        foreach (string old in former)
        {
            if (old == id || _byId.ContainsKey(old) || (_formerIds.TryGetValue(old, out var owner) && owner != id))
            {
                throw new ArgumentException($"Former id '{old}' of '{id}' collides with another action.", nameof(info));
            }
        }

        _all.Add(info);
        _byId[id] = info;
        foreach (string old in former)
        {
            _formerIds[old] = id;
        }
    }

    public bool Unregister(string id)
    {
        if (!_byId.Remove(id, out var info))
        {
            return false;
        }

        _all.Remove(info);
        foreach (string old in (info.FormerIds ?? []).Where(o => _formerIds.TryGetValue(o, out var owner) && owner == id).ToList())
        {
            _formerIds.Remove(old);
        }

        return true;
    }

    /// <summary>Convenience for tests and plugin hosts: the registered ids, in order.</summary>
    internal IReadOnlyList<string> Ids => _all.Select(i => i.Id).ToList();
}
