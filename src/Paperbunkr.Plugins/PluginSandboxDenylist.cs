namespace Paperbunkr.Plugins;

/// <summary>
/// The one denylist both script sandboxes check against - <see cref="BlockedMetadataReferenceResolver"/>
/// (Roslyn <c>#r</c> directives, C#) and <see cref="PythonCommand"/>'s static <c>clr.AddReference</c>
/// scan (Python). Shared so the two can't quietly drift apart (docs/superpowers/specs/2026-08-30-
/// python-plugin-scripting-design.md's "Sandbox" section). Neither check is adversarial-proof -
/// both are the same "accidental overreach, not a hardened boundary" bar this whole in-process
/// plugin architecture has always targeted.
/// </summary>
internal static class PluginSandboxDenylist
{
    public static readonly string[] AssemblyPrefixes =
    {
        "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite", "SQLitePCLRaw",
    };

    public static bool IsDenied(string assemblyName) =>
        AssemblyPrefixes.Any(p => assemblyName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
