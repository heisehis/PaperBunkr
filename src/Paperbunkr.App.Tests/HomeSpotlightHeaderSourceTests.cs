using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="HomeSpotlightHeaderSource"/> adapts the Home spotlight pick to the shared
/// <see cref="IDetailHeaderSource"/> so the Home hero renders through <c>DetailHero</c>
/// (docs/superpowers/specs/2026-08-28-home-screen-redesign-design.md §3).
/// </summary>
public class HomeSpotlightHeaderSourceTests
{
    private static SpotlightIssueSample Pick(string title = "Eleven Days of Thirst") => new()
    {
        IssueId = 7,
        SeriesId = 3,
        SeriesName = "The Salt Marches",
        Title = title,
        CoverBrush = Brushes.SlateGray,
        Meta = "#7 · 28 pages · New",
        Synopsis = "The caravan finds a statue that was not there on the way out.",
    };

    [Fact]
    public void MapsEveryHeaderMember_FromTheCurrentPick()
    {
        var pick = Pick();
        var source = new HomeSpotlightHeaderSource(() => pick, new RelayCommand(() => { }));

        Assert.Equal("Eleven Days of Thirst", source.HeaderTitle);
        Assert.Equal("The Salt Marches", source.SecondaryTitle);
        Assert.Equal("#7 · 28 pages · New", source.MetaLine);
        Assert.Equal("The caravan finds a statue that was not there on the way out.", source.Synopsis);
        Assert.Same(pick.CoverBrush, source.CoverBrush);
        Assert.Null(source.TrackerProgress);
    }

    [Fact]
    public void ExposesASinglePrimaryReadNowAction_BoundToTheGivenCommand()
    {
        var invoked = false;
        var source = new HomeSpotlightHeaderSource(() => Pick(), new RelayCommand(() => invoked = true));

        var action = Assert.Single(source.Actions);
        Assert.Equal("Read now", action.Label);
        Assert.True(action.IsPrimary);

        action.Command.Execute(null);
        Assert.True(invoked);
    }

    [Fact]
    public void ReadsThePickLive_AndRaiseChanged_NotifiesAllMembers()
    {
        var current = Pick("First");
        var source = new HomeSpotlightHeaderSource(() => current, new RelayCommand(() => { }));

        string? changedName = "unset";
        ((INotifyPropertyChanged)source).PropertyChanged += (_, e) => changedName = e.PropertyName;

        current = Pick("Second");
        source.RaiseChanged();

        Assert.Equal("Second", source.HeaderTitle);
        Assert.True(string.IsNullOrEmpty(changedName));
    }

    [Fact]
    public void ToleratesANullPick()
    {
        var source = new HomeSpotlightHeaderSource(() => null, new RelayCommand(() => { }));

        Assert.Equal(string.Empty, source.HeaderTitle);
        Assert.Equal(string.Empty, source.MetaLine);
        Assert.Null(source.SecondaryTitle);
        Assert.Null(source.Synopsis);
        Assert.Null(source.CoverImage);
        Assert.NotNull(source.CoverBrush);
    }
}
