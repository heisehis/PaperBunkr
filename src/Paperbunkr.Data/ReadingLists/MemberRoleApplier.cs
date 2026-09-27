using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>What <see cref="MemberRoleApplier.Apply(ReadingListItem, RoleSuggestion?)"/> did with a suggestion.</summary>
public enum RoleApplyResult
{
    /// <summary>Nothing to record (no suggestion, dismissed, or the role is already what was suggested).</summary>
    None,

    /// <summary>Written into the role, marked automatic.</summary>
    Applied,

    /// <summary>Held as a pending suggestion for the user to accept or dismiss.</summary>
    Suggested,
}

/// <summary>
/// Puts a detected <see cref="RoleSuggestion"/> onto an event member or reading-list item without ever overriding the user
/// (docs/superpowers/specs/2026-09-25-reading-list-role-detection-design.md, section 3):
/// <list type="bullet">
/// <item>A high-confidence suggestion is written into the role only when the slot is open - a reading-list item with no role yet, an
/// automatically set role, or a brand-new event member. Its <c>RoleSource</c> becomes <see cref="RoleAssignmentSource.Auto"/>.</item>
/// <item>Anything else (a low-confidence suggestion, or a role the user set - including every role set before this feature existed) is only
/// stored as <c>SuggestedRole</c>, for the user to accept or dismiss.</item>
/// <item>A dismissed suggestion is never made again.</item>
/// </list>
/// </summary>
public static class MemberRoleApplier
{
    /// <summary>A reading-list item: its role is empty until something sets it.</summary>
    public static RoleApplyResult Apply(ReadingListItem item, RoleSuggestion? suggestion)
    {
        bool slotOpen = item.Role is null || item.RoleSource == RoleAssignmentSource.Auto;
        return Decide(suggestion, item.Role, slotOpen, item.RoleSuggestionDismissed) switch
        {
            Decision.Apply => Write(item, suggestion!),
            Decision.Suggest => Suggest(item, suggestion!),
            Decision.Clear => ClearSuggestion(item),
            _ => RoleApplyResult.None,
        };
    }

    /// <summary>An event member. Its role is never empty, so the slot is open only for a member that was just created
    /// (<paramref name="isNew"/>) or whose role was itself set automatically.</summary>
    public static RoleApplyResult Apply(EventMembership member, RoleSuggestion? suggestion, bool isNew = false)
    {
        bool slotOpen = isNew || member.RoleSource == RoleAssignmentSource.Auto;
        return Decide(suggestion, isNew ? null : member.Role, slotOpen, member.RoleSuggestionDismissed) switch
        {
            Decision.Apply => Write(member, suggestion!),
            Decision.Suggest => Suggest(member, suggestion!),
            Decision.Clear => ClearSuggestion(member),
            _ => RoleApplyResult.None,
        };
    }

    private enum Decision { None, Apply, Suggest, Clear }

    private static Decision Decide(RoleSuggestion? suggestion, EventMembershipRole? current, bool slotOpen, bool dismissed)
    {
        if (suggestion is null || dismissed)
        {
            return Decision.None;
        }

        if (current == suggestion.Role)
        {
            return Decision.Clear;      // already what was suggested - any older pending suggestion is moot
        }

        return suggestion.Confidence == RoleConfidence.High && slotOpen ? Decision.Apply : Decision.Suggest;
    }

    private static RoleApplyResult Write(ReadingListItem item, RoleSuggestion s)
    {
        item.Role = s.Role;
        item.RoleSource = RoleAssignmentSource.Auto;
        item.RoleReason = s.Reason;
        item.SuggestedRole = null;
        item.SuggestedReason = null;
        return RoleApplyResult.Applied;
    }

    private static RoleApplyResult Write(EventMembership member, RoleSuggestion s)
    {
        member.Role = s.Role;
        member.RoleSource = RoleAssignmentSource.Auto;
        member.RoleReason = s.Reason;
        member.SuggestedRole = null;
        member.SuggestedReason = null;
        return RoleApplyResult.Applied;
    }

    private static RoleApplyResult Suggest(ReadingListItem item, RoleSuggestion s)
    {
        item.SuggestedRole = s.Role;
        item.SuggestedReason = s.Reason;
        return RoleApplyResult.Suggested;
    }

