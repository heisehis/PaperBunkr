using Paperbunkr.App.Services;

namespace Paperbunkr.App.Models;

public sealed partial class SeriesCardSample : ISpineSource
{
    public double SpineWidth => SpineMetrics.WidthForSeries(IssueCount);

    public double SpineHeight => SpineMetrics.HeightFor(SeriesId);

    public string SpineTitle => Name;

    public string? SpineNumber => IssueCount > 1 ? IssueCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
}
