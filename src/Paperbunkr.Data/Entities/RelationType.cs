namespace Paperbunkr.Data.Entities;

/// <summary>
/// Canonical vocabulary for a <see cref="MediaRelation"/> (docs/superpowers/specs/2026-08-17-
/// metadata-model-phase3-media-relations-design.md, source doc §25.2). See
/// <see cref="RelationTypeCatalog"/> for each value's directionality and, where directional,
/// its inverse-display type.
/// </summary>
public enum RelationType
{
    // Narrative
    Prequel,
    Sequel,
    SideStory,
    SpinOff,
    AlternateStory,
    AlternateVersion,
    ParallelStory,

    // Universe / story
    SameUniverse,
    SharedUniverse,
    SameContinuity,
    Crossover,
    SharedCharacters,

    // Production
    Adaptation,
    SourceMaterial,
    Remake,
    Compilation,
    Contains,
    CollectedFrom,

    // Publication
    DifferentEdition,
    Variant,
    SecondPrinting,
    Continuation,
    Reboot,
    Reimagining,

    // Generic
    Related,
    Similar,
    Other
}
