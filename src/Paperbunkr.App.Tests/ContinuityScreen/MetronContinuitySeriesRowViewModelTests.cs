using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.ComicVine;

namespace Paperbunkr.App.Tests.ContinuityScreen;

public sealed class MetronContinuitySeriesRowViewModelTests
{
    [Fact]
    public void CatalogSeriesWithoutExactLocalId_RemainsVisibleButCannotBeAdded()
    {
        var row = new MetronContinuitySeriesRowViewModel(new MetronUniverseSeries(15, "Example", "2024"), null);

        Assert.Equal("Not found in library", row.LibraryStatus);
        Assert.False(row.HasExactLibraryMatch);
        Assert.False(row.CanAdd);
    }

    [Fact]
    public void ExactLocalMatch_IsAddableUntilAlreadyInContinuity()
    {
        var row = new MetronContinuitySeriesRowViewModel(new MetronUniverseSeries(15, "Example", "2024"), 42);

        Assert.Equal("Exact library match", row.LibraryStatus);
        Assert.True(row.CanAdd);

        row.AlreadyInContinuity = true;

        Assert.Equal("Already in this continuity", row.LibraryStatus);
        Assert.False(row.CanAdd);
    }

    [Fact]
    public void DuplicateLocalIdentity_IsReportedAndNeverSelectedForAutomaticAdd()
    {
        var row = new MetronContinuitySeriesRowViewModel(new MetronUniverseSeries(15, "Example", "2024"), null, hasIdentityCollision: true);

        Assert.Equal("Conflicting or duplicate provider IDs", row.LibraryStatus);
        Assert.False(row.CanAdd);
    }

    [Fact]
    public void CrossProviderMatch_ShowsProviderEvidenceAndAvailableIdentifiers()
    {
        var row = new MetronContinuitySeriesRowViewModel(
            new MetronUniverseSeries(15, "Example", "2024", 12345, 6789), 42,
            identityMatch: "ComicVine ID match");

        Assert.Equal("2024 · Metron #15 · ComicVine #12345 · GCD #6789", row.Details);
        Assert.Equal("ComicVine ID match", row.LibraryStatus);
        Assert.True(row.CanAdd);
    }
}
