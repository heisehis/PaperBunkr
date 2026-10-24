using Paperbunkr.App.Services;

namespace Paperbunkr.App.Models;

public sealed partial class IssueListRow : ISpineSource
{
    public double SpineWidth => SpineMetrics.WidthForPages(PageCount);

    public double SpineHeight => SpineMetrics.HeightFor(Id);

    public string SpineTitle => SeriesName;

    public string? SpineNumber => string.IsNullOrWhiteSpace(Number) ? null : "#" + Number;
}
