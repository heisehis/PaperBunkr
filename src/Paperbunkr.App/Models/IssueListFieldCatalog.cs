using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One <see cref="IssueListSortField"/>'s comparison logic and display label.</summary>
public sealed record IssueListSortFieldDescriptor(IssueListSortField Field, string DisplayName, Comparison<IssueListRow> Compare)
{
    /// <summary>
    /// Cell-text projection for this field when it's shown as a column in Library's configurable
    /// Details table (docs/superpowers/specs/2026-08-27-library-browsing-4b-toolbar-rework-design.md
    /// §8). <see langword="null"/> means the field is sort-only and not offered as a column
    /// (e.g. <see cref="IssueListSortField.Status"/>). Must be null-safe for a fully-unpopulated row.
    /// </summary>
    public Func<IssueListRow, string?>? Display { get; init; }
}

/// <summary>One <see cref="IssueListGroupField"/>'s bucketing/ordering logic and display label. <see cref="GroupKey"/> also serves as the resulting group's display header.</summary>
public sealed record IssueListGroupFieldDescriptor(IssueListGroupField Field, string DisplayName, Func<IssueListRow, string> GroupKey, Comparison<string> GroupOrder);

/// <summary>
/// Data-driven field catalog for <b>all</b> of Library's sort/group logic - per-issue cards
/// directly, per-series cards via <c>SeriesCardSample.RepresentativeRow</c> (2026-09-03: this
/// replaced the separate <c>LibraryFieldCatalog</c>). Same shape as <c>SmartListCatalog</c>/
/// <c>BulkFieldRegistry</c>, built on <see cref="SortStrategies"/>/<see cref="GroupStrategies"/>'s
/// reusable comparison-logic helpers. Design: docs/superpowers/specs/2026-08-18-issue-list-
/// pluggable-sort-group-design.md.
/// </summary>
public static class IssueListFieldCatalog
{
    public static readonly IReadOnlyDictionary<IssueListSortField, IssueListSortFieldDescriptor> SortFields = BuildSortFields();

