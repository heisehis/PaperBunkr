using System.Xml.Serialization;

namespace Paperbunkr.Plugins;

/// <summary>
/// Ported from ComicRackCE's <c>XmlPluginInitializer</c> (docs/superpowers/specs/
/// 2026-08-24-plugin-api-v2-design.md §2) - parses a <c>plugin.xml</c> manifest into
/// <see cref="CSharpCommand"/> instances, one per &lt;Command&gt; element, script path resolved
/// relative to the manifest's own folder.
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
            return manifest.Commands.Select(entry => (Command)new CSharpCommand
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
            }).ToList();
        }
        catch (Exception)
        {
            return Array.Empty<Command>();
        }
    }
}
