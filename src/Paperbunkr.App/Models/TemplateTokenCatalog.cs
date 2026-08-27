using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>
/// Shared token catalog for the templated/token text field editor (docs/ce-feature-inventory.md §A
/// "Templated/token text field editor"). No CE precedent exists for this feature (verified against
/// <c>ComicBookDialog.cs</c>/<c>MultipleComicBooksDialog.cs</c> - see docs/superpowers/specs/
/// 2026-08-23-issue-editor-borderless-overlay-design.md's sibling work), so this is a deliberate
/// Paperbunkr addition rather than a port.
///
/// The single-book editor (<see cref="Paperbunkr.App.ViewModels.IssuePropertiesScreenViewModel"/>)
/// evaluates a token immediately, against that one issue's own already-buffered field values, so it
/// doesn't use <see cref="Expand"/> - it reads its own properties directly. The bulk editor
/// (<see cref="Paperbunkr.App.ViewModels.BulkIssuePropertiesScreenViewModel"/>) instead inserts the
/// literal placeholder text (e.g. <c>{Series}</c>) into a staged field's <c>Value</c>, since one
/// typed template has to expand differently for every selected issue - <see cref="Expand"/> is what
/// <c>Save</c> calls per issue to do that.
/// </summary>
public static class TemplateTokenCatalog
{
    public static readonly string[] Names = { "Series", "Number", "Volume", "Year", "Title", "Publisher" };

    public static string Expand(string template, Issue issue)
    {
        if (!template.Contains('{'))
        {
            return template;
        }

        return template
            .Replace("{Series}", issue.Series?.Name ?? string.Empty)
            .Replace("{Number}", issue.EffectiveNumber() ?? string.Empty)
            .Replace("{Volume}", issue.EffectiveVolume() ?? string.Empty)
            .Replace("{Year}", issue.EffectiveYear()?.ToString() ?? string.Empty)
            .Replace("{Title}", issue.Title ?? string.Empty)
            .Replace("{Publisher}", issue.Publisher ?? string.Empty);
    }
}
