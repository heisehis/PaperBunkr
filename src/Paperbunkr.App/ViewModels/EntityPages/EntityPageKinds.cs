using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using EntityPageData = Paperbunkr.Data.Metadata.EntityPage;

namespace Paperbunkr.App.ViewModels.EntityPages;

public enum EntityImageShape { Portrait, Wide, Squircle, BrandMark }

public enum EntityPrimaryAction { Continue, ReadInOrder, ShowInLibrary }

public enum EntityFilterMode { Continuity, Roles, Imprints }

/// <summary>Tab ids shared by the profiles and the view (one panel per id).</summary>
public static class EntityTabIds
{
    public const string Overview = "overview";
    public const string Appearances = "appearances";
    public const string Series = "series";
    public const string Events = "events";
    public const string Career = "career";
    public const string Credits = "credits";
    public const string Continuities = "continuities";
    public const string Characters = "characters";
}

/// <summary>
/// One variation of the entity page (docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md §4): the image shape, main action, filter
/// and tabs. Character and Team share <see cref="CharacterTeamKind"/>; Location, Creator and Publisher each have their own.
/// </summary>
public interface IEntityPageKind
{
    string Overline { get; }
    EntityImageShape ImageShape { get; }
    EntityPrimaryAction PrimaryAction { get; }
    bool ShowsSaveAsList { get; }
    bool ShowsMapAction { get; }
    EntityFilterMode FilterMode { get; }

    /// <summary>In display order; the first is the lead tab the page opens on.</summary>
    IReadOnlyList<string> TabIds { get; }

    /// <summary>The header's counts line.</summary>
    string CountsLine(EntityPageData page);

    /// <summary>Caption for one of the page's issues in a count ("appearance" / "issue").</summary>
    string IssueNoun(int count);
}

public sealed class CharacterTeamKind(string overline) : IEntityPageKind
{
    public string Overline { get; } = overline;
    public EntityImageShape ImageShape => EntityImageShape.Portrait;
    public EntityPrimaryAction PrimaryAction => EntityPrimaryAction.Continue;
    public bool ShowsSaveAsList => true;
    public bool ShowsMapAction => true;
    public EntityFilterMode FilterMode => EntityFilterMode.Continuity;
    public IReadOnlyList<string> TabIds { get; } = new[] { EntityTabIds.Overview, EntityTabIds.Appearances, EntityTabIds.Series, EntityTabIds.Events };
    public string CountsLine(EntityPageData page) => EntityPageKinds.AppearanceLine(page);
    public string IssueNoun(int count) => count == 1 ? "appearance" : "appearances";
}

public sealed class LocationKind : IEntityPageKind
{
    public string Overline => "Location";
    public EntityImageShape ImageShape => EntityImageShape.Wide;
    public EntityPrimaryAction PrimaryAction => EntityPrimaryAction.ReadInOrder;
    public bool ShowsSaveAsList => true;
    public bool ShowsMapAction => false;
    public EntityFilterMode FilterMode => EntityFilterMode.Continuity;
    public IReadOnlyList<string> TabIds { get; } = new[] { EntityTabIds.Overview, EntityTabIds.Appearances, EntityTabIds.Series };
    public string CountsLine(EntityPageData page) => EntityPageKinds.AppearanceLine(page);
    public string IssueNoun(int count) => count == 1 ? "appearance" : "appearances";
}

public sealed class CreatorKind : IEntityPageKind
{
    public string Overline => "Creator";
    public EntityImageShape ImageShape => EntityImageShape.Squircle;
    public EntityPrimaryAction PrimaryAction => EntityPrimaryAction.ReadInOrder;
    public bool ShowsSaveAsList => true;
    public bool ShowsMapAction => false;
    public EntityFilterMode FilterMode => EntityFilterMode.Roles;
    public IReadOnlyList<string> TabIds { get; } = new[] { EntityTabIds.Career, EntityTabIds.Credits, EntityTabIds.Series };

    public string CountsLine(EntityPageData page) => EntityPageText.JoinDetail(
        EntityPageText.Count(page.AppearanceCount, "issue", "issues"), EntityPageText.Count(page.SeriesCount, "series", "series"), EntityPageKinds.Years(page));

    public string IssueNoun(int count) => count == 1 ? "issue" : "issues";
}

public sealed class PublisherKind : IEntityPageKind
{
    public string Overline => "Publisher";
    public EntityImageShape ImageShape => EntityImageShape.BrandMark;
    public EntityPrimaryAction PrimaryAction => EntityPrimaryAction.ShowInLibrary;
    public bool ShowsSaveAsList => false;
    public bool ShowsMapAction => false;
    public EntityFilterMode FilterMode => EntityFilterMode.Imprints;
    public IReadOnlyList<string> TabIds { get; } = new[] { EntityTabIds.Series, EntityTabIds.Continuities, EntityTabIds.Characters };

    public string CountsLine(EntityPageData page)
    {
        string read = page.AppearanceCount == 0 ? string.Empty : $"{(int)Math.Round(100.0 * page.ReadCount / page.AppearanceCount)}% read";
        return EntityPageText.JoinDetail(EntityPageText.Count(page.SeriesCount, "series", "series"),
            EntityPageText.Count(page.AppearanceCount, "issue", "issues"), EntityPageKinds.Years(page), read);
    }

    public string IssueNoun(int count) => count == 1 ? "issue" : "issues";
}

public static class EntityPageKinds
{
    public static readonly IEntityPageKind Character = new CharacterTeamKind("Character");
    public static readonly IEntityPageKind Team = new CharacterTeamKind("Team");
    public static readonly IEntityPageKind Location = new LocationKind();
    public static readonly IEntityPageKind Creator = new CreatorKind();
    public static readonly IEntityPageKind Publisher = new PublisherKind();

    public static IEntityPageKind For(ComicMetadataEntityKind kind) => kind switch
    {
        ComicMetadataEntityKind.Team => Team,
        ComicMetadataEntityKind.Location => Location,
        ComicMetadataEntityKind.Creator => Creator,
        ComicMetadataEntityKind.Publisher => Publisher,
        _ => Character,
    };

    /// <summary>"32 appearances · 9 series · 4 read".</summary>
    internal static string AppearanceLine(EntityPageData page) => EntityPageText.JoinDetail(
        EntityPageText.Count(page.AppearanceCount, "appearance", "appearances"), EntityPageText.Count(page.SeriesCount, "series", "series"), $"{page.ReadCount:N0} read");

    /// <summary>"2009–2026" from the dated appearances.</summary>
    internal static string Years(EntityPageData page)
    {
        var years = page.ByYear.Where(y => y.Year is not null).Select(y => y.Year!.Value).ToList();
        return years.Count == 0 ? string.Empty : EntityPageText.YearSpan(years.Min(), years.Max());
    }
}
