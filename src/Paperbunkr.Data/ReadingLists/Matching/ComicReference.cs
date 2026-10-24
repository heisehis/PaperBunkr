namespace Paperbunkr.Data.ReadingLists.Matching;

/// <summary>
/// One comic as a source asked for it (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.3) - a .cbl <c>&lt;Book&gt;</c>, a
/// .csv row, or one issue of an online reading order. Provider ids are present only when the source itself supplied them; nothing here is
/// ever derived from a title. <see cref="Year"/> is the issue's own year (cover or store date), not the series' start year.
/// </summary>
public sealed record ComicReference(
    string Series,
    string Number,
    string? Volume = null,
    int? Year = null,
    string? Format = null,
    string? Title = null,
    string? SourceKey = null,
    string? SourceEntryId = null,
    string? ComicVineIssueId = null,
    string? ComicVineSeriesId = null,
    string? MetronIssueId = null,
    string? MetronSeriesId = null)
{
    public string SeriesKey => SeriesKeys.Of(Series);

    public string NumberKey => IssueNumberKey.Of(Number);

    /// <summary>"Series #Number (Year)" - for reports and messages.</summary>
    public string Display => Year is int y ? $"{Series} #{Number} ({y})" : $"{Series} #{Number}";
}
