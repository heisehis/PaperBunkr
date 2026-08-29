using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Automation;

/// <summary>
/// A curated, audited write surface for a Data Manager plugin (docs/superpowers/specs/2026-08-28-
/// plugin-api-v3-data-manager-design.md §5). Narrow per-field setters rather than a generic
/// "set any property by name", so the compile-time surface documents exactly what a plugin may
/// touch and each setter can carry its own validation.
///
/// Every method loads the tracked entity by <c>Id</c> (never trusting the caller's possibly-stale
/// <see cref="Issue"/> instance for anything but its Id, mirroring
/// <c>PaperbunkrApplication.RemoveBook</c>), applies the change through normal EF change-tracking,
/// saves, and logs an audit line via <c>DiagnosticsService.LogMilestone</c>. Returns
/// <see langword="false"/> (never throws) if the Issue no longer exists.
///
/// <b>Confirmation gate:</b> when the calling command's manifest declares
/// <c>confirmWrites="true"</c>, every method here fails closed (returns <see langword="false"/>, no
/// DB write) until <c>IApplication.AskQuestion</c> has returned an affirmative answer (option index
/// 0, the primary button) in the same invocation.
/// </summary>
public interface IMetadataWriter
{
    bool SetFormat(Issue issue, string? value);

    bool SetBookAge(Issue issue, string? value);

    bool SetCustomValue(Issue issue, string name, string? value);

    bool AddTag(Issue issue, string tag);

    bool RemoveTag(Issue issue, string tag);
}
