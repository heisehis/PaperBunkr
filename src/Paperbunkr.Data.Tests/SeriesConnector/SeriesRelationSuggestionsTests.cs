using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.SeriesConnector;
using Paperbunkr.Data.Tests.StoryEventIdentity;

namespace Paperbunkr.Data.Tests.SeriesConnector;

/// <summary><see cref="SeriesRelationSuggestions"/> on a temp SQLite database (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.3).</summary>
public class SeriesRelationSuggestionsTests : IdentityTestDb
{
    /// <summary>A series whose issues (one per year from <paramref name="firstYear"/>) feature <paramref name="leadIn"/> of them with <paramref name="character"/>.</summary>
    private int Series(string name, int issues, int firstYear, string? character = null, int leadIn = 0, string? writer = null, int writtenIn = 0, string publisher = "DC Comics")
    {
        using var context = NewContext();
        var series = new Series { Name = name, Publisher = publisher };
        for (int i = 0; i < issues; i++)
        {
            series.Issues.Add(new Issue { Number = (i + 1).ToString(), Year = firstYear + i });
        }

        context.Series.Add(series);
        context.SaveChanges();

        var ordered = series.Issues.OrderBy(i => i.Year).ToList();
        if (character is not null)
        {
            var c = context.Characters.FirstOrDefault(x => x.Name == character) ?? context.Characters.Add(new Character { Name = character }).Entity;
            context.SaveChanges();
            foreach (var issue in ordered.Take(leadIn))
            {
                context.CharacterAppearances.Add(new CharacterAppearance { CharacterId = c.Id, IssueId = issue.Id });
            }
        }

        if (writer is not null)
        {
            var w = context.Creators.FirstOrDefault(x => x.Name == writer) ?? context.Creators.Add(new Creator { Name = writer }).Entity;
            context.SaveChanges();
            foreach (var issue in ordered.Take(writtenIn))
            {
                context.CreatorCredits.Add(new CreatorCredit { CreatorId = w.Id, IssueId = issue.Id, Role = "Writer" });
            }
        }

        context.SaveChanges();
        return series.Id;
    }

    private IReadOnlyList<InferredSeriesRelation> For(int seriesId)
    {
        using var context = NewContext();
        return SeriesRelationSuggestions.For(context, seriesId);
    }

    private static int Other(InferredSeriesRelation r, int self) => r.SourceSeriesId == self ? r.TargetSeriesId : r.SourceSeriesId;

    [Fact]
    public void SharedLead_NeedsHalfTheIssuesOfBoth()
    {
        int action = Series("Action Comics", 4, 2016, "Clark Kent", leadIn: 4);
        int lead = Series("Superman Unlimited", 4, 2025, "Clark Kent", leadIn: 2);
        int cameo = Series("Justice League", 10, 2018, "Clark Kent", leadIn: 2);

        var suggestions = For(action);

        var shared = Assert.Single(suggestions, s => s.Type == RelationType.SharedCharacters);
        Assert.Equal(lead, Other(shared, action));
        Assert.Equal("Shares Clark Kent as lead", shared.Reason);
        Assert.InRange(shared.Confidence, 0.6m, 0.7m);
        Assert.DoesNotContain(suggestions, s => Other(s, action) == cameo);
    }

    [Fact]
    public void SharedLead_OneShotsDontCount_AndMoreSharedLeadsRankHigher()
    {
        int superman = Series("Superman", 4, 2020, "Clark Kent", leadIn: 4);
        int oneShot = Series("Superman: The Special", 1, 2021, "Clark Kent", leadIn: 1);
        int one = Series("Action Comics", 4, 2020, "Clark Kent", leadIn: 4);
        int two = Series("Adventures of Superman", 4, 2020, "Clark Kent", leadIn: 4);
        using (var context = NewContext())
        {
            var lois = context.Characters.Add(new Character { Name = "Lois Lane" }).Entity;
            context.SaveChanges();
            foreach (var issue in context.Issues.Where(i => i.SeriesId == superman || i.SeriesId == two).ToList())
            {
                context.CharacterAppearances.Add(new CharacterAppearance { CharacterId = lois.Id, IssueId = issue.Id });
            }

            context.SaveChanges();
        }

        var leads = For(superman).Where(s => s.Type == RelationType.SharedCharacters).ToList();

        Assert.DoesNotContain(leads, s => Other(s, superman) == oneShot);
        Assert.Equal(new[] { two, one }, leads.Select(s => Other(s, superman)));
        Assert.Equal("Shares Lois Lane and 1 more as leads", leads[0].Reason); // she leads fewer series than Clark, so she says more
    }

