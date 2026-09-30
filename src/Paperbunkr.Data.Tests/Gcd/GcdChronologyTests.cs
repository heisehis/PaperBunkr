using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.Gcd;

/// <summary>Event chronology prefers GCD on-sale dates for matched issues (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4).</summary>
public class GcdChronologyTests : GcdLibraryDb
{
    private int AddEvent(string name, IEnumerable<Issue> members)
    {
        using var context = NewContext();
        var storyEvent = new StoryEvent { Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        int position = 1;
        foreach (var issue in members)
        {
            storyEvent.Members.Add(new EventMembership { IssueId = issue.Id, Position = position++ });
        }

        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        return storyEvent.Id;
    }

    [Fact]
    public void Spans_UseOnSaleDates_ForMatchedIssues_AndCoverDatesOtherwise()
    {
        Dump.Series(1, "Hulk", 2006, "Marvel").Issue(10, 1, "1", keyDate: "2006-04-00", onSaleDate: "2006-02-15");
        var series = AddSeries("Hulk: Planet Hulk", "Marvel", ("1", 2006, 10), ("2", 2006, null));
        using (var context = NewContext())
        {
            var second = context.Issues.Single(i => i.SeriesId == series.Id && i.Number == "2");
            second.Month = 6;
            var first = context.Issues.Single(i => i.SeriesId == series.Id && i.Number == "1");
            first.Month = 4;
            context.SaveChanges();
        }

        int eventId;
        using (var context = NewContext())
        {
            eventId = AddEvent("Planet Hulk", context.Issues.Where(i => i.SeriesId == series.Id).ToList());
        }

        using var store = Dump.Open();
        using var check = NewContext();
        var withGcd = EventChronology.LoadSpans(check, new[] { eventId }, store)[eventId];
        var withoutGcd = EventChronology.LoadSpans(check, new[] { eventId })[eventId];

        Assert.Equal((200602, 200606), (withGcd.Start, withGcd.End));
        Assert.Equal((200604, 200606), (withoutGcd.Start, withoutGcd.End));
    }

    [Fact]
    public void IssueDateKey_FallsBackToCoverDate_WhenGcdHasNoDate()
    {
        var dates = new Dictionary<int, int> { [10] = 200602 };

        Assert.Equal(200602, EventChronology.IssueDateKey(2006, 4, 10, dates));
        Assert.Equal(200604, EventChronology.IssueDateKey(2006, 4, 11, dates));
        Assert.Equal(200604, EventChronology.IssueDateKey(2006, 4, 10, null));
        Assert.Null(EventChronology.IssueDateKey(null, null, 11, dates));
    }
}
