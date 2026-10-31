using Paperbunkr.App.Services;
using Paperbunkr.App.Services.ContinuityScreen;
using Paperbunkr.App.Tests.Gcd;
using Paperbunkr.Data;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary><see cref="ContinuityOverviewLoader"/> against a real database and a small GCD extract.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ContinuityOverviewLoaderTests : ContinuityScreenTestBase
{
    private static void MatchToGcd(int seriesId, int gcdSeriesId)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Series.Single(s => s.Id == seriesId).GcdSeriesId = gcdSeriesId;
        context.SaveChanges();
    }

    [Fact]
    public void TwoMemberSeriesMatchedToTheSameGcdSeries_LoadsAndLinksThePlaceholderToBoth()
    {
        // A wrong Metron gcd_id (or a duplicated series row) can leave two library series on one GCD series. The overview used to
        // key a dictionary by GCD id and failed with "An item with the same key has already been added".
        int fcbd = SeedSeries("Free Comic Book Day 2015 (Secret Wars)", ("1", 2015));
        int sw2099 = SeedSeries("Secret Wars 2099", ("1", 2015));
        int continuityId = SeedContinuity("Earth 616", fcbd, sw2099);
        MatchToGcd(fcbd, 20);
        MatchToGcd(sw2099, 20);

        using var extract = new GcdTestExtract()
            .Series(10, "Secret Wars 2099 Prelude", 2014).Series(20, "Secret Wars 2099", 2015)
            .Bond(10, 20, "major_name_numbering_continues");
        using var gcd = GcdDataStore.TryOpen(extract.ExtractPath)!;
        using var context = PaperbunkrDb.CreateContext();

        var overview = ContinuityOverviewLoader.Load(context, continuityId, gcd);

        Assert.NotNull(overview);
        var run = Assert.Single(overview!.Runs);
        Assert.Contains(run.Items, i => i.IsPlaceholder && i.Url == "https://www.comics.org/series/10/");
        Assert.Contains(run.Items, i => i.SeriesId == fcbd);
        Assert.Contains(run.Items, i => i.SeriesId == sw2099);
    }
}
