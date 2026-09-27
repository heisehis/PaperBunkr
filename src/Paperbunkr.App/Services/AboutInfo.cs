using System.Runtime.InteropServices;

namespace Paperbunkr.App.Services;

/// <summary>
/// The text About's "Copy version info" puts on the clipboard for bug reports (docs/superpowers/specs/2026-09-26-about-polish-design.md
/// §5). The composing overload is pure so it can be tested; <see cref="VersionReport()"/> fills it from the running process.
/// </summary>
public static class AboutInfo
{
    public static string VersionReport()
        => VersionReport(ReleaseVersion.DisplayString, ReleaseVersion.BuildMetadata, RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), RuntimeInformation.FrameworkDescription);

    public static string VersionReport(string version, string? build, string os, string architecture, string framework)
    {
        string buildPart = string.IsNullOrWhiteSpace(build) ? string.Empty : $" (build {build})";
        return $"Paperbunkr {version}{buildPart}\n{os.Trim()} ({architecture})\n{framework.Trim()}";
    }
}
