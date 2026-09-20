using Paperbunkr.Data.Naming;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Organizing;

/// <summary>CE's own `Mode` enum (design doc §6, `locommon.py:165-168`) - default Move, matching CE's
/// own default. Simulate performs no real file I/O and no database write (CE's own semantics).</summary>
public enum OrganizerMode
{
    Move,
    Copy,
    Simulate,
}

/// <summary>Used only for a non-interactive (Scheduled Task) run (design doc §6/§9) - an interactive
/// run always shows the collision dialog regardless of this setting. Default Rename, CE's own
/// numeric-suffix algorithm being the least destructive automatic choice.</summary>
public enum AutomationCollisionPolicy
{
    Skip,
    Rename,
    Overwrite,
}

/// <summary>What a collision resolver (interactive dialog or automation policy) decided for one
/// colliding item.</summary>
public enum CollisionResolution
{
    Replace,
    Rename,
    Skip,
}

/// <summary>One book's computed source/destination pair from <see cref="LibraryOrganizerService.PlanAsync"/>.</summary>
public sealed record PlannedMove(Issue Issue, string SourcePath, string DestinationPath, bool IsCollision);

public sealed record OrganizePlan(IReadOnlyList<PlannedMove> Moves);

/// <summary>Per-item outcome of <see cref="LibraryOrganizerService.ExecuteAsync"/> - never a single
/// batch-wide success/failure (design doc §6's "failure isolation, per-item" rule).</summary>
public sealed class OrganizeResult
{
    public List<PlannedMove> Succeeded { get; } = new();
    public List<PlannedMove> Skipped { get; } = new();
    public List<(PlannedMove Move, string Error)> Failed { get; } = new();
}

/// <summary>Outcome of <see cref="LibraryOrganizerService.UndoLastOrganizeAsync"/> - the "Undo last
/// organize" action design doc §8 scoped but never wired to a real UI trigger. Deliberately not
/// <see cref="OrganizeResult"/>/<see cref="PlannedMove"/> shaped - those carry a real <see cref="Issue"/>
/// per item, which an undo entry doesn't have on hand (only the two paths recorded in
/// <see cref="UndoLogEntry"/>) without an extra lookup this summary doesn't need.</summary>
public sealed record UndoResult(int Reversed, int Failed, IReadOnlyList<string> Errors);
