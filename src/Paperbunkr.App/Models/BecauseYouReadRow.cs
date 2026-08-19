using System.Collections.ObjectModel;

namespace Paperbunkr.App.Models;

/// <summary>Home screen's "Because you read {SeedSeriesName}" row (docs/superpowers/specs/
/// 2026-08-18-home-screen-design.md Module 3) - one per recently-opened seed series that produced at
/// least one <c>RecommendationResolver</c> candidate; seeds with zero candidates never get a row.</summary>
public sealed class BecauseYouReadRow
{
    public required string SeedSeriesName { get; init; }
    public required ObservableCollection<SeriesCardSample> Cards { get; init; }
}
