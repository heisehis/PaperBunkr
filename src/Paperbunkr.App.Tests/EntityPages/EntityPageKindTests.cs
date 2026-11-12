using Paperbunkr.App.ViewModels.EntityPages;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests.EntityPages;

/// <summary>Per-kind variations, docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md §4.</summary>
public class EntityPageKindTests
{
    private static EntityPage Page(int appearances, int read, params int?[] years)
    {
        var issues = Enumerable.Range(1, appearances).Select(i => new EntityPageIssue(i, 1, $"X #{i}", years.Length > 0 ? years[(i - 1) % years.Length] : null, i <= read)).ToList();
        var byYear = issues.GroupBy(i => i.Year).Select(g => new EntityPageYear(g.Key, g.Count())).ToList();
        return new EntityPage("Storm", null, null, null, null, issues, Array.Empty<EntityPageContinuity>(), Array.Empty<EntityPageEvent>(),
            new[] { new EntityPageSeries(1, "X", appearances, read) }, byYear, Array.Empty<EntityPageRole>());
    }

    [Theory]
    [InlineData(ComicMetadataEntityKind.Character, "Character", EntityPrimaryAction.Continue, EntityFilterMode.Continuity, EntityImageShape.Portrait, "overview,appearances,series,events")]
    [InlineData(ComicMetadataEntityKind.Team, "Team", EntityPrimaryAction.Continue, EntityFilterMode.Continuity, EntityImageShape.Portrait, "overview,appearances,series,events")]
    [InlineData(ComicMetadataEntityKind.Location, "Location", EntityPrimaryAction.ReadInOrder, EntityFilterMode.Continuity, EntityImageShape.Wide, "overview,appearances,series")]
    [InlineData(ComicMetadataEntityKind.Creator, "Creator", EntityPrimaryAction.ReadInOrder, EntityFilterMode.Roles, EntityImageShape.Squircle, "career,credits,series")]
    [InlineData(ComicMetadataEntityKind.Publisher, "Publisher", EntityPrimaryAction.ShowInLibrary, EntityFilterMode.Imprints, EntityImageShape.BrandMark, "series,continuities,characters")]
    public void EachKind_HasItsVariation(ComicMetadataEntityKind kind, string overline, EntityPrimaryAction action, EntityFilterMode filter, EntityImageShape shape, string tabs)
    {
        var profile = EntityPageKinds.For(kind);
        Assert.Equal(overline, profile.Overline);
        Assert.Equal(action, profile.PrimaryAction);
        Assert.Equal(filter, profile.FilterMode);
        Assert.Equal(shape, profile.ImageShape);
        Assert.Equal(tabs, string.Join(",", profile.TabIds));
    }

    [Fact]
    public void OnlyCharactersAndTeams_OfferTheMap_AndPublishersDontSaveLists()
    {
        Assert.True(EntityPageKinds.Character.ShowsMapAction);
        Assert.True(EntityPageKinds.Team.ShowsMapAction);
        Assert.False(EntityPageKinds.Location.ShowsMapAction);
        Assert.False(EntityPageKinds.Creator.ShowsMapAction);
        Assert.False(EntityPageKinds.Publisher.ShowsSaveAsList);
    }

    [Fact]
    public void CountsLines_FollowTheKind()
    {
        Assert.Equal("1 appearance · 1 series · 0 read", EntityPageKinds.Character.CountsLine(Page(1, 0, 2009)));
        Assert.Equal("4 issues · 1 series · 2009–2026", EntityPageKinds.Creator.CountsLine(Page(4, 1, 2009, 2026)));
        Assert.Equal("1 series · 4 issues · 2009 · 25% read", EntityPageKinds.Publisher.CountsLine(Page(4, 1, 2009)));
        Assert.Equal("1 series · 0 issues", EntityPageKinds.Publisher.CountsLine(Page(0, 0)));
    }

    [Fact]
    public void Tab_ShowsItsCountAfterTheLabel()
    {
        EntityPageTab? picked = null;
        var tab = new EntityPageTab(EntityTabIds.Appearances, "Appearances", t => picked = t);
        Assert.Equal("Appearances", tab.Text);
        tab.Count = 1234;
        Assert.Equal("Appearances 1,234", tab.Text);
        tab.SelectCommand.Execute(null);
        Assert.Same(tab, picked);
    }

    [Fact]
    public void Tile_ShowsAMonogramWithAnInsetCover_OnlyWhenThereIsNoPortrait()
    {
        var noPortrait = new EntityTileItem(ComicMetadataEntityKind.Team, 1, "Dora Milaje", "14", null, "42", "DM");
        Assert.True(noPortrait.ShowMonogram);
        Assert.True(noPortrait.HasInsetCover);
        var portrait = noPortrait with { ImagePath = "c:\\x.jpg" };
        Assert.False(portrait.ShowMonogram);
        Assert.False(portrait.HasInsetCover);
        Assert.Null(EntityTileItem.CoverKeyFor(42, _ => false));
        Assert.Equal("42", EntityTileItem.CoverKeyFor(42, _ => true));
    }
}
