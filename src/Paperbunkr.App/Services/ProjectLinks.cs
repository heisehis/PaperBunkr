namespace Paperbunkr.App.Services;

/// <summary>The project's public URLs, in one place (docs/superpowers/specs/2026-09-26-about-polish-design.md §5).</summary>
public static class ProjectLinks
{
    public const string Repository = "https://github.com/heisehis/PaperBunkr";
    public const string Wiki = Repository + "/wiki";
    public const string NewIssue = Repository + "/issues/new/choose";
    public const string Releases = Repository + "/releases";

    /// <summary>The Grand Comics Database extract's own public repo: its manifest and one release per GCD dump (docs/superpowers/specs/2026-09-27-gcd-data-design.md §2).</summary>
    public const string GcdDataRepository = "https://github.com/heisehis/paperbunkr-gcd-data";

    /// <summary>A release's own page. Release tags are <c>v{version}</c> (<c>.github/workflows/release.yml</c>), e.g. <c>v0.7.0-beta</c>.</summary>
    public static string ReleaseNotes(string version) => $"{Releases}/tag/v{version.TrimStart('v', 'V')}";
}
