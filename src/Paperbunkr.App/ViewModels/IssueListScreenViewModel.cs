using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Comic List (docs/superpowers/specs/2026-08-18-issue-list-pluggable-sort-group-design.md) -
/// a flat, cross-series, sortable/groupable list of individual <see cref="Issue"/>s, the per-issue
/// counterpart to Library's series-card grid (CE's real "Comic List" Detail view mode). Owned as a
/// child object by <see cref="LibraryScreenViewModel"/> and rendered as one of its view modes
/// rather than a standalone screen with its own query, so it shares Library's filter pipeline
/// (search/content-type/category/unread/missing/tracked) instead of running an unfiltered query -
/// see the merge rationale in the spec's follow-up note. Library calls <see cref="SetRows"/>
/// whenever its filtered set changes; this VM caches those source issues and re-sorts/regroups
/// them locally when the sort/group field changes, so a sort tweak doesn't need to round-trip
/// back to Library's own query.
/// </summary>
public partial class IssueListScreenViewModel : ViewModelBase
{
    private readonly Action<int> _goReaderForIssue;
    private readonly Func<int, bool> _isSelected;
    private List<Issue> _sourceIssues = new();
    private IReadOnlyList<VirtualTagDefinition> _availableVirtualTagDefinitions = new List<VirtualTagDefinition>();

    /// <summary>
    /// <paramref name="isSelected"/> (docs/superpowers/specs/2026-08-24-library-multiselect-slice1-
    /// design.md §2/§7) lets this view model re-stamp each freshly-built <see cref="IssueListRow"/>'s
    /// <see cref="IssueListRow.IsSelected"/> from the owning <c>LibraryScreenViewModel</c>'s live
    /// selection set. Necessary because <see cref="Render"/> discards and rebuilds every row on every
    /// sort/group/filter change - without this, changing the sort field while a selection is active
    /// would silently wipe it, since the old row instances (and their <c>IsSelected</c> flags) get
    /// thrown away.
    /// </summary>
    public IssueListScreenViewModel(Action<int> goReaderForIssue, Func<int, bool>? isSelected = null)
    {
        _goReaderForIssue = goReaderForIssue;
        _isSelected = isSelected ?? (_ => false);
        Rows = new ObservableCollection<IssueListRow>();
        Groups = new ObservableCollection<IssueListRowGroup>();
        FlatRows = new ObservableCollection<object>();
    }

    public ObservableCollection<IssueListRow> Rows { get; }
    public ObservableCollection<IssueListRowGroup> Groups { get; }

    /// <summary>
    /// Flattened view adapter over <see cref="Rows"/> / <see cref="Groups"/> for the virtualized
    /// List / Details modes: ungrouped it is just the rows; grouped it interleaves a
    /// <see cref="GridSectionHeader"/> before each section's <see cref="IssueListRow"/>s. One
    /// <c>ListBox</c> + <c>VirtualizingStackPanel</c> binds here for both cases. Rebuilt every
    /// <see cref="Render"/>; holds the same row instances as <see cref="Rows"/>/<see cref="Groups"/>
    /// so selection re-stamping already applies.
    /// </summary>
    public ObservableCollection<object> FlatRows { get; }

    public static IReadOnlyList<IssueListSortFieldDescriptor> SortFieldOptions => IssueListFieldCatalog.SortFields.Values.ToList();
    public static IReadOnlyList<IssueListGroupFieldDescriptor> GroupFieldOptions => IssueListFieldCatalog.GroupFields.Values.ToList();

    /// <summary>Enabled Virtual Tags, for the toolbar's dynamic per-tag sort/group entries
    /// (docs/superpowers/specs/2026-09-12-library-sort-group-axes-design.md §1) - populated by
    /// <see cref="SetVirtualTags"/>, called from <c>LibraryScreenViewModel</c> alongside <see cref="SetRows"/>.</summary>
    public IReadOnlyList<VirtualTagOption> AvailableVirtualTags =>
        _availableVirtualTagDefinitions.Select(t => new VirtualTagOption(t.Id, t.Name)).ToList();

