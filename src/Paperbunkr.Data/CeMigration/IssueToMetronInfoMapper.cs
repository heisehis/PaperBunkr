using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cYo.Projects.ComicRack.Engine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.CeMigration;

/// <summary>
/// Overlays an <see cref="Issue"/>'s current database state onto a <see cref="MetronInfo"/> document -
/// the MetronInfo counterpart of <see cref="IssueToComicInfoMapper"/> (docs/superpowers/specs/2026-10-
/// 05-metroninfo-write-back-design.md). ComicRack CE only ever reads this format, so writing it is a
/// deliberate Paperbunkr deviation: it is what Metron-Tagger, Codex and Comicbox read, and the only
/// one of our file formats that can carry provider ids and one credit per creator.
///
/// The caller passes the file's <i>existing</i> document when it has one, so what we hold nothing for
/// (prices, universes, reprints, store date, manga volume, collection title) survives. Everything we
/// do hold is overwritten from database truth. <c>LastModified</c> is always dropped: a fresh stamp
/// would make <see cref="MetadataFileFieldSnapshot"/> differ on every check, and a stale one from
/// another tool would be a lie about content we just changed.
/// </summary>
public static class IssueToMetronInfoMapper
{
    public static void Apply(Issue issue, MetronInfo target, MetronIdContext ids)
    {
        ApplyIds(issue, target, ids);
        ApplyPublisher(issue, target, ids);
        ApplySeries(issue, target, ids);

        target.Number = NullIfEmpty(issue.EffectiveNumber());
        target.AlternativeNumber = NullIfEmpty(issue.AlternateNumber);
        target.Summary = NullIfEmpty(issue.Summary);
        target.Notes = NullIfEmpty(issue.Notes);

        // Stories is the schema's home for ComicInfo's Title. Only the first entry is ours; a file
        // another tool tagged with several stories keeps the rest.
        if (NullIfEmpty(issue.EffectiveTitle()) is { } title)
        {
            if (target.Stories.Count == 0)
            {
                target.Stories.Add(new ResourceType { Value = title });
            }
            else if (!string.Equals(target.Stories[0].Value, title, StringComparison.Ordinal))
            {
                target.Stories[0] = new ResourceType { Value = title };
            }
        }

        // xs:date needs a whole date. A cover date is a month in practice (Metron itself records
        // them as the 1st), so a missing day becomes 1; a missing month means there is no date.
        if (issue.EffectiveYear() is int year and > 0 and < 10000 && issue.Month is int month and >= 1 and <= 12)
        {
            int day = issue.Day is int d && d >= 1 && d <= DateTime.DaysInMonth(year, month) ? d : 1;
            target.CoverDate = new DateTime(year, month, day);
            target.CoverDateSpecified = true;
        }
        else
        {
            target.CoverDateSpecified = false;
        }

        if (issue.PageCount is int pageCount && pageCount > 0)
        {
            target.PageCount = pageCount;
        }

        Replace(target.Genres, TagValues(issue, IssueTagField.Genre).Select(g => new GenreType { Value = g }));
        Replace(target.Tags, TagValues(issue, IssueTagField.Tags).Select(t => new ResourceType { Value = t }));
        Replace(target.Arcs, Arcs(issue));
        Replace(target.Characters, Resources(issue.Characters, IdsFor(ids, ids.CharacterIds, target.Characters)));
        Replace(target.Teams, Resources(issue.Teams, IdsFor(ids, ids.TeamIds, target.Teams)));
        Replace(target.Locations, Resources(issue.Locations, IdsFor(ids, ids.LocationIds, target.Locations)));
        Replace(target.Credits, Credits(issue, IdsFor(ids, ids.CreatorIds, target.Credits.Select(c => c.Creator).Where(c => c is not null))));

        string? isbn = NullIfEmpty(issue.ISBN);
        string? upc = NullIfEmpty(issue.Upc);
        target.Gtin = isbn is null && upc is null ? null : new GtinType { Isbn = isbn, Upc = upc };

        target.AgeRating = MapAgeRating(issue.AgeRating);

        // The schema bounds the average to 0-5, the scale Issue.CommunityRating already uses.
        target.CommunityRating = issue.CommunityRating is float rating and > 0f and <= 5f
            ? new CommunityRatingType
            {
                AverageRating = Math.Round((decimal)rating, 2),
                RatingCount = issue.CommunityRatingCount ?? 0,
                RatingCountSpecified = issue.CommunityRatingCount is > 0,
            }
            : null;

        if (NullIfEmpty(issue.Web) is { } web)
        {
            Replace(target.UrLs, web
                .Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(url => new UrlType { Value = url }));
        }

        KeepOnePrimaryUrl(target);

        target.LastModifiedSpecified = false;
    }

