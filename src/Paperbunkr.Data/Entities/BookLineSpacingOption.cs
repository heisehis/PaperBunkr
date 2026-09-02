namespace Paperbunkr.Data.Entities;

/// <summary>
/// Reader line-spacing choice (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md
/// §5). See <see cref="BookFontFamilyOption"/> for why this lives in <c>Paperbunkr.Data.Entities</c>
/// rather than <c>Paperbunkr.App.Models</c>.
/// </summary>
public enum BookLineSpacingOption
{
    Compact,
    Normal,
    Relaxed,
}
