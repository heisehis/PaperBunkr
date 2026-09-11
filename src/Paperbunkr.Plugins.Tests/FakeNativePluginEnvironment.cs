using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Abstractions.Native;

namespace Paperbunkr.Plugins.Tests;

/// <summary>Test double for <see cref="INativePluginEnvironment"/> (implementation plan Phase 1
/// Step 1.3/1.4 verification) - wraps a <see cref="FakePluginEnvironment"/> for the base
/// <c>IPluginEnvironment</c> surface and adds no-op/throwing stand-ins for the native-only members,
/// since no test in this pass needs a real database or a real Activity Center job.</summary>
internal sealed class FakeNativePluginEnvironment : INativePluginEnvironment
{
    private readonly FakePluginEnvironment _inner;

    public FakeNativePluginEnvironment(FakePluginEnvironment? inner = null)
    {
        _inner = inner ?? new FakePluginEnvironment();
    }

    public Func<PaperbunkrDbContext> CreateDbContext { get; set; } =
        () => throw new NotSupportedException("No test in this pass needs a real PaperbunkrDbContext.");

    public Func<ActivityJobKind, string, bool, IPluginActivityHandle> StartActivityJob { get; set; } =
        (_, _, _) => throw new NotSupportedException("No test in this pass needs a real activity job.");

    public IPluginHostWindow MainWindow => _inner.MainWindow;
    public Automation.IApplication App => _inner.App;
    public Automation.IOpenBooksManager OpenBooks => _inner.OpenBooks;
    public Automation.IBrowser Browser => _inner.Browser;
    public Automation.IComicDisplay ComicDisplay => _inner.ComicDisplay;
    public Automation.IMetadataGraph Metadata => _inner.Metadata;
    public Automation.IRulesEngine Rules => _inner.Rules;
    public Automation.IMetadataWriter Writer => _inner.Writer;
    public Theme.IThemePlugin ThemePlugin => _inner.ThemePlugin;
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

    public object Clone() => new FakeNativePluginEnvironment((FakePluginEnvironment)_inner.Clone())
    {
        CreateDbContext = CreateDbContext,
        StartActivityJob = StartActivityJob,
    };
}
