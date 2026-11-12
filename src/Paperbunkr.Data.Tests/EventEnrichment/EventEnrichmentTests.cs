using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.EventEnrichment;
using Paperbunkr.Data.Tests.StoryEventIdentity;

namespace Paperbunkr.Data.Tests.EventEnrichment;

public sealed class EventEnrichmentTests : IdentityTestDb
{
    [Fact]
    public void TimestormPartialDateDoesNotInventLastDay()
    {
        var date = EventPublicationDate.Bound([EventPublicationDate.Parse("2009-08-26"), EventPublicationDate.Parse("2009-08-00")], true, true, "6/6");
        Assert.Equal("2009-08", date!.Entry); Assert.Null(date.Exact); Assert.True(date.Provisional);
        Assert.Null(EventPublicationDate.Parse("2009-02-30"));
    }

    [Theory]
    [InlineData("Collected Editions\nTimestorm 2009/2099\nNon-U.S. Editions\nTormenta Temporal", null)]
    [InlineData("<p>Heroes meet their future selves.</p><h2>Collected Editions</h2><ul><li>Timestorm</li></ul>", "Heroes meet their future selves.")]
    [InlineData("<p></p>", null)]
    public void BibliographyIsNotAnOverview(string input, string? expected) => Assert.Equal(expected, EventDescriptionText.Overview(input));

    [Fact]
    public void EqualQualityConflictsDoNotDependOnProviderOrder()
    {
        var id = AddEvent("Event", StoryEventOrigin.User, []);
        using var db = NewContext(); var e = EventMetadataStore.Query(db).Single(e => e.Id == id);
        var a = new EventEvidence(EventMetadataField.Description, "Synopsis A", "Metron", "1", "Arc overview");
        var b = a with { Value = "Synopsis B", Provider = "ComicVine" };
        var snapshot = EventMetadataStore.Capture(db, e);
        Assert.Empty(new EventEnrichmentProposal(snapshot, [a, b], []).Automatic);
        Assert.Empty(new EventEnrichmentProposal(snapshot, [b, a], []).Automatic);
    }

