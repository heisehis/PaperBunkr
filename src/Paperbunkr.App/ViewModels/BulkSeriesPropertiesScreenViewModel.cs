using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Bulk multi-series properties editor (docs/superpowers/specs/2026-08-24-library-multiselect-
/// slice3-design.md), reached from Library's series-card selection action bar. Same edit-buffer
/// Save/Cancel discipline as <see cref="BulkIssuePropertiesScreenViewModel"/> - Cancel never touches
/// the database - but over <see cref="SeriesBulkFieldRegistry.All"/> instead. Deliberately not
/// sharing that type's undo/redo integration: <c>MetadataEditHistoryService</c> is Issue-snapshot-
/// shaped, and extending it to Series is real additional scope not requested for this slice.
/// </summary>
public partial class BulkSeriesPropertiesScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<int>? _enqueueMetadataWriteBack;
    private List<int> _seriesIds = new();

    public BulkSeriesPropertiesScreenViewModel(Action goBack, Action<int>? enqueueMetadataWriteBack = null)
        : this(goBack, PaperbunkrDb.CreateContext, enqueueMetadataWriteBack)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal BulkSeriesPropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory, Action<int>? enqueueMetadataWriteBack = null)
    {
        _goBack = goBack;
        _contextFactory = contextFactory;
        _enqueueMetadataWriteBack = enqueueMetadataWriteBack;
    }

    public ObservableCollection<SeriesBulkFieldViewModel> Fields { get; } = new();

    /// <summary>Reuses the per-field <see cref="SeriesBulkFieldViewModel.IsStaged"/> flag as the
    /// unsaved-changes signal, same precedent as <see cref="BulkIssuePropertiesScreenViewModel.HasUnsavedChanges"/>.</summary>
    public bool HasUnsavedChanges() => Fields.Any(f => f.IsStaged);

    [ObservableProperty]
    private string _headerLabel = string.Empty;

    public void Load(IReadOnlyList<int> seriesIds)
    {
        _seriesIds = seriesIds.ToList();

        using var context = _contextFactory();
        var series = context.Series.Where(s => _seriesIds.Contains(s.Id)).ToList();
        if (series.Count == 0)
        {
            return;
        }

        HeaderLabel = series.Count == 1 ? $"Editing {series[0].Name}" : $"Editing {series.Count} series";

        Fields.Clear();
        foreach (var descriptor in SeriesBulkFieldRegistry.All)
        {
            var field = new SeriesBulkFieldViewModel(descriptor);
            var values = series.Select(descriptor.Get).ToList();
            bool allSame = values.All(v => string.Equals(v, values[0], StringComparison.Ordinal));
            field.Value = allSame ? (values[0] ?? string.Empty) : string.Empty;

            // Value assignment above auto-stages via OnValueChanged - correct for a user edit, not
            // for this programmatic Load population.
            field.IsStaged = false;
            Fields.Add(field);
        }
    }

    [RelayCommand]
    private void Save()
    {
        using var context = _contextFactory();
        var series = context.Series.Where(s => _seriesIds.Contains(s.Id)).ToList();
        var stagedFields = Fields.Where(f => f.IsStaged).ToList();

        foreach (var field in stagedFields)
        {
            foreach (var s in series)
            {
                field.Descriptor.Set(s, field.Value);
            }
        }

        context.SaveChanges();

        // File metadata write-back (docs/superpowers/specs/2026-09-03-file-metadata-write-back-
        // design.md): only Content Type / Reading Mode reach a member issue's ComicInfo (<Manga>);
        // Publisher/Genre/Summary here are Series-level and IssueToComicInfoMapper never reads them.
        // Deliberately fans out to every member issue - a series edit can be many file writes, which
        // the write-back queue serialises into one pass + one toast.
        if (_enqueueMetadataWriteBack is not null
            && stagedFields.Any(f => f.Descriptor.Label is "Content Type" or "Reading Mode"))
        {
            foreach (int issueId in context.Issues.Where(i => _seriesIds.Contains(i.SeriesId)).Select(i => i.Id).ToList())
            {
                _enqueueMetadataWriteBack(issueId);
            }
        }

        // Matches IssuePropertiesScreenViewModel/BulkIssuePropertiesScreenViewModel's dirty reset -
        // without this, HasUnsavedChanges() would still report true immediately post-Save, until the
        // next Load() rebuilds the field list from scratch.
        foreach (var field in stagedFields)
        {
            field.IsStaged = false;
        }

        _goBack();
    }

    [RelayCommand]
    private void Cancel() => _goBack();
}
