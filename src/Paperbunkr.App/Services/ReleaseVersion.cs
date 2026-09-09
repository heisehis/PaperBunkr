using System;
using System.Reflection;

namespace Paperbunkr.App.Services;

/// <summary>
/// The one place that knows how Paperbunkr's version is shaped and compared
/// (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-design.md, Decision 5).
///
/// <para>
/// The assembly carries a four-part <see cref="AssemblyName.Version"/> (<c>0.3.0.0</c> - the
/// project-wide single source of truth, <c>Paperbunkr.App.csproj</c>'s <c>&lt;Version&gt;</c>). CI
/// derives the release/tag string by dropping the trailing revision segment and appending
/// <c>-beta</c> (a single release stream - the prerelease tag carries no ordering); this class does
/// the same derivation for display, and compares versions on the first three components only.
/// </para>
///
/// <para>
/// <see cref="TryParseHeading"/> turns a <c>CHANGELOG.md</c> heading version
/// (<see cref="ChangelogEntry.Version"/>, e.g. <c>"0.3.0-beta"</c>, <c>"0.1.1-alpha"</c>) back into
/// a comparable <see cref="Version"/> by stripping any <c>-suffix</c> and parsing the <c>x.y.z</c>.
/// </para>
/// </summary>
public static class ReleaseVersion
{
    /// <summary>The running assembly's four-part version (<c>0.3.0.0</c>). Falls back to
    /// <c>0.0.0.0</c> only if the assembly somehow has no version (never in a real build).</summary>
    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>The release/tag-style string for display - <c>"0.3.0-beta"</c> from
    /// <see cref="Current"/>. Matches how CI derives the tag from the csproj <c>&lt;Version&gt;</c>.</summary>
    public static string DisplayString => $"{Current.Major}.{Current.Minor}.{Current.Build}-beta";

    /// <summary>
    /// Parses a changelog heading version (<c>"0.3.0-beta"</c>, <c>"0.1.1-alpha"</c>, or a bare
    /// <c>"0.3.0"</c>) into a three-component <see cref="Version"/> for comparison. Any <c>-suffix</c>
    /// (and anything after a <c>+</c> build-metadata marker) is dropped. Returns <see langword="false"/>
    /// for anything that isn't at least <c>major.minor</c>.
    /// </summary>
    public static bool TryParseHeading(string? headingVersion, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(headingVersion))
        {
            return false;
        }

        string core = headingVersion.Trim();
        int cut = core.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            core = core[..cut];
        }

        if (!Version.TryParse(core, out var parsed))
        {
            return false;
        }

        // Normalise to exactly three components so comparisons never trip on a missing/extra part.
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is a newer release than <paramref name="baseline"/>,
    /// comparing <see cref="Version.Major"/>/<see cref="Version.Minor"/>/<see cref="Version.Build"/>
    /// only - the assembly version's revision segment is always <c>0</c> and carries no meaning.
    /// </summary>
    public static bool IsNewerThan(Version candidate, Version baseline)
    {
        return ThreePart(candidate) > ThreePart(baseline);
    }

    private static Version ThreePart(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
