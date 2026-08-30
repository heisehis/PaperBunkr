namespace Paperbunkr.App.Models;

/// <summary>
/// One entry in a rule-slot picker dropdown (docs/superpowers/specs/2026-08-30-smart-collections-
/// design.md) - just enough to display and select an existing <c>SmartList</c> of a given
/// <c>SmartListTargetKind</c>. Deliberately smaller than <see cref="SmartListSummary"/> (no match
/// count, no active/delete state) - this is a plain picker option, not a sidebar row.
/// </summary>
public sealed record SmartListOption(int Id, string Name);
