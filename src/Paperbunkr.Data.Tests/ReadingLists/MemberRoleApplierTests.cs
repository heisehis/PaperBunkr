using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests.ReadingLists;

public sealed class MemberRoleApplierTests
{
    private static RoleSuggestion High(EventMembershipRole role = EventMembershipRole.Prologue) => new(role, RoleConfidence.High, "Format: Prologue");

    private static RoleSuggestion Low(EventMembershipRole role = EventMembershipRole.TieIn) => new(role, RoleConfidence.Low, "From Thor");

    // -- reading-list items --

    [Fact]
    public void AHighConfidenceRole_FillsAnEmptyListItem_AndIsMarkedAutomatic()
    {
        var item = new ReadingListItem();

        var result = MemberRoleApplier.Apply(item, High());

        Assert.Equal(RoleApplyResult.Applied, result);
        Assert.Equal(EventMembershipRole.Prologue, item.Role);
        Assert.Equal(RoleAssignmentSource.Auto, item.RoleSource);
        Assert.Equal("Format: Prologue", item.RoleReason);
        Assert.Null(item.SuggestedRole);
    }

    [Fact]
    public void ALowConfidenceRole_IsOnlySuggested_EvenForAnEmptyListItem()
    {
        var item = new ReadingListItem();

        var result = MemberRoleApplier.Apply(item, Low());

        Assert.Equal(RoleApplyResult.Suggested, result);
        Assert.Null(item.Role);
        Assert.Equal(EventMembershipRole.TieIn, item.SuggestedRole);
        Assert.Equal("From Thor", item.SuggestedReason);
    }

    [Fact]
    public void ARoleTheUserSet_NeverChanges_AHighConfidenceOneBecomesASuggestion()
    {
        var item = new ReadingListItem { Role = EventMembershipRole.Core, RoleSource = RoleAssignmentSource.User };

        var result = MemberRoleApplier.Apply(item, High());

        Assert.Equal(RoleApplyResult.Suggested, result);
        Assert.Equal(EventMembershipRole.Core, item.Role);
        Assert.Equal(RoleAssignmentSource.User, item.RoleSource);
        Assert.Equal(EventMembershipRole.Prologue, item.SuggestedRole);
    }

    [Fact]
    public void ARoleSetBeforeThisFeatureExisted_IsTreatedAsTheUsers()
    {
        var item = new ReadingListItem { Role = EventMembershipRole.TieIn };      // RoleSource null = legacy

        MemberRoleApplier.Apply(item, High(EventMembershipRole.Epilogue));

        Assert.Equal(EventMembershipRole.TieIn, item.Role);
        Assert.Equal(EventMembershipRole.Epilogue, item.SuggestedRole);
    }

    [Fact]
    public void AnAutomaticRole_MayBeReplacedByAnotherHighConfidenceOne()
    {
        var item = new ReadingListItem { Role = EventMembershipRole.TieIn, RoleSource = RoleAssignmentSource.Auto };

        var result = MemberRoleApplier.Apply(item, High(EventMembershipRole.Aftermath));

        Assert.Equal(RoleApplyResult.Applied, result);
        Assert.Equal(EventMembershipRole.Aftermath, item.Role);
    }

    [Fact]
    public void ADismissedSuggestion_IsNeverMadeAgain()
    {
        var item = new ReadingListItem { RoleSuggestionDismissed = true };

        Assert.Equal(RoleApplyResult.None, MemberRoleApplier.Apply(item, High()));
        Assert.Null(item.Role);
        Assert.Null(item.SuggestedRole);
    }

    [Fact]
    public void ASuggestionEqualToTheCurrentRole_ClearsAnyOlderPendingSuggestion()
    {
        var item = new ReadingListItem { Role = EventMembershipRole.Prologue, RoleSource = RoleAssignmentSource.User, SuggestedRole = EventMembershipRole.TieIn };

        var result = MemberRoleApplier.Apply(item, High(EventMembershipRole.Prologue));

        Assert.Equal(RoleApplyResult.None, result);
        Assert.Null(item.SuggestedRole);
    }

    [Fact]
    public void NoSuggestion_ChangesNothing()
    {
        var item = new ReadingListItem { SuggestedRole = EventMembershipRole.TieIn };

        Assert.Equal(RoleApplyResult.None, MemberRoleApplier.Apply(item, null));
        Assert.Equal(EventMembershipRole.TieIn, item.SuggestedRole);
    }

