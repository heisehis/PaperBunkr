using System.Xml.Serialization;

namespace Paperbunkr.Plugins;

/// <summary>
/// XML shape of <c>plugin.xml</c> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §2).
/// Deliberately flat/single-concrete-type rather than CE's polymorphic <c>CommandCollection</c>
/// XML round-trip (abstract <c>Command</c> with <c>[XmlInclude]</c>-registered subclasses) - v1
/// only has one script-backed command type (C#), so there's no polymorphism to preserve.
/// </summary>
[XmlRoot("Plugin")]
public sealed class PluginManifest
{
    [XmlAttribute("key")]
    public string Key { get; set; } = string.Empty;

    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

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

    /// <summary>Relative path (from the manifest's own folder) to the .csx script implementing this command.</summary>
    [XmlAttribute("script")]
    public string Script { get; set; } = string.Empty;
}