    /// <summary>Gates the toolbar's Virtual Tag sort/group sections - hidden entirely for a library
    /// with none enabled, rather than showing an empty section.</summary>
    public bool HasVirtualTags => _availableVirtualTagDefinitions.Count > 0;

    [ObservableProperty]
    private IssueListSortField _sortField = IssueListSortField.Added;

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Descending;

    [ObservableProperty]
    private IssueListGroupField _groupField = IssueListGroupField.None;

    /// <summary>Meaningful only when <see cref="SortField"/> is <see cref="IssueListSortField.VirtualTag"/>.</summary>
    [ObservableProperty]
    private int? _sortVirtualTagId;

    /// <summary>Meaningful only when <see cref="GroupField"/> is <see cref="IssueListGroupField.VirtualTag"/>.</summary>
    [ObservableProperty]
    private int? _groupVirtualTagId;

    public bool IsGrouped => GroupField != IssueListGroupField.None;

    private VirtualTagDefinition? ResolveVirtualTag(int? id) =>
        id is int tagId ? _availableVirtualTagDefinitions.FirstOrDefault(t => t.Id == tagId) : null;

    public string SortFieldLabel =>
        SortField == IssueListSortField.VirtualTag
            ? ResolveVirtualTag(SortVirtualTagId)?.Name ?? "Virtual Tag"
            : IssueListFieldCatalog.SortFields.TryGetValue(SortField, out var d) ? d.DisplayName : SortField.ToString();

    public string GroupFieldLabel =>
        GroupField == IssueListGroupField.None ? "None"
        : GroupField == IssueListGroupField.VirtualTag ? ResolveVirtualTag(GroupVirtualTagId)?.Name ?? "Virtual Tag"
        : IssueListFieldCatalog.GroupFields[GroupField].DisplayName;

    /// <summary>
    /// Null unless <see cref="SortField"/> is actually <see cref="IssueListSortField.VirtualTag"/> -
    /// collapses the compound "field is VirtualTag AND id matches" check the toolbar's dynamic
    /// per-tag buttons need down to a single nullable-int comparison, so their
    /// <c>Classes.active</c> binding can reuse the existing single-value
    /// <see cref="Avalonia.Data.Converters.ObjectConverters.Equal"/> pattern every other sort/group
    /// option button already uses, instead of a new <c>MultiBinding</c>/converter.
    /// </summary>
    public int? ActiveVirtualTagSortId => SortField == IssueListSortField.VirtualTag ? SortVirtualTagId : null;

    /// <summary>Group equivalent of <see cref="ActiveVirtualTagSortId"/>.</summary>
    public int? ActiveVirtualTagGroupId => GroupField == IssueListGroupField.VirtualTag ? GroupVirtualTagId : null;

    /// <summary>Toolbar pill text - <see cref="SortFieldLabel"/> plus a direction glyph, since
    /// Library's Sort pill (the only Sort control now that every Display mode is per-issue) shows
    /// both in one string.</summary>
    public string SortLabelWithDirection => SortFieldLabel + (SortDirection == SortDirection.Ascending ? " ↑" : " ↓");

    /// <summary>Library's "no results" empty-state check now reads this instead of its own
    /// series-card collections, since every mode renders <see cref="Rows"/>/<see cref="Groups"/>.</summary>
    public bool HasAnyResults => Rows.Count > 0 || Groups.Count > 0;

    /// <summary>Called by <see cref="LibraryScreenViewModel"/> whenever its filtered issue set
    /// changes (search/content-type/category/unread/missing/tracked). Caches the source issues
    /// and re-renders; sort/group field changes re-render from this cache without needing a
    /// fresh call from Library.</summary>
    public void SetRows(IEnumerable<Issue> issues)
    {
        _sourceIssues = issues.Where(i => i.Series != null).ToList();
        Render();
    }

