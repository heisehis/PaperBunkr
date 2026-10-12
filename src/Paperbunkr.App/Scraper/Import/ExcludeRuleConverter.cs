using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Scraper.Import;

/// <summary>The converted exclude rule: a group naming the books to <em>skip</em> (null = skip none), or the reason the rules could not be carried over exactly.</summary>
public sealed record ExcludeConversion(PluginConditionGroup? Group, string? RejectedBecause)
{
    public bool Rejected => RejectedBecause is not null;
}

/// <summary>
/// Turns a Library Organizer profile's exclude rules into an <see cref="OrganizerProfile.ExcludeRuleJson"/> group
/// (docs/superpowers/specs/2026-10-04-lop-profile-import-design.md; lives in the app project because the rule JSON is a Paperbunkr.Plugins.Abstractions type), exactly or not at all.
/// </summary>
/// <remarks>
/// <para>Our profile's rule names the books to <em>skip</em>. The plugin's <c>ExcludeMode</c> has two meanings (<c>locommon.py:615-630</c>):
/// <c>Do not</c> skips the books that fall under the rules, which is our model as it stands; <c>Only</c> organizes <em>only</em> those books, so the ones to skip are the
/// books that do <em>not</em> fall under the rules. <see cref="PluginConditionGroup"/> has no group-level Not, so that complement is built by De Morgan: every group's
/// mode flips and every condition's Not flips.</para>
/// <para>A plugin group with no rules reports "no result" and is ignored by its parent, and a profile with no rules at all moves everything in either mode, so empty
/// groups drop out and an empty tree converts to null.</para>
/// <para>A rule that cannot be matched exactly (an unknown or date field, a numeric <c>is</c>, a text <c>greater than</c>, a Yes/No value other than <c>Yes</c>) rejects the
/// whole profile: dropping or approximating it would change which files move.</para>
/// </remarks>
public static class ExcludeRuleConverter
{
    public static ExcludeConversion Convert(LopExcludeRules rules)
    {
        if (!TryMode(rules.Operator, out SmartListGroupMode rootMode))
        {
            return Reject($"the exclude rules combine with '{rules.Operator}', which is not 'Any' or 'All'");
        }

        bool only = string.Equals(rules.ExcludeMode, "Only", StringComparison.OrdinalIgnoreCase);
        if (!only && !string.Equals(rules.ExcludeMode, "Do not", StringComparison.OrdinalIgnoreCase))
        {
            return Reject($"the exclude mode '{rules.ExcludeMode}' is not 'Do not' or 'Only'");
        }

        var (group, error) = ConvertGroup(rootMode, rules.Rules);
        if (error is not null)
        {
            return Reject(error);
        }

        if (group is null)
        {
            return new ExcludeConversion(null, null);
        }

        return new ExcludeConversion(only ? Negate(group) : group, null);
    }

    private static (PluginConditionGroup? Group, string? Error) ConvertGroup(SmartListGroupMode mode, IReadOnlyList<LopRuleNode> nodes)
    {
        var conditions = new List<PluginCondition>();
        var children = new List<PluginConditionGroup>();

        foreach (LopRuleNode node in nodes)
        {
            switch (node)
            {
                case LopRule rule:
                    var (condition, ruleError) = ConvertRule(rule);
                    if (ruleError is not null)
                    {
                        return (null, ruleError);
                    }

                    conditions.Add(condition!);
                    break;

                case LopRuleGroup nested:
                    if (!TryMode(nested.Operator, out SmartListGroupMode nestedMode))
                    {
                        return (null, $"a rule group combines with '{nested.Operator}', which is not 'Any' or 'All'");
                    }

                    var (child, childError) = ConvertGroup(nestedMode, nested.Children);
                    if (childError is not null)
                    {
                        return (null, childError);
                    }

                    if (child is not null)
                    {
                        children.Add(child);
                    }

                    break;
            }
        }

        // The plugin ignores a group that has no rules (it reports no result), so it must not count as true or false here either.
        return conditions.Count == 0 && children.Count == 0 ? (null, null) : (new PluginConditionGroup(mode, conditions, children), null);
    }

    private static (PluginCondition? Condition, string? Error) ConvertRule(LopRule rule)
    {
        string describe = $"'{rule.Field} {rule.Operator} {rule.Value}'";
        if (!LopFieldMap.TryGet(rule.Field, out LopFieldMap.Entry? entry))
        {
            return (null, $"the rule {describe} uses the field '{rule.Field}', which has no exact equivalent here");
        }

        string op = rule.Operator.Trim().ToLowerInvariant();
        switch (entry.Kind)
        {
            case LopFieldKind.Text:
                return op switch
                {
                    "is" => (Text(entry, SmartListOperator.Is, rule.Value, not: false), null),
                    "is not" => (Text(entry, SmartListOperator.Is, rule.Value, not: true), null),
                    "contains" => (Text(entry, SmartListOperator.Contains, rule.Value, not: false), null),
                    "does not contain" => (Text(entry, SmartListOperator.Contains, rule.Value, not: true), null),
                    "greater than" or "less than" => (null, $"the rule {describe} compares text with greater/less than, which has no exact equivalent here"),
                    _ => (null, $"the rule {describe} uses the operator '{rule.Operator}', which is not one the plugin has"),
                };

            case LopFieldKind.Number:
                if (op is not ("greater than" or "less than"))
                {
                    return (null, $"the rule {describe} compares a number with text, which never matches in the plugin and so cannot be carried over");
                }

                if (!int.TryParse(rule.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                {
                    return (null, $"the rule {describe} needs a whole number");
                }

                return (new PluginCondition(
                    entry.Field,
                    op == "greater than" ? SmartListOperator.GreaterThan : SmartListOperator.LessThan,
                    number.ToString(CultureInfo.InvariantCulture)), null);

            default:
                if (!string.Equals(rule.Value.Trim(), "Yes", StringComparison.OrdinalIgnoreCase))
                {
                    return (null, $"the rule {describe} tests for '{rule.Value}', but only 'Yes' has an exact equivalent (unknown is not 'No' here)");
                }

                return op switch
                {
                    "is" => (new PluginCondition(entry.Field, SmartListOperator.Is, "true"), null),
                    "is not" => (new PluginCondition(entry.Field, SmartListOperator.Is, "true", Not: true), null),
                    _ => (null, $"the rule {describe} uses the operator '{rule.Operator}' on a Yes/No field"),
                };
        }
    }

    // The plugin compares text case-sensitively (`field_data == value`, `value in field_data`), so the native condition must too.
    private static PluginCondition Text(LopFieldMap.Entry entry, SmartListOperator op, string value, bool not) =>
        new(entry.Field, op, value, Not: not, IgnoreCase: false);

    private static bool TryMode(string op, out SmartListGroupMode mode)
    {
        mode = string.Equals(op, "Any", StringComparison.OrdinalIgnoreCase) ? SmartListGroupMode.Or : SmartListGroupMode.And;
        return string.Equals(op, "Any", StringComparison.OrdinalIgnoreCase) || string.Equals(op, "All", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The complement of <paramref name="group"/> by De Morgan: modes flip, every condition's Not flips.</summary>
    private static PluginConditionGroup Negate(PluginConditionGroup group) => new(
        group.Mode == SmartListGroupMode.And ? SmartListGroupMode.Or : SmartListGroupMode.And,
        group.Conditions.Select(c => c with { Not = !c.Not }).ToList(),
        group.ChildGroups.Select(Negate).ToList());

    private static ExcludeConversion Reject(string reason) => new(null, reason);
}