    private static void ApplyIds(Issue issue, MetronInfo target, MetronIdContext ids)
    {
        var list = new List<IdType>();
        foreach (var (provider, value) in ids.IssueIds.OrderBy(pair => pair.Key))
        {
            bool primary = ids.Primary == provider;
            list.Add(new IdType
            {
                Source = provider == ComicProvider.Metron ? InformationSource.Metron : InformationSource.ComicVine,
                Value = value,
                Primary = primary,
                PrimarySpecified = primary,
            });
        }

        if (issue.GcdIssueId is int gcdId)
        {
            list.Add(new IdType { Source = InformationSource.GrandComicsDatabase, Value = gcdId.ToString(CultureInfo.InvariantCulture) });
        }

        // An id the file already carries for a source we hold nothing for (another tool's Metron id
        // on a book we never scraped, an AniList id) is still true - keep it. Only its primary flag
        // goes when we have a primary of our own, since at most one ID may carry it.
        var ours = list.Select(id => id.Source).ToHashSet();
        foreach (var foreign in target.Ids.Where(id => !ours.Contains(id.Source)).ToList())
        {
            if (ids.Primary is not null)
            {
                foreign.Primary = false;
                foreign.PrimarySpecified = false;
            }

            list.Add(foreign);
        }

        Replace(target.Ids, list);
    }

    private static void ApplyPublisher(Issue issue, MetronInfo target, MetronIdContext ids)
    {
        if (NullIfEmpty(issue.Publisher) is not { } publisher)
        {
            target.Publisher = null;
            return;
        }

        target.Publisher ??= new PublisherType();
        // With no primary of ours, an id= already in the file belongs to the file's own primary
        // source and is left alone - here and on Series - when it still names the same thing.
        bool samePublisher = string.Equals(target.Publisher.Name, publisher, StringComparison.OrdinalIgnoreCase);
        target.Publisher.Name = publisher;
        target.Publisher.Id = ids.Primary is null && samePublisher ? target.Publisher.Id : ids.PublisherId;
        target.Publisher.Imprint = NullIfEmpty(issue.Imprint) is { } imprint ? new ResourceType { Value = imprint } : null;
    }

