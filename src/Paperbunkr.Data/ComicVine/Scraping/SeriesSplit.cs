using System.Globalization;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>Where a matched comic belongs, and whether its match may speak for the whole series (rename it, fill its fields, link it).</summary>
public sealed record SeriesPlacement(Series Target, bool Moved, bool OwnsSeries, string? PreviousSeriesName);

/// <summary>
/// Keeps one comic's match from rewriting a series that other comics share (docs/superpowers/specs/2026-10-22-scrape-series-split-and-series-
/// fields-design.md). A library series is one row for all its comics, unlike ComicRack's per-book "Series" text, so a match to a different
/// source series than the one the series is linked to moves the comic out - to the series linked to that source series, else one of the same
/// name, else a new one - instead of renaming everything ("Secret Wars" and "Secret Wars 2099"; "The Amazing Spider-Man" renamed after its
/// Halloween Special). A series marked keep-together (a user merge) is never split.
/// </summary>
public static class SeriesSplit
{
    /// <summary>The <see cref="Series.LockedFields"/> key a manual merge sets: scrapes never split this series nor relink it.</summary>
    public const string KeepTogetherKey = "KeepTogether";

    public static bool KeepsTogether(Series series) => MangaFieldLocks.IsLocked(series, KeepTogetherKey);

    public static void SetKeepTogether(Series series, bool keep)
    {
        var locks = MangaFieldLocks.Parse(series.LockedFields);
        if (keep)
        {
            locks.Add(KeepTogetherKey);
        }
        else
        {
            locks.Remove(KeepTogetherKey);
        }

        series.LockedFields = MangaFieldLocks.Join(locks);
    }

    /// <summary>The source series id a library series is linked to, or null.</summary>
    public static string? LinkOf(PaperbunkrDbContext context, int seriesId, ComicProvider provider) =>
        context.ComicMetadataExternalIds
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Series && e.EntityId == seriesId && e.Provider == provider)
            .Select(e => e.ExternalId)
            .FirstOrDefault();

    /// <summary>
    /// Decides where <paramref name="tracked"/> goes for a match to <paramref name="volume"/>, moving it (in <paramref name="context"/>, unsaved)
    /// when its series is linked elsewhere. A comic alone in its series is never moved: the match corrects that series instead.
    /// </summary>
    public static SeriesPlacement Resolve(PaperbunkrDbContext context, Issue tracked, ComicProvider provider, ComicVineVolumeSearchResult volume)
    {
        var series = tracked.Series;
        if (series is null)
        {
            throw new InvalidOperationException("The issue's series must be loaded.");
        }

        string wanted = volume.Id.ToString(CultureInfo.InvariantCulture);
        string? link = series.Id == 0 ? null : LinkOf(context, series.Id, provider);
        if (link == wanted)
        {
            return new SeriesPlacement(series, false, true, null);
        }

        int comics = series.Id == 0 ? 1 : context.Issues.Count(i => i.SeriesId == series.Id);
        Series? linked = FindLinked(context, provider, wanted, series.Id);

        // Mismatch: linked to another source series, or (no link yet) holding other comics under a name the match doesn't share - "Secret
        // Wars" matched to "Secret Wars 2099" must not rename the series it shares with the Secret Wars comics (reported 2026-10-22: the first
        // match of an unlinked series used to own it outright).
        bool mismatch = link is not null || (comics > 1 && !ComicFormatHints.SameTitle(series.Name, volume.Name));
        if (KeepsTogether(series))
        {
            return new SeriesPlacement(series, false, link is null, null);
        }

        if (!mismatch || comics <= 1)
        {
            // Nothing to split from: the match speaks for this series - unless another series already is that source series, in which case a
            // lone comic joins it rather than making a second copy of it.
            return comics <= 1 && linked is not null
                ? Move(tracked, series, linked)
                : new SeriesPlacement(series, false, true, null);
        }

        Series? target = linked;
        string lowered = volume.Name.Trim().ToLowerInvariant();
        target ??= context.Series
            .Where(s => s.Id != series.Id && s.RemoteSourceId == null && s.Name.ToLower() == lowered)
            .AsEnumerable()
            .FirstOrDefault(s => LinkOf(context, s.Id, provider) is null);

        if (target is null)
        {
            // A same-named series that is a different source series (another volume) keeps its name; this one says its start year.
            bool nameTaken = context.Series.Any(s => s.Id != series.Id && s.Name.ToLower() == lowered);
            target = new Series
            {
                Name = nameTaken && !string.IsNullOrWhiteSpace(volume.StartYear) ? $"{volume.Name.Trim()} ({volume.StartYear})" : volume.Name.Trim(),
                ContentType = series.ContentType,
                ContentTypeSource = series.ContentTypeSource,
                ContentTypeLocked = series.ContentTypeLocked,
                ReadingMode = series.ReadingMode,
                PageLayoutMode = series.PageLayoutMode,
                PageFitModeOverride = series.PageFitModeOverride,
                AutoRotateOverride = series.AutoRotateOverride,
                ReaderProfileId = series.ReaderProfileId,
                EmbeddedSyncEnabled = series.EmbeddedSyncEnabled,
            };
            context.Series.Add(target);
        }

        return Move(tracked, series, target);
    }

    private static Series? FindLinked(PaperbunkrDbContext context, ComicProvider provider, string externalId, int exceptSeriesId)
    {
        int id = context.ComicMetadataExternalIds
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Series && e.Provider == provider && e.ExternalId == externalId && e.EntityId != exceptSeriesId)
            .Select(e => e.EntityId)
            .FirstOrDefault();
        return id == 0 ? null : context.Series.Find(id);
    }

    private static SeriesPlacement Move(Issue tracked, Series from, Series to)
    {
        tracked.Series = to;
        if (to.Id != 0)
        {
            tracked.SeriesId = to.Id;
        }

        return new SeriesPlacement(to, true, true, from.Name);
    }

    /// <summary>The series' name before a scrape first renamed it, if one did.</summary>
    public static string? NameBeforeScrape(Series series) =>
        series.Titles.FirstOrDefault(t => t.Type == SeriesTitleType.BeforeScrape)?.Value;

    /// <summary>Keeps the current name as the before-scrape name, once: the first rename is the one worth undoing.</summary>
    public static void RememberNameBeforeRename(Series series)
    {
        if (series.Titles.All(t => t.Type != SeriesTitleType.BeforeScrape))
        {
            series.Titles.Add(new SeriesTitle { Value = series.Name, Type = SeriesTitleType.BeforeScrape });
        }
    }
}
