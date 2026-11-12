using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.EventEnrichment;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.Tests.ContinuityScreen;

[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventSmartFillTests : ContinuityScreenTestBase
{
    private static int Seed()
    {
        using var db = PaperbunkrDb.CreateContext();
        var e = new StoryEvent { Name = "Timestorm", MetronArcId = "1" };
        db.StoryEvents.Add(e); db.SaveChanges(); return e.Id;
    }
    private static EventSmartFillRunner Runner() => new()
    { OpenGcd = () => null, ResolveSource = (_, key) => key == "Metron" ? new FakeSource() : null, PrepareCover = (_, _, _) => Task.FromResult(false) };

    [Fact]
    public async Task EditorFillChangesDraftOnlyUntilSave()
    {
        int id = Seed();
        var vm = new NewEventOrContinuityViewModel((_, _) => { }, () => { }) { SmartFillRunner = Runner() };
        vm.LoadForEdit(NewEventOrContinuityViewModel.Kind.Event, id);
        vm.DraftSources.Open(id, true);
        await vm.DraftSources.FillSingleCommand.ExecutionTask!;
        Assert.Contains("future", vm.Description);
        using (var db = PaperbunkrDb.CreateContext()) Assert.Null(db.StoryEvents.Single(e => e.Id == id).Description);
        vm.CreateCommand.Execute(null);
        using (var db = PaperbunkrDb.CreateContext())
        {
            Assert.Contains("future", db.StoryEvents.Single(e => e.Id == id).Description);
            Assert.NotEmpty(db.EventMetadataChanges.Where(c => c.StoryEventId == id));
        }
    }

    [Fact]
    public async Task EditorCancelDoesNotPersistEnrichment()
    {
        int id = Seed();
        var vm = new NewEventOrContinuityViewModel((_, _) => { }, () => { }) { SmartFillRunner = Runner() };
        vm.LoadForEdit(NewEventOrContinuityViewModel.Kind.Event, id);
        vm.DraftSources.Open(id, true); await vm.DraftSources.FillSingleCommand.ExecutionTask!;
        vm.CancelCommand.Execute(null);
        using var db = PaperbunkrDb.CreateContext(); Assert.Null(db.StoryEvents.Single(e => e.Id == id).Description); Assert.Empty(db.EventMetadataChanges);
    }

    [Fact]
    public void EditorPartialDatesAndUncertaintyRoundTrip()
    {
        int id = Seed();
        var vm = new NewEventOrContinuityViewModel((_, _) => { }, () => { });
        vm.LoadForEdit(NewEventOrContinuityViewModel.Kind.Event, id);
        vm.StartDate = "2009"; vm.EndDate = "2009-08"; vm.EndDateUncertain = true; vm.EndDateProvisional = true;
        Assert.True(vm.CanCreate); vm.CreateCommand.Execute(null);
        vm.LoadForEdit(NewEventOrContinuityViewModel.Kind.Event, id);
        Assert.Equal("2009", vm.StartDate); Assert.Equal("2009-08", vm.EndDate); Assert.True(vm.EndDateUncertain); Assert.True(vm.EndDateProvisional);
        using var db = PaperbunkrDb.CreateContext(); Assert.Null(db.StoryEvents.Single(e => e.Id == id).EndDate);
    }

    [Fact]
    public async Task ClearingAnEnrichedDraftBeforeSaveProtectsTheBlank()
    {
        int id = Seed();
        var vm = new NewEventOrContinuityViewModel((_, _) => { }, () => { }) { SmartFillRunner = Runner() };
        vm.LoadForEdit(NewEventOrContinuityViewModel.Kind.Event, id);
        vm.DraftSources.Open(id, true); await vm.DraftSources.FillSingleCommand.ExecutionTask!;
        vm.Description = ""; vm.CreateCommand.Execute(null);
        using var db = PaperbunkrDb.CreateContext();
        Assert.Null(db.StoryEvents.Single(e => e.Id == id).Description);
        Assert.True(db.EventMetadataStates.Single(s => s.StoryEventId == id && s.Field == EventMetadataField.Description).ProtectedBlank);
    }

    [Fact]
    public async Task RunnerPreservesExistingValuesAndFailedCover()
    {
        int id = Seed(); using (var db = PaperbunkrDb.CreateContext()) { db.StoryEvents.Single(e => e.Id == id).Description = "Manual"; db.SaveChanges(); }
        var runner = Runner(); var proposal = await runner.FetchAsync(id, CancellationToken.None);
        await runner.ApplyAsync(proposal, null, Guid.NewGuid(), CancellationToken.None);
        using var check = PaperbunkrDb.CreateContext(); var e = check.StoryEvents.Single(e => e.Id == id);
        Assert.Equal("Manual", e.Description); Assert.Null(e.CoverImageUrl); Assert.Single(check.StoryEventTypeAssignments);
    }

    private sealed class FakeSource : IReadingListSource
    {
        public string SourceKey => "Metron"; public string DisplayName => SourceKey;
        public bool RequiresCredentials => false; public bool HasBrowsableCatalog => false;
        public Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken ct) => Task.FromResult<IReadOnlyList<ArcSearchResult>>([]);
        public Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string id, CancellationToken ct) => Task.FromResult<IReadOnlyList<ArcIssue>>([]);
        public Task<ArcOverviewInfo?> GetArcOverviewAsync(string id, CancellationToken ct) => Task.FromResult<ArcOverviewInfo?>(new("Present-day heroes encounter their counterparts from the future.", "https://example.test/cover.jpg", Name: "Timestorm"));
    }
}
