namespace Paperbunkr.Data.Entities;

/// <summary>
/// What started an Activity Center job (docs/superpowers/specs/2026-09-03-activity-center-design.md).
/// Shown in the history row ("13:54 · drag-drop"). Stored as its string name.
/// </summary>
public enum ActivityTrigger
{
    Manual,
    DragDrop,
    Startup,
    Scheduled,
    Plugin,
    Watch,
}
