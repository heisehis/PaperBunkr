using Paperbunkr.Data.Entities;

namespace ClusterLibraryManager.Organizing;

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

/// <summary>
/// A named organizer configuration (design doc §7, grilling Q16=B - full CE-parity multiple named
/// profiles, not a single active configuration). Persisted in the plugin's own <c>PluginDatabase</c>
/// (design doc §10), not a core Paperbunkr migration.
/// </summary>
public sealed class OrganizerProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // CE's own fallback default (design doc §5, losettings.py:396-397) - used only when a brand new
    // profile is created with nothing else specified.
    public string FolderTemplate { get; set; } = @"{<publisher>}\{<imprint>}\{<series>}{ (<startyear>{ <format>})}";
    public string FileTemplate { get; set; } = "{<series>}{ Vol.<volume>}{ #<number2>}{ (of <count2>)}{ ({<month>, }<year>)}";

    public OrganizerMode Mode { get; set; } = OrganizerMode.Move;
    public AutomationCollisionPolicy AutomationCollisionPolicy { get; set; } = AutomationCollisionPolicy.Rename;
    public bool RemoveEmptyFolders { get; set; } = true;
    public string BaseFolder { get; set; } = string.Empty;

    /// <summary>Serialized <c>PluginConditionGroup</c> (System.Text.Json), reusing Paperbunkr's own
    /// rule engine instead of porting CE's bespoke ExcludeGroup engine (design doc §5). Null means "no
    /// exclude rule - organize everything", matching CE's own zero-rules default.</summary>
    public string? ExcludeRuleJson { get; set; }
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
