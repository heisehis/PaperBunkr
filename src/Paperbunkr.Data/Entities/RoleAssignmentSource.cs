namespace Paperbunkr.Data.Entities;

/// <summary>Who set an event member's or reading-list item's <c>Role</c> (docs/superpowers/specs/2026-09-25-reading-list-role-detection-design.md).
/// A null source on a row means it was set before role detection existed and is treated as <see cref="User"/>: detection never overwrites it.</summary>
public enum RoleAssignmentSource
{
    /// <summary>Chosen by the user (or carried over from a list/event they built).</summary>
    User,

    /// <summary>Applied automatically from a high-confidence signal; may be replaced or cleared by the user.</summary>
    Auto,
}
