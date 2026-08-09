using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Bulk multi-book properties editor (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md),
/// reached via right-click when 2+ issue tiles are selected on the Detail screen. Same edit-buffer
/// Save/Cancel discipline as the single-book <see cref="IssuePropertiesScreenViewModel"/> - Cancel
/// never touches the database - but data-driven over <see cref="BulkFieldRegistry.All"/> instead of
/// one hand-written property per field, per the user's explicit choice given the field count here.
/// </summary>
public partial class BulkIssuePropertiesScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private List<int> _issueIds = new();

    public BulkIssuePropertiesScreenViewModel(Action goBack) : this(goBack, PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal BulkIssuePropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory)
    {
        _goBack = goBack;
        _contextFactory = contextFactory;
    }

    public ObservableCollection<BulkFieldViewModel> MainFields { get; } = new();
    public ObservableCollection<BulkFieldViewModel> ArtistFields { get; } = new();
    public ObservableCollection<BulkFieldViewModel> PlotNotesFields { get; } = new();

    private IEnumerable<BulkFieldViewModel> AllFields => MainFields.Concat(ArtistFields).Concat(PlotNotesFields);

    [ObservableProperty]
    private string _headerLabel = string.Empty;

    public void Load(IReadOnlyList<int> issueIds)
    {
        _issueIds = issueIds.ToList();

        using var context = _contextFactory();
        var issues = context.Issues.Include(i => i.Series).Where(i => _issueIds.Contains(i.Id)).ToList();
        if (issues.Count == 0)
        {
            return;
        }

        string seriesName = issues[0].Series?.Name ?? "Unknown Series";
        HeaderLabel = $"Editing {issues.Count} issues in {seriesName}";

        MainFields.Clear();
        ArtistFields.Clear();
        PlotNotesFields.Clear();

        foreach (var descriptor in BulkFieldRegistry.All)
        {
            var field = new BulkFieldViewModel(descriptor);

            if (descriptor.IsListField)
            {
                var tokenSets = issues.Select(i => ListFieldTokens.Parse(descriptor.Get(i))).ToList();
                var intersection = tokenSets.Aggregate((a, b) => new HashSet<string>(a.Intersect(b, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase));
                field.OriginalTokens = intersection;
                field.Value = ListFieldTokens.Join(intersection);
            }
            else
            {
                var values = issues.Select(descriptor.Get).ToList();
                bool allSame = values.All(v => string.Equals(v, values[0], StringComparison.Ordinal));
                field.Value = allSame ? (values[0] ?? string.Empty) : string.Empty;
            }

            // Value assignment above auto-stages via BulkFieldViewModel.OnValueChanged - that's the
            // correct behavior for a user edit, but this is programmatic Load population, not one.
            field.IsStaged = false;

            var target = descriptor.Group switch
            {
                BulkFieldRegistry.Main => MainFields,
                BulkFieldRegistry.Artists => ArtistFields,
                _ => PlotNotesFields,
            };
            target.Add(field);
        }
    }

    [RelayCommand]
    private void Save()
    {
        using var context = _contextFactory();
        var issues = context.Issues.Where(i => _issueIds.Contains(i.Id)).ToList();

        foreach (var field in AllFields.Where(f => f.IsStaged))
        {
            if (field.Descriptor.IsListField)
            {
                var currentTokens = ListFieldTokens.Parse(field.Value);
                var added = new HashSet<string>(currentTokens.Except(field.OriginalTokens, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
                var removed = new HashSet<string>(field.OriginalTokens.Except(currentTokens, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

                foreach (var issue in issues)
                {
                    var issueTokens = ListFieldTokens.Parse(field.Descriptor.Get(issue));
                    issueTokens.ExceptWith(removed);
                    issueTokens.UnionWith(added);
                    field.Descriptor.Set(issue, ListFieldTokens.Join(issueTokens));
                }
            }
            else
            {
                foreach (var issue in issues)
                {
                    field.Descriptor.Set(issue, field.Value);
                }
            }
        }

        context.SaveChanges();
        _goBack();
    }

    [RelayCommand]
    private void Cancel() => _goBack();
}
