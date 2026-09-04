using System.Linq;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers <see cref="DetailBandViewModel"/> (docs/superpowers/specs/
/// 2026-08-28-detail-screens-streaming-redesign-design.md) - the band that replaced
/// <c>DetailMetaViewModel</c> + <c>DetailPillsViewModel</c>. Focus: tamed groups (cap + expand),
/// the CVDB junk-tag filter, empty-group suppression, and the Credits group's Writer/Artist-only
/// scope.
/// </summary>
public class DetailBandViewModelTests
{
    private static Issue IssueWith(params (IssueTagField Field, string Value)[] tags)
    {
        var issue = new Issue();
        foreach (var group in tags.GroupBy(t => t.Field))
        {
            issue.MergeFrom(group.Key, group.Select(t => t.Value).ToArray());
        }

        return issue;
    }

    [Fact]
    public void JunkTagPattern_MatchesOnlyComicVineIds()
    {
        Assert.Matches(DetailBandViewModel.JunkTagPattern, "CVDB1073108");
        Assert.Matches(DetailBandViewModel.JunkTagPattern, "cvdb9");
        Assert.DoesNotMatch(DetailBandViewModel.JunkTagPattern, "CVDBX");
        Assert.DoesNotMatch(DetailBandViewModel.JunkTagPattern, "Absolute Batman");
        Assert.DoesNotMatch(DetailBandViewModel.JunkTagPattern, "DC All-In");
    }

    [Fact]
    public void LoadIssue_TagsGroup_HidesCvdbIdsAndCountsThem()
    {
        var issue = IssueWith(
            (IssueTagField.Tags, "DC All-In"),
            (IssueTagField.Tags, "Variant Cover"),
            (IssueTagField.Tags, "CVDB1073108"),
            (IssueTagField.Tags, "CVDB1083601"),
            (IssueTagField.Tags, "CVDB1095530"));

        var vm = new DetailBandViewModel();
        vm.LoadIssue(issue);

        var tags = vm.Groups.Single(g => g.Label == "Tags");
        Assert.Equal(new[] { "DC All-In", "Variant Cover" }, tags.Chips.Select(c => c.Value));
        Assert.Equal(3, tags.HiddenCount);
        Assert.True(tags.HasHidden);

        tags.ToggleHiddenCommand.Execute(null);
        Assert.Contains("CVDB1073108", tags.Chips.Select(c => c.Value));
    }

    [Fact]
    public void Group_CapsAtTwelveUntilExpanded()
    {
        var issue = new Issue();
        issue.MergeFrom(IssueTagField.Genre, Enumerable.Range(1, 20).Select(i => $"Genre{i:D2}").ToArray());

        var vm = new DetailBandViewModel();
        vm.LoadIssue(issue);

        var genres = vm.Groups.Single(g => g.Label == "Genres & Concepts");
        Assert.Equal(DetailBandGroupViewModel.Cap, genres.Chips.Count);
        Assert.True(genres.HasOverflow);
        Assert.Equal(8, genres.OverflowCount);

        genres.ToggleExpandCommand.Execute(null);
        Assert.Equal(20, genres.Chips.Count);
        Assert.Equal("show less", genres.MoreLabel);
    }

    [Fact]
    public void EmptyGroups_AreNotAdded()
    {
        var issue = IssueWith((IssueTagField.Genre, "Superhero"));

        var vm = new DetailBandViewModel();
        vm.LoadIssue(issue);

        Assert.Contains(vm.Groups, g => g.Label == "Genres & Concepts");
        Assert.DoesNotContain(vm.Groups, g => g.Label == "Teams");
        Assert.DoesNotContain(vm.Groups, g => g.Label == "Locations");
        Assert.DoesNotContain(vm.Groups, g => g.Label == "Characters");
        Assert.DoesNotContain(vm.Groups, g => g.Label == "Tags");
        Assert.DoesNotContain(vm.Groups, g => g.IsCreditsGroup);
    }

    [Fact]
    public void CreditsGroup_HoldsWriterAndArtistOnly_AndJumpsToDetails()
    {
        bool jumped = false;
        var series = new Series { Name = "S" };
        series.Issues.Add(new Issue { Writer = "Scott Snyder", Penciller = "Nick Dragotta", Inker = "Someone Else", Editor = "Also Not Shown" });

        var vm = new DetailBandViewModel(goToDetailsTab: () => jumped = true);
        vm.LoadSeries(series);

        var credits = vm.Groups.Single(g => g.IsCreditsGroup);
        Assert.Equal(new[] { "Scott Snyder" }, credits.Writers!.Select(p => p.Value));
        Assert.Equal(new[] { "Nick Dragotta" }, credits.Artists!.Select(p => p.Value));

        credits.FullCreditsCommand!.Execute(null);
        Assert.True(jumped);
    }

    [Fact]
    public void LoadSeries_Characters_SplitFromCsv()
    {
        var series = new Series { Name = "S" };
        series.Issues.Add(new Issue { Characters = "Batman, Alfred Pennyworth" });
        series.Issues.Add(new Issue { Characters = "Batman, Black Mask" });

        var vm = new DetailBandViewModel();
        vm.LoadSeries(series);

        var characters = vm.Groups.Single(g => g.Label == "Characters");
        Assert.Equal(new[] { "Batman", "Alfred Pennyworth", "Black Mask" }, characters.Chips.Select(c => c.Value));
    }

    [Fact]
    public void LoadIssue_GenreChip_ExposesWeightClassAndDot()
    {
        var issue = new Issue();
        issue.MergeFrom(IssueTagField.Genre, new[] { "superhero", "cosmic" });
        issue.Tags.Single(t => t.Value == "superhero").Weight = IssueTagWeight.Core;
        // "cosmic" left Unset

        var vm = new DetailBandViewModel();
        vm.LoadIssue(issue);

        var genres = vm.Groups.Single(g => g.Label == "Genres & Concepts");
        var core = genres.Chips.Single(c => c.Value == "superhero");
        var unset = genres.Chips.Single(c => c.Value == "cosmic");

        Assert.Equal("core", core.WeightClass);
        Assert.True(core.IsWeighted);
        Assert.Equal("unset", unset.WeightClass);
        Assert.False(unset.IsWeighted);
    }

    [Fact]
    public void InlineMeta_FlagsTrackWhetherEachSegmentIsPresent()
    {
        var vm = new DetailBandViewModel();
        Assert.False(vm.HasStatus);
        Assert.False(vm.HasPublisher);

        vm.StatusText = "Ongoing";
        vm.PublisherText = "Image Comics";

        Assert.True(vm.HasStatus);
        Assert.True(vm.HasPublisher);
        Assert.False(vm.HasYear);
    }

    // --- Language flag (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-design.md Part 2 §A) ---

    [Fact]
    public void LoadIssue_LanguageIso_ComesFromTheIssue()
    {
        var vm = new DetailBandViewModel();
        vm.LoadIssue(new Issue { LanguageISO = "ja" });
        Assert.Equal("ja", vm.LanguageIso);
        Assert.True(vm.HasLanguage);
    }

    [Fact]
    public void LoadSeries_LanguageIso_SetOnlyWhenIssuesAgree()
    {
        var same = new Series { Name = "S" };
        same.Issues.Add(new Issue { LanguageISO = "en" });
        same.Issues.Add(new Issue { LanguageISO = "EN" });
        var vm1 = new DetailBandViewModel();
        vm1.LoadSeries(same);
        Assert.Equal("en", vm1.LanguageIso);
        Assert.True(vm1.HasLanguage);

        var mixed = new Series { Name = "S" };
        mixed.Issues.Add(new Issue { LanguageISO = "en" });
        mixed.Issues.Add(new Issue { LanguageISO = "ja" });
        var vm2 = new DetailBandViewModel();
        vm2.LoadSeries(mixed);
        Assert.Null(vm2.LanguageIso);
        Assert.False(vm2.HasLanguage);
    }
}