    private static void ApplySeries(Issue issue, MetronInfo target, MetronIdContext ids)
    {
        target.Series ??= new SeriesType();

        // Series is the one required element. The navigation is only overwritten when it is loaded,
        // as in IssueToComicInfoMapper; an empty name is still a valid (if useless) document.
        string seriesName = NullIfEmpty(issue.Series?.Name) ?? target.Series.Name ?? string.Empty;
        bool sameSeries = string.Equals(target.Series.Name, seriesName, StringComparison.OrdinalIgnoreCase);
        target.Series.Name = seriesName;
        target.Series.Id = ids.Primary is null && sameSeries ? target.Series.Id : ids.SeriesId;

        if (NullIfEmpty(issue.Series?.SortName) is { } sortName)
        {
            target.Series.SortName = sortName;
        }

        if (int.TryParse(issue.EffectiveVolume(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int volume) && volume >= 0)
        {
            target.Series.Volume = volume;
            target.Series.VolumeSpecified = true;
        }
        else
        {
            target.Series.VolumeSpecified = false;
        }

        if (issue.EffectiveCount() is int count and > 0)
        {
            target.Series.IssueCount = count;
            target.Series.IssueCountSpecified = true;
        }
        else
        {
            target.Series.IssueCountSpecified = false;
        }

        if (MapFormat(issue.EffectiveFormat()) is { } format)
        {
            target.Series.Format = format;
            target.Series.FormatSpecified = true;
        }
        else
        {
            target.Series.FormatSpecified = false;
        }

        // The schema's lang is exactly two lower-case letters; anything else ("en-US", "English")
        // would be an invalid document, so it is left as the file had it.
        string? language = issue.LanguageISO?.Trim().ToLowerInvariant();
        if (language is { Length: 2 } && language.All(c => c is >= 'a' and <= 'z'))
        {
            target.Series.Lang = language;
        }
    }

    private static IEnumerable<ArcType> Arcs(Issue issue)
    {
        // StoryArc and StoryArcNumber are parallel comma lists, as in ComicInfo.
        string[] names = SplitList(issue.StoryArc);
        string[] numbers = (issue.StoryArcNumber ?? string.Empty).Split(',');
        for (int i = 0; i < names.Length; i++)
        {
            var arc = new ArcType { Name = names[i] };
            if (i < numbers.Length && int.TryParse(numbers[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number > 0)
            {
                arc.Number = number;
                arc.NumberSpecified = true;
            }

            yield return arc;
        }
    }

    /// <summary>
    /// The name-to-id table for one kind of resource: ours when the document has a primary of ours,
    /// otherwise whatever ids the file's own entries already carry (they belong to the file's primary).
    /// </summary>
    private static IReadOnlyDictionary<string, string> IdsFor(MetronIdContext ids, IReadOnlyDictionary<string, string> ours, IEnumerable<ResourceType> existing)
    {
        if (ids.Primary is not null)
        {
            return ours;
        }

        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in existing)
        {
            if (!string.IsNullOrWhiteSpace(resource.Value) && !string.IsNullOrWhiteSpace(resource.Id))
            {
                kept[resource.Value.Trim()] = resource.Id;
            }
        }

        return kept;
    }

    private static IEnumerable<ResourceType> Resources(string? names, IReadOnlyDictionary<string, string> idsByName) =>
        CharacterResolver.ParseNames(names).Select(name => new ResourceType
        {
            Value = name,
            Id = idsByName.TryGetValue(name, out string? id) ? id : null,
        });

    /// <summary>
    /// One <c>Credit</c> per creator, carrying every role that person holds - the schema allows each
    /// creator once. Our eight credit fields are all the role detail there is to write.
    /// </summary>
    private static IEnumerable<CreditType> Credits(Issue issue, IReadOnlyDictionary<string, string> creatorIds)
    {
        var fields = new (string? Names, RoleValues Role)[]
        {
            (issue.Writer, RoleValues.Writer),
            (issue.Penciller, RoleValues.Penciller),
            (issue.Inker, RoleValues.Inker),
            (issue.Colorist, RoleValues.Colorist),
            (issue.Letterer, RoleValues.Letterer),
            (issue.CoverArtist, RoleValues.Cover),
            (issue.Editor, RoleValues.Editor),
            (issue.Translator, RoleValues.Translator),
        };

        var credits = new List<CreditType>();
        var byName = new Dictionary<string, CreditType>(StringComparer.OrdinalIgnoreCase);
        foreach (var (names, role) in fields)
        {
            foreach (string name in CharacterResolver.ParseNames(names))
            {
                if (!byName.TryGetValue(name, out var credit))
                {
                    credit = new CreditType
                    {
                        Creator = new ResourceType { Value = name, Id = creatorIds.TryGetValue(name, out string? id) ? id : null },
                    };
                    byName[name] = credit;
                    credits.Add(credit);
                }

                credit.Roles.Add(new RoleType { Value = role });
            }
        }

        return credits;
    }

    private static void KeepOnePrimaryUrl(MetronInfo target)
    {
        bool seen = false;
        foreach (var url in target.UrLs)
        {
            if (url.PrimarySpecified && url.Primary)
            {
                if (seen)
                {
                    url.Primary = false;
                    url.PrimarySpecified = false;
                }

                seen = true;
            }
        }
    }

    /// <summary>
    /// ComicInfo's free-text Format to the schema's nine series types. Anything not plainly one of
    /// them ("Preview", "Director's Cut") has no MetronInfo equivalent and is left out.
    /// </summary>
    internal static FormatType? MapFormat(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        "annual" => FormatType.Annual,
        "digital chapter" or "digital" => FormatType.DigitalChapter,
        "graphic novel" or "gn" or "ogn" => FormatType.GraphicNovel,
        "hardcover" or "hc" => FormatType.Hardcover,
        "limited series" or "mini-series" or "maxi-series" => FormatType.LimitedSeries,
        "omnibus" => FormatType.Omnibus,
        "one-shot" or "one shot" or "oneshot" => FormatType.OneShot,
        "single issue" or "series" => FormatType.SingleIssue,
        "trade paperback" or "tpb" or "trade" => FormatType.TradePaperback,
        _ => null,
    };

    /// <summary>
    /// ComicInfo's age-rating vocabulary (and the names a Metron scrape stores) to the schema's seven,
    /// following the schema's own rating matrix. Unrecognised text stays Unknown, which is omitted.
    /// </summary>
    internal static AgeRatingType MapAgeRating(string? ageRating) => ageRating?.Trim().ToLowerInvariant() switch
    {
        "everyone" or "all ages" or "early childhood" or "g" or "kids to adults" or "everyone 10+" => AgeRatingType.Everyone,
        "teen" or "pg" or "t" or "13+" => AgeRatingType.Teen,
        "teen plus" or "t+" or "15+" or "ma15+" => AgeRatingType.TeenPlus,
        "mature" or "mature 17+" or "m" or "17+" => AgeRatingType.Mature,
        "explicit" or "r18+" => AgeRatingType.Explicit,
        "adult" or "adults only 18+" or "x18+" => AgeRatingType.Adult,
        _ => AgeRatingType.Unknown,
    };

    private static IEnumerable<string> TagValues(Issue issue, IssueTagField field) =>
        issue.Tags
            .Where(t => t.Field == field && !string.IsNullOrWhiteSpace(t.Value))
            .Select(t => t.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string[] SplitList(string? text) =>
        (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void Replace<T>(System.Collections.ObjectModel.Collection<T> collection, IEnumerable<T> items)
    {
        var list = items.ToList();
        collection.Clear();
        foreach (var item in list)
        {
            collection.Add(item);
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
