using System.Collections.Generic;
namespace Paperbunkr.App.Scraper.Import;

/// <summary>A node of a Library Organizer profile's exclude rules (the plugin's <c>ExcludeRule</c> / <c>ExcludeGroup</c>, <c>locommon.py:178-390</c>), read from a <c>.lop</c> file.</summary>
public abstract record LopRuleNode;

/// <summary>One rule: a field's display name (<c>Format</c>, <c>Series Complete</c>...), an operator word (<c>is</c>, <c>does not contain</c>...) and the text it compares against.</summary>
public sealed record LopRule(string Field, string Operator, string Value) : LopRuleNode;

/// <summary>A group of rules combined by <see cref="Operator"/> (<c>Any</c> or <c>All</c>).</summary>
public sealed record LopRuleGroup(string Operator, IReadOnlyList<LopRuleNode> Children) : LopRuleNode;

/// <summary>
/// A profile's whole exclude section: the top-level <see cref="Rules"/>, how they combine (<see cref="Operator"/>), and what a book that falls under them means
/// (<see cref="ExcludeMode"/>): <c>Do not</c> = such a book is left alone, <c>Only</c> = such a book is the <em>only</em> kind that is organized.
/// </summary>
public sealed record LopExcludeRules(string Operator, string ExcludeMode, IReadOnlyList<LopRuleNode> Rules);
