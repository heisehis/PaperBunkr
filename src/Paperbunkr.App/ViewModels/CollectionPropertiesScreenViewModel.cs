using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Collection properties overlay (docs/superpowers/specs/2026-08-27-collections-design.md, step 8) -
/// same modal-popup-over-MainWindow pattern as <see cref="ReadingListPropertiesScreenViewModel"/>
/// (cloned from it): buffered Load/edit/Save/Cancel, a two-ctor test seam. Fields: Name,
/// Description, an accent-colour swatch picker, Auto-vs-manual cover, and a reorderable/removable
/// member list. No per-row cover thumbnail here (unlike Reading Lists' issue rows) - members are
/// three different entity kinds (Series/Issue/Book) and there's no single "resolve any of them to a
/// Bitmap" utility in the App layer yet; adding one is out of scope for this pass.
/// </summary>
public partial class CollectionPropertiesScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private int? _collectionId;

    /// <summary>Buffered until Save, same reasoning as the reading-list overlay's own field.</summary>
    private string? _pendingCoverImagePath;
    private readonly List<int> _removedItemIds = new();

    public CollectionPropertiesScreenViewModel(Action goBack) : this(goBack, PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal CollectionPropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory)
    {
        _goBack = goBack;
        _contextFactory = contextFactory;
    }

    /// <summary>A small fixed palette rather than a full colour picker - matches this app's existing
    /// "swatch buttons" idiom elsewhere (tag/role chips) rather than introducing a new control.</summary>
    public static string[] AccentSwatches { get; } = { "#C9803F", "#5FA889", "#D7AC4C", "#7C93C9", "#C97C9E", "#8F7CC9" };

    [ObservableProperty]
    private string _headerLabel = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>Hex string or empty (no accent). Empty renders as the generic accent via
    /// <see cref="Paperbunkr.App.Views.AccentColorToBrushConverter"/>.</summary>
    [ObservableProperty]
    private string _accentColor = string.Empty;

    [ObservableProperty]
    private bool _isAutoCover = true;

    [ObservableProperty]
    private string _coverImagePath = string.Empty;

    public ObservableCollection<CollectionMemberRowViewModel> Members { get; } = new();

    public void Load(int collectionId)
    {
        _collectionId = collectionId;
        _pendingCoverImagePath = null;
        _removedItemIds.Clear();

        using var context = _contextFactory();
        var collection = context.Collections.Find(collectionId);
        if (collection is null)
        {
            return;
        }

        HeaderLabel = $"Edit \"{collection.Name}\"";
        Name = collection.Name;
        Description = collection.Description ?? string.Empty;
        AccentColor = collection.AccentColor ?? string.Empty;
        IsAutoCover = collection.IsAutoCover;
        CoverImagePath = collection.CoverImagePath ?? string.Empty;

        Members.Clear();
        foreach (var member in CollectionResolver.GetMembers(context, collectionId))
        {
            Members.Add(new CollectionMemberRowViewModel(member, RemoveMemberRow));
        }

        RefreshRelatedCollections(context, collectionId);
    }

    [RelayCommand]
    private void SetAccentColor(string hex) => AccentColor = hex;

    [RelayCommand]
    private void ClearAccentColor() => AccentColor = string.Empty;

    [RelayCommand]
    private async System.Threading.Tasks.Task ChangeCoverAsync()
    {
        string? path = await new FilePickerService().PickImageFileAsync("Choose Cover Image");
        if (path is null)
        {
            return;
        }

        _pendingCoverImagePath = path;
        CoverImagePath = path;
        IsAutoCover = false;
    }

    [RelayCommand]
    private void UseAutoCover()
    {
        IsAutoCover = true;
        CoverImagePath = string.Empty;
        _pendingCoverImagePath = null;
    }

    private void RemoveMemberRow(CollectionMemberRowViewModel row)
    {
        Members.Remove(row);
        _removedItemIds.Add(row.CollectionItemId);
    }

    [RelayCommand]
    private void MoveMemberUp(CollectionMemberRowViewModel? row) => MoveMember(row, offset: -1);

    [RelayCommand]
    private void MoveMemberDown(CollectionMemberRowViewModel? row) => MoveMember(row, offset: 1);

    private void MoveMember(CollectionMemberRowViewModel? row, int offset)
    {
        if (row is null)
        {
            return;
        }

        int index = Members.IndexOf(row);
        int newIndex = index + offset;
        if (index < 0 || newIndex < 0 || newIndex >= Members.Count)
        {
            return;
        }

        Members.Move(index, newIndex);
    }

    [RelayCommand]
    private void Save()
    {
        if (_collectionId is not int collectionId)
        {
            return;
        }

        using var context = _contextFactory();
        if (context.Collections.Find(collectionId) is null)
        {
            _goBack();
            return;
        }

        CollectionService.Rename(context, collectionId, Name);
        CollectionService.SetAppearance(
            context,
            collectionId,
            description: NullIfEmpty(Description),
            accentColor: NullIfEmpty(AccentColor),
            coverImagePath: IsAutoCover ? null : (_pendingCoverImagePath ?? NullIfEmpty(CoverImagePath)),
            isAutoCover: IsAutoCover);

        foreach (int itemId in _removedItemIds)
        {
            CollectionService.RemoveItem(context, itemId);
        }

        if (Members.Count > 0)
        {
            CollectionService.ReorderItems(context, collectionId, Members.Select(m => m.CollectionItemId).ToList());
        }

        _goBack();
    }

    [RelayCommand]
    private void Cancel() => _goBack();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // --- Related Collections (Collection-to-Collection relations, e.g. "these two collections are
    // the same fictional universe") - byte-for-byte DetailTabsViewModel's MediaRelation add/remove
    // section, just against CollectionRelationResolver/context.Collections instead of
    // MediaRelationResolver/context.Series. Immediate-write like that section (not part of the
    // buffered Save/Cancel flow above), same rationale: a relation isn't a property of *this*
    // collection's own row, it's a fact about two collections that should persist the moment it's
    // asserted, same as MediaRelation never waited for a Detail-page "Save" either. ---

    public ObservableCollection<RelatedCollectionChip> RelatedCollections { get; } = new();

    private void RefreshRelatedCollections(PaperbunkrDbContext context, int collectionId)
    {
        RelatedCollections.Clear();
        foreach (var (other, displayType, relationId) in CollectionRelationResolver.GetRelatedCollections(context, collectionId))
        {
            RelatedCollections.Add(new RelatedCollectionChip
            {
                CollectionRelationId = relationId,
                CollectionId = other.Id,
                Name = other.Name,
                RelationTypeLabel = RelationTypeOption.FormatLabel(displayType),
            });
        }
    }

    public static IReadOnlyList<RelationTypeOption> RelationTypeOptions => RelationTypeOption.All;

    [ObservableProperty]
    private bool _isAddingRelation;

    [ObservableProperty]
    private string _relationSearchQuery = string.Empty;

    [ObservableProperty]
    private RelationType _selectedRelationType = RelationType.Related;

    /// <summary>Bound to the ComboBox's SelectedItem, not SelectedValue/SelectedValueBinding - see
    /// DetailTabsViewModel.SelectedRelationTypeOption's own doc comment for why (a real, permanent
    /// XAML binding-resolution bug in this Avalonia version, not tooling noise).</summary>
    [ObservableProperty]
    private RelationTypeOption _selectedRelationTypeOption = RelationTypeOptions.First(o => o.Type == RelationType.Related);

    partial void OnSelectedRelationTypeOptionChanged(RelationTypeOption value) => SelectedRelationType = value.Type;

    public ObservableCollection<CollectionSearchResult> RelationSearchResults { get; } = new();

    [RelayCommand]
    private void ToggleAddRelation()
    {
        IsAddingRelation = !IsAddingRelation;
        RelationSearchQuery = string.Empty;
        RelationSearchResults.Clear();
    }

    partial void OnRelationSearchQueryChanged(string value) => SearchRelationCandidates();

    /// <summary>No "create new" row here (unlike the Add-to-Collection picker's <c>CollectionSearchResult.IsNew</c>) -
    /// a relation always names an existing collection on the other end, same as the Series relation
    /// picker only ever searches real series.</summary>
    [RelayCommand]
    private void SearchRelationCandidates()
    {
        RelationSearchResults.Clear();
        if (string.IsNullOrWhiteSpace(RelationSearchQuery) || _collectionId is not int currentCollectionId)
        {
            return;
        }

        using var context = _contextFactory();
        var matches = context.Collections
            .Where(c => c.Id != currentCollectionId)
            .AsEnumerable()
            .Where(c => c.Name.Contains(RelationSearchQuery, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var collection in matches)
        {
            RelationSearchResults.Add(new CollectionSearchResult { CollectionId = collection.Id, Name = collection.Name });
        }
    }

    [RelayCommand]
    private void AddRelation(CollectionSearchResult? target)
    {
        if (target is null || _collectionId is not int currentCollectionId)
        {
            return;
        }

        using var context = _contextFactory();
        CollectionRelationResolver.TryCreate(context, currentCollectionId, target.CollectionId, SelectedRelationType);

        IsAddingRelation = false;
        RelationSearchQuery = string.Empty;
        RelationSearchResults.Clear();
        RefreshRelatedCollections(context, currentCollectionId);
    }

    [RelayCommand]
    private void RemoveRelation(RelatedCollectionChip? chip)
    {
        if (chip is null || _collectionId is not int currentCollectionId)
        {
            return;
        }

        using var context = _contextFactory();
        CollectionRelationResolver.Remove(context, chip.CollectionRelationId);
        RefreshRelatedCollections(context, currentCollectionId);
    }
}
