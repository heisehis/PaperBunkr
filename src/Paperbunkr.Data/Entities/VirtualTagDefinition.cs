namespace Paperbunkr.Data.Entities;

/// <summary>
/// A user-defined computed metadata field (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md
/// §1) - a plain EF row rather than CE's fixed 20-slot <c>VirtualTag01..20</c> property scheme
/// (a WinForms reflection-driven-panel workaround this schema has no need for).
/// </summary>
public class VirtualTagDefinition
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Template string with <c>{FieldName}</c> tokens - see <c>VirtualTagTemplateEvaluator</c>.</summary>
    public string CaptionFormat { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public int SortOrder { get; set; }
}