    [Fact]
    public void SameWriter_NeedsThreeIssuesEach_AndOverlappingYears()
    {
        int a = Series("Superman Unlimited", 4, 2025, writer: "Dan Slott", writtenIn: 3);
        int b = Series("Mighty Things", 4, 2026, writer: "Dan Slott", writtenIn: 3);
        int tooFew = Series("Other Book", 4, 2025, writer: "Dan Slott", writtenIn: 2);
        int tooEarly = Series("Old Book", 3, 1990, writer: "Dan Slott", writtenIn: 3);

        var suggestions = For(a);

        var writer = Assert.Single(suggestions, s => s.Type == RelationType.Similar);
        Assert.Equal((b, "Same writer · Dan Slott"), (Other(writer, a), writer.Reason));
        Assert.DoesNotContain(suggestions, s => Other(s, a) == tooFew || Other(s, a) == tooEarly);
    }

    [Fact]
    public void SpinOff_IsANamedCharacterWhoWasAlreadyInTheEarlierSeries_SeenFromEitherSide()
    {
        int teenTitans = Series("Teen Titans", 5, 1980, "Nightwing", leadIn: 1);
        int nightwing = Series("Nightwing (1996)", 3, 1996);

        var fromSpinOff = Assert.Single(For(nightwing), s => s.Type == RelationType.SpinOff);
        Assert.Equal((nightwing, teenTitans, "Spin-off · Nightwing"), (fromSpinOff.SourceSeriesId, fromSpinOff.TargetSeriesId, fromSpinOff.Reason));

        var fromOrigin = Assert.Single(For(teenTitans), s => s.Type == RelationType.SpinOff);
        Assert.Equal(nightwing, fromOrigin.SourceSeriesId);
    }

    [Fact]
    public void SimilarTitle_OneContainsTheOther_ButNotCompanionsOrTeamUps()
    {
        int superman = Series("Superman", 2, 2023);
        int unlimited = Series("Superman Unlimited", 2, 2025);
        Series("Superman Annual", 1, 2024);
        Series("Batman/Superman", 2, 2024);

        var titles = For(superman).Where(s => s.Type == RelationType.Related).ToList();

        Assert.Equal(new[] { unlimited }, titles.Select(t => Other(t, superman)));
        Assert.Equal("Similar title", titles[0].Reason);
    }

    [Fact]
    public void LeavesOut_RelatedPairs_DismissedPairs_OtherPublishers_AndManga()
    {
        int superman = Series("Superman", 2, 2023);
        int related = Series("Superman Unlimited", 2, 2025);
        int dismissed = Series("Superman Adventures", 2, 2024);
        Series("Superman Classics", 2, 2024, publisher: "Marvel");
        using (var context = NewContext())
        {
            MediaRelationResolver.TryCreate(context, superman, related, RelationType.Sequel);
            MediaRelationResolver.DismissSeriesPair(context, superman, dismissed);
            context.Series.Add(new Series { Name = "Superman Manga", ContentType = ContentType.Manga, Publisher = "DC Comics", Issues = { new Issue { Number = "1", Year = 2024 } } });
            context.SaveChanges();
        }

        Assert.Empty(For(superman));
    }

    [Fact]
    public void CapsAtEight_StrongestFirst()
    {
        int superman = Series("Superman", 3, 2020, "Clark Kent", leadIn: 3);
        for (int i = 0; i < 12; i++)
        {
            Series($"Superman Tales {i:00}", 3, 2020, i < 3 ? "Clark Kent" : null, leadIn: i < 3 ? 3 : 0);
        }

        var suggestions = For(superman);

        Assert.Equal(SeriesRelationSuggestions.DefaultLimit, suggestions.Count);
        Assert.Equal(3, suggestions.Take(3).Count(s => s.Type == RelationType.SharedCharacters));
    }

    [Fact]
    public void NothingToSay_Empty()
    {
        Assert.Empty(For(Series("Saga", 3, 2012, publisher: "Image")));
    }
}
