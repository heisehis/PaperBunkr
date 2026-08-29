using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real adapter for <see cref="IMetadataWriter"/> (docs/superpowers/specs/2026-08-28-plugin-api-v3-
/// data-manager-design.md §5). Every setter:
/// <list type="number">
/// <item>checks the per-invocation confirmation gate (<see cref="PluginInvocationContext"/>) and
/// returns <see langword="false"/> without writing if a <c>confirmWrites="true"</c> command hasn't
/// had an affirmative <c>AskQuestion</c> this invocation;</item>
/// <item>opens its own <see cref="PaperbunkrDb.CreateContext"/>, loads the tracked entity by
/// <c>Id</c> (never trusting the caller's <see cref="Issue"/> instance for anything but its Id,
/// mirroring <see cref="PaperbunkrApplication.RemoveBook"/>);</item>
/// <item>applies the change through EF change-tracking, <c>SaveChanges()</c>s, and logs an audit
/// line via <see cref="DiagnosticsService.LogMilestone"/>;</item>
/// <item>returns <see langword="false"/> (never throws) if the Issue no longer exists.</item>
/// </list>
/// </summary>
public sealed class PaperbunkrMetadataWriter : IMetadataWriter
{
    public bool SetFormat(Issue issue, string? value) =>
        Write(issue.Id, "Format", q => q, i => i.Format = value);

    public bool SetBookAge(Issue issue, string? value) =>
        Write(issue.Id, "BookAge", q => q, i => i.BookAge = value);

    public bool SetCustomValue(Issue issue, string name, string? value) =>
        Write(issue.Id, $"CustomValue '{name}'", q => q.Include(i => i.CustomValues), i =>
        {
            var existing = i.CustomValues.FirstOrDefault(cv => cv.Name == name);
            if (value is null)
            {
                if (existing is not null)
                {
                    i.CustomValues.Remove(existing);
                }
            }
            else if (existing is null)
            {
                i.CustomValues.Add(new IssueCustomValue { IssueId = i.Id, Name = name, Value = value });
            }
            else
            {
                existing.Value = value;
            }
        });

    public bool AddTag(Issue issue, string tag) =>
        Write(issue.Id, $"add tag '{tag}'", q => q.Include(i => i.Tags), i =>
        {
            var values = i.Tags.Where(t => t.Field == IssueTagField.Tags).Select(t => t.Value).Append(tag);
            i.MergeFrom(IssueTagField.Tags, values);
        });

    public bool RemoveTag(Issue issue, string tag) =>
        Write(issue.Id, $"remove tag '{tag}'", q => q.Include(i => i.Tags), i =>
        {
            var values = i.Tags.Where(t => t.Field == IssueTagField.Tags)
                .Select(t => t.Value)
                .Where(v => !string.Equals(v, tag, StringComparison.OrdinalIgnoreCase));
            i.MergeFrom(IssueTagField.Tags, values);
        });

    private static bool Write(int issueId, string field, Func<IQueryable<Issue>, IQueryable<Issue>> include, Action<Issue> apply)
    {
        // Fail closed for a confirmWrites command that hasn't been confirmed this invocation.
        if (PluginInvocationContext.Current is { WritesAllowed: false })
        {
            return false;
        }

        using var context = PaperbunkrDb.CreateContext();
        var tracked = include(context.Issues).FirstOrDefault(i => i.Id == issueId);
        if (tracked is null)
        {
            return false;
        }

        apply(tracked);
        context.SaveChanges();

        string pluginKey = PluginInvocationContext.Current?.PluginKey ?? "(unknown)";
        DiagnosticsService.LogMilestone($"Plugin '{pluginKey}' set {field} on Issue #{issueId}");
        return true;
    }
}