    /// <summary>Called by <see cref="LibraryScreenViewModel"/> whenever the enabled Virtual Tag set
    /// changes (mirrors <see cref="SetRows"/>'s call shape). Re-renders since a changed tag's
    /// <c>CaptionFormat</c> or enabled-state can change previously-cached row values.</summary>
    public void SetVirtualTags(IReadOnlyList<VirtualTagDefinition> tags)
    {
        _availableVirtualTagDefinitions = tags;
        OnPropertyChanged(nameof(AvailableVirtualTags));
        OnPropertyChanged(nameof(HasVirtualTags));
        Reload();
    }

    /// <summary>
    /// Sets sort field + direction + group in one shot <b>without rendering</b> - the caller is
    /// expected to trigger a single render immediately afterwards (e.g. via <see cref="SetRows"/>).
    /// Used by workspace apply (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md)
    /// so switching a preset doesn't re-render a large list three times back to back before the
    /// real reload.
    /// </summary>
    public void ConfigureSortGroup(IssueListSortField sortField, SortDirection sortDirection, IssueListGroupField groupField)
    {
        _suppressRender = true;
        try
        {
            SortField = sortField;
            SortDirection = sortDirection;
            GroupField = groupField;
        }
        finally
        {
            _suppressRender = false;
        }
    }

    private bool _suppressRender;

    private void Render()
    {
        if (_suppressRender)
        {
            return;
        }

        var rows = SortRows(_sourceIssues.Select(ToRow).ToList());

        Rows.Clear();
        Groups.Clear();
        FlatRows.Clear();
        if (IsGrouped)
        {
            foreach (var group in GroupRows(rows))
            {
                Groups.Add(group);
                FlatRows.Add(new GridSectionHeader(group.Header, group.Items.Count));
                foreach (var row in group.Items)
                {
                    FlatRows.Add(row);
                }
            }
        }
        else
        {
            foreach (var row in rows)
            {
                Rows.Add(row);
                FlatRows.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasAnyResults));
    }

    // Cover is resolved lazily via CoverImageConverter, keyed on Id, only when this row's container
    // is actually realized - not decoded here regardless of visibility (docs/superpowers/specs/
    // 2026-08-22-cover-memory-virtualization-design.md). The projection itself lives on
    // IssueListRow.FromIssue so per-series cards' representative rows build from the same code.
    private IssueListRow ToRow(Issue issue) => IssueListRow.FromIssue(issue, issue.Series!, _isSelected, _availableVirtualTagDefinitions);

    /// <summary>Resolves <see cref="SortField"/>/<see cref="GroupField"/> to a descriptor, handling
    /// the dynamic <see cref="IssueListSortField.VirtualTag"/>/<see cref="IssueListGroupField.VirtualTag"/>
    /// case (not in the static catalog dictionaries - built on the fly from the selected tag id) and
    /// falling back to a sane default when a selected tag id no longer resolves (e.g. the tag was
    /// deleted after being selected).</summary>
    private IssueListSortFieldDescriptor ResolveSortDescriptor()
    {
        if (SortField == IssueListSortField.VirtualTag)
        {
            var tag = ResolveVirtualTag(SortVirtualTagId);
            if (tag is not null)
            {
                return IssueListFieldCatalog.BuildVirtualTagSortDescriptor(tag);
            }
        }
        else if (IssueListFieldCatalog.SortFields.TryGetValue(SortField, out var found))
        {
            return found;
        }

        return IssueListFieldCatalog.SortFields[IssueListSortField.Added];
    }

    private IssueListGroupFieldDescriptor? ResolveGroupDescriptor()
    {
        if (GroupField == IssueListGroupField.VirtualTag)
        {
            var tag = ResolveVirtualTag(GroupVirtualTagId);
            return tag is not null ? IssueListFieldCatalog.BuildVirtualTagGroupDescriptor(tag) : null;
        }

        return IssueListFieldCatalog.GroupFields.TryGetValue(GroupField, out var descriptor) ? descriptor : null;
    }

