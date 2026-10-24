using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// One column of a detail list (docs/superpowers/specs/2026-10-19-detail-layouts-v2-design.md). <see cref="Field"/> is an
/// <see cref="IssueListSortField"/> name (any Library field with a Display projection) or a detail-only key
/// (<see cref="DetailColumnKeys"/>). A string so one list holds both kinds and v1 JSON (<c>"Field":"FullTitle"</c>) still reads.
/// </summary>
public sealed record DetailListColumn(string Field, bool Visible = true, double Width = 120);

/// <summary>
/// State saved for one detail type (v2). Comic: Poster captions and overlays, List columns, Card elements, cover fit, sort and
/// group. Manga: chapter columns, sort and group. The <c>Legacy*</c> members only read v1 JSON and are never written.
/// </summary>
public sealed record DetailListLayoutState(
    DetailIssueViewMode ViewMode = DetailIssueViewMode.Poster,
    IReadOnlyList<IssueListSortField>? CaptionFields = null,
    bool HideCaptions = false,
    bool ShowReadOverlay = true,
    bool ShowEditionOverlay = true,
    IReadOnlyList<DetailListColumn>? ComicColumns = null,
    IReadOnlyList<TileTextElement>? CardElements = null,
    bool ShowCardStatus = true,
    LibraryGridCoverFit CoverFit = LibraryGridCoverFit.Poster,
    IssueListSortField SortField = IssueListSortField.Number,
    SortDirection SortDirection = SortDirection.Ascending,
    int? SortVirtualTagId = null,
    string GroupBy = DetailGrouping.Run,
    IReadOnlyList<DetailListColumn>? ChapterColumns = null,
    IssueListSortField ChapterSortBy = IssueListSortField.Number,
    SortDirection ChapterSortDirection = SortDirection.Ascending,
    int? ChapterSortVirtualTagId = null,
    string ChapterGroupBy = DetailGrouping.Volume,
    [property: JsonPropertyName("PosterFields"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? LegacyPosterFields = null,
    [property: JsonPropertyName("CardFields"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? LegacyCardFields = null,
    [property: JsonPropertyName("ChapterSortField"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LegacyChapterSortField = null)
{
    public const int MaxCaptionLines = ListLayoutState.MaxCaptionLines;

    public static DetailListLayoutState ComicDefault { get; } = new(
        ViewMode: DetailIssueViewMode.Poster,
        CaptionFields: new[] { IssueListSortField.Number, IssueListSortField.StoryArc },
        ComicColumns: new[]
        {
            new DetailListColumn(DetailColumnKeys.ReadStatus, true, 54),
            new DetailListColumn(nameof(IssueListSortField.Number), true, 48),
            new DetailListColumn(nameof(IssueListSortField.Title), true, 220),
            new DetailListColumn(nameof(IssueListSortField.StoryArc), true, 150),
            new DetailListColumn(nameof(IssueListSortField.Released), true, 92),
            new DetailListColumn(nameof(IssueListSortField.Rating), true, 48),
        },
        CardElements: new[] { TileTextElement.Title, TileTextElement.Number });

    public static DetailListLayoutState MangaDefault { get; } = new(
        ChapterColumns: new[]
        {
            new DetailListColumn(nameof(IssueListSortField.Number), true, 60),
            new DetailListColumn(nameof(IssueListSortField.Title), true, 250),
            new DetailListColumn(DetailColumnKeys.NewStatus, true, 48),
            new DetailListColumn(DetailColumnKeys.ReadStatus, true, 48),
            new DetailListColumn(DetailColumnKeys.Bookmark, true, 50),
            new DetailListColumn(DetailColumnKeys.Missing, true, 58),
            new DetailListColumn(nameof(IssueListSortField.ScanInformation), true, 120),
            new DetailListColumn(nameof(IssueListSortField.Released), true, 110),
            new DetailListColumn(DetailColumnKeys.Progress, true, 74),
        });
}

/// <summary>The grouping values a detail layout stores: today's grouping, none, or a Library group field / virtual tag.</summary>
public static class DetailGrouping
{
    /// <summary>Comic default: one section per run (volume), headed "{writer} ({volume})" - the grouping the Issues tab always had.</summary>
    public const string Run = "run";

    /// <summary>Manga default: one section per volume, "No volume" last.</summary>
    public const string Volume = "volume";

    public const string None = "none";

    public const string VirtualTagPrefix = "vtag:";

    public static string ForField(IssueListGroupField field) => field.ToString();

    public static string ForVirtualTag(int id) => VirtualTagPrefix + id;

    public static IssueListGroupField? FieldOf(string? value) =>
        Enum.TryParse<IssueListGroupField>(value, out var field) && IssueListFieldCatalog.GroupFields.ContainsKey(field) ? field : null;

    public static int? VirtualTagOf(string? value) =>
        value is not null && value.StartsWith(VirtualTagPrefix, StringComparison.Ordinal) && int.TryParse(value[VirtualTagPrefix.Length..], out int id) ? id : null;
}

/// <summary>The detail-only column keys, and how every column key reads as a name.</summary>
public static class DetailColumnKeys
{
    /// <summary>The read / in-progress glyph (comic and manga).</summary>
    public const string ReadStatus = "ReadStatus";

    public const string NewStatus = "NewStatus";
    public const string Bookmark = "Bookmark";
    public const string Missing = "Missing";
    public const string Progress = "Progress";

    public static IReadOnlyList<string> ComicOnly { get; } = new[] { ReadStatus };

    public static IReadOnlyList<string> MangaOnly { get; } = new[] { NewStatus, ReadStatus, Bookmark, Missing, Progress };

    /// <summary>Library fields that can be a column (they have a Display projection), in catalog order.</summary>
    public static IReadOnlyList<IssueListSortField> CatalogColumns { get; } =
        IssueListFieldCatalog.ColumnFields.Select(d => d.Field).ToList();

    public static bool IsDetailOnly(string key, bool manga) => (manga ? MangaOnly : ComicOnly).Contains(key);

    public static IssueListSortField? CatalogField(string key) =>
        Enum.TryParse<IssueListSortField>(key, out var field) && IssueListFieldCatalog.SortFields.TryGetValue(field, out var d) && d.Display is not null
            ? field
            : null;

    public static bool IsStatus(string key) => key is ReadStatus or NewStatus or Bookmark or Missing;

    public static string DisplayName(string key, bool manga) => key switch
    {
        ReadStatus => "Read status",
        NewStatus => "New status",
        Bookmark => "Bookmark",
        Missing => "Missing file",
        Progress => "Reading progress",
        nameof(IssueListSortField.Number) => manga ? "Chapter number" : "Issue number",
        _ => CatalogField(key) is { } field ? IssueListFieldCatalog.SortFields[field].DisplayName : key,
    };
}

/// <summary>Tolerant JSON, v1 upgrade and catalog normalization for detail-screen layouts.</summary>
public static class DetailListLayoutStateJson
{
    public const double MinColumnWidth = 24;
    public const double MaxColumnWidth = 800;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(DetailListLayoutState state) => JsonSerializer.Serialize(Normalize(state, manga: false), Options);

    public static DetailListLayoutState Deserialize(string? json, bool manga)
    {
        var fallback = manga ? DetailListLayoutState.MangaDefault : DetailListLayoutState.ComicDefault;
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            return Normalize(JsonSerializer.Deserialize<DetailListLayoutState>(json, Options) ?? fallback, manga);
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
    }

    /// <summary>Upgrades v1 members, drops unknown fields, clamps widths and fills anything missing from the built-in defaults.</summary>
    public static DetailListLayoutState Normalize(DetailListLayoutState state, bool manga = false)
    {
        var comic = DetailListLayoutState.ComicDefault;
        var mangaDefaults = DetailListLayoutState.MangaDefault;
        state = Upgrade(state);

        return state with
        {
            ViewMode = Enum.IsDefined(state.ViewMode) ? state.ViewMode : comic.ViewMode,
            CaptionFields = NormalizeCaptions(state.CaptionFields, comic.CaptionFields!),
            ComicColumns = NormalizeColumns(state.ComicColumns, comic.ComicColumns!, manga: false),
            CardElements = NormalizeElements(state.CardElements, comic.CardElements!),
            CoverFit = Enum.IsDefined(state.CoverFit) ? state.CoverFit : LibraryGridCoverFit.Poster,
            SortField = SortOrDefault(state.SortField, state.SortVirtualTagId),
            SortDirection = Enum.IsDefined(state.SortDirection) ? state.SortDirection : SortDirection.Ascending,
            SortVirtualTagId = state.SortField == IssueListSortField.VirtualTag ? state.SortVirtualTagId : null,
            GroupBy = NormalizeGroup(state.GroupBy, DetailGrouping.Run),
            ChapterColumns = NormalizeColumns(state.ChapterColumns, mangaDefaults.ChapterColumns!, manga: true),
            ChapterSortBy = SortOrDefault(state.ChapterSortBy, state.ChapterSortVirtualTagId),
            ChapterSortDirection = Enum.IsDefined(state.ChapterSortDirection) ? state.ChapterSortDirection : SortDirection.Ascending,
            ChapterSortVirtualTagId = state.ChapterSortBy == IssueListSortField.VirtualTag ? state.ChapterSortVirtualTagId : null,
            ChapterGroupBy = NormalizeGroup(state.ChapterGroupBy, DetailGrouping.Volume),
            LegacyPosterFields = null,
            LegacyCardFields = null,
            LegacyChapterSortField = null,
        };
    }

    /// <summary>v1 (2026-10-18) stored Poster/Card as fixed field sets and the chapter sort as Number/Date; map them onto v2 (spec Q13).</summary>
    private static DetailListLayoutState Upgrade(DetailListLayoutState state)
    {
        if (state.LegacyPosterFields is { } poster && state.CaptionFields is null)
        {
            var captions = new List<IssueListSortField>();
            if (poster.Contains("Number")) captions.Add(IssueListSortField.Number);
            if (poster.Contains("ArcTitle")) captions.Add(IssueListSortField.StoryArc);
            state = state with
            {
                CaptionFields = captions,
                HideCaptions = captions.Count == 0,
                ShowReadOverlay = poster.Contains("ReadStatus"),
                ShowEditionOverlay = poster.Contains("Edition"),
            };
        }

        if (state.LegacyCardFields is { } card && state.CardElements is null)
        {
            var elements = new List<TileTextElement>();
            if (card.Contains("FullTitle")) elements.Add(TileTextElement.Title);
            if (card.Contains("NumberAndProgress")) elements.Add(TileTextElement.Number);
            state = state with { CardElements = elements, ShowCardStatus = card.Contains("ReadStatus") };
        }

        if (state.LegacyChapterSortField is { } chapterSort)
        {
            state = state with { ChapterSortBy = chapterSort == "Date" ? IssueListSortField.Released : IssueListSortField.Number };
        }

        return state;
    }

    private static readonly IReadOnlyDictionary<string, string> ColumnAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["FullTitle"] = nameof(IssueListSortField.Title),
        ["ArcTitle"] = nameof(IssueListSortField.StoryArc),
        ["CoverDate"] = nameof(IssueListSortField.Released),
        ["Date"] = nameof(IssueListSortField.Released),
    };

    private static IReadOnlyList<DetailListColumn> NormalizeColumns(IReadOnlyList<DetailListColumn>? columns, IReadOnlyList<DetailListColumn> fallback, bool manga)
    {
        if (columns is null) return fallback.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<DetailListColumn>();
        foreach (var column in columns)
        {
            if (column?.Field is not { } raw) continue;
            string key = ColumnAliases.TryGetValue(raw, out var alias) ? alias : raw;
            bool known = DetailColumnKeys.IsDetailOnly(key, manga) || DetailColumnKeys.CatalogField(key) is not null;
            if (!known || !seen.Add(key)) continue;
            double width = double.IsFinite(column.Width) ? Math.Clamp(column.Width, MinColumnWidth, MaxColumnWidth) : 120;
            kept.Add(new DetailListColumn(key, column.Visible, width));
        }

        return kept.Any(c => c.Visible) ? kept : fallback.ToList();
    }

    private static IReadOnlyList<IssueListSortField> NormalizeCaptions(IReadOnlyList<IssueListSortField>? fields, IReadOnlyList<IssueListSortField> fallback)
    {
        if (fields is null) return fallback.ToList();
        return fields
            .Where(f => IssueListFieldCatalog.SortFields.TryGetValue(f, out var d) && d.Display is not null)
            .Distinct()
            .Take(DetailListLayoutState.MaxCaptionLines)
            .ToList();
    }

    private static IReadOnlyList<TileTextElement> NormalizeElements(IReadOnlyList<TileTextElement>? elements, IReadOnlyList<TileTextElement> fallback)
    {
        if (elements is null) return fallback.ToList();

        // An issue card has no series summary line; keep everything else the Library tiles offer.
        return elements.Where(e => Enum.IsDefined(e) && e != TileTextElement.Summary).Distinct().ToList();
    }

    private static IssueListSortField SortOrDefault(IssueListSortField field, int? virtualTagId) =>
        field == IssueListSortField.VirtualTag
            ? virtualTagId is null ? IssueListSortField.Number : field
            : IssueListFieldCatalog.SortFields.ContainsKey(field) ? field : IssueListSortField.Number;

    private static string NormalizeGroup(string? value, string fallback) => value switch
    {
        DetailGrouping.None => DetailGrouping.None,
        DetailGrouping.Run when fallback == DetailGrouping.Run => DetailGrouping.Run,
        DetailGrouping.Volume when fallback == DetailGrouping.Volume => DetailGrouping.Volume,
        _ when DetailGrouping.FieldOf(value) is { } field => DetailGrouping.ForField(field),
        _ when DetailGrouping.VirtualTagOf(value) is { } id => DetailGrouping.ForVirtualTag(id),
        _ => fallback,
    };
}

public enum DetailListFieldKind
{
    Text,
    Status,
}

/// <summary>A rendered value for one configurable detail-list column.</summary>
public sealed record DetailListCell(string Field, string Text, double Width, DetailListFieldKind Kind = DetailListFieldKind.Text, string Header = "")
{
    public string AccessibleLabel => string.IsNullOrWhiteSpace(Header) ? Text : $"{Header}: {Text}";
}

/// <summary>One header cell above a detail list: the column's name and width, the target of the resize and reorder drags.</summary>
public sealed partial class DetailListHeaderCell : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required string Field { get; init; }
    public required string Header { get; init; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private double _width;
}
