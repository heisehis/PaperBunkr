namespace Paperbunkr.Data.Entities;

/// <summary>One entry in a <see cref="ReadingList"/>, always a real <see cref="Issue"/> reference — see <see cref="ReadingList"/>'s remarks.</summary>
public class ReadingListItem
{
    public int Id { get; set; }

    public int ReadingListId { get; set; }

    public ReadingList? ReadingList { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    /// <summary>Sub-arc header shown above a run of items in the UI. Paperbunkr-original — no CE or CBLManager precedent; null renders ungrouped.</summary>
    public string? GroupLabel { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Phase 4c overhaul - reuses <see cref="EventMembershipRole"/> rather than a second near-identical vocabulary (source doc §24 doesn't specify distinct values for reading-list items).</summary>
    public EventMembershipRole? Role { get; set; }

    /// <summary>Who set <see cref="Role"/>. Null = set before role detection existed; treated as <see cref="RoleAssignmentSource.User"/>.</summary>
    public RoleAssignmentSource? RoleSource { get; set; }

    /// <summary>Why an automatically applied <see cref="Role"/> was chosen, e.g. "Format: Prologue".</summary>
    public string? RoleReason { get; set; }

    /// <summary>A pending low-confidence (or would-replace-a-user-choice) role suggestion, awaiting Accept or Dismiss.</summary>
    public EventMembershipRole? SuggestedRole { get; set; }

    public string? SuggestedReason { get; set; }

    /// <summary>The user rejected the suggestion (or cleared an automatic role): do not suggest again.</summary>
    public bool RoleSuggestionDismissed { get; set; }

    public string? Notes { get; set; }
}
