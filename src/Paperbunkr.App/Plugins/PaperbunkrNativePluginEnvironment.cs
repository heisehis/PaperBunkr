using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Abstractions.Native;
using Paperbunkr.Plugins.Abstractions.Ui;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real <see cref="INativePluginUiEnvironment"/> (docs/superpowers/specs/2026-09-11-plugin-api-v4-
/// native-tier-design.md §3, implementation plan Phase 1 Step 1.5) - the one real environment
/// instance every native plugin actually receives (both headless and UI-capable ones; a headless
/// plugin's own code just never casts to the wider interface). Composes over the existing
/// <see cref="PaperbunkrPluginEnvironment"/> for the whole base <see cref="IPluginEnvironment"/>
/// surface (that class is sealed, so composition rather than inheritance) and adds exactly the
/// full-trust/native members - same "adapter wraps an existing service" pattern the rest of this
/// environment already uses.
/// </summary>
public sealed class PaperbunkrNativePluginEnvironment : INativePluginUiEnvironment
{
    private readonly PaperbunkrPluginEnvironment _inner;
    private readonly IActivityService _activity;
    private readonly NativePluginModalHostViewModel _modalHost;

    public PaperbunkrNativePluginEnvironment(PaperbunkrPluginEnvironment inner, IActivityService activity, NativePluginModalHostViewModel modalHost)
    {
        _inner = inner;
        _activity = activity;
        _modalHost = modalHost;
    }

    public Func<PaperbunkrDbContext> CreateDbContext { get; } = PaperbunkrDb.CreateContext;

    public Func<ActivityJobKind, string, bool, IPluginActivityHandle> StartActivityJob =>
        (kind, title, cancellable) => new PluginActivityHandleAdapter(_activity.StartJob(kind, title, cancellable));

    public Task<TResult> ShowModalAsync<TResult>(Func<Action<TResult>, Control> contentFactory) =>
        _modalHost.ShowAsync(contentFactory);

    public IPluginHostWindow MainWindow => _inner.MainWindow;
    public IApplication App => _inner.App;
    public IOpenBooksManager OpenBooks => _inner.OpenBooks;
    public IBrowser Browser => _inner.Browser;
    public IComicDisplay ComicDisplay => _inner.ComicDisplay;
    public IMetadataGraph Metadata => _inner.Metadata;
    public IRulesEngine Rules => _inner.Rules;
    public IMetadataWriter Writer => _inner.Writer;
    public IThemePlugin ThemePlugin => _inner.ThemePlugin;
    public IEnumerable<string> LibraryPaths => _inner.LibraryPaths;

    public string CommandPath
    {
        get => _inner.CommandPath;
        set => _inner.CommandPath = value;
    }

    public string PluginKey
    {
        get => _inner.PluginKey;
        set => _inner.PluginKey = value;
    }

    public string? GetSetting(string key) => _inner.GetSetting(key);

    public void SetSetting(string key, string value) => _inner.SetSetting(key, value);

    public string Localize(string resourceKey, string elementKey, string text) => _inner.Localize(resourceKey, elementKey, text);

    /// <summary>Shallow, matching <see cref="PaperbunkrPluginEnvironment.Clone"/>'s own convention -
    /// every sub-adapter (including <see cref="_activity"/>/<see cref="_modalHost"/>) is a shared,
    /// app-lifetime singleton; only <see cref="CommandPath"/>/<see cref="PluginKey"/> (held on the
    /// cloned <see cref="_inner"/>) differ per command clone.</summary>
    public object Clone() => new PaperbunkrNativePluginEnvironment((PaperbunkrPluginEnvironment)_inner.Clone(), _activity, _modalHost);
}
