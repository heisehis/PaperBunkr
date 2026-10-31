using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// <see cref="IPluginFields"/> for one plugin (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md
/// §D1). A field's value is the issue custom value <c>&lt;pluginKey&gt;.&lt;field&gt;</c>, validated and normalised against the declared
/// type. Plugin fields are the plugin's own derived data, not the user's metadata, so they are not behind the <c>confirmWrites</c> gate.
/// Never throws: a refusal returns false and is written to the plugin's log.
/// </summary>
public sealed class PluginFieldsAdapter : IPluginFields
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly PluginFieldSchema? _schema;
    private readonly string _pluginKey;
    private readonly IPluginLogger _log;

    public PluginFieldsAdapter(Func<PaperbunkrDbContext> contextFactory, PluginFieldSchema? schema, string pluginKey, IPluginLogger log)
    {
        _contextFactory = contextFactory;
        _schema = schema;
        _pluginKey = pluginKey;
        _log = log;
    }

    public bool Set(Issue issue, string field, string? value)
    {
        PluginFieldDefinition? definition = _schema?.Find(field);
        if (definition is null)
        {
            _log.Warn($"Fields.Set: '{field}' isn't declared in plugin.xml's <Fields>.");
            return false;
        }

        string? stored = null;
        if (value is not null)
        {
            stored = PluginFieldSchema.Normalize(definition.Type, value);
            if (stored is null)
            {
                _log.Warn($"Fields.Set: '{value}' isn't a valid {definition.Type.ToString().ToLowerInvariant()} for '{field}'.");
                return false;
            }
        }

        try
        {
            using var context = _contextFactory();
            var tracked = context.Issues.Include(i => i.CustomValues).FirstOrDefault(i => i.Id == issue.Id);
            if (tracked is null)
            {
                return false;
            }

            string name = definition.StorageName;
            var existing = tracked.CustomValues.FirstOrDefault(cv => cv.Name == name);
            if (stored is null)
            {
                if (existing is not null)
                {
                    tracked.CustomValues.Remove(existing);
                }
            }
            else if (existing is null)
            {
                tracked.CustomValues.Add(new IssueCustomValue { IssueId = tracked.Id, Name = name, Value = stored });
            }
            else
            {
                existing.Value = stored;
            }

            context.SaveChanges();
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Fields.Set: couldn't save '{field}' on issue {issue.Id}.", ex);
            return false;
        }
    }

    public string? Get(Issue issue, string field)
    {
        if (_schema?.Find(field) is not { } definition)
        {
            return null;
        }

        try
        {
            using var context = _contextFactory();
            string name = definition.StorageName;
            return context.Set<IssueCustomValue>().Where(cv => cv.IssueId == issue.Id && cv.Name == name).Select(cv => cv.Value).FirstOrDefault();
        }
        catch (Exception ex)
        {
            _log.Error($"Fields.Get: couldn't read '{field}'.", ex);
            return null;
        }
    }

    public int Clear(string field)
    {
        if (_schema?.Find(field) is not { } definition)
        {
            return 0;
        }

        try
        {
            using var context = _contextFactory();
            string name = definition.StorageName;
            var rows = context.Set<IssueCustomValue>().Where(cv => cv.Name == name).ToList();
            context.RemoveRange(rows);
            context.SaveChanges();
            return rows.Count;
        }
        catch (Exception ex)
        {
            _log.Error($"Fields.Clear: couldn't clear '{field}'.", ex);
            return 0;
        }
    }
}
