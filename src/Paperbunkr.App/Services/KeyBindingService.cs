using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Resolves and persists <see cref="KeyboardCommandRegistry"/> commands' current keys (Preferences
/// &gt; Keyboard Shortcuts, docs/alpha-roadmap.md P5 follow-up). Same context-factory-injection
/// test seam as <see cref="SkinService"/>/<c>CoverThumbnailService</c>.
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
    public Key GetKey(string commandId)
    {
        using var context = _contextFactory();
        return GetKey(context, commandId);
    }

    public Key GetKey(PaperbunkrDbContext context, string commandId)
    {
        var descriptor = KeyboardCommandRegistry.Commands.First(c => c.Id == commandId);
        string? stored = context.KeyBindings.FirstOrDefault(k => k.CommandId == commandId)?.Key;
        return stored is not null && Enum.TryParse(stored, out Key parsed) ? parsed : descriptor.DefaultKey;
    }

    public void SetKey(string commandId, Key key)
    {
        using var context = _contextFactory();
        var existing = context.KeyBindings.FirstOrDefault(k => k.CommandId == commandId);
        if (existing is null)
        {
            context.KeyBindings.Add(new Paperbunkr.Data.Entities.KeyBinding { CommandId = commandId, Key = key.ToString() });
        }
        else
        {
            existing.Key = key.ToString();
        }

        context.SaveChanges();
    }

    /// <summary>Every registered command paired with its current (default-or-remapped) key - drives the Preferences &gt; Keyboard Shortcuts list.</summary>
    public IReadOnlyList<(KeyboardCommandDescriptor Command, Key CurrentKey)> GetAllBindings()
    {
        using var context = _contextFactory();
        return KeyboardCommandRegistry.Commands.Select(c => (c, GetKey(context, c.Id))).ToList();
    }
}
