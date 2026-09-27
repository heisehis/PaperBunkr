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

/// <summary>One book's computed source/destination pair from <see cref="LibraryOrganizerService.PlanAsync"/>.
/// <see cref="IsAlreadyInPlace"/> means the destination is exactly where the file already is (nothing to do);
/// <see cref="Problem"/> means this book could not be planned (a bad template token, an empty file name...) and
/// is reported as a failure of that one book instead of aborting the batch. <see cref="SkipReason"/> means the book was deliberately
/// left alone (a required field is empty) and is reported as skipped.</summary>
public sealed record PlannedMove(
    Issue Issue,
    string SourcePath,
    string DestinationPath,
    bool IsCollision,
    bool IsAlreadyInPlace = false,
    string? Problem = null,
    string? SkipReason = null);

public sealed record OrganizePlan(IReadOnlyList<PlannedMove> Moves);

/// <summary>One profile's part of a multi-profile run: the profile and what it would do.</summary>
public sealed record ProfilePlan(OrganizerProfile Profile, OrganizePlan Plan);

/// <summary>One profile's part of a finished multi-profile run.</summary>
public sealed record ProfileResult(OrganizerProfile Profile, OrganizeResult Result);

/// <summary>Per-item outcome of <see cref="LibraryOrganizerService.ExecuteAsync"/> - never a single
/// batch-wide success/failure (design doc §6's "failure isolation, per-item" rule).</summary>
public sealed class OrganizeResult
{
    public List<PlannedMove> Succeeded { get; } = new();
    public List<PlannedMove> Skipped { get; } = new();
    public List<(PlannedMove Move, string Error)> Failed { get; } = new();

    /// <summary>Books whose file already sits at the calculated path - nothing was done, nothing was logged for undo, no write-back is wanted.</summary>
    public List<PlannedMove> AlreadyInPlace { get; } = new();

    /// <summary>Library entries whose file was replaced by another book's file (the old file went to the Recycle Bin); they were left without a file rather than pointing at someone else's.</summary>
    public List<int> ReplacedIssueIds { get; } = new();
}

/// <summary>Outcome of <see cref="LibraryOrganizerService.UndoLastOrganizeAsync"/> - the "Undo last
/// organize" action design doc §8 scoped but never wired to a real UI trigger. Deliberately not
/// <see cref="OrganizeResult"/>/<see cref="PlannedMove"/> shaped - those carry a real <see cref="Issue"/>
/// per item, which an undo entry doesn't have on hand (only the two paths recorded in
/// <see cref="UndoLogEntry"/>) without an extra lookup this summary doesn't need.</summary>
public sealed record UndoResult(int Reversed, int Failed, IReadOnlyList<string> Errors);
