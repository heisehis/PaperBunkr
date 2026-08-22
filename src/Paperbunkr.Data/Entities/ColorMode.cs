namespace Paperbunkr.Data.Entities;

/// <summary>
/// Replaces the old <c>bool BlackAndWhite</c> (docs/superpowers/specs/2026-08-17-metadata-model-
/// phase1-canonical-metadata-design.md). Deliberate deviation from CE, not parity - CE's own field
/// is a 3-state <c>YesNo</c> (Yes/No/Unknown); <see cref="Grayscale"/>/<see cref="Mixed"/> have no
/// CE precedent at all.
/// </summary>
public enum ColorMode
{
    Unknown,
    Color,
    BlackAndWhite,
    Grayscale,
    Mixed,
}
