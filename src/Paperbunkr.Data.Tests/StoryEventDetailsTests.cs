using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Tests.StoryEventIdentity;

namespace Paperbunkr.Data.Tests;

public sealed class StoryEventDetailsTests : IdentityTestDb
{
    [Fact]
    public void EventDetailsAndCuratedTagsPersistIndependently()
    {
        int id;
        using (var db = NewContext())
        {
            var storyEvent = new StoryEvent
            {
                Name = "Test event", Description = "Curated summary", StartDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CoverImageUrl = "https://example.test/cover.jpg", CoverSource = StoryEventCoverSource.Manual,
                Tags = { new StoryEventTag { Value = "Cosmic", Category = "Theme", Weight = IssueTagWeight.Defining } },
            };
            db.StoryEvents.Add(storyEvent);
            db.SaveChanges();
            id = storyEvent.Id;
        }

        using (var db = NewContext())
        {
            var loaded = db.StoryEvents.Include(e => e.Tags).Single(e => e.Id == id);
            Assert.Equal("Curated summary", loaded.Description);
            Assert.Equal(StoryEventCoverSource.Manual, loaded.CoverSource);
            var tag = Assert.Single(loaded.Tags);
            Assert.Equal("Theme", tag.Category);
            Assert.Equal(IssueTagWeight.Defining, tag.Weight);
        }
    }
}