    private static Dictionary<IssueListSortField, IssueListSortFieldDescriptor> BuildSortFields()
    {
        var d = new Dictionary<IssueListSortField, IssueListSortFieldDescriptor>
    {
        [IssueListSortField.Number] = new(IssueListSortField.Number, "Number", SortStrategies.IssueNumber()),
        [IssueListSortField.Series] = new(IssueListSortField.Series, "Series",
            Combine(SortStrategies.CaseInsensitiveString(r => r.SeriesName), SortStrategies.IssueNumber())),
        [IssueListSortField.Title] = new(IssueListSortField.Title, "Title", SortStrategies.CaseInsensitiveString(r => r.Title)),
        [IssueListSortField.Writer] = new(IssueListSortField.Writer, "Writer", SortStrategies.CaseInsensitiveString(r => r.Writer)),
        [IssueListSortField.Publisher] = new(IssueListSortField.Publisher, "Publisher", SortStrategies.CaseInsensitiveString(r => r.Publisher)),
        [IssueListSortField.Genre] = new(IssueListSortField.Genre, "Genre", SortStrategies.CaseInsensitiveString(r => r.Genre)),
        [IssueListSortField.Format] = new(IssueListSortField.Format, "Format", SortStrategies.CaseInsensitiveString(r => r.Format)),
        [IssueListSortField.Added] = new(IssueListSortField.Added, "Date Added", SortStrategies.Date(r => r.AddedTime)),
        [IssueListSortField.Opened] = new(IssueListSortField.Opened, "Last Read", SortStrategies.Date(r => r.OpenedTime)),
        [IssueListSortField.Released] = new(IssueListSortField.Released, "Released", SortStrategies.Date(r => r.ReleasedTime)),
        // Deliberately Issue.Year directly, not CE's real ComicBookYearComparer (which actually
        // sorts by the full Published date, a confirmed upstream CE quirk) - the sane fix, per the
        // spec's "don't replicate CE bugs" rule.
        [IssueListSortField.Year] = new(IssueListSortField.Year, "Year", SortStrategies.Numeric(r => r.Year)),
        [IssueListSortField.PageCount] = new(IssueListSortField.PageCount, "Page Count", SortStrategies.Numeric(r => r.PageCount)),
        [IssueListSortField.FileSize] = new(IssueListSortField.FileSize, "File Size", SortStrategies.Numeric(r => r.FileSize)),
        [IssueListSortField.Rating] = new(IssueListSortField.Rating, "My Rating", SortStrategies.Numeric(r => r.Rating)),
        [IssueListSortField.CommunityRating] = new(IssueListSortField.CommunityRating, "Community Rating", SortStrategies.Numeric(r => r.CommunityRating)),
        [IssueListSortField.ReadPercentage] = new(IssueListSortField.ReadPercentage, "Read %", (a, b) => a.ReadPercentage.CompareTo(b.ReadPercentage)),
        [IssueListSortField.OpenCount] = new(IssueListSortField.OpenCount, "Times Opened", (a, b) => a.OpenCount.CompareTo(b.OpenCount)),
        // Sortable but not groupable, matching CE's own column table precedent.
        [IssueListSortField.Tags] = new(IssueListSortField.Tags, "Tags", SortStrategies.CaseInsensitiveString(r => r.Tags)),
        [IssueListSortField.Status] = new(IssueListSortField.Status, "Status", SortStrategies.Boolean(r => r.IsMissing)),

        // --- Slice 1 additions (docs/superpowers/specs/2026-08-18-library-book-centric-redesign-
        // design.md) - the remaining real CE comparer columns. ---
        [IssueListSortField.Volume] = new(IssueListSortField.Volume, "Volume", SortStrategies.Numeric(r => r.VolumeSortKey)),
        [IssueListSortField.Penciller] = new(IssueListSortField.Penciller, "Penciller", SortStrategies.CaseInsensitiveString(r => r.Penciller)),
        [IssueListSortField.Inker] = new(IssueListSortField.Inker, "Inker", SortStrategies.CaseInsensitiveString(r => r.Inker)),
        [IssueListSortField.Colorist] = new(IssueListSortField.Colorist, "Colorist", SortStrategies.CaseInsensitiveString(r => r.Colorist)),
        [IssueListSortField.Letterer] = new(IssueListSortField.Letterer, "Letterer", SortStrategies.CaseInsensitiveString(r => r.Letterer)),
        [IssueListSortField.CoverArtist] = new(IssueListSortField.CoverArtist, "Cover Artist", SortStrategies.CaseInsensitiveString(r => r.CoverArtist)),
        [IssueListSortField.Editor] = new(IssueListSortField.Editor, "Editor", SortStrategies.CaseInsensitiveString(r => r.Editor)),
        [IssueListSortField.Translator] = new(IssueListSortField.Translator, "Translator", SortStrategies.CaseInsensitiveString(r => r.Translator)),
        [IssueListSortField.Characters] = new(IssueListSortField.Characters, "Characters", SortStrategies.CaseInsensitiveString(r => r.Characters)),
        [IssueListSortField.Teams] = new(IssueListSortField.Teams, "Teams", SortStrategies.CaseInsensitiveString(r => r.Teams)),
        [IssueListSortField.Locations] = new(IssueListSortField.Locations, "Locations", SortStrategies.CaseInsensitiveString(r => r.Locations)),
        [IssueListSortField.BookPrice] = new(IssueListSortField.BookPrice, "Book Price", SortStrategies.Numeric(r => r.BookPrice)),
        [IssueListSortField.BookAge] = new(IssueListSortField.BookAge, "Book Age", SortStrategies.CaseInsensitiveString(r => r.BookAge)),
        [IssueListSortField.BookStore] = new(IssueListSortField.BookStore, "Book Store", SortStrategies.CaseInsensitiveString(r => r.BookStore)),
        [IssueListSortField.BookOwner] = new(IssueListSortField.BookOwner, "Book Owner", SortStrategies.CaseInsensitiveString(r => r.BookOwner)),
        [IssueListSortField.BookCondition] = new(IssueListSortField.BookCondition, "Book Condition", SortStrategies.CaseInsensitiveString(r => r.BookCondition)),
        [IssueListSortField.BookCollectionStatus] = new(IssueListSortField.BookCollectionStatus, "Book Collection Status", SortStrategies.CaseInsensitiveString(r => r.BookCollectionStatus)),
        [IssueListSortField.BookLocation] = new(IssueListSortField.BookLocation, "Book Location", SortStrategies.CaseInsensitiveString(r => r.BookLocation)),
        // Sortable but not groupable, matching CE's own column table precedent (id 57 has no grouper).
        [IssueListSortField.ISBN] = new(IssueListSortField.ISBN, "ISBN", SortStrategies.CaseInsensitiveString(r => r.ISBN)),
        [IssueListSortField.Read] = new(IssueListSortField.Read, "Read", SortStrategies.Boolean(r => r.IsRead)),
        [IssueListSortField.Imprint] = new(IssueListSortField.Imprint, "Imprint", SortStrategies.CaseInsensitiveString(r => r.Imprint)),
        [IssueListSortField.Language] = new(IssueListSortField.Language, "Language", SortStrategies.CaseInsensitiveString(r => r.LanguageIso)),
        [IssueListSortField.AgeRating] = new(IssueListSortField.AgeRating, "Age Rating", SortStrategies.CaseInsensitiveString(r => r.AgeRating)),
        // The fields that actually surface a case like "Warhammer 40" (one generic Series value
        // covering ~15 real mini-series) - grouping by Series alone can't split that apart, since
        // all 51 issues share it; Story Arc/Series Group are CE's real per-story fields for exactly
        // this anthology/imprint pattern.
        [IssueListSortField.StoryArc] = new(IssueListSortField.StoryArc, "Story Arc", SortStrategies.CaseInsensitiveString(r => r.StoryArc)),
        [IssueListSortField.SeriesGroup] = new(IssueListSortField.SeriesGroup, "Series Group", SortStrategies.CaseInsensitiveString(r => r.SeriesGroup)),
        // Sortable but not groupable, matching CE's own column table precedent (id 9 File Path and
        // id 43 Bookmark Count both have no grouper in CE).
        [IssueListSortField.FilePath] = new(IssueListSortField.FilePath, "File Path", SortStrategies.CaseInsensitiveString(r => r.FilePath)),
        [IssueListSortField.BookmarkCount] = new(IssueListSortField.BookmarkCount, "Bookmark Count", (a, b) => a.BookmarkCount.CompareTo(b.BookmarkCount)),
        [IssueListSortField.FileName] = new(IssueListSortField.FileName, "File Name", SortStrategies.CaseInsensitiveString(r => r.FileName)),
        [IssueListSortField.FileDirectory] = new(IssueListSortField.FileDirectory, "File Directory", SortStrategies.CaseInsensitiveString(r => r.FileDirectory)),
        [IssueListSortField.FileModified] = new(IssueListSortField.FileModified, "File Modified", SortStrategies.Date(r => r.FileModifiedTime)),
        [IssueListSortField.FileCreated] = new(IssueListSortField.FileCreated, "File Created", SortStrategies.Date(r => r.FileCreationTime)),
        [IssueListSortField.FileFormat] = new(IssueListSortField.FileFormat, "File Format", SortStrategies.CaseInsensitiveString(r => r.FileFormat)),
        [IssueListSortField.Count] = new(IssueListSortField.Count, "Count", SortStrategies.Numeric(r => r.Count)),
        [IssueListSortField.AlternateSeries] = new(IssueListSortField.AlternateSeries, "Alternate Series", SortStrategies.CaseInsensitiveString(r => r.AlternateSeries)),
        [IssueListSortField.AlternateNumber] = new(IssueListSortField.AlternateNumber, "Alternate Number", SortStrategies.CaseInsensitiveString(r => r.AlternateNumber)),
        [IssueListSortField.Month] = new(IssueListSortField.Month, "Month", SortStrategies.Numeric(r => r.Month)),
        [IssueListSortField.Day] = new(IssueListSortField.Day, "Day", SortStrategies.Numeric(r => r.Day)),
        [IssueListSortField.ScanInformation] = new(IssueListSortField.ScanInformation, "Scan Information", SortStrategies.CaseInsensitiveString(r => r.ScanInformation)),

        // --- Series-level aggregates, carried over from the retired LibrarySortField when the
        // sort/group pool was unified across per-issue and per-series cards (2026-09-03). ---
        [IssueListSortField.SeriesIssueCount] = new(IssueListSortField.SeriesIssueCount, "Issue Count", (a, b) => a.SeriesIssueCount.CompareTo(b.SeriesIssueCount)),
        [IssueListSortField.SeriesUnreadCount] = new(IssueListSortField.SeriesUnreadCount, "Unread Count", (a, b) => a.SeriesUnreadCount.CompareTo(b.SeriesUnreadCount)),
    };

        // Per-field cell-text projection for the configurable Details table (design §8). Everything
        // except Status is column-eligible; each projection is null-safe for a fully-unpopulated row.
        void Col(IssueListSortField field, Func<IssueListRow, string?> display) =>
            d[field] = d[field] with { Display = display };

        Col(IssueListSortField.Number, r => r.Number);
        Col(IssueListSortField.Series, r => r.SeriesName);
        Col(IssueListSortField.Title, r => r.Title);
        Col(IssueListSortField.Writer, r => r.Writer);
        Col(IssueListSortField.Publisher, r => r.Publisher);
        Col(IssueListSortField.Genre, r => r.Genre);
        Col(IssueListSortField.Format, r => r.Format);
        Col(IssueListSortField.Added, r => Date(r.AddedTime));
        Col(IssueListSortField.Opened, r => Date(r.OpenedTime));
        Col(IssueListSortField.Released, r => Date(r.ReleasedTime));
        Col(IssueListSortField.Year, r => r.Year?.ToString(CultureInfo.InvariantCulture));
        Col(IssueListSortField.PageCount, r => r.PageCount?.ToString(CultureInfo.InvariantCulture));
        Col(IssueListSortField.FileSize, r => FormatFileSize(r.FileSize));
        Col(IssueListSortField.Rating, r => r.Rating?.ToString("0.#", CultureInfo.InvariantCulture));
        Col(IssueListSortField.CommunityRating, r => r.CommunityRating?.ToString("0.#", CultureInfo.InvariantCulture));
        Col(IssueListSortField.ReadPercentage, r => $"{r.ReadPercentage:0}%");
        Col(IssueListSortField.OpenCount, r => r.OpenCount.ToString(CultureInfo.InvariantCulture));
        Col(IssueListSortField.Tags, r => r.Tags);
        Col(IssueListSortField.Volume, r => r.Volume);
        Col(IssueListSortField.Penciller, r => r.Penciller);
        Col(IssueListSortField.Inker, r => r.Inker);
        Col(IssueListSortField.Colorist, r => r.Colorist);
        Col(IssueListSortField.Letterer, r => r.Letterer);
        Col(IssueListSortField.CoverArtist, r => r.CoverArtist);
        Col(IssueListSortField.Editor, r => r.Editor);
        Col(IssueListSortField.Translator, r => r.Translator);
        Col(IssueListSortField.Characters, r => r.Characters);
        Col(IssueListSortField.Teams, r => r.Teams);
        Col(IssueListSortField.Locations, r => r.Locations);
        Col(IssueListSortField.BookPrice, r => r.BookPrice?.ToString("0.00", CultureInfo.InvariantCulture));
        Col(IssueListSortField.BookAge, r => r.BookAge);
        Col(IssueListSortField.BookStore, r => r.BookStore);
        Col(IssueListSortField.BookOwner, r => r.BookOwner);
        Col(IssueListSortField.BookCondition, r => r.BookCondition);
        Col(IssueListSortField.BookCollectionStatus, r => r.BookCollectionStatus);
        Col(IssueListSortField.BookLocation, r => r.BookLocation);
        Col(IssueListSortField.ISBN, r => r.ISBN);
        Col(IssueListSortField.Read, r => r.IsRead ? "Read" : "Unread");
        Col(IssueListSortField.Imprint, r => r.Imprint);
        Col(IssueListSortField.Language, r => r.LanguageIso);
        Col(IssueListSortField.AgeRating, r => r.AgeRating);
        Col(IssueListSortField.StoryArc, r => r.StoryArc);
        Col(IssueListSortField.SeriesGroup, r => r.SeriesGroup);
        Col(IssueListSortField.FilePath, r => r.FilePath);
        Col(IssueListSortField.BookmarkCount, r => r.BookmarkCount.ToString(CultureInfo.InvariantCulture));
        Col(IssueListSortField.FileName, r => r.FileName);
        Col(IssueListSortField.FileDirectory, r => r.FileDirectory);
        Col(IssueListSortField.FileModified, r => Date(r.FileModifiedTime));
        Col(IssueListSortField.FileCreated, r => Date(r.FileCreationTime));
        Col(IssueListSortField.FileFormat, r => r.FileFormat);
        Col(IssueListSortField.Count, r => r.Count?.ToString(CultureInfo.InvariantCulture));
        Col(IssueListSortField.AlternateSeries, r => r.AlternateSeries);
        Col(IssueListSortField.AlternateNumber, r => r.AlternateNumber);
        Col(IssueListSortField.Month, r => r.Month?.ToString(CultureInfo.InvariantCulture));
        Col(IssueListSortField.Day, r => r.Day?.ToString(CultureInfo.InvariantCulture));
        Col(IssueListSortField.ScanInformation, r => r.ScanInformation);
        Col(IssueListSortField.SeriesIssueCount, r => r.SeriesIssueCount.ToString(CultureInfo.InvariantCulture));
        Col(IssueListSortField.SeriesUnreadCount, r => r.SeriesUnreadCount.ToString(CultureInfo.InvariantCulture));

        return d;
    }

