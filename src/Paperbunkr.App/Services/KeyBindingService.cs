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
    public KeyGesture GetKey(string commandId)
    {
        using var context = _contextFactory();
        return GetKey(context, commandId);
    }

    public KeyGesture GetKey(PaperbunkrDbContext context, string commandId)
    {
        var descriptor = KeyboardCommandRegistry.Commands.First(c => c.Id == commandId);
        string? stored = context.KeyBindings.FirstOrDefault(k => k.CommandId == commandId)?.Key;
        if (stored is null)
        {
            return descriptor.DefaultGesture;
        }

        try
        {
            return KeyGesture.Parse(stored);
        }
        catch (ArgumentException)
        {
            // Stale/corrupt row (e.g. hand-edited DB, or a format from before this feature added
            // modifier support) - fall back to the registry default rather than throwing, same
            // safety Enum.TryParse gave us before KeyGesture (which has no TryParse).
            return descriptor.DefaultGesture;
        }
    }

    public void SetKey(string commandId, KeyGesture gesture)
    {
        using var context = _contextFactory();
        var existing = context.KeyBindings.FirstOrDefault(k => k.CommandId == commandId);
        if (existing is null)
        {
            context.KeyBindings.Add(new Paperbunkr.Data.Entities.KeyBinding { CommandId = commandId, Key = gesture.ToString() });
        }
        else
        {
            existing.Key = gesture.ToString();
        }

        context.SaveChanges();
    }

    /// <summary>Every registered command paired with its current (default-or-remapped) gesture - drives the Preferences &gt; Keyboard Shortcuts list.</summary>
    public IReadOnlyList<(KeyboardCommandDescriptor Command, KeyGesture CurrentKey)> GetAllBindings()
    {
        using var context = _contextFactory();
        return KeyboardCommandRegistry.Commands.Select(c => (c, GetKey(context, c.Id))).ToList();
    }
}
