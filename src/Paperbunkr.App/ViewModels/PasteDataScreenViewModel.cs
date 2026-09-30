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

/// <summary>One checkbox of the Paste Data dialog.</summary>
public sealed partial class PasteDataFieldOption : ObservableObject
{
    public required string Label { get; init; }

    /// <summary>The copied book has a value here - shown bold, and what "Mark defined" ticks (CE's <c>IsDefaultValue</c> check).</summary>
    public required bool HasValue { get; init; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>A heading and its checkboxes (the bulk editor's own field groups).</summary>
public sealed record PasteDataFieldGroup(string Name, IReadOnlyList<PasteDataFieldOption> Fields);

/// <summary>
/// The Paste Data dialog (CE's <c>ComicDataPasteDialog</c>; docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §3): one
/// checkbox per book-owned field of the copied data, bold where the copy has a value, Mark defined / All / None, and the ticks remembered in
/// <see cref="Paperbunkr.Data.Entities.AppSettings.PasteDataFields"/> (first use: none ticked, CE's default). The paste itself runs through
/// the callback, which owns the undo step and the write-back.
/// </summary>
public sealed partial class PasteDataScreenViewModel : ViewModelBase
{
    private readonly Action _close;
    private readonly Action<IReadOnlyList<int>, IReadOnlyDictionary<string, string?>> _apply;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private IReadOnlyList<int> _targetIds = Array.Empty<int>();
    private MetadataClipboardContent? _content;

    public PasteDataScreenViewModel(Action close, Action<IReadOnlyList<int>, IReadOnlyDictionary<string, string?>> apply)
        : this(close, apply, PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test seam - production uses the real per-user database.</summary>
    internal PasteDataScreenViewModel(Action close, Action<IReadOnlyList<int>, IReadOnlyDictionary<string, string?>> apply, Func<PaperbunkrDbContext> contextFactory)
    {
        _close = close;
        _apply = apply;
        _contextFactory = contextFactory;
    }

    public ObservableCollection<PasteDataFieldGroup> Groups { get; } = new();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    public int CheckedCount => AllFields.Count(f => f.IsChecked);

    public string PasteLabel => CheckedCount == 1 ? "Paste 1 field" : $"Paste {CheckedCount} fields";

    private IEnumerable<PasteDataFieldOption> AllFields => Groups.SelectMany(g => g.Fields);

    public void Load(IReadOnlyList<int> targetIds, MetadataClipboardContent content)
    {
        _targetIds = targetIds;
        _content = content;
        Title = targetIds.Count == 1 ? "Paste data onto 1 book" : $"Paste data onto {targetIds.Count} books";
        Subtitle = $"From {content.SourceCaption}. Bold fields have a value in the copied book. Ticked fields are overwritten, and an empty value clears the field.";

        HashSet<string> remembered;
        using (var context = _contextFactory())
        {
            remembered = ParseFields(context.GetOrCreateAppSettings().PasteDataFields);
        }

        foreach (var field in AllFields)
        {
            field.PropertyChanged -= OnFieldChanged;
        }

        Groups.Clear();
        foreach (var group in BulkFieldRegistry.IssueOwned.GroupBy(f => f.Group))
        {
            var options = group.Select(f => new PasteDataFieldOption
            {
                Label = f.Label,
                HasValue = content.Fields.TryGetValue(f.Label, out var v) && !string.IsNullOrWhiteSpace(v),
                IsChecked = remembered.Contains(f.Label),
            }).ToList();
            foreach (var option in options)
            {
                option.PropertyChanged += OnFieldChanged;
            }

            Groups.Add(new PasteDataFieldGroup(group.Key, options));
        }

        RaiseCounts();
    }

    private void OnFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PasteDataFieldOption.IsChecked))
        {
            RaiseCounts();
        }
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(PasteLabel));
        PasteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void MarkDefined()
    {
        foreach (var field in AllFields)
        {
            field.IsChecked = field.HasValue;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var field in AllFields)
        {
            field.IsChecked = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var field in AllFields)
        {
            field.IsChecked = false;
        }
    }

    private bool CanPaste() => CheckedCount > 0 && _content is not null;

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (_content is null)
        {
            return;
        }

        var ticked = AllFields.Where(f => f.IsChecked).Select(f => f.Label).ToList();
        using (var context = _contextFactory())
        {
            context.GetOrCreateAppSettings().PasteDataFields = string.Join(",", ticked);
            context.SaveChanges();
        }

        var values = ticked.ToDictionary(label => label, label => _content.Fields.GetValueOrDefault(label));
        var ids = _targetIds;

        // Deferred: this runs from the dialog's own button, which the close detaches (CLAUDE.md's detach-while-routing rule).
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _close();
            _apply(ids, values);
        });
    }

    [RelayCommand]
    private void Cancel() => Avalonia.Threading.Dispatcher.UIThread.Post(_close);

    private static HashSet<string> ParseFields(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? new HashSet<string>()
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
}
