using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>First test coverage for <see cref="TagEditRowViewModel"/> - previously had none.
/// Exercises <see cref="TagEditRowViewModel.SetWeightCommand"/>, added for the segmented Weight
/// picker (docs/superpowers/specs/2026-09-14-metadata-editors-redesign-design.md §6).</summary>
public class TagEditRowViewModelTests
{
    [Fact]
    public void SetWeightCommand_SetsWeightAndWeightText()
    {
        var row = new TagEditRowViewModel("Superhero", category: null, IssueTagWeight.Unset);

        row.SetWeightCommand.Execute(IssueTagWeight.Defining);

        Assert.Equal(IssueTagWeight.Defining, row.Weight);
        Assert.Equal("Defining", row.WeightText);
    }

    [Fact]
    public void SetWeightCommand_CanRoundTripThroughAllValues()
    {
        var row = new TagEditRowViewModel("Cosmic Horror", category: "Theme", IssueTagWeight.Unset);

        foreach (var weight in Enum.GetValues<IssueTagWeight>())
        {
            row.SetWeightCommand.Execute(weight);
            Assert.Equal(weight, row.Weight);
        }
    }
}
