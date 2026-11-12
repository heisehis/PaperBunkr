using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>How the Sort menu of the Edit Collection dialog reorders the manual members.</summary>
public enum CollectionMemberSort
{
    TitleAscending,
    TitleDescending,
    Type,
}

/// <summary>
/// Collection properties overlay (docs/superpowers/specs/2026-08-27-collections-design.md, step 8) -
/// same modal-popup-over-MainWindow pattern as <see cref="ReadingListPropertiesScreenViewModel"/>
/// (cloned from it): buffered Load/edit/Save/Cancel, a two-ctor test seam. Fields: Name,
/// Description, an accent-colour swatch picker, Auto-vs-manual cover, and a reorderable/removable
/// member list. Rows carry a lazily resolved cover (<see cref="CollectionMemberRowViewModel.Tile"/>, via
/// <c>LibraryTile.FromMember</c>); drag, Sort and a live preview were added by
/// docs/superpowers/specs/2026-11-06-collection-dialog-polish-design.md.
/// </summary>
public partial class CollectionPropertiesScreenViewModel : ViewModelBase, IContextMenuProvider
{
    /// <summary>Right-click/Menu-key menu for member rows (docs/superpowers/specs/2026-08-31-
    /// keyboard-operability-design.md) - a new menu, this screen had none before.</summary>
    IReadOnlyList<ContextMenuEntry>? IContextMenuProvider.BuildContextMenu(object? target) =>
        new CollectionMemberContextMenuBuilder().Build(target);

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
        Members.CollectionChanged += OnMembersChanged;
        Members.CollectionChanged += (_, _) => OnPropertyChanged(nameof(MembersSummary));
    }

    /// <summary>The preset palette; a trailing "+" swatch in the dialog adds a custom colour (<see cref="ApplyCustomAccentCommand"/>).</summary>
    public static string[] AccentSwatches { get; } = { "#C9803F", "#5FA889", "#D7AC4C", "#7C93C9", "#C97C9E", "#8F7CC9" };

    /// <summary>Soft description limit: the counter turns to the danger colour past it, Save is never blocked.</summary>
    public const int DescriptionSoftLimit = 500;

    /// <summary>The surface the accent is judged against for the advisory contrast hint. A seam so tests need no Application.</summary>
    public Func<Color> SurfaceColorProvider { get; set; } = ResolveSurfaceColor;

    private static Color ResolveSurfaceColor()
    {
        if (Avalonia.Application.Current?.TryGetResource("PbSurface2Brush", null, out var resource) == true
            && resource is ISolidColorBrush brush)
        {
            return brush.Color;
        }

        return Color.FromRgb(0x2A, 0x20, 0x30);
    }

    [ObservableProperty]
    private string _headerLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewName))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionCounter))]
    [NotifyPropertyChangedFor(nameof(DescriptionOverLimit))]
    private string _description = string.Empty;

    public string DescriptionCounter => $"{Description.Length} / {DescriptionSoftLimit}";

    public bool DescriptionOverLimit => Description.Length > DescriptionSoftLimit;

    /// <summary>Hex string or empty (no accent). Empty renders as the generic accent via
    /// <see cref="Paperbunkr.App.Views.AccentColorToBrushConverter"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomAccent))]
    private string _accentColor = string.Empty;

    partial void OnAccentColorChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            CustomAccentHex = value;
        }

        RefreshContrastWarning();
        RefreshSwatchSelection();
        OnPropertyChanged(nameof(PickerColor));
    }

    /// <summary>True when the chosen accent is not one of the presets - the dialog shows it as a seventh, selected swatch.</summary>
    public bool IsCustomAccent =>
        !string.IsNullOrEmpty(AccentColor)
        && !AccentSwatches.Contains(AccentColor, StringComparer.OrdinalIgnoreCase);

    /// <summary>The preset swatches with their selected state (the active one gets a ring).</summary>
    public ObservableCollection<AccentSwatchItem> SwatchItems { get; } =
        new(AccentSwatches.Select(hex => new AccentSwatchItem(hex)));

    private void RefreshSwatchSelection()
    {
        foreach (var item in SwatchItems)
        {
            item.IsSelected = string.Equals(item.Hex, AccentColor, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Two-way colour for the flyout's picker; writing it sets the (buffered) accent live.</summary>
    public Color PickerColor
    {
        get => AccentColorInput.TryParseHex(AccentColor, out var c) ? c : Color.FromRgb(0xC9, 0x80, 0x3F);
        set
        {
            string hex = AccentColorInput.ToHex(value);
            if (!string.Equals(hex, AccentColor, StringComparison.OrdinalIgnoreCase))
            {
                CustomAccentError = null;
                AccentColor = hex;
            }
        }
    }

    [ObservableProperty]
    private bool _isColorFlyoutOpen;

    [ObservableProperty]
    private bool _isSortMenuOpen;

    [RelayCommand]
    private void ToggleColorFlyout() => IsColorFlyoutOpen = !IsColorFlyoutOpen;

    [RelayCommand]
    private void ToggleSortMenu() => IsSortMenuOpen = !IsSortMenuOpen;

    /// <summary>The hex field of the custom-colour flyout.</summary>
    [ObservableProperty]
    private string _customAccentHex = string.Empty;

    [ObservableProperty]
    private string? _customAccentError;

    [ObservableProperty]
    private bool _hasAccentContrastWarning;

    public string AccentContrastWarningText => "Low contrast on this theme";

    /// <summary>Advisory only; never blocks Save.</summary>
    public void RefreshContrastWarning()
    {
        HasAccentContrastWarning =
            AccentColorInput.TryParseHex(AccentColor, out var accent)
            && AccentColorInput.IsLowContrast(accent, SurfaceColorProvider());
    }

    [RelayCommand]
    private void ApplyCustomAccent()
    {
        if (AccentColorInput.TryParseHex(CustomAccentHex, out var color))
        {
            CustomAccentError = null;
            AccentColor = AccentColorInput.ToHex(color);
        }
        else
        {
            CustomAccentError = "Enter a hex colour like #C9803F";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewCoverTile))]
    [NotifyPropertyChangedFor(nameof(PreviewManualCover))]
    private bool _isAutoCover = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewManualCover))]
    private string _coverImagePath = string.Empty;

    public ObservableCollection<CollectionMemberRowViewModel> Members { get; } = new();

    // --- Live preview (reads the buffered values, not the database) ---

    public string PreviewName => string.IsNullOrWhiteSpace(Name) ? "Untitled collection" : Name;

    public string PreviewCountText => Members.Count == 1 ? "1 item" : $"{Members.Count} items";

    /// <summary>The chosen cover member, else the first member in the *current* order, while the cover is automatic - reordering updates the preview.</summary>
    public LibraryTile? PreviewCoverTile => IsAutoCover ? (Members.FirstOrDefault(m => m.IsCoverMember) ?? Members.FirstOrDefault())?.Tile : null;

    // --- Identity (docs/superpowers/specs/2026-11-06-collection-identity-design.md): glyph, pin, cover style, chosen cover member. All buffered until Save. ---

    public IReadOnlyList<GlyphChoice> GlyphChoices { get; } = CollectionGlyphs.All.Select(o => new GlyphChoice(o)).ToList();

    public IReadOnlyList<CoverStyleChoice> CoverStyleChoices { get; } = new List<CoverStyleChoice>
    {
        new(CollectionCoverStyle.Auto, "Auto", "A 2×2 mosaic when there are enough covers, otherwise the first cover."),
        new(CollectionCoverStyle.Single, "Single", "One cover."),
        new(CollectionCoverStyle.Mosaic, "Mosaic", "A 2×2 collage of covers."),
        new(CollectionCoverStyle.Fan, "Fan", "Up to three overlapping covers."),
    };

    /// <summary>The stored icon (<see cref="CollectionIcon"/>: a symbol name or a <c>hero:</c>/<c>publisher:</c>/<c>file:</c> value), or empty for the accent dot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGlyph))]
    [NotifyPropertyChangedFor(nameof(HasIconValue))]
    private string _iconGlyph = string.Empty;

    /// <summary>True when the icon can be drawn; a value that no longer resolves (a deleted file) shows the dot.</summary>
    public bool HasGlyph => CollectionIconResolver.Resolve(IconGlyph) is not null;

    /// <summary>True when there is any stored value, even one that no longer draws, so "No icon" can always clear it.</summary>
    public bool HasIconValue => !string.IsNullOrEmpty(IconGlyph);

    /// <summary>The bundled hero icons (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md).</summary>
    public IReadOnlyList<GlyphChoice> HeroChoices { get; } = HeroGlyphs.All.Select(GlyphChoice.ForHero).ToList();

    /// <summary>Publishers in the library that have a logo; the one most common in this collection first (suggested), the rest alphabetical.</summary>
    public ObservableCollection<GlyphChoice> PublisherChoices { get; } = new();

    public bool HasPublisherChoices => PublisherChoices.Count > 0;

    /// <summary>The images in the user's "My icons" folder, by label.</summary>
    public ObservableCollection<GlyphChoice> MyIconChoices { get; } = new();

    public bool HasMyIconChoices => MyIconChoices.Count > 0;

    /// <summary>"3 files skipped" when the folder holds files that are too large or will not load; null otherwise.</summary>
    [ObservableProperty]
    private string? _myIconsSkippedText;

    private int _myIconsVersion;

    /// <summary>The "My icons" folder; tests point it at a temporary folder.</summary>
    public string IconsFolder { get; set; } = CollectionIconFolder.Path;

    /// <summary>Opens a folder in the file manager; replaced in tests.</summary>
    public Action<string> OpenFolderInShell { get; set; } = folder => cYo.Common.Win32.FileExplorer.OpenFolder(folder);

    private IEnumerable<GlyphChoice> AllIconChoices => GlyphChoices.Concat(HeroChoices).Concat(PublisherChoices).Concat(MyIconChoices);

    partial void OnIconGlyphChanged(string value) => SyncIconSelection();

    private void SyncIconSelection()
    {
        foreach (var choice in AllIconChoices)
        {
            choice.IsSelected = string.Equals(choice.Name, IconGlyph, StringComparison.Ordinal);
        }
    }

    /// <summary>Re-reads the "My icons" folder on a worker thread (the picker's Refresh button, and when the dialog loads): user SVGs are never rendered on the UI thread.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshMyIconsAsync()
    {
        string folder = IconsFolder;
        int version = ++_myIconsVersion;
        var scan = await System.Threading.Tasks.Task.Run(() => CollectionIconImages.Scan(folder));
        if (version != _myIconsVersion)
        {
            return; // a newer refresh started while this one was reading; let it win
        }

        MyIconChoices.Clear();
        foreach (var file in scan.Files)
        {
            MyIconChoices.Add(GlyphChoice.ForFile(file.FileName, file.Label));
        }

        MyIconsSkippedText = scan.Skipped switch
        {
            0 => null,
            1 => "1 file skipped (larger than 2 MB, or not a readable SVG/PNG)",
            _ => $"{scan.Skipped} files skipped (larger than 2 MB, or not readable SVG/PNG)",
        };
        OnPropertyChanged(nameof(HasMyIconChoices));
        SyncIconSelection();
    }

    [RelayCommand]
    private void OpenMyIconsFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(IconsFolder);
            OpenFolderInShell(IconsFolder);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MyIconsSkippedText = "The icons folder could not be opened: " + ex.Message;
        }
    }

    /// <summary>
    /// Fills <see cref="PublisherChoices"/>: every publisher of a series in the library that resolves to a logo, alphabetical, with the publisher most common
    /// among this collection's members first and marked suggested.
    /// </summary>
    private void LoadPublisherChoices(PaperbunkrDbContext context, IReadOnlyList<CollectionMember> members)
    {
        var names = context.Series.Select(s => s.Publisher).Where(p => p != null && p != "").Distinct().ToList()
            .Select(p => p!.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(CollectionIconResolver.HasPublisherLogo)
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        string? suggested = members
            .Select(m => m.Series?.Publisher ?? m.Issue?.Publisher ?? m.Issue?.Series?.Publisher)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .GroupBy(p => p!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => names.FirstOrDefault(n => string.Equals(n, g.Key, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(n => n is not null);

        PublisherChoices.Clear();
        if (suggested is not null)
        {
            PublisherChoices.Add(GlyphChoice.ForPublisher(suggested, isSuggested: true));
        }

        foreach (string name in names.Where(n => !string.Equals(n, suggested, StringComparison.OrdinalIgnoreCase)))
        {
            PublisherChoices.Add(GlyphChoice.ForPublisher(name, isSuggested: false));
        }

        OnPropertyChanged(nameof(HasPublisherChoices));
    }

    /// <summary>Picking the selected glyph again clears it.</summary>
    [RelayCommand]
    private void SetGlyph(string? name) =>
        IconGlyph = string.IsNullOrEmpty(name) || string.Equals(name, IconGlyph, StringComparison.Ordinal) ? string.Empty : name;

    [RelayCommand]
    private void ClearGlyph() => IconGlyph = string.Empty;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewCoverStyle))]
    private CollectionCoverStyle _coverStyle = CollectionCoverStyle.Auto;

    partial void OnCoverStyleChanged(CollectionCoverStyle value)
    {
        foreach (var choice in CoverStyleChoices)
        {
            choice.IsSelected = choice.Style == value;
        }
    }

    public CollectionCoverStyle PreviewCoverStyle => CoverStyle;

    [RelayCommand]
    private void SetCoverStyle(string? style)
    {
        if (Enum.TryParse<CollectionCoverStyle>(style, out var parsed))
        {
            CoverStyle = parsed;
        }
    }

    /// <summary>The chosen cover member's <c>CollectionItem.Id</c>; null = the first member.</summary>
    private int? _coverMemberItemId;

    private void UseMemberAsCover(CollectionMemberRowViewModel row)
    {
        if (row.CollectionItemId is not int itemId)
        {
            return;
        }

        _coverMemberItemId = _coverMemberItemId == itemId ? null : itemId;
        RefreshCoverMember();
    }

    private void RefreshCoverMember()
    {
        foreach (var member in Members)
        {
            member.IsCoverMember = _coverMemberItemId is int id && member.CollectionItemId == id;
        }

        OnPropertyChanged(nameof(AutoCoverLabel));
        OnPropertyChanged(nameof(PreviewCoverTile));
        OnPropertyChanged(nameof(PreviewCoverBrush));
    }

    /// <summary>The Auto-cover button's text: names the member it will use.</summary>
    public string AutoCoverLabel
    {
        get
        {
            var chosen = Members.FirstOrDefault(m => m.IsCoverMember);
            return chosen is null ? "Auto (first member)" : $"Auto ({chosen.DisplayTitle})";
        }
    }

    /// <summary>Placeholder behind the preview cover: the first member's, else a stable colour from the name.</summary>
    public IBrush PreviewCoverBrush => PreviewCoverTile?.CoverBrush ?? SeriesCardSample.CoverBrushFor(PreviewName);

    private string? _manualCoverLoadedFor;
    private Avalonia.Media.Imaging.Bitmap? _manualCover;

    /// <summary>The manually chosen cover file (also an unsaved pick); null for the automatic cover or an unreadable file.</summary>
    public Avalonia.Media.Imaging.Bitmap? PreviewManualCover
    {
        get
        {
            if (IsAutoCover || string.IsNullOrEmpty(CoverImagePath))
            {
                return null;
            }

            if (_manualCoverLoadedFor == CoverImagePath)
            {
                return _manualCover;
            }

            _manualCoverLoadedFor = CoverImagePath;
            try
            {
                _manualCover = System.IO.File.Exists(CoverImagePath) ? new Avalonia.Media.Imaging.Bitmap(CoverImagePath) : null;
            }
            catch
            {
                _manualCover = null;
            }

            return _manualCover;
        }
    }

    private void OnMembersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PreviewCountText));
        OnPropertyChanged(nameof(PreviewCoverTile));
        OnPropertyChanged(nameof(PreviewCoverBrush));
    }

    // --- Accordion sections: open/closed is remembered for the session only (no setting, no migration) ---

    private static bool s_appearanceOpen = true;
    private static bool s_membersOpen = true;
    private static bool s_rulesOpen;
    private static bool s_relatedOpen;

    /// <summary>Test seam: restore the session defaults.</summary>
    internal static void ResetSectionState()
    {
        s_appearanceOpen = true;
        s_membersOpen = true;
        s_rulesOpen = false;
        s_relatedOpen = false;
    }

    public bool IsAppearanceOpen
    {
        get => s_appearanceOpen;
        set
        {
            if (s_appearanceOpen != value)
            {
                s_appearanceOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsMembersOpen
    {
        get => s_membersOpen;
        set
        {
            if (s_membersOpen != value)
            {
                s_membersOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsRulesOpen
    {
        get => s_rulesOpen;
        set
        {
            if (s_rulesOpen != value)
            {
                s_rulesOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsRelatedOpen
    {
        get => s_relatedOpen;
        set
        {
            if (s_relatedOpen != value)
            {
                s_relatedOpen = value;
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    private void ToggleAppearance() => IsAppearanceOpen = !IsAppearanceOpen;

    [RelayCommand]
    private void ToggleMembers() => IsMembersOpen = !IsMembersOpen;

    [RelayCommand]
    private void ToggleRules() => IsRulesOpen = !IsRulesOpen;

    [RelayCommand]
    private void ToggleRelated() => IsRelatedOpen = !IsRelatedOpen;

    private int _ruleCount;

    /// <summary>Header caption shown even while the Rules section is collapsed.</summary>
    public string RulesSummary => _ruleCount == 0 ? "None set" : _ruleCount == 1 ? "1 rule set" : $"{_ruleCount} rules set";

    public string RelatedSummary
    {
        get
        {
            int count = RelatedCollections.Count + RelatedSeries.Count;
            return count == 0 ? "None" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public string MembersSummary => Members.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

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

        HeaderLabel = collection.Name; // the dialog subtitle; the title says "Edit collection"
        Name = collection.Name;
        Description = collection.Description ?? string.Empty;
        AccentColor = collection.AccentColor ?? string.Empty;
        IsAutoCover = collection.IsAutoCover;
        CoverImagePath = collection.CoverImagePath ?? string.Empty;

        IconGlyph = collection.IconGlyph ?? string.Empty;
        IsPinned = collection.IsPinned;
        CoverStyle = collection.CoverStyle;
        OnIconGlyphChanged(IconGlyph);
        OnCoverStyleChanged(CoverStyle);
        _coverMemberItemId = collection.CoverMemberItemId;

        Members.Clear();
        var members = CollectionResolver.GetMembers(context, collectionId);
        foreach (var member in members)
        {
            Members.Add(new CollectionMemberRowViewModel(member, RemoveMemberRow, UseMemberAsCover));
        }

        LoadPublisherChoices(context, members);
        _ = RefreshMyIconsAsync();

        RefreshCoverMember();

        LoadRuleSlots(context, collection);
        RefreshRelatedCollections(context, collectionId);
        RefreshRelatedSeries(context, collectionId);
        RefreshContrastWarning();
        RefreshSwatchSelection();
        OnPropertyChanged(nameof(PickerColor));
        OnPropertyChanged(nameof(PreviewCoverTile));
        OnPropertyChanged(nameof(PreviewCoverBrush));
        OnPropertyChanged(nameof(PreviewManualCover));
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
        // Remove is disabled (CanExecute) for a rule-matched row, so CollectionItemId is always set
        // here in practice - the null-check is a defensive no-op backstop, not the primary guard.
        if (row.CollectionItemId is int itemId)
        {
            Members.Remove(row);
            _removedItemIds.Add(itemId);
            if (_coverMemberItemId == itemId)
            {
                _coverMemberItemId = null;
                RefreshCoverMember();
            }
        }
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

    /// <summary>
    /// Drag-and-drop: puts <paramref name="dragged"/> where <paramref name="target"/> is. Rule-matched rows have no place in the manual order,
    /// so a drag from or onto one does nothing (they stay where the rule puts them).
    /// </summary>
    public void MoveMemberTo(CollectionMemberRowViewModel dragged, CollectionMemberRowViewModel target)
    {
        if (ReferenceEquals(dragged, target) || dragged.IsRuleMatched || target.IsRuleMatched)
        {
            return;
        }

        int from = Members.IndexOf(dragged);
        int to = Members.IndexOf(target);
        if (from < 0 || to < 0)
        {
            return;
        }

        Members.Move(from, to);
    }

    /// <summary>
    /// Sorts the manual rows only (rule-matched rows keep their positions); buffered like every other edit here, so Cancel undoes it.
    /// </summary>
    public void SortMembers(CollectionMemberSort sort)
    {
        var slots = Enumerable.Range(0, Members.Count).Where(i => !Members[i].IsRuleMatched).ToList();
        var manual = slots.Select(i => Members[i]).ToList();

        var comparer = StringComparer.CurrentCultureIgnoreCase;
        IEnumerable<CollectionMemberRowViewModel> ordered = sort switch
        {
            CollectionMemberSort.TitleDescending => manual.OrderByDescending(r => r.DisplayTitle, comparer),
            CollectionMemberSort.Type => manual.OrderBy(r => r.Kind).ThenBy(r => r.DisplayTitle, comparer),
            _ => manual.OrderBy(r => r.DisplayTitle, comparer),
        };

        // The wanted final order: the same list with only the manual slots refilled in sorted order.
        var sorted = ordered.ToList();
        var desired = Members.ToList();
        for (int n = 0; n < slots.Count; n++)
        {
            desired[slots[n]] = sorted[n];
        }

        for (int i = 0; i < desired.Count; i++)
        {
            int current = Members.IndexOf(desired[i]);
            if (current != i)
            {
                Members.Move(current, i);
            }
        }
    }

    [RelayCommand]
    private void SortMembersBy(string? mode)
    {
        if (Enum.TryParse<CollectionMemberSort>(mode, out var sort))
        {
            SortMembers(sort);
        }

        // The menu row that raised this lives inside the popup: close it after the click has finished routing, not inside it.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => IsSortMenuOpen = false);
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

        // After the removals: SetIdentity drops a cover member id that is no longer one of this collection's items.
        CollectionService.SetIdentity(context, collectionId, NullIfEmpty(IconGlyph), CoverStyle, _coverMemberItemId, IsPinned);

        // Rule-matched rows (CollectionItemId null) aren't part of the manual order at all - only
        // real CollectionItem rows are reordered here.
        var manualOrder = Members.Where(m => m.CollectionItemId is not null).Select(m => m.CollectionItemId!.Value).ToList();
        if (manualOrder.Count > 0)
        {
            CollectionService.ReorderItems(context, collectionId, manualOrder);
        }

        _goBack();
    }

    [RelayCommand]
    private void Cancel() => _goBack();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // --- Rule slots (docs/superpowers/specs/2026-08-30-smart-collections-design.md) - up to three
    // independent SmartList rules, one per target kind, whose live matches union with the manual
    // members above (CollectionResolver.GetMembers). Each slot is set/cleared immediately (like the
    // Related Collections section below), not buffered into Save - picking a rule is a structural
    // change to what the collection *is*, not a property edit worth an accidental-Cancel undo. ---

    public ObservableCollection<SmartListOption> IssueSmartLists { get; } = new();

    public ObservableCollection<SmartListOption> SeriesSmartLists { get; } = new();

    public ObservableCollection<SmartListOption> NovelSmartLists { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedIssueSmartListText))]
    private SmartListOption? _selectedIssueSmartList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSeriesSmartListText))]
    private SmartListOption? _selectedSeriesSmartList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNovelSmartListText))]
    private SmartListOption? _selectedNovelSmartList;

    // --- SuggestBox string projections (docs/superpowers/specs/2026-09-10-suggestbox-migration-
    // plan.md). SmartList names are user-defined and may collide - first match by name wins
    // (accepted; see the plan doc). Empty text clears the rule slot's picker selection (the Set
    // button is what actually persists it, so this matches the old placeholder behavior).
    public IReadOnlyList<string> IssueSmartListNames => IssueSmartLists.Select(o => o.Name).ToList();

    public IReadOnlyList<string> SeriesSmartListNames => SeriesSmartLists.Select(o => o.Name).ToList();

    public IReadOnlyList<string> NovelSmartListNames => NovelSmartLists.Select(o => o.Name).ToList();

    public string SelectedIssueSmartListText
    {
        get => SelectedIssueSmartList?.Name ?? string.Empty;
        set => SelectedIssueSmartList = IssueSmartLists.FirstOrDefault(o => o.Name == value);
    }

    public string SelectedSeriesSmartListText
    {
        get => SelectedSeriesSmartList?.Name ?? string.Empty;
        set => SelectedSeriesSmartList = SeriesSmartLists.FirstOrDefault(o => o.Name == value);
    }

    public string SelectedNovelSmartListText
    {
        get => SelectedNovelSmartList?.Name ?? string.Empty;
        set => SelectedNovelSmartList = NovelSmartLists.FirstOrDefault(o => o.Name == value);
    }

    private void LoadRuleSlots(PaperbunkrDbContext context, Collection collection)
    {
        IssueSmartLists.Clear();
        SeriesSmartLists.Clear();
        NovelSmartLists.Clear();

        foreach (var list in context.SmartLists.Where(l => l.TargetKind == SmartListTargetKind.Issue).OrderBy(l => l.Name))
        {
            IssueSmartLists.Add(new SmartListOption(list.Id, list.Name));
        }

        foreach (var list in context.SmartLists.Where(l => l.TargetKind == SmartListTargetKind.Series).OrderBy(l => l.Name))
        {
            SeriesSmartLists.Add(new SmartListOption(list.Id, list.Name));
        }

        foreach (var list in context.SmartLists.Where(l => l.TargetKind == SmartListTargetKind.Novel).OrderBy(l => l.Name))
        {
            NovelSmartLists.Add(new SmartListOption(list.Id, list.Name));
        }

        OnPropertyChanged(nameof(IssueSmartListNames));
        OnPropertyChanged(nameof(SeriesSmartListNames));
        OnPropertyChanged(nameof(NovelSmartListNames));

        SelectedIssueSmartList = IssueSmartLists.FirstOrDefault(o => o.Id == collection.IssueSmartListId);
        SelectedSeriesSmartList = SeriesSmartLists.FirstOrDefault(o => o.Id == collection.SeriesSmartListId);
        SelectedNovelSmartList = NovelSmartLists.FirstOrDefault(o => o.Id == collection.NovelSmartListId);

        _ruleCount = (collection.IssueSmartListId is null ? 0 : 1)
            + (collection.SeriesSmartListId is null ? 0 : 1)
            + (collection.NovelSmartListId is null ? 0 : 1);
        OnPropertyChanged(nameof(RulesSummary));
    }

    [RelayCommand]
    private void SetIssueRule()
    {
        if (_collectionId is int collectionId && SelectedIssueSmartList is { } option)
        {
            using var context = _contextFactory();
            CollectionService.SetIssueSmartList(context, collectionId, option.Id);
            Load(collectionId);
        }
    }

    [RelayCommand]
    private void ClearIssueRule()
    {
        if (_collectionId is int collectionId)
        {
            using var context = _contextFactory();
            CollectionService.ClearIssueSmartList(context, collectionId);
            Load(collectionId);
        }
    }

    [RelayCommand]
    private void SetSeriesRule()
    {
        if (_collectionId is int collectionId && SelectedSeriesSmartList is { } option)
        {
            using var context = _contextFactory();
            CollectionService.SetSeriesSmartList(context, collectionId, option.Id);
            Load(collectionId);
        }
    }

    [RelayCommand]
    private void ClearSeriesRule()
    {
        if (_collectionId is int collectionId)
        {
            using var context = _contextFactory();
            CollectionService.ClearSeriesSmartList(context, collectionId);
            Load(collectionId);
        }
    }

    [RelayCommand]
    private void SetNovelRule()
    {
        if (_collectionId is int collectionId && SelectedNovelSmartList is { } option)
        {
            using var context = _contextFactory();
            CollectionService.SetNovelSmartList(context, collectionId, option.Id);
            Load(collectionId);
        }
    }

    [RelayCommand]
    private void ClearNovelRule()
    {
        if (_collectionId is int collectionId)
        {
            using var context = _contextFactory();
            CollectionService.ClearNovelSmartList(context, collectionId);
            Load(collectionId);
        }
    }

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

        OnPropertyChanged(nameof(RelatedSummary));
    }

    public static IReadOnlyList<RelationTypeOption> RelationTypeOptions => RelationTypeOption.All;

    /// <summary>Relation-type labels for the string-only <c>SuggestBox</c> pickers - unique per value.</summary>
    public static string[] RelationTypeNames { get; } = RelationTypeOption.All.Select(o => o.Label).ToArray();

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
    [NotifyPropertyChangedFor(nameof(SelectedRelationTypeOptionText))]
    private RelationTypeOption _selectedRelationTypeOption = RelationTypeOptions.First(o => o.Type == RelationType.Related);

    partial void OnSelectedRelationTypeOptionChanged(RelationTypeOption value) => SelectedRelationType = value.Type;

    public string SelectedRelationTypeOptionText
    {
        get => SelectedRelationTypeOption.Label;
        set
        {
            var match = RelationTypeOptions.FirstOrDefault(o => o.Label == value);
            if (match is not null)
            {
                SelectedRelationTypeOption = match;
            }
        }
    }

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

    // --- Related (Series-node MediaRelation edges, docs/superpowers/specs/2026-08-30-media-
    // relation-collection-nodes-design.md) - a Collection can now sit on one side of a typed
    // MediaRelation with a Series on the other (e.g. "this omnibus is a Crossover tying into this
    // standalone series"). Structurally parallel to "Related Collections" above, distinctly named
    // to avoid colliding with it, and deliberately Series-only in its search: a Collection↔
    // Collection match found here would just be rejected by MediaRelationResolver.TryCreate (that's
    // "Related Collections"'/CollectionRelation's job), so scoping the search up front avoids a
    // confusing "found it, but can't add it" dead end. Same immediate-write posture as "Related
    // Collections" - not part of the buffered Save/Cancel flow. ---

    public ObservableCollection<RelatedSeriesChip> RelatedSeries { get; } = new();

    private void RefreshRelatedSeries(PaperbunkrDbContext context, int collectionId)
    {
        RelatedSeries.Clear();
        foreach (var endpoint in MediaRelationResolver.GetRelatedFromCollection(context, collectionId))
        {
            if (endpoint.Kind != MediaRelationEndpointKind.Series || endpoint.Series is not { } series)
            {
                continue;
            }

            RelatedSeries.Add(new RelatedSeriesChip
            {
                MediaRelationId = endpoint.MediaRelationId,
                SeriesId = series.Id,
                Name = series.Name,
                RelationTypeLabel = RelationTypeOption.FormatLabel(endpoint.DisplayType),
            });
        }

        OnPropertyChanged(nameof(RelatedSummary));
    }

    [ObservableProperty]
    private bool _isAddingSeriesRelation;

    [ObservableProperty]
    private string _seriesRelationSearchQuery = string.Empty;

    [ObservableProperty]
    private RelationType _selectedSeriesRelationType = RelationType.Related;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSeriesRelationTypeOptionText))]
    private RelationTypeOption _selectedSeriesRelationTypeOption = RelationTypeOptions.First(o => o.Type == RelationType.Related);

    partial void OnSelectedSeriesRelationTypeOptionChanged(RelationTypeOption value) => SelectedSeriesRelationType = value.Type;

    public string SelectedSeriesRelationTypeOptionText
    {
        get => SelectedSeriesRelationTypeOption.Label;
        set
        {
            var match = RelationTypeOptions.FirstOrDefault(o => o.Label == value);
            if (match is not null)
            {
                SelectedSeriesRelationTypeOption = match;
            }
        }
    }

    public ObservableCollection<SeriesSearchResult> SeriesRelationSearchResults { get; } = new();

    [RelayCommand]
    private void ToggleAddSeriesRelation()
    {
        IsAddingSeriesRelation = !IsAddingSeriesRelation;
        SeriesRelationSearchQuery = string.Empty;
        SeriesRelationSearchResults.Clear();
    }

    partial void OnSeriesRelationSearchQueryChanged(string value) => SearchSeriesRelationCandidates();

    [RelayCommand]
    private void SearchSeriesRelationCandidates()
    {
        SeriesRelationSearchResults.Clear();
        if (string.IsNullOrWhiteSpace(SeriesRelationSearchQuery))
        {
            return;
        }

        using var context = _contextFactory();
        var matches = context.Series
            .AsEnumerable()
            .Where(s => s.Name.Contains(SeriesRelationSearchQuery, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var series in matches)
        {
            SeriesRelationSearchResults.Add(new SeriesSearchResult { SeriesId = series.Id, Name = series.Name });
        }
    }

    [RelayCommand]
    private void AddSeriesRelation(SeriesSearchResult? target)
    {
        if (target is null || _collectionId is not int currentCollectionId)
        {
            return;
        }

        using var context = _contextFactory();
        MediaRelationResolver.TryCreate(
            context,
            MediaRelationEndpointKind.Collection, currentCollectionId,
            MediaRelationEndpointKind.Series, target.SeriesId,
            SelectedSeriesRelationType);

        IsAddingSeriesRelation = false;
        SeriesRelationSearchQuery = string.Empty;
        SeriesRelationSearchResults.Clear();
        RefreshRelatedSeries(context, currentCollectionId);
    }

    [RelayCommand]
    private void RemoveSeriesRelation(RelatedSeriesChip? chip)
    {
        if (chip is null || _collectionId is not int currentCollectionId)
        {
            return;
        }

        using var context = _contextFactory();
        MediaRelationResolver.Remove(context, chip.MediaRelationId);
        RefreshRelatedSeries(context, currentCollectionId);
    }
}