    private List<IssueListRow> SortRows(List<IssueListRow> rows)
    {
        var descriptor = ResolveSortDescriptor();

        var result = rows.ToList();
        result.Sort(descriptor.Compare);
        if (SortDirection == SortDirection.Descending)
        {
            result.Reverse();
        }

        return result;
    }

    private IEnumerable<IssueListRowGroup> GroupRows(List<IssueListRow> rows)
    {
        var descriptor = ResolveGroupDescriptor();
        if (descriptor is null)
        {
            return Enumerable.Empty<IssueListRowGroup>();
        }

        return rows
            .GroupBy(descriptor.GroupKey)
            .OrderBy(g => g.Key, Comparer<string>.Create(descriptor.GroupOrder))
            .Select(g => new IssueListRowGroup { Header = g.Key, Items = new ObservableCollection<IssueListRow>(g) });
    }

    partial void OnSortFieldChanged(IssueListSortField value)
    {
        OnPropertyChanged(nameof(SortFieldLabel));
        OnPropertyChanged(nameof(SortLabelWithDirection));
        OnPropertyChanged(nameof(ActiveVirtualTagSortId));
        Reload();
    }

    partial void OnSortDirectionChanged(SortDirection value)
    {
        OnPropertyChanged(nameof(SortLabelWithDirection));
        Reload();
    }

    partial void OnGroupFieldChanged(IssueListGroupField value)
    {
        OnPropertyChanged(nameof(IsGrouped));
        OnPropertyChanged(nameof(GroupFieldLabel));
        OnPropertyChanged(nameof(ActiveVirtualTagGroupId));
        Reload();
    }

    partial void OnSortVirtualTagIdChanged(int? value)
    {
        OnPropertyChanged(nameof(SortFieldLabel));
        OnPropertyChanged(nameof(SortLabelWithDirection));
        OnPropertyChanged(nameof(ActiveVirtualTagSortId));
        Reload();
    }

    partial void OnGroupVirtualTagIdChanged(int? value)
    {
        OnPropertyChanged(nameof(GroupFieldLabel));
        OnPropertyChanged(nameof(ActiveVirtualTagGroupId));
        Reload();
    }

    private void Reload() => Render();

    [RelayCommand]
    private void ToggleSortDirection() =>
        SortDirection = SortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;

    [RelayCommand]
    private void SetSortField(IssueListSortField field) => SortField = field;

    [RelayCommand]
    private void SetGroupField(IssueListGroupField field) => GroupField = field;

    /// <summary>Sets both <see cref="SortField"/> (to <see cref="IssueListSortField.VirtualTag"/>)
    /// and <see cref="SortVirtualTagId"/> in one shot - suppressing <see cref="Render"/> in between
    /// so setting the pair doesn't render twice, same rationale as <see cref="ConfigureSortGroup"/>.</summary>
    [RelayCommand]
    private void SetVirtualTagSortField(VirtualTagOption tag)
    {
        _suppressRender = true;
        try
        {
            SortVirtualTagId = tag.Id;
            SortField = IssueListSortField.VirtualTag;
        }
        finally
        {
            _suppressRender = false;
        }

        OnPropertyChanged(nameof(SortFieldLabel));
        OnPropertyChanged(nameof(SortLabelWithDirection));
        Reload();
    }

    /// <summary>See <see cref="SetVirtualTagSortField"/>.</summary>
    [RelayCommand]
    private void SetVirtualTagGroupField(VirtualTagOption tag)
    {
        _suppressRender = true;
        try
        {
            GroupVirtualTagId = tag.Id;
            GroupField = IssueListGroupField.VirtualTag;
        }
        finally
        {
            _suppressRender = false;
        }

        OnPropertyChanged(nameof(IsGrouped));
        OnPropertyChanged(nameof(GroupFieldLabel));
        Reload();
    }

    [RelayCommand]
    private void OpenIssue(IssueListRow? row)
    {
        if (row is not null)
        {
            _goReaderForIssue(row.Id);
        }
    }
}
