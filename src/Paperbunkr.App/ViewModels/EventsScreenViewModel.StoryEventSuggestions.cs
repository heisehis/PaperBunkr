using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// "New story-event suggestions" section of the Story Events screen (docs/superpowers/specs/
/// 2026-09-17-storyevent-continuity-autopopulate-design.md, Phase 1) - library-wide, not scoped to
/// the currently-selected event/continuity, so it refreshes alongside the sidebar rather than
/// <c>LoadEvent</c>. Deliberately local-data-only on load (<see cref="StoryArcGroupingResolver.GetCandidates"/>
/// makes no network calls) so opening this screen never blocks on ComicVine/Metron - verification
/// against those sources happens once, on explicit Accept, not while browsing.
/// </summary>
public partial class EventsScreenViewModel
{
    public ObservableCollection<StoryEventCandidateRowViewModel> NewStoryEventCandidates { get; } = new();

    public bool HasNoNewStoryEventCandidates => NewStoryEventCandidates.Count == 0;

    public void RefreshStoryEventCandidates()
    {
        using var context = PaperbunkrDb.CreateContext();
        NewStoryEventCandidates.Clear();
        foreach (var candidate in StoryArcGroupingResolver.GetCandidates(context))
        {
            NewStoryEventCandidates.Add(new StoryEventCandidateRowViewModel(candidate, AcceptStoryEventCandidateAsync, DismissStoryEventCandidate));
        }

        OnPropertyChanged(nameof(HasNoNewStoryEventCandidates));
    }

    private async Task AcceptStoryEventCandidateAsync(StoryEventCandidateRowViewModel row)
    {
        using var context = PaperbunkrDb.CreateContext();

        // One last verification attempt against ComicVine/Metron (if configured) at accept time -
        // this is the one point in the flow where a short async wait is acceptable, since the user
        // just took an explicit action.
        var verifiedList = await ArcExternalVerificationService.VerifyAsync(context, new[] { row.Candidate }, CancellationToken.None);
        var verified = verifiedList.Single();

        var storyEvent = StoryEventResolver.GetOrCreate(context, verified.ArcName);
        storyEvent.ComicVineArcId ??= verified.ComicVineArcId;
        storyEvent.MetronArcId ??= verified.MetronArcId;
        context.SaveChanges();

        // Calling AddMember in position order lets its own auto-incrementing Position assignment
        // (max + 1 per call) reproduce the verified/local order, with no separate position-setter
        // needed on EventMembershipResolver.
        foreach (var member in verified.Members.OrderBy(m => m.Position ?? int.MaxValue).ThenBy(m => m.Issue.Year ?? int.MaxValue))
        {
            EventMembershipResolver.AddMember(context, storyEvent.Id, member.Issue.Id, EventMembershipRole.Core);
        }

        NewStoryEventCandidates.Remove(row);
        OnPropertyChanged(nameof(HasNoNewStoryEventCandidates));
        RefreshSidebar();
        _notify("Story event created", $"\"{verified.ArcName}\" added with {verified.Members.Count} issue(s).");
    }

    private void DismissStoryEventCandidate(StoryEventCandidateRowViewModel row)
    {
        using var context = PaperbunkrDb.CreateContext();
        StoryArcGroupingResolver.Dismiss(context, row.Candidate.ArcName, row.Candidate.Publisher);
        NewStoryEventCandidates.Remove(row);
        OnPropertyChanged(nameof(HasNoNewStoryEventCandidates));
    }
}