    [Fact]
    public void FillUndoAndProtectedBlankSurviveRestart()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, []); Guid operation;
        using (var db = NewContext())
        {
            var proposal = Proposal(db, id, "A synopsis");
            var result = EventEnrichmentService.Apply(db, proposal);
            Assert.Equal(1, result.Changed); operation = result.OperationId;
        }
        using (var db = NewContext()) Assert.Equal(1, EventEnrichmentService.Undo(db, operation).Changed);
        using (var db = NewContext())
        {
            Assert.Empty(Proposal(db, id, "A synopsis").Automatic);
            Assert.Equal(0, EventEnrichmentService.Undo(db, operation).Changed);
        }
    }

    [Fact]
    public void ManualClearProtectsAndEqualValueLaterEditBlocksUndo()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, []); Guid operation;
        using (var db = NewContext()) operation = EventEnrichmentService.Apply(db, Proposal(db, id, "Original")).OperationId;
        using (var db = NewContext()) { db.StoryEvents.Single(e => e.Id == id).Description = "Edited"; db.SaveChanges(); }
        using (var db = NewContext()) { db.StoryEvents.Single(e => e.Id == id).Description = "Original"; db.SaveChanges(); }
        using (var db = NewContext()) Assert.Equal(0, EventEnrichmentService.Undo(db, operation).Changed);
        using (var db = NewContext()) { db.StoryEvents.Single(e => e.Id == id).Description = null; db.SaveChanges(); }
        using (var db = NewContext()) Assert.Empty(Proposal(db, id, "Refill").Automatic);
    }

    [Fact]
    public void StaleProposalDoesNotOverwriteAConcurrentEdit()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, []);
        EventEnrichmentProposal proposal;
        using (var db = NewContext()) proposal = Proposal(db, id, "Provider");
        using (var db = NewContext()) { db.StoryEvents.Single(e => e.Id == id).Description = "Manual"; db.SaveChanges(); }
        using (var db = NewContext())
        {
            var result = EventEnrichmentService.Apply(db, proposal); Assert.Equal(0, result.Changed); Assert.NotEmpty(result.Skipped);
            Assert.Equal("Manual", db.StoryEvents.Single(e => e.Id == id).Description);
        }
    }

    [Fact]
    public void PartialDatePersistsWithoutLegacyExactValue()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, []);
        using (var db = NewContext())
        {
            var p = Proposal(db, id, "unused");
            var evidence = new EventEvidence(EventMetadataField.EndDate, JsonSerializer.Serialize(EventPublicationDate.Parse("2009-08-00")!), "GCD", "2", "Publication month");
            Assert.Equal(1, EventEnrichmentService.Apply(db, p with { Evidence = [evidence] }).Changed);
        }
        using (var db = NewContext())
        {
            var e = db.StoryEvents.Single(e => e.Id == id);
            Assert.Null(e.EndDate); Assert.Equal("2009-08", EventPublicationDate.Effective(e, true)!.Entry);
            Assert.False(EventMetadataStore.Eligible(db, e, EventMetadataField.EndDate));
        }
    }

    private static EventEnrichmentProposal Proposal(PaperbunkrDbContext db, int id, string text)
    {
        var e = EventMetadataStore.Query(db).Single(e => e.Id == id);
        return new(EventMetadataStore.Capture(db, e), [new(EventMetadataField.Description, text, "Metron", "1", "Arc overview")], []);
    }

    [Fact]
    public void ExplicitDatePairMovesTogetherAndCanBeUndone()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, []);
        using var db = NewContext(); var e = db.StoryEvents.Single(e => e.Id == id);
        e.StartDate = new(2000, 1, 1); e.EndDate = new(2001, 1, 1); db.SaveChanges();
        var proposal = Proposal(db, id, "unused");
        var values = new[] { new EventEvidence(EventMetadataField.StartDate, JsonSerializer.Serialize(EventPublicationDate.Parse("2010-01-01")), "Metron", "1", "Reviewed date"),
            new EventEvidence(EventMetadataField.EndDate, JsonSerializer.Serialize(EventPublicationDate.Parse("2011-01-01")), "Metron", "1", "Reviewed date") };
        proposal = proposal with { Evidence = values };
        var result = EventEnrichmentService.Apply(db, proposal, values);
        Assert.Equal(2, result.Changed); Assert.Equal(2010, db.StoryEvents.Single(e => e.Id == id).StartDate!.Value.Year);
        Assert.Equal(2, EventEnrichmentService.Undo(db, result.OperationId).Changed);
        Assert.Equal(2000, db.StoryEvents.Single(e => e.Id == id).StartDate!.Value.Year);
    }

    [Fact]
    public void SaveFailureRollsBackValuesAndHistoryTogether()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, []);
        using (var db = NewContext())
        {
            db.Database.ExecuteSqlRaw("CREATE TRIGGER reject_metadata_history BEFORE INSERT ON EventMetadataChanges BEGIN SELECT RAISE(ABORT, 'test rollback'); END");
            var proposal = Proposal(db, id, "Provider description");
            Assert.Throws<DbUpdateException>(() => EventEnrichmentService.Apply(db, proposal));
        }
        using var check = NewContext(); Assert.Null(check.StoryEvents.Single(e => e.Id == id).Description); Assert.Empty(check.EventMetadataChanges);
    }

    [Fact]
    public void IdentityChangeInvalidatesFetchedMetadata()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, [], metronId: "1");
        EventEnrichmentProposal proposal;
        using (var db = NewContext()) proposal = Proposal(db, id, "Old arc description");
        using (var db = NewContext()) { db.StoryEvents.Single(e => e.Id == id).MetronArcId = "2"; db.SaveChanges(); }
        using var apply = NewContext(); var result = EventEnrichmentService.Apply(apply, proposal);
        Assert.Equal(0, result.Changed); Assert.Contains(result.Skipped, s => s.Contains("identity"));
    }

    [Fact]
    public void CancellationAfterSaveStillRollsBackTheEventTransaction()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, []);
        using var source = NewContext();
        using var cancellation = new CancellationTokenSource();
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite(source.Database.GetConnectionString())
            .AddInterceptors(new CancelAfterSave(cancellation)).Options;
        using (var apply = new PaperbunkrDbContext(options))
        {
            var proposal = Proposal(apply, id, "Cancelled provider description");
            Assert.Throws<OperationCanceledException>(() => EventEnrichmentService.Apply(apply, proposal, cancellationToken: cancellation.Token));
        }
        using var check = NewContext(); Assert.Null(check.StoryEvents.Single(e => e.Id == id).Description); Assert.Empty(check.EventMetadataChanges);
        Assert.Single(Proposal(check, id, "Retry").Automatic);
    }

    private sealed class CancelAfterSave(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) { cancellation.Cancel(); return result; }
    }

    [Fact]
    public void LegacyExactDateWriterReplacesAnExistingPartialEndpoint()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, []);
        using (var db = NewContext())
        {
            var e = db.StoryEvents.Single(e => e.Id == id);
            EventMetadataStore.SetDate(db, e, false, EventPublicationDate.Parse("2009-08")); db.SaveChanges();
        }
        using (var db = NewContext())
        {
            var e = db.StoryEvents.Single(e => e.Id == id);
            var endpoint = Assert.Single(e.DateEndpoints);
            Assert.Equal(EntityState.Unchanged, db.Entry(endpoint).State);
            e.StartDate = new(2010, 6, 2);
            db.SaveChanges();
            Assert.Equal(2010, endpoint.Year);
        }
        using var check = NewContext(); Assert.Equal("2010-06-02", EventPublicationDate.Effective(check.StoryEvents.Single(e => e.Id == id), false)!.Entry);
    }
}
