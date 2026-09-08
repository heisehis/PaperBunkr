using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// Resolves and persists <see cref="KeyboardCommandRegistry"/> commands' current gestures
/// (Preferences &gt; Keyboard Shortcuts, docs/Paperbunkr-Roadmap.md P5 follow-up). Same context-factory-
/// injection test seam as <see cref="SkinService"/>/<c>CoverThumbnailService</c>.
/// </summary>
public class KeyBindingService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public KeyBindingService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal KeyBindingService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>Opens its own context - use the <see cref="PaperbunkrDbContext"/> overload instead when the caller already has one open.</summary>
    public IReadOnlyList<KeyGesture> GetKeys(string commandId)
    {
        using var context = _contextFactory();
        return GetKeys(context, commandId);
    }

    /// <summary>
    /// Every gesture bound to <paramref name="commandId"/> (docs/superpowers/specs/2026-09-07-
    /// keyboard-shortcuts-redesign-design.md) - zero stored rows means "never customized", which
    /// returns the registry default as the sole entry; one or more stored rows are authoritative and
    /// the registry default is not implicitly included alongside them.
    /// </summary>
    public IReadOnlyList<KeyGesture> GetKeys(PaperbunkrDbContext context, string commandId)
    {
        var descriptor = KeyboardCommandRegistry.Commands.First(c => c.Id == commandId);
        var stored = context.KeyBindings.Where(k => k.CommandId == commandId).Select(k => k.Key).ToList();
        if (stored.Count == 0)
        {
            return [descriptor.DefaultGesture];
        }

        var parsed = new List<KeyGesture>();
        foreach (string key in stored)
        {
            try
            {
                parsed.Add(KeyGesture.Parse(key));
            }
            catch (ArgumentException)
            {
                // Stale/corrupt row (e.g. hand-edited DB, or a format from before this feature added
                // modifier support) - skip it rather than throwing, same safety Enum.TryParse gave
                // us before KeyGesture (which has no TryParse).
            }
        }

        return parsed.Count > 0 ? parsed : [descriptor.DefaultGesture];
    }

    /// <summary>No-ops if <paramref name="commandId"/> already has a row for this exact gesture (mirrors the unique index on (CommandId, Key)).</summary>
    public void AddKey(string commandId, KeyGesture gesture)
    {
        using var context = _contextFactory();
        string key = gesture.ToString();
        bool exists = context.KeyBindings.Any(k => k.CommandId == commandId && k.Key == key);
        if (exists)
        {
            return;
        }

        context.KeyBindings.Add(new Paperbunkr.Data.Entities.KeyBinding { CommandId = commandId, Key = key });
        context.SaveChanges();
    }

    /// <summary>No-ops if no row matches. Callers are responsible for never removing a command's last remaining binding (docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md's Non-goals) - this method doesn't enforce it itself.</summary>
    public void RemoveKey(string commandId, KeyGesture gesture)
    {
        using var context = _contextFactory();
        string key = gesture.ToString();
        var existing = context.KeyBindings.FirstOrDefault(k => k.CommandId == commandId && k.Key == key);
        if (existing is not null)
        {
            context.KeyBindings.Remove(existing);
            context.SaveChanges();
        }
    }

    /// <summary>Deletes every existing row for <paramref name="commandId"/>, then adds exactly the given gestures - used by Import (docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md §5) so a re-import is idempotent rather than additive.</summary>
    public void ReplaceKeys(string commandId, IReadOnlyList<KeyGesture> gestures)
    {
        using var context = _contextFactory();
        var existing = context.KeyBindings.Where(k => k.CommandId == commandId);
        context.KeyBindings.RemoveRange(existing);
        foreach (var gesture in gestures)
        {
            context.KeyBindings.Add(new Paperbunkr.Data.Entities.KeyBinding { CommandId = commandId, Key = gesture.ToString() });
        }

        context.SaveChanges();
    }

    /// <summary>Clears every stored binding - every command reverts to its registry default (docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md's Reset to Defaults).</summary>
    public void ResetToDefaults()
    {
        using var context = _contextFactory();
        context.KeyBindings.RemoveRange(context.KeyBindings);
        context.SaveChanges();
    }

    /// <summary>Every registered command paired with its current (default-or-remapped) gesture(s) - drives the Preferences &gt; Keyboard Shortcuts list.</summary>
    public IReadOnlyList<(KeyboardCommandDescriptor Command, IReadOnlyList<KeyGesture> Keys)> GetAllBindings()
    {
        using var context = _contextFactory();
        return KeyboardCommandRegistry.Commands.Select(c => (c, GetKeys(context, c.Id))).ToList();
    }
}
