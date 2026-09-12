using System.Reflection;
using System.Runtime.Loader;
using Paperbunkr.Plugins.Abstractions.Native;

namespace Paperbunkr.Plugins.Native;

/// <summary>
/// Loads one native-tier plugin assembly in isolation (docs/superpowers/specs/2026-09-11-plugin-api-
/// v4-native-tier-design.md §4, implementation plan Phase 1 Step 1.3). Non-collectible - per grilling
/// Q22, live hot-unload was ruled out entirely (every native-plugin change requires a full app
/// restart to take effect, per the staged-install design in v4 §4), so there's no unload path to
/// build and none of the well-known collectible-<see cref="AssemblyLoadContext"/> pitfalls apply.
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginAssemblyPath) : base(isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    /// <summary>Resolves the plugin's own dependency assemblies (its `.deps.json`-recorded refs)
    /// relative to its own extracted folder; returning null falls back to the default load context,
    /// which is how shared assemblies (BCL, Avalonia, Paperbunkr.Plugins.Abstractions,
    /// Paperbunkr.Data) resolve instead of the plugin bundling its own duplicate copies.
    ///
    /// Critically, the default-context check below runs FIRST, before consulting
    /// <see cref="_resolver"/> at all - found the hard way (implementation plan Phase 1 Step 1.3's
    /// own fixture test): ordinary dependency-copying puts a copy of every shared contract assembly
    /// (e.g. Paperbunkr.Plugins.Abstractions.dll) right next to the plugin's own DLL, so the resolver
    /// happily finds and loads a second, distinct copy into this context if asked. That second copy
    /// is a genuinely different runtime <c>Type</c> for e.g. <c>INativePluginModule</c> than the
    /// host's own copy, so `is INativePluginModule` checks written against the host's type silently
    /// fail against a plugin class that "implements" only the duplicate. Checking whether the default
    /// context already has an assembly of this name loaded - and returning null immediately if so -
    /// keeps every shared contract assembly a single type identity across the plugin boundary.</summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (Default.Assemblies.Any(a => a.GetName().Name == assemblyName.Name))
        {
            return null;
        }

        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is not null ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    /// <summary>Resolves a native (P/Invoke) dependency the same way - e.g. a LiteDB-free plugin
    /// needing some other native library. This is also what makes the v4 §4 packaging fix (preserving
    /// `runtimes/&lt;rid&gt;/native/...` structure on extraction) actually matter: this resolver reads
    /// the plugin's own `.deps.json`, which records exactly that relative path.</summary>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is not null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }

    /// <summary>
    /// Loads <paramref name="assemblyPath"/> into a fresh <see cref="PluginLoadContext"/>, finds the
    /// single type implementing <see cref="INativePluginModule"/>, and runs the plugin's own
    /// <c>Initialize</c>/<c>RegisterCommands</c> sequence. Throws (caught by the caller, per
    /// <see cref="PluginEngine.Discover"/>'s "one broken plugin doesn't abort the rest" contract) if
    /// zero or more than one <see cref="INativePluginModule"/> implementation is found - an ambiguous
    /// or missing entry point is a real authoring error, not something to silently pick a winner for.
    /// </summary>
    public static (INativePluginModule Module, IReadOnlyList<NativeCommand> Commands) LoadPlugin(
        string assemblyPath, string pluginKey, INativePluginEnvironment environment)
    {
        var context = new PluginLoadContext(assemblyPath);
        Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);

        var moduleTypes = assembly.GetTypes()
            .Where(t => typeof(INativePluginModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .ToList();

        if (moduleTypes.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one INativePluginModule implementation in '{assemblyPath}', found {moduleTypes.Count}.");
        }

        var module = (INativePluginModule)Activator.CreateInstance(moduleTypes[0])!;
        module.Initialize(environment);

        var registrar = new NativeCommandRegistrar(pluginKey);
        module.RegisterCommands(registrar);

        return (module, registrar.Commands);
    }
}
