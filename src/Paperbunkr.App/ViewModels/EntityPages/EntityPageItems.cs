using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels.EntityPages;

/// <summary>One column of the year chart (entity pages redesign §5.4). <see cref="BarHeight"/> is in pixels of a <see cref="YearChartModel.Height"/>-tall plot.</summary>
public sealed record YearColumn(string Label, int Count, double BarHeight)
{
    public string Tip => $"{Label} · {Count:N0}";
}

/// <summary>The year column chart's whole input: columns, the axis captions and the "N undated" note.</summary>
public sealed record YearChartModel(IReadOnlyList<YearColumn> Columns, string FirstLabel, string LastLabel, string Note)
{
    public const double Height = 64;
    public static readonly YearChartModel Empty = new(System.Array.Empty<YearColumn>(), string.Empty, string.Empty, string.Empty);
    public bool HasColumns => Columns.Count > 0;
    public bool HasNote => Note.Length > 0;
    public int ColumnCount => System.Math.Max(1, Columns.Count);

    public static YearChartModel From(IReadOnlyList<EntityPageYear> years)
    {
        var dated = years.Where(y => y.Year is not null).OrderBy(y => y.Year).ToList();
        int undated = years.Where(y => y.Year is null).Sum(y => y.Count);
        if (dated.Count == 0) return Empty with { Note = undated > 0 ? $"{undated:N0} undated" : string.Empty };
        int max = dated.Max(y => y.Count);
        var columns = dated.Select(y => new YearColumn(y.Year!.Value.ToString(), y.Count, System.Math.Max(2, Height * y.Count / max))).ToList();
        return new YearChartModel(columns, columns[0].Label, columns.Count > 1 ? columns[^1].Label : string.Empty, undated > 0 ? $"{undated:N0} undated" : string.Empty);
    }
}

/// <summary>A cover cell in an entity page grid: an issue (Appearances / Credits) or a series poster with read progress (Series).</summary>
public sealed record EntityPosterItem(int Id, string Title, string Caption, string? CoverKey, double? Progress, ICommand OpenCommand, bool IsSeries = false)
{
    public bool HasProgress => Progress is not null;
    public double ProgressPercent => (Progress ?? 0) * 100;
}

/// <summary>One bar of a creator's career timeline (§4.3): <see cref="Start"/> and <see cref="End"/> are 0..1 across the creator's active years.</summary>
public sealed record CareerRunItem(string Series, string Subtitle, string ReadLabel, double Start, double End, bool IsWriter, ICommand ReadCommand, ICommand SaveCommand)
{
    public string Tip => $"{Series} · {Subtitle}";
}
