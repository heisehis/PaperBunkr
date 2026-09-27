namespace Paperbunkr.App.Services;

/// <summary>The project's public URLs, in one place (docs/superpowers/specs/2026-09-26-about-polish-design.md §5).</summary>
public static class ProjectLinks
{
    public const string Repository = "https://github.com/heisehis/PaperBunkr";
    public const string Wiki = Repository + "/wiki";
    public const string NewIssue = Repository + "/issues/new/choose";
    public const string Releases = Repository + "/releases";

    /// <summary>A release's own page. Release tags are <c>v{version}</c> (<c>.github/workflows/release.yml</c>), e.g. <c>v0.7.0-beta</c>.</summary>
    public static string ReleaseNotes(string version) => $"{Releases}/tag/v{version.TrimStart('v', 'V')}";
}
