using System.Collections.Generic;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>Whether a <see cref="RelationType"/> reads the same from either series' side, or needs a different label depending on which side is being viewed.</summary>
public enum RelationDirectionality
{
    Symmetric,
    Directional
}

/// <summary>
/// One <see cref="RelationType"/>'s directionality and, for a directional type, its inverse -
/// the type that describes the *other* series' role when it differs from the stored type's own
/// role. <see cref="MediaRelation.RelationType"/> always describes <see cref="MediaRelation.SourceSeries"/>'s
/// role relative to <see cref="MediaRelation.TargetSeries"/> (e.g. "Source is the Prequel of
/// Target") - so <see cref="MediaRelationResolver.GetRelatedSeries"/> uses <see cref="InverseType"/>
/// when displaying the *target* series' card (viewed from the source's page), and the stored type
/// as-is when displaying the *source* series' card (viewed from the target's page). Equal to the
/// type itself for symmetric types and for directional types with no named inverse in the source
/// doc's vocabulary.
/// </summary>
public sealed record RelationTypeInfo(RelationDirectionality Directionality, RelationType InverseType);

/// <summary>
/// Directionality/inversion classification for every <see cref="RelationType"/> (docs/superpowers/
/// specs/2026-08-17-metadata-model-phase3-media-relations-design.md, source doc §26). Only 13 of
/// the ~20 members are explicitly classified by the source doc - the rest is this project's own
/// extension, flagged in the design doc so it isn't mistaken for a cited fact. Same data-driven
/// catalog shape as <c>SmartListCatalog</c>/<c>BulkFieldRegistry</c>/<c>LibraryFieldCatalog</c>.
/// </summary>
public static class RelationTypeCatalog
{
    public static readonly IReadOnlyDictionary<RelationType, RelationTypeInfo> All = new Dictionary<RelationType, RelationTypeInfo>
    {
        // Directional, named inverse pair (source doc §26, explicit).
        [RelationType.Prequel] = new(RelationDirectionality.Directional, RelationType.Sequel),
        [RelationType.Sequel] = new(RelationDirectionality.Directional, RelationType.Prequel),
        [RelationType.Adaptation] = new(RelationDirectionality.Directional, RelationType.SourceMaterial),
        [RelationType.SourceMaterial] = new(RelationDirectionality.Directional, RelationType.Adaptation),
        [RelationType.Contains] = new(RelationDirectionality.Directional, RelationType.CollectedFrom),
        [RelationType.CollectedFrom] = new(RelationDirectionality.Directional, RelationType.Contains),

        // Directional, no named inverse in the source doc - same label shown from both sides
        // rather than inventing new vocabulary (this project's extension, not the source doc's).
        [RelationType.SideStory] = new(RelationDirectionality.Directional, RelationType.SideStory),
        [RelationType.SpinOff] = new(RelationDirectionality.Directional, RelationType.SpinOff),
        [RelationType.Remake] = new(RelationDirectionality.Directional, RelationType.Remake),
        [RelationType.Compilation] = new(RelationDirectionality.Directional, RelationType.Compilation),
        [RelationType.Continuation] = new(RelationDirectionality.Directional, RelationType.Continuation),
        [RelationType.Reboot] = new(RelationDirectionality.Directional, RelationType.Reboot),
        [RelationType.Reimagining] = new(RelationDirectionality.Directional, RelationType.Reimagining),

        // Symmetric, explicit in source doc §26.
        [RelationType.SameUniverse] = new(RelationDirectionality.Symmetric, RelationType.SameUniverse),
        [RelationType.SharedUniverse] = new(RelationDirectionality.Symmetric, RelationType.SharedUniverse),
        [RelationType.SameContinuity] = new(RelationDirectionality.Symmetric, RelationType.SameContinuity),
        [RelationType.Crossover] = new(RelationDirectionality.Symmetric, RelationType.Crossover),
        [RelationType.SharedCharacters] = new(RelationDirectionality.Symmetric, RelationType.SharedCharacters),
        [RelationType.Similar] = new(RelationDirectionality.Symmetric, RelationType.Similar),
        [RelationType.Related] = new(RelationDirectionality.Symmetric, RelationType.Related),

        // Symmetric, this project's extension - each describes an inherently mutual relationship.
        [RelationType.AlternateStory] = new(RelationDirectionality.Symmetric, RelationType.AlternateStory),
        [RelationType.AlternateVersion] = new(RelationDirectionality.Symmetric, RelationType.AlternateVersion),
        [RelationType.ParallelStory] = new(RelationDirectionality.Symmetric, RelationType.ParallelStory),
        [RelationType.DifferentEdition] = new(RelationDirectionality.Symmetric, RelationType.DifferentEdition),
        [RelationType.Variant] = new(RelationDirectionality.Symmetric, RelationType.Variant),
        [RelationType.SecondPrinting] = new(RelationDirectionality.Symmetric, RelationType.SecondPrinting),
        [RelationType.Other] = new(RelationDirectionality.Symmetric, RelationType.Other),
    };
}