    // -- event members (Role is never empty) --

    [Fact]
    public void ANewEventMember_TakesAHighConfidenceRole()
    {
        var member = new EventMembership { Role = EventMembershipRole.Core };

        var result = MemberRoleApplier.Apply(member, High(EventMembershipRole.Epilogue), isNew: true);

        Assert.Equal(RoleApplyResult.Applied, result);
        Assert.Equal(EventMembershipRole.Epilogue, member.Role);
        Assert.Equal(RoleAssignmentSource.Auto, member.RoleSource);
    }

    [Fact]
    public void AnExistingEventMember_OnlyEverGetsASuggestion_BecauseItsDefaultCoreIsIndistinguishableFromAChoice()
    {
        var member = new EventMembership { Role = EventMembershipRole.Core };

        var result = MemberRoleApplier.Apply(member, High(EventMembershipRole.Epilogue));

        Assert.Equal(RoleApplyResult.Suggested, result);
        Assert.Equal(EventMembershipRole.Core, member.Role);
        Assert.Equal(EventMembershipRole.Epilogue, member.SuggestedRole);
    }

    [Fact]
    public void AnAutomaticEventRole_CanBeRefreshed()
    {
        var member = new EventMembership { Role = EventMembershipRole.TieIn, RoleSource = RoleAssignmentSource.Auto };

        Assert.Equal(RoleApplyResult.Applied, MemberRoleApplier.Apply(member, High(EventMembershipRole.Prologue)));
        Assert.Equal(EventMembershipRole.Prologue, member.Role);
    }

    // -- user actions --

    [Fact]
    public void Accepting_MakesTheSuggestionTheUsersOwnChoice()
    {
        var item = new ReadingListItem { SuggestedRole = EventMembershipRole.TieIn, SuggestedReason = "From Thor" };

        Assert.True(MemberRoleApplier.Accept(item));

        Assert.Equal(EventMembershipRole.TieIn, item.Role);
        Assert.Equal(RoleAssignmentSource.User, item.RoleSource);
        Assert.Null(item.SuggestedRole);
        Assert.False(MemberRoleApplier.Accept(item));            // nothing left to accept
    }

    [Fact]
    public void Dismissing_ClearsTheSuggestionAndBlocksFutureOnes()
    {
        var member = new EventMembership { Role = EventMembershipRole.Core, SuggestedRole = EventMembershipRole.TieIn };

        MemberRoleApplier.Dismiss(member);

        Assert.Null(member.SuggestedRole);
        Assert.True(member.RoleSuggestionDismissed);
        Assert.Equal(RoleApplyResult.None, MemberRoleApplier.Apply(member, High(), isNew: false));
    }

    [Fact]
    public void ClearingAnAutomaticRole_ReturnsAListItemToNoRole_AndAnEventMemberToCore_AndDoesNotComeBack()
    {
        var item = new ReadingListItem { Role = EventMembershipRole.Prologue, RoleSource = RoleAssignmentSource.Auto, RoleReason = "x" };
        var member = new EventMembership { Role = EventMembershipRole.Prologue, RoleSource = RoleAssignmentSource.Auto };

        Assert.True(MemberRoleApplier.ClearAuto(item));
        Assert.True(MemberRoleApplier.ClearAuto(member));

        Assert.Null(item.Role);
        Assert.Equal(EventMembershipRole.Core, member.Role);
        Assert.Equal(RoleApplyResult.None, MemberRoleApplier.Apply(item, High()));
    }

    [Fact]
    public void ClearingARoleTheUserSet_IsRefused()
    {
        var item = new ReadingListItem { Role = EventMembershipRole.Core, RoleSource = RoleAssignmentSource.User };

        Assert.False(MemberRoleApplier.ClearAuto(item));
        Assert.Equal(EventMembershipRole.Core, item.Role);
    }

    [Fact]
    public void ChoosingARoleByHand_MarksItTheUsersAndDropsAPendingSuggestion()
    {
        var item = new ReadingListItem { Role = EventMembershipRole.Prologue, RoleSource = RoleAssignmentSource.Auto, RoleReason = "x", SuggestedRole = EventMembershipRole.TieIn };

        MemberRoleApplier.MarkUserSet(item);

        Assert.Equal(RoleAssignmentSource.User, item.RoleSource);
        Assert.Null(item.RoleReason);
        Assert.Null(item.SuggestedRole);
    }
}
