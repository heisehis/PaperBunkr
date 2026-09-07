namespace Paperbunkr.App.Models;

/// <summary>
/// Which icon-driven state a <see cref="Paperbunkr.App.Controls.StatusBadge"/> shows
/// (docs/superpowers/specs/2026-09-06-feedback-notification-system-design.md §4).
/// </summary>
public enum StatusBadgeVariant
{
    Read,
    InProgress,
    New,
}