    private static RoleApplyResult Suggest(EventMembership member, RoleSuggestion s)
    {
        member.SuggestedRole = s.Role;
        member.SuggestedReason = s.Reason;
        return RoleApplyResult.Suggested;
    }

    private static RoleApplyResult ClearSuggestion(ReadingListItem item)
    {
        item.SuggestedRole = null;
        item.SuggestedReason = null;
        return RoleApplyResult.None;
    }

    private static RoleApplyResult ClearSuggestion(EventMembership member)
    {
        member.SuggestedRole = null;
        member.SuggestedReason = null;
        return RoleApplyResult.None;
    }

    // -- Persistence helpers -----------------------------------------------------------------------------------------

    /// <summary>Copies the role-detection state (not the role itself) from a row's in-memory entity onto the tracked one being saved.</summary>
    public static void CopyState(ReadingListItem from, ReadingListItem to)
    {
        to.RoleSource = from.RoleSource;
        to.RoleReason = from.RoleReason;
        to.SuggestedRole = from.SuggestedRole;
        to.SuggestedReason = from.SuggestedReason;
        to.RoleSuggestionDismissed = from.RoleSuggestionDismissed;
    }

    public static void CopyState(EventMembership from, EventMembership to)
    {
        to.RoleSource = from.RoleSource;
        to.RoleReason = from.RoleReason;
        to.SuggestedRole = from.SuggestedRole;
        to.SuggestedReason = from.SuggestedReason;
        to.RoleSuggestionDismissed = from.RoleSuggestionDismissed;
    }

    // -- User actions ------------------------------------------------------------------------------------------------

    /// <summary>The user chose a role by hand (dropdown, bulk edit, drag between roles...): it is theirs from now on, and any pending
    /// suggestion is moot. Every call site that assigns <c>Role</c> from the UI calls this.</summary>
    public static void MarkUserSet(ReadingListItem item)
    {
        item.RoleSource = RoleAssignmentSource.User;
        item.RoleReason = null;
        item.SuggestedRole = null;
        item.SuggestedReason = null;
    }

    public static void MarkUserSet(EventMembership member)
    {
        member.RoleSource = RoleAssignmentSource.User;
        member.RoleReason = null;
        member.SuggestedRole = null;
        member.SuggestedReason = null;
    }

    /// <summary>Accepts the pending suggestion as the user's own choice. Returns false when there was none.</summary>
    public static bool Accept(ReadingListItem item)
    {
        if (item.SuggestedRole is not { } role)
        {
            return false;
        }

        item.Role = role;
        MarkUserSet(item);
        return true;
    }

    public static bool Accept(EventMembership member)
    {
        if (member.SuggestedRole is not { } role)
        {
            return false;
        }

        member.Role = role;
        MarkUserSet(member);
        return true;
    }

    /// <summary>Rejects the pending suggestion and remembers not to make it again.</summary>
    public static void Dismiss(ReadingListItem item)
    {
        item.SuggestedRole = null;
        item.SuggestedReason = null;
        item.RoleSuggestionDismissed = true;
    }

    public static void Dismiss(EventMembership member)
    {
        member.SuggestedRole = null;
        member.SuggestedReason = null;
        member.RoleSuggestionDismissed = true;
    }

    /// <summary>Removes an automatically applied role: a reading-list item goes back to no role, an event member back to Core (its
    /// role can never be empty). Not offered again. A role the user set is left alone - returns false.</summary>
    public static bool ClearAuto(ReadingListItem item)
    {
        if (item.RoleSource != RoleAssignmentSource.Auto)
        {
            return false;
        }

        item.Role = null;
        item.RoleSource = null;
        item.RoleReason = null;
        item.RoleSuggestionDismissed = true;
        return true;
    }

    public static bool ClearAuto(EventMembership member)
    {
        if (member.RoleSource != RoleAssignmentSource.Auto)
        {
            return false;
        }

        member.Role = EventMembershipRole.Core;
        member.RoleSource = RoleAssignmentSource.User;
        member.RoleReason = null;
        member.RoleSuggestionDismissed = true;
        return true;
    }
}
