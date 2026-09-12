using System.Xml.Serialization;

namespace Paperbunkr.Plugins;

/// <summary>
/// XML shape of <c>plugin.xml</c> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §2).
/// Deliberately flat/single-concrete-type rather than CE's polymorphic <c>CommandCollection</c>
/// XML round-trip (abstract <c>Command</c> with <c>[XmlInclude]</c>-registered subclasses) - v1
/// only has one script-backed command type (C#), so there's no polymorphism to preserve. A second
/// type joined without a schema change for the type itself (docs/superpowers/specs/2026-08-30-
/// python-plugin-scripting-design.md) - <see cref="XmlPluginInitializer"/> dispatches on
/// <see cref="CommandManifestEntry.Script"/>'s file extension instead.
/// </summary>
[XmlRoot("Plugin")]
public sealed class PluginManifest
{
    [XmlAttribute("key")]
    public string Key { get; set; } = string.Empty;

    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>"Script" (default, unchanged from before this attribute existed - every manifest
    /// written before Plugin API v4 has no `tier` attribute at all and defaults here) or "Native"
    /// (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md §4). A Native-tier
    /// manifest declares no <see cref="Commands"/> at all - its commands come from
    /// <see cref="Assembly"/>'s <c>INativePluginModule.RegisterCommands</c> instead.</summary>
    [XmlAttribute("tier")]
    public string Tier { get; set; } = "Script";

    /// <summary>Relative path (from the manifest's own folder) to the plugin's main compiled
    /// assembly. Required when <see cref="Tier"/> is "Native"; ignored otherwise.</summary>
    [XmlAttribute("assembly")]
    public string? Assembly { get; set; }

    [XmlElement("Command")]
    public List<CommandManifestEntry> Commands { get; set; } = new();
}

/// <summary>One &lt;Command&gt; element in a plugin manifest - mirrors CE's <c>Command</c> XML attributes plus a <see cref="Script"/> path pointing at the .csx file this entry compiles into a <see cref="CSharpCommand"/>.</summary>
public sealed class CommandManifestEntry
{
    [XmlAttribute("hook")]
    public string Hook { get; set; } = string.Empty;

    [XmlAttribute("key")]
    public string Key { get; set; } = string.Empty;

    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute("description")]
    public string? Description { get; set; }

    [XmlAttribute("image")]
    public string? Image { get; set; }

    [XmlAttribute("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md §5 - when true, this
    /// command's <c>IMetadataWriter</c> calls are structurally required to obtain an affirmative
    /// <c>IApplication.AskQuestion</c> answer before any DB write. Default false.
    /// </summary>
    [XmlAttribute("confirmWrites")]
    public bool ConfirmWrites { get; set; }

    /// <summary>Relative path (from the manifest's own folder) to the .csx/.py script implementing this command.</summary>
    [XmlAttribute("script")]
    public string Script { get; set; } = string.Empty;

    /// <summary>Function name within a .py <see cref="Script"/> to invoke - required for Python
    /// commands (a .csx script's whole body is the command; a .py file can define several
    /// top-level functions). Ignored for .csx entries.</summary>
    [XmlAttribute("method")]
    public string? Method { get; set; }
}