    private static string? Date(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Human-readable byte size for the File Size column; null passes through.</summary>
    private static string? FormatFileSize(long? bytes)
    {
        if (bytes is not { } b || b < 0)
        {
            return null;
        }

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = b;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{b} B"
            : $"{size.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    /// <summary>
    /// The curated column set a fresh <see cref="Paperbunkr.Data.Entities.AppSettings.LibraryDetailsColumns"/>
    /// (null) falls back to (design §8) - a readable default that covers the fields most Library
    /// users actually scan by.
    /// </summary>
    public static readonly IssueListSortField[] DefaultDetailsColumns =
    {
        IssueListSortField.Title,
        IssueListSortField.Series,
        IssueListSortField.Number,
        IssueListSortField.Volume,
        IssueListSortField.Year,
        IssueListSortField.PageCount,
        IssueListSortField.Publisher,
        IssueListSortField.Format,
        IssueListSortField.Rating,
        IssueListSortField.Added,
        IssueListSortField.ReadPercentage,
    };

    /// <summary>
    /// Every <see cref="SortFields"/> descriptor that carries a non-null <see cref="IssueListSortFieldDescriptor.Display"/>,
    /// i.e. the fields that can appear as a Details-table column, in stable <see cref="SortFields"/>
    /// insertion order. Backs the right-click "Columns" header menu.
    /// </summary>
    public static readonly IReadOnlyList<IssueListSortFieldDescriptor> ColumnFields =
        SortFields.Values.Where(d => d.Display is not null).ToList();

    public static readonly IReadOnlyDictionary<IssueListGroupField, IssueListGroupFieldDescriptor> GroupFields = new Dictionary<IssueListGroupField, IssueListGroupFieldDescriptor>
    {
        [IssueListGroupField.Series] = MakeGroup(IssueListGroupField.Series, "Series", GroupStrategies.Alphabetical(r => r.SeriesName)),
        [IssueListGroupField.Publisher] = MakeGroup(IssueListGroupField.Publisher, "Publisher", GroupStrategies.Alphabetical(r => r.Publisher)),
        [IssueListGroupField.Genre] = MakeGroup(IssueListGroupField.Genre, "Genre", GroupStrategies.Alphabetical(r => r.Genre)),
        [IssueListGroupField.Format] = MakeGroup(IssueListGroupField.Format, "Format", GroupStrategies.Alphabetical(r => r.Format)),
        [IssueListGroupField.Year] = MakeGroup(IssueListGroupField.Year, "Year", GroupStrategies.NumericBucket(r => r.Year)),
        [IssueListGroupField.Status] = MakeGroup(IssueListGroupField.Status, "Status", GroupStrategies.Boolean(r => r.IsMissing, "Missing", "Available")),
        [IssueListGroupField.Opened] = MakeGroup(IssueListGroupField.Opened, "Last Read", GroupStrategies.Alphabetical(r => r.OpenedTime?.ToString("yyyy-MM-dd"))),

        // --- Slice 1 additions (docs/superpowers/specs/2026-08-18-library-book-centric-redesign-
        // design.md) - the remaining real CE grouper columns. ---
        [IssueListGroupField.Volume] = MakeGroup(IssueListGroupField.Volume, "Volume", GroupStrategies.Alphabetical(r => r.Volume)),
        [IssueListGroupField.Penciller] = MakeGroup(IssueListGroupField.Penciller, "Penciller", GroupStrategies.Alphabetical(r => r.Penciller)),
        [IssueListGroupField.Inker] = MakeGroup(IssueListGroupField.Inker, "Inker", GroupStrategies.Alphabetical(r => r.Inker)),
        [IssueListGroupField.Colorist] = MakeGroup(IssueListGroupField.Colorist, "Colorist", GroupStrategies.Alphabetical(r => r.Colorist)),
        [IssueListGroupField.Letterer] = MakeGroup(IssueListGroupField.Letterer, "Letterer", GroupStrategies.Alphabetical(r => r.Letterer)),
        [IssueListGroupField.CoverArtist] = MakeGroup(IssueListGroupField.CoverArtist, "Cover Artist", GroupStrategies.Alphabetical(r => r.CoverArtist)),
        [IssueListGroupField.Editor] = MakeGroup(IssueListGroupField.Editor, "Editor", GroupStrategies.Alphabetical(r => r.Editor)),
        [IssueListGroupField.Translator] = MakeGroup(IssueListGroupField.Translator, "Translator", GroupStrategies.Alphabetical(r => r.Translator)),
        [IssueListGroupField.Characters] = MakeGroup(IssueListGroupField.Characters, "Characters", GroupStrategies.Alphabetical(r => r.Characters)),
        [IssueListGroupField.Teams] = MakeGroup(IssueListGroupField.Teams, "Teams", GroupStrategies.Alphabetical(r => r.Teams)),
        [IssueListGroupField.Locations] = MakeGroup(IssueListGroupField.Locations, "Locations", GroupStrategies.Alphabetical(r => r.Locations)),
        [IssueListGroupField.BookPrice] = MakeGroup(IssueListGroupField.BookPrice, "Book Price", GroupStrategies.Alphabetical(r => r.BookPrice?.ToString("0.00"))),
        [IssueListGroupField.BookAge] = MakeGroup(IssueListGroupField.BookAge, "Book Age", GroupStrategies.Alphabetical(r => r.BookAge)),
        [IssueListGroupField.BookStore] = MakeGroup(IssueListGroupField.BookStore, "Book Store", GroupStrategies.Alphabetical(r => r.BookStore)),
        [IssueListGroupField.BookOwner] = MakeGroup(IssueListGroupField.BookOwner, "Book Owner", GroupStrategies.Alphabetical(r => r.BookOwner)),
        [IssueListGroupField.BookCondition] = MakeGroup(IssueListGroupField.BookCondition, "Book Condition", GroupStrategies.Alphabetical(r => r.BookCondition)),
        [IssueListGroupField.BookCollectionStatus] = MakeGroup(IssueListGroupField.BookCollectionStatus, "Book Collection Status", GroupStrategies.Alphabetical(r => r.BookCollectionStatus)),
        [IssueListGroupField.BookLocation] = MakeGroup(IssueListGroupField.BookLocation, "Book Location", GroupStrategies.Alphabetical(r => r.BookLocation)),
        [IssueListGroupField.Read] = MakeGroup(IssueListGroupField.Read, "Read", GroupStrategies.Boolean(r => r.IsRead, "Read", "Unread")),
        [IssueListGroupField.Imprint] = MakeGroup(IssueListGroupField.Imprint, "Imprint", GroupStrategies.Alphabetical(r => r.Imprint)),
        [IssueListGroupField.Language] = MakeGroup(IssueListGroupField.Language, "Language", GroupStrategies.Alphabetical(r => r.LanguageIso)),
        [IssueListGroupField.AgeRating] = MakeGroup(IssueListGroupField.AgeRating, "Age Rating", GroupStrategies.Alphabetical(r => r.AgeRating)),
        [IssueListGroupField.StoryArc] = MakeGroup(IssueListGroupField.StoryArc, "Story Arc", GroupStrategies.Alphabetical(r => r.StoryArc)),
        [IssueListGroupField.SeriesGroup] = MakeGroup(IssueListGroupField.SeriesGroup, "Series Group", GroupStrategies.Alphabetical(r => r.SeriesGroup)),
        [IssueListGroupField.FileName] = MakeGroup(IssueListGroupField.FileName, "File Name", GroupStrategies.Alphabetical(r => r.FileName)),
        [IssueListGroupField.FileDirectory] = MakeGroup(IssueListGroupField.FileDirectory, "File Directory", GroupStrategies.Alphabetical(r => r.FileDirectory)),
        [IssueListGroupField.FileModified] = MakeGroup(IssueListGroupField.FileModified, "File Modified", GroupStrategies.Alphabetical(r => r.FileModifiedTime?.ToString("yyyy-MM-dd"))),
        [IssueListGroupField.FileCreated] = MakeGroup(IssueListGroupField.FileCreated, "File Created", GroupStrategies.Alphabetical(r => r.FileCreationTime?.ToString("yyyy-MM-dd"))),
        [IssueListGroupField.FileFormat] = MakeGroup(IssueListGroupField.FileFormat, "File Format", GroupStrategies.Alphabetical(r => r.FileFormat)),
        [IssueListGroupField.Count] = MakeGroup(IssueListGroupField.Count, "Count", GroupStrategies.NumericBucket(r => r.Count)),
        [IssueListGroupField.AlternateSeries] = MakeGroup(IssueListGroupField.AlternateSeries, "Alternate Series", GroupStrategies.Alphabetical(r => r.AlternateSeries)),
        [IssueListGroupField.AlternateNumber] = MakeGroup(IssueListGroupField.AlternateNumber, "Alternate Number", GroupStrategies.Alphabetical(r => r.AlternateNumber)),
        [IssueListGroupField.Month] = MakeGroup(IssueListGroupField.Month, "Month", GroupStrategies.NumericBucket(r => r.Month)),
        [IssueListGroupField.Day] = MakeGroup(IssueListGroupField.Day, "Day", GroupStrategies.NumericBucket(r => r.Day)),
        [IssueListGroupField.ScanInformation] = MakeGroup(IssueListGroupField.ScanInformation, "Scan Information", GroupStrategies.Alphabetical(r => r.ScanInformation)),

        // --- Carried over from the retired LibraryGroupField (2026-09-03 sort/group pool
        // unification). ContentType keeps its enum-declaration group ordering; Alphabetical buckets
        // the series name's first letter, verbatim from the old LibraryFieldCatalog. ---
        [IssueListGroupField.ContentType] = new(IssueListGroupField.ContentType, "Content Type",
            r => string.IsNullOrWhiteSpace(r.ContentTypeLabel) ? "Unknown" : r.ContentTypeLabel,
            (a, b) => Enum.TryParse<ContentType>(a, out var ea) && Enum.TryParse<ContentType>(b, out var eb)
                ? ea.CompareTo(eb)
                : string.Compare(a, b, StringComparison.OrdinalIgnoreCase)),
        [IssueListGroupField.Alphabetical] = new(IssueListGroupField.Alphabetical, "Alphabetical",
            r => r.SeriesName.Length > 0 && char.IsAsciiLetter(r.SeriesName[0]) ? char.ToUpperInvariant(r.SeriesName[0]).ToString() : "#",
            (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase)),
        [IssueListGroupField.SeriesIssueCount] = MakeGroup(IssueListGroupField.SeriesIssueCount, "Issue Count", GroupStrategies.NumericBucket(r => r.SeriesIssueCount)),
    };

    private static IssueListGroupFieldDescriptor MakeGroup(IssueListGroupField field, string displayName, (Func<IssueListRow, string> Key, Comparison<string> Order) strategy) =>
        new(field, displayName, strategy.Key, strategy.Order);

    /// <summary>Primary comparison, falling back to <paramref name="tieBreak"/> when equal - backs <see cref="IssueListSortField.Series"/>'s simplified (name, then number) tie-break, a deliberate simplification of CE's real three-level Format-&gt;Volume-&gt;Number tie-break (see the spec).</summary>
    private static Comparison<IssueListRow> Combine(Comparison<IssueListRow> primary, Comparison<IssueListRow> tieBreak) =>
        (a, b) =>
        {
            int result = primary(a, b);
            return result != 0 ? result : tieBreak(a, b);
        };
}
