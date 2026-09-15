using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="DogEarEligibility.IsEligible"/> (docs/superpowers/specs/2026-09-13-
/// preferences-cosmetic-toggles-design.md) - CE's own gate condition (single-page comic, no custom
/// cover override, not a missing file), extracted as a pure function so each branch is testable
/// without a real decode.
/// </summary>
public class DogEarEligibilityTests
{
    [Fact]
    public void MultiPageFileWithNoCustomCover_AndNotMissing_IsEligible()
    {
        Assert.True(DogEarEligibility.IsEligible(pageCount: 2, fileIsMissing: false, hasCustomCover: false));
    }

    [Fact]
    public void SinglePage_IsNotEligible()
    {
        Assert.False(DogEarEligibility.IsEligible(pageCount: 1, fileIsMissing: false, hasCustomCover: false));
    }

    [Fact]
    public void UnknownPageCount_IsNotEligible()
    {
        Assert.False(DogEarEligibility.IsEligible(pageCount: null, fileIsMissing: false, hasCustomCover: false));
    }

    [Fact]
    public void MissingFile_IsNotEligible()
    {
        Assert.False(DogEarEligibility.IsEligible(pageCount: 5, fileIsMissing: true, hasCustomCover: false));
    }

    [Fact]
    public void CustomCoverOverride_IsNotEligible()
    {
        Assert.False(DogEarEligibility.IsEligible(pageCount: 5, fileIsMissing: false, hasCustomCover: true));
    }
}
