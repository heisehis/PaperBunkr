using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="RelationTypeCatalog"/>'s directionality/inversion classification (docs/
/// superpowers/specs/2026-08-17-metadata-model-phase3-media-relations-design.md, source doc §26).
/// </summary>
public class RelationTypeCatalogTests
{
    [Theory]
    [InlineData(RelationType.Prequel)]
    [InlineData(RelationType.Sequel)]
    [InlineData(RelationType.SideStory)]
    [InlineData(RelationType.SpinOff)]
    [InlineData(RelationType.AlternateStory)]
    [InlineData(RelationType.AlternateVersion)]
    [InlineData(RelationType.ParallelStory)]
    [InlineData(RelationType.SameUniverse)]
    [InlineData(RelationType.SharedUniverse)]
    [InlineData(RelationType.SameContinuity)]
    [InlineData(RelationType.Crossover)]
    [InlineData(RelationType.SharedCharacters)]
    [InlineData(RelationType.Adaptation)]
    [InlineData(RelationType.SourceMaterial)]
    [InlineData(RelationType.Remake)]
    [InlineData(RelationType.Compilation)]
    [InlineData(RelationType.Contains)]
    [InlineData(RelationType.CollectedFrom)]
    [InlineData(RelationType.DifferentEdition)]
    [InlineData(RelationType.Variant)]
    [InlineData(RelationType.SecondPrinting)]
    [InlineData(RelationType.Continuation)]
    [InlineData(RelationType.Reboot)]
    [InlineData(RelationType.Reimagining)]
    [InlineData(RelationType.Related)]
    [InlineData(RelationType.Similar)]
    [InlineData(RelationType.Other)]
    public void Catalog_HasAnEntryForEveryRelationType(RelationType type)
    {
        Assert.True(RelationTypeCatalog.All.ContainsKey(type));
    }

    [Theory]
    [InlineData(RelationType.Prequel, RelationType.Sequel)]
    [InlineData(RelationType.Sequel, RelationType.Prequel)]
    [InlineData(RelationType.Adaptation, RelationType.SourceMaterial)]
    [InlineData(RelationType.SourceMaterial, RelationType.Adaptation)]
    [InlineData(RelationType.Contains, RelationType.CollectedFrom)]
    [InlineData(RelationType.CollectedFrom, RelationType.Contains)]
    public void NamedInversePairs_AreDirectional_AndCrossReferenceCorrectly(RelationType stored, RelationType expectedInverse)
    {
        var info = RelationTypeCatalog.All[stored];
        Assert.Equal(RelationDirectionality.Directional, info.Directionality);
        Assert.Equal(expectedInverse, info.InverseType);
    }

    [Theory]
    [InlineData(RelationType.SameUniverse)]
    [InlineData(RelationType.SharedUniverse)]
    [InlineData(RelationType.SameContinuity)]
    [InlineData(RelationType.Crossover)]
    [InlineData(RelationType.SharedCharacters)]
    [InlineData(RelationType.Similar)]
    [InlineData(RelationType.Related)]
    [InlineData(RelationType.AlternateStory)]
    [InlineData(RelationType.AlternateVersion)]
    [InlineData(RelationType.ParallelStory)]
    [InlineData(RelationType.DifferentEdition)]
    [InlineData(RelationType.Variant)]
    [InlineData(RelationType.SecondPrinting)]
    [InlineData(RelationType.Other)]
    public void SymmetricTypes_DisplaySameLabelFromEitherSide(RelationType type)
    {
        var info = RelationTypeCatalog.All[type];
        Assert.Equal(RelationDirectionality.Symmetric, info.Directionality);
        Assert.Equal(type, info.InverseType);
    }

    [Theory]
    [InlineData(RelationType.SideStory)]
    [InlineData(RelationType.SpinOff)]
    [InlineData(RelationType.Remake)]
    [InlineData(RelationType.Compilation)]
    [InlineData(RelationType.Continuation)]
    [InlineData(RelationType.Reboot)]
    [InlineData(RelationType.Reimagining)]
    public void DirectionalTypesWithNoNamedInverse_StillDisplaySameLabel_ButAreMarkedDirectional(RelationType type)
    {
        var info = RelationTypeCatalog.All[type];
        Assert.Equal(RelationDirectionality.Directional, info.Directionality);
        Assert.Equal(type, info.InverseType);
    }
}
