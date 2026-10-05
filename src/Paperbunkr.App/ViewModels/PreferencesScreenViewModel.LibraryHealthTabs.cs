using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Preferences &gt; Library &gt; Library Health sub-tabs (docs/superpowers/specs/2026-09-26-library-health-subtabs-design.md): the
/// active tab, which one opens by default, the remembered tab, the collapsible sections and the deep links that target them.
/// </summary>
public partial class PreferencesScreenViewModel
{
    /// <summary>
    /// True once something other than "nothing yet" decided the tab - a remembered value, the user switching, or a deep link.
    /// Until then the tab follows the first-open rule: Review if anything is pending, else Overview.
    /// </summary>
    private bool _libraryHealthTabChosen;

    /// <summary>The eleven collapsible sections' open state, sub-tab and anchor.</summary>
    public LibraryHealthSections LibraryHealthSections { get; } = new();

    private PublisherGapsViewModel? _publisherGaps;

    /// <summary>The "Publisher logos" section's own view model (publishers with no logo, issues with no publisher, the user icon folder).</summary>
    public PublisherGapsViewModel PublisherGaps => _publisherGaps ??= new PublisherGapsViewModel(dialogs: _dialogService);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryHealthOverviewTab), nameof(IsLibraryHealthReviewTab), nameof(IsLibraryHealthFilesTab))]
    private LibraryHealthTab _activeLibraryHealthTab = LibraryHealthTab.Overview;

    public bool IsLibraryHealthOverviewTab => ActiveLibraryHealthTab == LibraryHealthTab.Overview;

    public bool IsLibraryHealthReviewTab => ActiveLibraryHealthTab == LibraryHealthTab.Review;

    public bool IsLibraryHealthFilesTab => ActiveLibraryHealthTab == LibraryHealthTab.Files;

    /// <summary>Wired from the constructor: applies the first-open rule as soon as the first refresh shows something is pending.</summary>
    private void InitLibraryHealthTabs()
    {
        NeedsReview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NeedsReviewViewModel.HasLoaded) && !_libraryHealthTabChosen && NeedsReview.HasPendingItems)
            {
                ActiveLibraryHealthTab = LibraryHealthTab.Review; // a default, not a choice: not persisted, and the user can still switch
            }
        };
    }

    /// <summary>Restores the remembered tab from settings (no write back). A missing or unknown value leaves the first-open rule in charge.</summary>
    private void LoadLibraryHealthTab(AppSettings settings)
    {
        if (Enum.TryParse<LibraryHealthTab>(settings.LibraryHealthTab, out var remembered) && Enum.IsDefined(remembered))
        {
            _libraryHealthTabChosen = true;
            ActiveLibraryHealthTab = remembered;
        }
    }

    /// <summary>The user picked a tab: show it and remember it.</summary>
    private void ChooseLibraryHealthTab(LibraryHealthTab tab)
    {
        _libraryHealthTabChosen = true;
        ActiveLibraryHealthTab = tab;
        PersistBehaviorSetting(s => s.LibraryHealthTab = tab.ToString());
    }

    [RelayCommand]
    private void ShowLibraryHealthOverviewTab() => ChooseLibraryHealthTab(LibraryHealthTab.Overview);

    [RelayCommand]
    private void ShowLibraryHealthReviewTab() => ChooseLibraryHealthTab(LibraryHealthTab.Review);

    [RelayCommand]
    private void ShowLibraryHealthFilesTab() => ChooseLibraryHealthTab(LibraryHealthTab.Files);

    /// <summary>
    /// Deep link into Library Health. <paramref name="tab"/> and <paramref name="sectionKeyOrAnchor"/> override the remembered
    /// tab for this visit only (they are not saved); with neither, the remembered tab / first-open rule decides. Switches to
    /// Preferences &gt; Library, shows the tab, opens the section and scrolls to it (or to the card).
    /// </summary>
    public void OpenLibraryHealth(LibraryHealthTab? tab = null, string? sectionKeyOrAnchor = null)
    {
        ActiveSection = PreferencesSection.Library;

        var section = LibraryHealthSections.Find(sectionKeyOrAnchor);
        if (section is not null)
        {
            _libraryHealthTabChosen = true;
            ActiveLibraryHealthTab = section.Tab;
            section.IsOpen = true;
        }
        else if (tab is { } explicitTab)
        {
            _libraryHealthTabChosen = true;
            ActiveLibraryHealthTab = explicitTab;
        }

        ScrollToAnchorRequested?.Invoke(section?.Anchor ?? "library.health");
    }

    /// <summary>
    /// Makes an anchor visible before a search hit scrolls to it: a section inside a hidden tab is not laid out, so the tab must
    /// switch and the section open first. Anchors that are not Library Health sections are left alone.
    /// </summary>
    private void RevealLibraryHealthAnchor(string anchorKey)
    {
        var section = LibraryHealthSections.Find(anchorKey);
        if (section is null)
        {
            return;
        }

        _libraryHealthTabChosen = true;
        ActiveLibraryHealthTab = section.Tab;
        section.IsOpen = true;
    }

    /// <summary>The "Review →" link on an Overview "Needs attention" row: jump to the tab and open that section.</summary>
    [RelayCommand]
    private void ReviewLibraryHealthSection(LibraryHealthSectionState? section)
    {
        if (section is null)
        {
            return;
        }

        ChooseLibraryHealthTab(section.Tab);
        section.IsOpen = true;
        ScrollToAnchorRequested?.Invoke(section.Anchor);
    }
}
