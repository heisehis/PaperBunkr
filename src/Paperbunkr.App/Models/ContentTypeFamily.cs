using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Which family a <see cref="ContentType"/> belongs to - the one rule behind routing a series to the Manga
/// Detail screen (<c>MainViewModel.LoadDetailSeries</c>) and the Insights History tab's Comics/Manga split
/// (docs/superpowers/specs/2026-09-29-insights-reading-history-design.md §2).
/// </summary>
public static class ContentTypeFamily
{
    public static bool IsManga(ContentType contentType) =>
        contentType is ContentType.Manga or ContentType.Manhua or ContentType.Manhwa;
}
