using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="TagPillContextMenuBuilder"/> (docs/superpowers/specs/2026-08-31-keyboard-
/// operability-design.md) - ports a menu formerly dead in two places (<c>ReadingScreen.axaml</c>'s
/// tag pills and the shared <c>DetailBand.axaml</c>'s tag chips), found during a broader sweep for
/// dead menus beyond the original four.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class TagPillContextMenuBuilderTests
{
    private static TagPillViewModel MakePill(bool reweightable = true, IssueTagWeight weight = IssueTagWeight.Unset) =>
        new("Tag", "Category", weight, search: _ => { }, reweight: reweightable ? _ => { } : null);

    [Fact]
    public void Build_ReweightablePill_ReturnsFourWeightEntries()
    {
        var pill = MakePill();
        var builder = new TagPillContextMenuBuilder();

        var entries = builder.Build(pill);

        Assert.NotNull(entries);
        Assert.Equal(new[] { "Incidental", "Recurrent", "Defining", "Core" }, entries!.Select(e => e.Header));
        Assert.Same(pill.SetWeightCommand, entries[0].Command);
    }

    [Fact]
    public void Build_ReweightablePill_ChecksTheCurrentWeight()
    {
        var pill = MakePill(weight: IssueTagWeight.Defining);
        var builder = new TagPillContextMenuBuilder();

        var entries = builder.Build(pill);

        Assert.True(entries!.Single(e => e.Header == "Defining").IsChecked);
        Assert.False(entries.Single(e => e.Header == "Core").IsChecked);
    }

    [Fact]
    public void Build_NonReweightablePill_ReturnsNull()
    {
        var pill = MakePill(reweightable: false);
        var builder = new TagPillContextMenuBuilder();

        Assert.Null(builder.Build(pill));
    }

    [Fact]
    public void Build_UnrecognizedTarget_ReturnsNull()
    {
        var builder = new TagPillContextMenuBuilder();

        Assert.Null(builder.Build(new object()));
        Assert.Null(builder.Build(null));
    }
}
