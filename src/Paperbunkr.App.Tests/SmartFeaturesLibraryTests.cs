using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Scheduling;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Smart features S3-S5 on the App side (docs/superpowers/specs/2026-10-06-smart-features-design.md §5-§7): relinking a missing entry
/// to its re-imported file, the duplicate "Recommended" badge, the scheduled genre task, shared-character suggestions, and the app's
/// reactions to a finished continuity and a completed series.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SmartFeaturesLibraryTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public SmartFeaturesLibraryTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smart_library_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    // ----- Relink (§5.1) -----

    [Fact]
    public void RelinkToCandidate_KeepsTheOldEntryAndItsHistory_TakesTheNewFile_AndRemovesTheDuplicateEntry()
    {
        int missingId, candidateId, listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "Saga" };
            var missing = new Issue
            {
                Number = "13", FilePath = @"D:\old\Saga 013.cbz", FileSize = 1000, PageCount = 28, FileIsMissing = true,
                MissingVerificationCount = 3, MissingAcknowledged = true, LastPageRead = 10, OpenCount = 2, Rating = 4,
            };
            var candidate = new Issue { Number = "13", FilePath = @"D:\new\Saga 013.cbz", FileSize = 1000, PageCount = 28, LastPageRead = 20, OpenCount = 1 };
            series.Issues.Add(missing);
            series.Issues.Add(candidate);
            context.Series.Add(series);
            context.SaveChanges();

            var list = new ReadingList { Name = "Favourites", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            list.Items.Add(new ReadingListItem { IssueId = missing.Id, SortOrder = 0 });
            context.ReadingLists.Add(list);
            context.SaveChanges();
            (missingId, candidateId, listId) = (missing.Id, candidate.Id, list.Id);
        }

        bool relinked;
        using (var context = PaperbunkrDb.CreateContext())
        {
            relinked = MissingFileRelinker.RelinkToCandidate(context, missingId, candidateId);
        }

        Assert.True(relinked);
        using var verify = PaperbunkrDb.CreateContext();
        var kept = verify.Issues.Single();
        Assert.Equal(missingId, kept.Id);
        Assert.Equal(@"D:\new\Saga 013.cbz", kept.FilePath);
        Assert.False(kept.FileIsMissing);
        Assert.Equal(0, kept.MissingVerificationCount);
        Assert.False(kept.MissingAcknowledged);
        Assert.Equal(4, kept.Rating);                 // the old entry's own data
        Assert.Equal(20, kept.LastPageRead);          // ...plus the further reading done on the re-imported copy
        Assert.Equal(3, kept.OpenCount);
        Assert.Equal(missingId, verify.ReadingListItems.Single(i => i.ReadingListId == listId).IssueId);
        Assert.Empty(verify.RemovedFilePaths);        // the file is still in use, so it is not put on the do-not-re-import list
    }

    [Fact]
    public void RelinkToCandidate_DoesNothing_WhenTheCandidateIsGone()
    {
        int missingId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "Saga" };
            var missing = new Issue { Number = "13", FilePath = @"D:\old\Saga 013.cbz", FileIsMissing = true };
            series.Issues.Add(missing);
            context.Series.Add(series);
            context.SaveChanges();
            missingId = missing.Id;
        }

        using var again = PaperbunkrDb.CreateContext();
        Assert.False(MissingFileRelinker.RelinkToCandidate(again, missingId, candidateIssueId: 9999));
        Assert.True(again.Issues.Single().FileIsMissing);
    }

    [Fact]
    public void AMissingFileRow_SaysWhatKindOfMatchItHas_AndRelinksThroughItsCallback()
    {
        MissingFileRowViewModel? relinked = null;
        MissingFileRowViewModel Row(MissingFileMatch? match) => new(1, "Saga #13", _ => Task.CompletedTask, _ => { }, _ => { })
        {
            Match = match,
            OnRelinkToMatch = match is null ? null : r => relinked = r,
        };

        var exact = Row(new MissingFileMatch(1, 2, @"D:\new\Saga 013.cbz", MissingFileMatchTier.Exact));
        var probable = Row(new MissingFileMatch(1, 2, @"D:\new\Saga 13.cbz", MissingFileMatchTier.Probable));
        var none = Row(null);

        Assert.True(exact.HasMatch);
        Assert.True(exact.IsExactMatch);
        Assert.Equal(@"Likely match (same size and page count): D:\new\Saga 013.cbz", exact.MatchLabel);
        Assert.False(probable.IsExactMatch);
        Assert.StartsWith("Possible match (same series, number and format):", probable.MatchLabel);
        Assert.False(none.HasMatch);
        Assert.Null(none.MatchLabel);

        exact.RelinkToMatchCommand.Execute(null);
        Assert.Same(exact, relinked);
    }

    // ----- Duplicate keeper (§5.2) -----

    [Fact]
    public void ADuplicateGroup_BadgesTheRecommendedCopy_WithoutChangingWhichOneIsSelected()
    {
        var first = new Issue { Id = 1, FilePath = @"C:\a.cbz", PageCount = 24, FileSize = 60_000_000 };
        var better = new Issue { Id = 2, FilePath = @"C:\b.cbz", PageCount = 28, FileSize = 40_000_000 };

        var group = new DuplicateGroupRowViewModel("Saga #13", [first, better], _ => { }, _ => { });

        Assert.True(group.Candidates[0].IsKeep);                    // the default selection is untouched
        Assert.False(group.Candidates[0].IsRecommended);
        Assert.True(group.Candidates[1].IsRecommended);
        Assert.Equal("Most pages: 28 vs 24", group.Candidates[1].RecommendedReason);
        Assert.Null(group.Candidates[0].RecommendedReason);
    }

    // ----- Scheduled genre suggestions (§6.2) -----

    [Fact]
    public void TheGenreSuggestionTask_IsInTheCatalog_OffByDefault_WithItsOwnPriority()
    {
        var task = Assert.Single(ScheduledTaskCatalog.All, t => t.Id == ScheduledTaskCatalog.SynopsisGenreSuggest);

        Assert.False(task.DefaultEnabled);
        Assert.Equal(TimeSpan.FromDays(1), task.DefaultInterval);
        Assert.Equal(ScheduledTaskCatalog.All.Count, ScheduledTaskCatalog.All.Select(t => t.Priority).Distinct().Count());
    }

    // ----- Shared-character suggestions (§7.1) -----

    private static (int ContinuityId, int CandidateSeriesId) SeedSharedCharacters()
    {
        using var context = PaperbunkrDb.CreateContext();
        var member = new Series { Name = "Amazing Spider-Man" };
        member.Issues.Add(new Issue { Number = "1", FilePath = @"C:\x\asm.cbz" });
        var candidate = new Series { Name = "Venom" };
        candidate.Issues.Add(new Issue { Number = "1", FilePath = @"C:\x\venom.cbz" });
        context.Series.AddRange(member, candidate);
        var continuity = new Continuity { Name = "Earth-616", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Continuities.Add(continuity);
        context.SaveChanges();
        ContinuityResolver.AddSeriesToContinuity(context, member.Id, continuity.Id);

        foreach (string name in new[] { "Spider-Man", "Venom", "Carnage" })
        {
            var character = new Character { Name = name };
            context.Characters.Add(character);
            context.SaveChanges();
            context.CharacterAppearances.Add(new CharacterAppearance { CharacterId = character.Id, IssueId = member.Issues[0].Id });
            context.CharacterAppearances.Add(new CharacterAppearance { CharacterId = character.Id, IssueId = candidate.Issues[0].Id });
        }

        context.SaveChanges();
        return (continuity.Id, candidate.Id);
    }

    private static SuggestionsChecksViewModel NewSuggestions(Action? changed = null) =>
        new((_, _) => { }, new ActivityService(), _ => { }, _ => { }, _ => { }, changed ?? (() => { }));

    [Fact]
    public void SharedCharacterSuggestions_AppearWithTheLocalLists_AndAcceptAddsTheSeries()
    {
        var (continuityId, seriesId) = SeedSharedCharacters();
        var vm = NewSuggestions();

        vm.RefreshStoryEventCandidates();

        var row = Assert.Single(vm.CharacterSuggestions);
        Assert.Equal("Venom", row.SeriesName);
        Assert.Equal("Earth-616", row.ContinuityName);
        Assert.Equal("Shares Carnage, Spider-Man, Venom with Amazing Spider-Man", row.Reason);
        Assert.True(vm.HasCharacterSuggestions);
        Assert.Equal(1, vm.TotalCount);

        row.AcceptCommand.Execute(null);
        TestDispatcher.Drain();

        Assert.Empty(vm.CharacterSuggestions);
        using var context = PaperbunkrDb.CreateContext();
        Assert.True(context.ContinuityMemberships.Any(m => m.ContinuityId == continuityId && m.SeriesId == seriesId));
    }

    [Fact]
    public void DismissingASharedCharacterSuggestion_KeepsItAway()
    {
        SeedSharedCharacters();
        var vm = NewSuggestions();
        vm.RefreshStoryEventCandidates();

        Assert.Single(vm.CharacterSuggestions).DismissCommand.Execute(null);
        TestDispatcher.Drain();
        vm.RefreshStoryEventCandidates();

        Assert.Empty(vm.CharacterSuggestions);
    }

    // ----- Reactions: completion (§7.4) and a completed series (§7.3) -----

    private static Task Inline(Func<Task> work) => work();

    private static int SeedContinuityOneIssueFromDone(out int lastIssueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = "Saga" };
        series.Issues.Add(new Issue { Number = "1", PageCount = 20, LastPageRead = 19, FilePath = @"C:\x\1.cbz" });
        series.Issues.Add(new Issue { Number = "2", PageCount = 20, FilePath = @"C:\x\2.cbz" });
        context.Series.Add(series);
        var continuity = new Continuity { Name = "Sagaverse", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Continuities.Add(continuity);
        context.SaveChanges();
        ContinuityResolver.AddSeriesToContinuity(context, series.Id, continuity.Id);
        lastIssueId = series.Issues[1].Id;
        return continuity.Id;
    }

    [Fact]
    public void FinishingTheLastIssueOfAContinuity_RaisesTheEvent_AndTellsTheReaderOnce()
    {
        int continuityId = SeedContinuityOneIssueFromDone(out int lastIssueId);
        var events = new LibraryEvents();
        var completed = new List<CollectionCompletedEvent>();
        events.CollectionCompleted += completed.Add;
        var activity = new ActivityService(dispatch: a => a());
        var recorder = new ReadingEventRecorder();
        using var reactions = new SmartLibraryReactions(recorder, events, activity, (_, _) => Task.FromResult("ok"), (_, _) => false, run: Inline);

        recorder.RecordFinished(ReadingItemType.Comic, lastIssueId, seriesId: null, publisher: null, primaryGenre: null, pagesRead: 20);
        recorder.RecordFinished(ReadingItemType.Comic, lastIssueId, seriesId: null, publisher: null, primaryGenre: null, pagesRead: 20);     // a re-read

        var e = Assert.Single(completed);
        Assert.Equal(CompletedCollectionKind.Continuity, e.Kind);
        Assert.Equal(continuityId, e.Id);
        var alert = Assert.Single(activity.Alerts);
        Assert.Equal("You finished Sagaverse", alert.Title);
        Assert.Equal(ActivityLinkKind.StoryEventsScreen, alert.ActionLink!.Kind);
    }

    [Fact]
    public void ALinkedSeriesMarkedCompleted_IsRefreshedOnce_ButNotWhenUnlinked_OrSwitchedOff_OrForOtherStatuses()
    {
        var events = new LibraryEvents();
        var activity = new ActivityService(dispatch: a => a());
        var refreshed = new List<int>();
        bool linked = true;
        using var reactions = new SmartLibraryReactions(
            recorder: null, events, activity,
            (seriesId, _) => { refreshed.Add(seriesId); return Task.FromResult("Refreshed from 1 provider."); },
            (_, _) => linked,
            run: Inline);

        events.Raise(new SeriesStatusChangedEvent(7, "Saga", SeriesStatus.Ongoing, SeriesStatus.Completed));
        Assert.Equal([7], refreshed);

        events.Raise(new SeriesStatusChangedEvent(8, "Hiatus", SeriesStatus.Ongoing, SeriesStatus.Hiatus));     // not Completed
        linked = false;
        events.Raise(new SeriesStatusChangedEvent(9, "Unlinked", SeriesStatus.Ongoing, SeriesStatus.Completed));
        linked = true;
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.GetOrCreateAppSettings().RefreshProviderDataOnComplete = false;
            context.SaveChanges();
        }

        events.Raise(new SeriesStatusChangedEvent(10, "Switched off", SeriesStatus.Ongoing, SeriesStatus.Completed));

        Assert.Equal([7], refreshed);
    }

    [Fact]
    public void AFailedRefresh_IsReportedAsAFailedJob_AndTheSeriesCanBeRefreshedAgainLater()
    {
        var events = new LibraryEvents();
        var activity = new ActivityService(dispatch: a => a());
        int attempts = 0;
        using var reactions = new SmartLibraryReactions(
            recorder: null, events, activity,
            (_, _) => { attempts++; throw new InvalidOperationException("offline"); },
            (_, _) => true,
            run: Inline);

        events.Raise(new SeriesStatusChangedEvent(7, "Saga", SeriesStatus.Ongoing, SeriesStatus.Completed));
        events.Raise(new SeriesStatusChangedEvent(7, "Saga", SeriesStatus.Ongoing, SeriesStatus.Completed));

        Assert.Equal(2, attempts);     // not retried on its own; the second is a second status change
        Assert.All(activity.RecentJobs, job => Assert.Equal(ActivityJobStatus.Failed, job.Status));
    }
}
