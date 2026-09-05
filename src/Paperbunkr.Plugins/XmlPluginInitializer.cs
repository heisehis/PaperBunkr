using System.Xml.Serialization;

namespace Paperbunkr.Plugins;

/// <summary>
/// Ported from ComicRackCE's <c>XmlPluginInitializer</c> (docs/superpowers/specs/
/// 2026-08-24-plugin-api-v2-design.md §2) - parses a <c>plugin.xml</c> manifest into
/// <see cref="Command"/> instances, one per &lt;Command&gt; element, script path resolved relative
/// to the manifest's own folder. Dispatches to <see cref="CSharpCommand"/> or
/// <see cref="PythonCommand"/> by the entry's <c>script</c> file extension (docs/superpowers/specs/
/// 2026-08-30-python-plugin-scripting-design.md) - matches CE's own convention (its
/// <c>PythonPluginInitializer</c> gates on the same <c>.py</c> extension), and needs no manifest
/// schema change for the type itself.
/// </summary>
public static class XmlPluginInitializer
{
    private static readonly XmlSerializer Serializer = new(typeof(PluginManifest));

    /// <summary>Never throws - a malformed manifest yields an empty command list so one bad plugin can't abort discovery of the rest (docs §2).</summary>
    public static IEnumerable<Command> GetCommands(string manifestFile)
    {
        try
        {
            using var stream = File.OpenRead(manifestFile);
            if (Serializer.Deserialize(stream) is not PluginManifest manifest)
            {
                return Array.Empty<Command>();
            }

            string pluginDir = Path.GetDirectoryName(manifestFile) ?? string.Empty;
            string pluginKey = string.IsNullOrWhiteSpace(manifest.Key)
                ? Path.GetFileName(pluginDir)
                : manifest.Key;
            return manifest.Commands
                .Select(entry => BuildCommand(entry, pluginKey, pluginDir))
                .Where(cmd => cmd is not null)
                .Cast<Command>()
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<Command>();
        }
    }

    /// <summary>Null for an entry whose script has neither a recognized extension - not a
    /// discovery-wide failure (docs §2's "one bad entry doesn't abort the rest" contract), just
    /// that one entry contributing no command.</summary>
    private static Command? BuildCommand(CommandManifestEntry entry, string pluginKey, string pluginDir)
    {
        switch (Path.GetExtension(entry.Script).ToLowerInvariant())
        {
            case ".csx":
                return new CSharpCommand
                {
                    PluginKey = pluginKey,
                    Hook = entry.Hook,
                    Key = entry.Key,
                    Name = entry.Name,
                    Description = entry.Description,
                    Image = entry.Image,
                    Enabled = entry.Enabled,
                    ConfirmWrites = entry.ConfirmWrites,
                    ScriptPath = Path.Combine(pluginDir, entry.Script),
                };
            case ".py":
                return new PythonCommand
                {
                    PluginKey = pluginKey,
                    Hook = entry.Hook,
                    Key = entry.Key,
                    Name = entry.Name,
                    Description = entry.Description,
                    Image = entry.Image,
                    Enabled = entry.Enabled,
                    ConfirmWrites = entry.ConfirmWrites,
                    ScriptPath = Path.Combine(pluginDir, entry.Script),
                    // An empty Method isn't rejected here - PreCompile's own ContainsVariable check
                    // turns it into a real CompileError message instead of a silent skip.
                    Method = entry.Method ?? string.Empty,
                };
            default:
                return null;
        }
    }
}
