using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>How a screen change is classified for the route transition (docs/superpowers/specs/2026-10-20-motion-system-v2-navigation-design.md §3.2).</summary>
public class RouteIntentTests
{
    [Theory]
    [InlineData("home", "library", DrillTransitionKind.None, RouteIntent.Lateral)]
    [InlineData("library", "home", DrillTransitionKind.Push, RouteIntent.Lateral)]       // a rail switch is lateral whatever came before
    [InlineData("library", "detail", DrillTransitionKind.Push, RouteIntent.DrillIn)]
    [InlineData("detail", "library", DrillTransitionKind.Pop, RouteIntent.Back)]
    [InlineData("detail", "reader", DrillTransitionKind.Push, RouteIntent.Reader)]
    [InlineData("reader", "detail", DrillTransitionKind.Pop, RouteIntent.Reader)]
    [InlineData("detail", "library", DrillTransitionKind.None, RouteIntent.Neutral)]     // no drill pending: unknown direction fades
    [InlineData("library", "compare", DrillTransitionKind.Push, RouteIntent.DrillIn)]
    public void ClassifiesTheChange(string from, string to, DrillTransitionKind pending, RouteIntent expected) =>
        Assert.Equal(expected, MainViewModel.ComputeRouteIntent(from, to, pending));
}
