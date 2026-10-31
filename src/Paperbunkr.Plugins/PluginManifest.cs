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

    /// <summary>
    /// The minimum plugin API this plugin needs, <c>"N"</c> or <c>"N.M"</c> (docs/superpowers/specs/
    /// 2026-09-20-plugin-api-4-1-design.md §3). Absent means the 4.0 baseline, so every manifest
    /// written before this attribute existed keeps working unchanged. A different major than the
    /// host's blocks the plugin at load; a different minor never does.
    /// </summary>
    [XmlAttribute("requiresApi")]
    public string? RequiresApi { get; set; }

    [XmlElement("Command")]
    public List<CommandManifestEntry> Commands { get; set; } = new();

    /// <summary>
    /// The plugin's declared settings (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §6.1),
    /// rendered by the host and stored through the existing per-plugin settings store. Null when the
    /// manifest has no <c>&lt;Settings&gt;</c> element (deliberately not pre-initialised, so "absent" and
    /// "empty" stay distinguishable to the serializer).
    /// </summary>
    [XmlArray("Settings")]
    [XmlArrayItem("Setting")]
    public List<SettingManifestEntry>? Settings { get; set; }

    /// <summary>Plugin smart fields (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §D1). Null when absent.</summary>
    [XmlArray("Fields")]
    [XmlArrayItem("Field")]
    public List<FieldManifestEntry>? Fields { get; set; }

    /// <summary>Hosts the plugin declares it talks to (Plugin API 4.5 §F). Null when absent.</summary>
    [XmlArray("Network")]
    [XmlArrayItem("Host")]
    public List<NetworkHostManifestEntry>? Network { get; set; }
}

/// <summary>One <c>&lt;Field key label type/&gt;</c>; raw strings, validated by <see cref="PluginFieldSchema.Parse"/>.</summary>
public sealed class FieldManifestEntry
{
    [XmlAttribute("key")]
    public string? Key { get; set; }

    [XmlAttribute("label")]
    public string? Label { get; set; }

    /// <summary><c>number</c>, <c>text</c>, <c>date</c> or <c>toggle</c> (case-insensitive).</summary>
    [XmlAttribute("type")]
    public string? Type { get; set; }
}

/// <summary>One <c>&lt;Host name/&gt;</c> under <c>&lt;Network&gt;</c>: an exact host or a <c>*.example.com</c> wildcard.</summary>
public sealed class NetworkHostManifestEntry
{
    [XmlAttribute("name")]
    public string? Name { get; set; }
}

/// <summary>One <c>&lt;Setting&gt;</c> in a manifest's <c>&lt;Settings&gt;</c>. Everything is a raw string here; <see cref="PluginSettingsSchema.Parse"/> validates it.</summary>
public sealed class SettingManifestEntry
{
    [XmlAttribute("key")]
    public string? Key { get; set; }

    [XmlAttribute("label")]
    public string? Label { get; set; }

    /// <summary><c>toggle</c>, <c>text</c>, <c>choice</c>, <c>number</c> or <c>secret</c> (case-insensitive).</summary>
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlAttribute("default")]
    public string? Default { get; set; }

    [XmlAttribute("description")]
    public string? Description { get; set; }

    [XmlAttribute("min")]
    public string? Min { get; set; }

    [XmlAttribute("max")]
    public string? Max { get; set; }

    /// <summary><c>true</c> to stop the user editing this setting in the overlay once a value is stored (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md §4). <c>true</c>/<c>false</c> only.</summary>
    [XmlAttribute("locked")]
    public string? Locked { get; set; }

    [XmlElement("Choice")]
    public List<ChoiceManifestEntry> Choices { get; set; } = new();
}

/// <summary>One <c>&lt;Choice value label/&gt;</c> under a choice setting.</summary>
public sealed class ChoiceManifestEntry
{
    [XmlAttribute("value")]
    public string? Value { get; set; }

    [XmlAttribute("label")]
    public string? Label { get; set; }
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
    /// Optional default keyboard shortcut for a <c>Library</c>-hook command, e.g. <c>Ctrl+Alt+K</c> (Plugin API 4.3). Written as the app's shortcut text: modifiers <c>Ctrl</c>, <c>Alt</c>, <c>Shift</c> joined to a key
    /// name with <c>+</c>. Absent or unparseable means the host's own default (CE gives the first twelve enabled commands Ctrl+Shift+F1 to F12). The user can always change it in Preferences &gt; Keyboard Shortcuts.
    /// </summary>
    [XmlAttribute("shortcut")]
    public string? Shortcut { get; set; }

    /// <summary>
    /// Which right-click menus show this command (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md
    /// §E): a comma list of <c>library</c>, <c>detail</c>, <c>readinglist</c>, <c>books</c>. Absent keeps the old behaviour - a Library
    /// command shows in the Library and Detail menus, a Books command in the Books menu.
    /// </summary>
    [XmlAttribute("menus")]
    public string? Menus { get; set; }

    /// <summary><c>true</c> to list a Library command under the Library toolbar's Plugins button (Plugin API 4.5 §E).</summary>
    [XmlAttribute("toolbar")]
    public bool Toolbar { get; set; }

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
