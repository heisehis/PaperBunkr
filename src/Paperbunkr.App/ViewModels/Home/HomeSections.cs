using CommunityToolkit.Mvvm.ComponentModel;
using FluentIcons.Common;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels.Home;

/// <summary>
/// One reorderable Home section (docs/superpowers/specs/2026-09-28-home-improvements-design.md, "Section model"). Each subclass is
/// picked by type by its own DataTemplate in <c>Views/Home/*Section.axaml</c>. Sections are thin: the data still lives on
/// <see cref="Home"/> (one background snapshot fills every section at once), a section only decides which slice its template
/// shows, its heading and its icon (C3).
/// </summary>
public abstract class HomeSectionViewModel : ObservableObject
{
    protected HomeSectionViewModel(HomeScreenViewModel home, string key, Symbol icon, string headerAutomationId)
    {
        Home = home;
        Key = key;
        Icon = icon;
        HeaderAutomationId = headerAutomationId;
    }

    public HomeScreenViewModel Home { get; }

    public string Key { get; }

    public virtual string Title => HomeSectionKey.DisplayName(Key);

    public Symbol Icon { get; }

    public string HeaderAutomationId { get; }
}

public sealed class SpotlightSectionViewModel : HomeSectionViewModel
{
    public SpotlightSectionViewModel(HomeScreenViewModel home)
        : base(home, HomeSectionKey.Spotlight, Symbol.Sparkle, "HomeSpotlightSectionHeader") { }
}

public sealed class NeedsAttentionSectionViewModel : HomeSectionViewModel
{
    public NeedsAttentionSectionViewModel(HomeScreenViewModel home)
        : base(home, HomeSectionKey.NeedsAttention, Symbol.Flag, "HomeNeedsAttentionHeader") { }
}

public sealed class ContinueReadingSectionViewModel : HomeSectionViewModel
{
    public ContinueReadingSectionViewModel(HomeScreenViewModel home)
        : base(home, HomeSectionKey.ContinueReading, Symbol.BookOpen, "HomeContinueReadingHeader") { }
}

public sealed class RecentlyAddedSectionViewModel : HomeSectionViewModel
{
    public RecentlyAddedSectionViewModel(HomeScreenViewModel home)
        : base(home, HomeSectionKey.RecentlyAdded, Symbol.New, "HomeRecentlyAddedHeader") { }
}

public sealed class CollectionsSectionViewModel : HomeSectionViewModel
{
    public CollectionsSectionViewModel(HomeScreenViewModel home)
        : base(home, HomeSectionKey.Collections, Symbol.Collections, "HomeCollectionsHeader") { }
}

public sealed class BecauseYouReadSectionViewModel : HomeSectionViewModel
{
    public BecauseYouReadSectionViewModel(HomeScreenViewModel home)
        : base(home, HomeSectionKey.BecauseYouRead, Symbol.Lightbulb, "HomeBecauseYouReadHeader") { }
}

public sealed class ReadingListSectionViewModel : HomeSectionViewModel
{
    public ReadingListSectionViewModel(HomeScreenViewModel home)
        : base(home, HomeSectionKey.ReadingList, Symbol.TextBulletList, "HomeReadingListSpotlightHeader") { }
}
