using System.Globalization;

namespace Paperbunkr.Plugins;

/// <summary>
/// The plugin API version this build of Paperbunkr provides (docs/superpowers/specs/2026-09-20-
/// plugin-api-4-1-design.md §3). <c>major.minor</c>: a major bump is a breaking change to existing
/// members, a minor bump is purely additive (a new hook, interface or member). A hand-maintained
/// constant, bumped in the same commit as any plugin-visible API addition - deliberately not derived
/// from the assembly version, which tracks app releases and would drift from real API changes.
/// </summary>
public static class PluginApi
{
    /// <summary>The current API version. 4.1 added <c>IPluginEnvironment.Activity</c> (the Activity reporter); 4.2 added <c>IPluginEnvironment.Log</c> (the per-plugin logger).</summary>
    public static readonly Version Current = new(4, 2);
}

/// <summary>How a manifest's declared <c>requiresApi</c> relates to the host's API version.</summary>
public enum PluginApiCompatibilityKind
{
    /// <summary>Same major (or no declaration, i.e. the 4.0 baseline). Any minor difference is lenient - the plugin loads.</summary>
    Compatible,

    /// <summary>Major differs from the host's, in either direction. A load error - the plugin's code is never loaded.</summary>
    MajorMismatch,

    /// <summary>The attribute isn't <c>"N"</c> or <c>"N.M"</c>. Tells the host nothing about compatibility, so it's treated as a malformed manifest.</summary>
    Malformed,
}

/// <param name="Kind">See <see cref="PluginApiCompatibilityKind"/>.</param>
/// <param name="Declared">The parsed declaration, or null when the attribute is absent or malformed.</param>
/// <param name="Reason">Human-readable reason for the two blocking kinds; null when <see cref="PluginApiCompatibilityKind.Compatible"/>.</param>
public sealed record PluginApiCompatibility(PluginApiCompatibilityKind Kind, Version? Declared, string? Reason)
{
    public bool IsBlocked => Kind != PluginApiCompatibilityKind.Compatible;

    /// <summary>
    /// Evaluates <paramref name="requiresApi"/> against <paramref name="host"/> (docs/superpowers/specs/
    /// 2026-09-20-plugin-api-4-1-design.md §3.2): a major mismatch in either direction blocks, any
    /// same-major minor difference is lenient, and an absent attribute is the 4.0 baseline. Pure and
    /// host-injectable so tests can simulate any host version.
    /// </summary>
    public static PluginApiCompatibility Evaluate(string? requiresApi, Version host)
    {
        if (requiresApi is null)
        {
            return new PluginApiCompatibility(PluginApiCompatibilityKind.Compatible, null, null);
        }

        if (!TryParse(requiresApi, out Version declared))
        {
            return new PluginApiCompatibility(
                PluginApiCompatibilityKind.Malformed,
                null,
                $"plugin declares an invalid requiresApi '{requiresApi}' (expected \"N\" or \"N.M\", e.g. \"4.1\")");
        }

        if (declared.Major != host.Major)
        {
            return new PluginApiCompatibility(
                PluginApiCompatibilityKind.MajorMismatch,
                declared,
                $"plugin requires API {Format(declared)}, this app provides {Format(host)} (major version mismatch)");
        }

        return new PluginApiCompatibility(PluginApiCompatibilityKind.Compatible, declared, null);
    }

    /// <summary>
    /// The text appended to a failure's error message when a version difference could plausibly
    /// explain it (§3.2), or null when it couldn't. A plugin that declares a higher minor than the
    /// host provides always gets the hint. A plugin that declares nothing gets it only when
    /// <paramref name="driftShapedFailure"/> is true (a missing member/type, the shape API drift
    /// produces): an absent declaration is the 4.0 baseline, which can never exceed the host's
    /// version, so attaching a version note to every ordinary script syntax error in every legacy
    /// plugin would be noise, not information. A major mismatch never reaches here - it's blocked at load.
    /// </summary>
    public static string? FailureHint(Version? declared, Version host, bool driftShapedFailure)
    {
        if (declared is null)
        {
            return driftShapedFailure
                ? $"plugin declares no requiresApi; if it uses API members added after {Format(new Version(host.Major, 0))}, declare one (this app provides {Format(host)})"
                : null;
        }

        return declared.Minor > host.Minor
            ? $"plugin declares API {Format(declared)}, this app provides {Format(host)}"
            : null;
    }

    private static bool TryParse(string text, out Version version)
    {
        version = new Version(0, 0);
        string[] parts = text.Trim().Split('.');
        if (parts.Length is < 1 or > 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major))
        {
            return false;
        }

        int minor = 0;
        if (parts.Length == 2 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor))
        {
            return false;
        }

        version = new Version(major, minor);
        return true;
    }

    private static string Format(Version version) => $"{version.Major}.{version.Minor}";
}
