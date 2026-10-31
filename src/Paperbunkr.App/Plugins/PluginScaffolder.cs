using System;
using System.IO;
using System.Linq;
using System.Text;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Plugins;

public enum PluginTemplate
{
    ScriptCSharp,
    ScriptPython,
    Native,
}

/// <summary>What <see cref="PluginScaffolder.Create"/> did: the new folder, or why it didn't.</summary>
public sealed record PluginScaffoldResult(bool Succeeded, string? Folder, string? Error);

/// <summary>
/// "New plugin…" (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §A3): writes a working starter plugin
/// into its own folder under the plugins root. Pure file I/O - the caller re-discovers afterwards. A script template runs as soon as
/// it's discovered; the native template is source only (it needs the .NET SDK to build, and says so in its README).
/// <para>
/// Editor help for <c>.csx</c>: the template starts with <c>#r</c> lines naming the installed API assemblies by absolute path, so an
/// editor can resolve every type. The host's sandbox resolves every <c>#r</c> to nothing (its fixed reference list already contains
/// those assemblies), so the lines are inert at run time. <c>Environment</c> and <c>Books</c> are the host's script globals, which an
/// editor can't see; the template says so in a comment.
/// </para>
/// </summary>
public static class PluginScaffolder
{
    /// <summary>A key from a display name: lowercase letters and digits, everything else collapsed to single hyphens.</summary>
    public static string KeyFor(string name)
    {
        var key = new StringBuilder();
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                key.Append(c);
            }
            else if (key.Length > 0 && key[^1] != '-')
            {
                key.Append('-');
            }
        }

        return key.ToString().Trim('-');
    }

    /// <summary>
    /// Creates <c>&lt;root&gt;\&lt;key&gt;\</c> with <paramref name="template"/>'s files. Refuses an empty name or key, and a key whose
    /// folder already exists. <paramref name="appDirectory"/> is where the running app's assemblies are (the C# and native templates
    /// reference them by absolute path).
    /// </summary>
    public static PluginScaffoldResult Create(string root, string name, string key, PluginTemplate template, string appDirectory)
    {
        name = name.Trim();
        key = KeyFor(key);
        if (name.Length == 0)
        {
            return new(false, null, "Give the plugin a name.");
        }

        if (key.Length == 0)
        {
            return new(false, null, "The key needs at least one letter or digit.");
        }

        string folder = Path.Combine(root, key);
        if (Directory.Exists(folder))
        {
            return new(false, null, $"A plugin folder named \"{key}\" already exists.");
        }

        try
        {
            Directory.CreateDirectory(folder);
            string api = $"{PluginApi.Current.Major}.{PluginApi.Current.Minor}";
            string abstractions = Path.Combine(appDirectory, "Paperbunkr.Plugins.Abstractions.dll");
            string data = Path.Combine(appDirectory, "Paperbunkr.Data.dll");
            string escapedName = System.Security.SecurityElement.Escape(name);

            switch (template)
            {
                case PluginTemplate.ScriptCSharp:
                    File.WriteAllText(Path.Combine(folder, "plugin.xml"), ScriptManifest(key, escapedName, api, "main.csx", method: null));
                    File.WriteAllText(Path.Combine(folder, "main.csx"), CSharpScript(name, abstractions, data));
                    break;

                case PluginTemplate.ScriptPython:
                    File.WriteAllText(Path.Combine(folder, "plugin.xml"), ScriptManifest(key, escapedName, api, "main.py", method: "run"));
                    File.WriteAllText(Path.Combine(folder, "main.py"), PythonScript(name));
                    break;

                case PluginTemplate.Native:
                    string typeName = TypeNameFor(name);
                    File.WriteAllText(Path.Combine(folder, "plugin.xml"),
                        $"""
                        <Plugin key="{key}" name="{escapedName}" tier="Native" assembly="{typeName}.dll" version="0.1.0" requiresApi="{api}">
                        </Plugin>
                        """);
                    File.WriteAllText(Path.Combine(folder, typeName + ".csproj"), NativeProject(abstractions, data));
                    File.WriteAllText(Path.Combine(folder, "Module.cs"), NativeModule(key, name, typeName));
                    File.WriteAllText(Path.Combine(folder, "README.txt"), NativeReadme(name, typeName));
                    break;
            }

            return new(true, folder, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, null, $"Couldn't create the plugin folder: {ex.Message}");
        }
    }

    private static string TypeNameFor(string name)
    {
        string pascal = string.Concat(name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]))
            .Where(char.IsAsciiLetterOrDigit).Aggregate(new StringBuilder(), (b, c) => b.Append(c)).ToString();
        return pascal.Length == 0 || char.IsDigit(pascal[0]) ? "Plugin" + pascal : pascal;
    }

    private static string ScriptManifest(string key, string name, string api, string script, string? method) =>
        $"""
        <Plugin key="{key}" name="{name}" requiresApi="{api}">
          <Command hook="Library" key="{key}.run" name="{name}"
                   description="Right-click books in the Library to run this." script="{script}"{(method is null ? "" : $" method=\"{method}\"")} />
          <Settings>
            <Setting key="greeting" label="Greeting" type="text" default="Hello" description="Shown when the command runs" />
          </Settings>
        </Plugin>
        """;

    private static string CSharpScript(string name, string abstractions, string data) =>
        $$"""
        #r "{{abstractions}}"
        #r "{{data}}"
        // {{name}} - a Library command: right-click books in the Library and pick it.
        // The two #r lines only help an editor find Paperbunkr's types; Paperbunkr already loads them and ignores these lines.
        // Environment (IPluginEnvironment) and Books (the selected books) are supplied by Paperbunkr when this runs - an
        // editor can't see them, so they will show as unknown there. See wiki/Plugins.md for everything Environment offers.

        string greeting = Environment.GetSetting("greeting") ?? "Hello";
        Environment.Log.Info($"Ran on {Books.Count} book(s).");
        return $"{greeting}! {Books.Count} book(s) selected.";
        """;

    private static string PythonScript(string name) =>
        $"""
        # {name} - a Library command: right-click books in the Library and pick it.
        # globals.Environment is the plugin environment and globals.Books the selected books (see wiki/Plugins.md).
        def run(globals):
            greeting = globals.Environment.GetSetting("greeting") or "Hello"
            globals.Environment.Log.Info("Ran on %d book(s)." % globals.Books.Count)
            return "%s! %d book(s) selected." % (greeting, globals.Books.Count)
        """;

    private static string NativeProject(string abstractions, string data) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <OutputPath>.</OutputPath>
            <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
            <EnableDynamicLoading>true</EnableDynamicLoading>
          </PropertyGroup>
          <ItemGroup>
            <!-- Paperbunkr supplies these at run time, so they are referenced but never copied. -->
            <Reference Include="Paperbunkr.Plugins.Abstractions"><HintPath>{abstractions}</HintPath><Private>false</Private></Reference>
            <Reference Include="Paperbunkr.Data"><HintPath>{data}</HintPath><Private>false</Private></Reference>
          </ItemGroup>
        </Project>
        """;

    private static string NativeModule(string key, string name, string typeName) =>
        $$"""
        using Paperbunkr.Plugins.Abstractions.Native;

        namespace {{typeName}};

        /// <summary>{{name}} - a native Paperbunkr plugin. Registers one Library command (right-click books in the Library).</summary>
        public sealed class Module : INativePluginModule
        {
            public void Initialize(INativePluginEnvironment environment)
            {
            }

            public void RegisterCommands(INativeCommandRegistrar registrar)
            {
                registrar.OnLibrary("{{key}}.run", "{{name}}", (environment, books) =>
                {
                    environment.Log.Info($"Ran on {books.Count} book(s).");
                    return Task.FromResult<object?>($"{books.Count} book(s) selected.");
                });
            }
        }
        """;

    private static string NativeReadme(string name, string typeName) =>
        $"""
        {name} - native plugin starter

        Build it with the .NET SDK (https://dot.net), from this folder:

            dotnet build

        That writes {typeName}.dll next to plugin.xml. Restart Paperbunkr to load it - native plugins can't be reloaded while the
        app runs. A native plugin has full read/write access to your library database.

        For tests, src/Paperbunkr.Plugins.Testing in the Paperbunkr repository has fake environments and a script runner.
        """;
}
