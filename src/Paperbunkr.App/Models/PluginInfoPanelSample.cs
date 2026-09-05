using System.Text.RegularExpressions;

namespace Paperbunkr.App.Models;

/// <summary>
/// One rendered panel in the Detail screen's Plugins tab (docs/superpowers/specs/2026-09-05-plugin-
/// api-v2-remaining-hooks-plan.md §10) - a ComicInfoHtml/ComicInfoUI command's name plus its result
/// text for the currently focused issue.
/// </summary>
public sealed class PluginInfoPanelSample
{
    public required string CommandName { get; init; }
    public required string Text { get; init; }

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// Paperbunkr has no WebView/HTML-rendering surface (retired per onboarding.md §12/§13) - a
    /// ComicInfoHtml command's result is shown as plain text with tags stripped, never as real HTML
    /// (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §5's explicit caveat). A null
    /// result (script returned nothing) renders as an empty string, not "(null)".
    /// </summary>
    public static string RenderText(string? raw, bool isHtml)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        return isHtml ? TagRegex.Replace(raw, string.Empty).Trim() : raw;
    }
}
