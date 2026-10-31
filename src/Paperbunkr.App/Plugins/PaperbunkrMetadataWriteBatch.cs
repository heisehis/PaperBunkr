using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Plugins;

/// <summary>One field change a batch would make, as the preview lists it.</summary>
public sealed record MetadataBatchChange(int IssueId, string Book, string Field, string Old, string New);

/// <summary>
/// <see cref="IMetadataWriteBatch"/> (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §D2).
/// Every setter applies its change to entities tracked by one private context, remembering each touched field's value the first time
/// it is touched; the preview compares that original with the final value, so a field changed and changed back drops out. Apply is one
/// <c>SaveChanges</c> (one transaction), Cancel disposes the context unsaved. Not behind the <c>confirmWrites</c> gate: the preview is
/// the confirmation. Single use.
/// </summary>
public sealed class PaperbunkrMetadataWriteBatch : IMetadataWriteBatch, IDisposable
{
    private readonly PaperbunkrDbContext _context;
    private readonly Func<ConfirmDialogRequest, Task<int>> _showDialog;
    private readonly Dictionary<int, Issue?> _issues = new();
    private readonly Dictionary<(int IssueId, string Field), string> _original = new();
    private bool _used;

    public PaperbunkrMetadataWriteBatch(PaperbunkrDbContext context, Func<ConfirmDialogRequest, Task<int>> showDialog)
    {
        _context = context;
        _showDialog = showDialog;
    }

    public bool SetFormat(Issue issue, string? value) => Change(issue.Id, "Format", i => i.Format ?? string.Empty, i => i.Format = value);

    public bool SetBookAge(Issue issue, string? value) => Change(issue.Id, "Book age", i => i.BookAge ?? string.Empty, i => i.BookAge = value);

    public bool SetCustomValue(Issue issue, string name, string? value) =>
        Change(issue.Id, $"Custom value '{name}'", i => i.CustomValues.FirstOrDefault(cv => cv.Name == name)?.Value ?? string.Empty, i =>
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
        Change(issue.Id, "Tags", Tags, i => i.MergeFrom(IssueTagField.Tags, i.Tags.Where(t => t.Field == IssueTagField.Tags).Select(t => t.Value).Append(tag)));

    public bool RemoveTag(Issue issue, string tag) =>
        Change(issue.Id, "Tags", Tags, i => i.MergeFrom(IssueTagField.Tags, i.Tags.Where(t => t.Field == IssueTagField.Tags)
            .Select(t => t.Value).Where(v => !string.Equals(v, tag, StringComparison.OrdinalIgnoreCase))));

    private static string Tags(Issue i) =>
        string.Join(", ", i.Tags.Where(t => t.Field == IssueTagField.Tags).Select(t => t.Value).OrderBy(v => v, StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<(int IssueId, string Field), Func<Issue, string>> _readers = new();

    private bool Change(int issueId, string field, Func<Issue, string> read, Action<Issue> apply)
    {
        if (_used)
        {
            return false;
        }

        if (!_issues.TryGetValue(issueId, out Issue? tracked))
        {
            tracked = _context.Issues.Include(i => i.Series).Include(i => i.CustomValues).Include(i => i.Tags).FirstOrDefault(i => i.Id == issueId);
            _issues[issueId] = tracked;
        }

        if (tracked is null)
        {
            return false;
        }

        var key = (issueId, field);
        if (!_original.ContainsKey(key))
        {
            _original[key] = read(tracked);
            _readers[key] = read;
        }

        apply(tracked);
        return true;
    }

    /// <summary>What would change: one entry per (book, field) whose final value differs from its original, in the order first touched.</summary>
    public IReadOnlyList<MetadataBatchChange> Changes =>
        _original.Select(o =>
            {
                Issue issue = _issues[o.Key.IssueId]!;
                string now = _readers[o.Key](issue);
                return new MetadataBatchChange(issue.Id, Describe(issue), o.Key.Field, o.Value, now);
            })
            .Where(c => c.Old != c.New)
            .ToList();

    public int PendingChanges => Changes.Count;

    private static string Describe(Issue issue)
    {
        string series = issue.Series?.Name ?? "(no series)";
        return string.IsNullOrEmpty(issue.Number) ? series : $"{series} #{issue.Number}";
    }

    private static string Show(string value) => value.Length == 0 ? "(empty)" : value;

    public async Task<bool> PreviewAndApplyAsync(string title)
    {
        if (_used)
        {
            return false;
        }

        _used = true;
        var changes = Changes;
        string pluginKey = PluginInvocationContext.Current?.PluginKey ?? "(unknown)";
        try
        {
            if (changes.Count == 0)
            {
                DiagnosticsService.LogMilestone($"Plugin '{pluginKey}' write batch \"{title}\": nothing to change.");
                return false;
            }

            int books = changes.Select(c => c.IssueId).Distinct().Count();
            var items = changes.Select(c => $"{c.Book} - {c.Field}: {Show(c.Old)} → {Show(c.New)}").ToList();
            int answer = await _showDialog(new ConfirmDialogRequest(
                $"{changes.Count} {(changes.Count == 1 ? "change" : "changes")} to {books} {(books == 1 ? "book" : "books")}. Nothing is saved until you apply.",
                Title: string.IsNullOrWhiteSpace(title) ? "Apply these changes?" : title,
                Items: items,
                PrimaryLabel: "Apply",
                SecondaryLabel: "Cancel")).ConfigureAwait(true);
            if (answer != 0)
            {
                return false;
            }

            _context.SaveChanges();
            foreach (var change in changes)
            {
                DiagnosticsService.LogMilestone($"Plugin '{pluginKey}' set {change.Field} on Issue #{change.IssueId} (write batch \"{title}\")");
            }

            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Plugin '{pluginKey}' write batch \"{title}\" failed, nothing saved: {ex.Message}");
            return false;
        }
        finally
        {
            _context.Dispose();
        }
    }

    public void Dispose() => _context.Dispose();
}
