using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.SmartLists;

/// <summary>Label + data type for one <see cref="SmartListField"/>, used to drive the rule-builder UI.</summary>
public sealed record SmartListFieldDefinition(SmartListField Field, string Label, SmartListDataType DataType);

/// <summary>
/// Data-driven field catalog for the smart-list rule engine
/// (docs/superpowers/specs/2026-08-06-smart-lists-design.md §4). Selectors are plain
/// <c>Func&lt;Issue,T&gt;</c> delegates evaluated in memory, not <c>Expression</c> trees translated
/// to SQL — matching how CE's own matcher system actually works (<c>IMatcher&lt;ComicBook&gt;</c>
/// evaluates in-memory over the fully-loaded library, per docs/onboarding.md §5: "no indexing, no
/// partial writes"). At a personal-library scale (thousands, not millions, of issues) this is both
/// simpler and safer than fighting heterogeneous SQL-translation edge cases (string-to-float
/// parsing, clamped percentage math, enum equality) across ~70 fields, and it's the more
/// CE-faithful architecture, not a compromise.
///
/// <see cref="SmartListField.CustomValue"/> and <see cref="SmartListField.Duplicate"/> are
/// deliberately absent — both need per-condition arguments (a custom-value name; a cross-issue
/// grouping) that don't fit a flat one-issue-in, one-value-out selector, and are evaluated directly
/// in <see cref="SmartListQueryBuilder"/> instead.
/// </summary>
public static class SmartListCatalog
{
    public static readonly IReadOnlyDictionary<SmartListField, SmartListFieldDefinition> Definitions =
        new SmartListFieldDefinition[]
        {
            // Series-level
            new(SmartListField.SeriesName, "Series", SmartListDataType.Text),
            new(SmartListField.Genre, "Genre", SmartListDataType.Text),
            new(SmartListField.Publisher, "Publisher", SmartListDataType.Text),
            new(SmartListField.ContentType, "Content Type", SmartListDataType.Text),
            new(SmartListField.ReadingMode, "Reading Mode", SmartListDataType.Text),
            new(SmartListField.SeriesComplete, "Series Complete", SmartListDataType.Toggle),
            new(SmartListField.ReadingStatus, "Reading Status", SmartListDataType.Text),
            new(SmartListField.Continuity, "Continuity", SmartListDataType.Text),

            // ComicInfo text fields
            new(SmartListField.Title, "Title", SmartListDataType.Text),
            new(SmartListField.Number, "Number", SmartListDataType.Text),
            new(SmartListField.AlternateSeries, "Alternate Series", SmartListDataType.Text),
            new(SmartListField.AlternateNumber, "Alternate Number", SmartListDataType.Text),
            new(SmartListField.StoryArc, "Story Arc", SmartListDataType.Text),
            new(SmartListField.StoryArcNumber, "Story Arc Number", SmartListDataType.Text),
            new(SmartListField.SeriesGroup, "Series Group", SmartListDataType.Text),
            new(SmartListField.Summary, "Summary", SmartListDataType.Text),
            new(SmartListField.Notes, "Notes", SmartListDataType.Text),
            new(SmartListField.Review, "Review", SmartListDataType.Text),
            new(SmartListField.Writer, "Writer", SmartListDataType.Text),
            new(SmartListField.Penciller, "Penciller", SmartListDataType.Text),
            new(SmartListField.Inker, "Inker", SmartListDataType.Text),
            new(SmartListField.Colorist, "Colorist", SmartListDataType.Text),
            new(SmartListField.Letterer, "Letterer", SmartListDataType.Text),
            new(SmartListField.CoverArtist, "Cover Artist", SmartListDataType.Text),
            new(SmartListField.Editor, "Editor", SmartListDataType.Text),
            new(SmartListField.Translator, "Translator", SmartListDataType.Text),
            new(SmartListField.Imprint, "Imprint", SmartListDataType.Text),
            new(SmartListField.Web, "Web", SmartListDataType.Text),
            new(SmartListField.LanguageISO, "Language", SmartListDataType.Text),
            new(SmartListField.Format, "Format", SmartListDataType.Text),
            new(SmartListField.AgeRating, "Age Rating", SmartListDataType.Text),
            new(SmartListField.Characters, "Characters", SmartListDataType.Text),
            new(SmartListField.Teams, "Teams", SmartListDataType.Text),
            new(SmartListField.Locations, "Locations", SmartListDataType.Text),
            new(SmartListField.MainCharacterOrTeam, "Main Character/Team", SmartListDataType.Text),
            new(SmartListField.Tags, "Tags", SmartListDataType.Text),

            // Numeric
            new(SmartListField.Year, "Year", SmartListDataType.Number),
            new(SmartListField.Month, "Month", SmartListDataType.Number),
            new(SmartListField.Day, "Day", SmartListDataType.Number),
            new(SmartListField.Volume, "Volume", SmartListDataType.Number),
            new(SmartListField.Count, "Count", SmartListDataType.Number),
            new(SmartListField.PageCount, "Page Count", SmartListDataType.Number),
            new(SmartListField.Rating, "My Rating", SmartListDataType.Number),
            new(SmartListField.CommunityRating, "Community Rating", SmartListDataType.Number),
            new(SmartListField.BookPrice, "Book Price", SmartListDataType.Number),
            new(SmartListField.FileSize, "File Size (MB)", SmartListDataType.Number),
            new(SmartListField.ReadPercentage, "Read Percentage", SmartListDataType.Number),
            new(SmartListField.BookmarkCount, "Bookmark Count", SmartListDataType.Number),
            new(SmartListField.NewPages, "New Pages", SmartListDataType.Number),

            // Toggle
            new(SmartListField.Checked, "Checked", SmartListDataType.Toggle),
            new(SmartListField.IsMissing, "Is Missing", SmartListDataType.Toggle),
            new(SmartListField.IsLinked, "Is Linked", SmartListDataType.Toggle),
            new(SmartListField.BlackAndWhite, "Black and White", SmartListDataType.Toggle),
            new(SmartListField.HasCustomValues, "Has Custom Values", SmartListDataType.Toggle),

            // Date
            new(SmartListField.Added, "Date Added", SmartListDataType.Date),
            new(SmartListField.Opened, "Date Opened", SmartListDataType.Date),
            new(SmartListField.Released, "Date Released", SmartListDataType.Date),
            new(SmartListField.Modified, "File Modified", SmartListDataType.Date),
            new(SmartListField.Created, "File Created", SmartListDataType.Date),

            // File-derived text
            new(SmartListField.File, "File Name", SmartListDataType.Text),
            new(SmartListField.FullPath, "File Path", SmartListDataType.Text),
            new(SmartListField.Directory, "File Directory", SmartListDataType.Text),
            new(SmartListField.FileFormat, "File Format", SmartListDataType.Text),

            // Book-collection text
            new(SmartListField.BookAge, "Book Age", SmartListDataType.Text),
            new(SmartListField.BookCollectionStatus, "Book Collection Status", SmartListDataType.Text),
            new(SmartListField.BookCondition, "Book Condition", SmartListDataType.Text),
            new(SmartListField.BookLocation, "Book Location", SmartListDataType.Text),
            new(SmartListField.BookNotes, "Book Notes", SmartListDataType.Text),
            new(SmartListField.BookOwner, "Book Owner", SmartListDataType.Text),
            new(SmartListField.BookStore, "Book Store", SmartListDataType.Text),
            new(SmartListField.ISBN, "ISBN", SmartListDataType.Text),
            new(SmartListField.ScanInformation, "Scan Information", SmartListDataType.Text),
        }.ToDictionary(d => d.Field);

    /// <summary>
    /// <see cref="SmartListField.Number"/> is deliberately Text, not Number, despite CE's
    /// <c>ComicBookNumberMatcher</c> parsing it to a float (default -1 when unparseable). Issue
    /// numbers in Paperbunkr's schema are free text specifically because they aren't purely
    /// numeric (annuals, "1a", half-numbers) — coercing to float loses information CE's own
    /// <c>ShadowNumber</c> string source has too, just later. A deliberate, judgment-call deviation
    /// from literal CE parity, not an oversight.
    /// </summary>
    public static readonly IReadOnlyDictionary<SmartListField, Func<Issue, string>> TextSelectors =
        new Dictionary<SmartListField, Func<Issue, string>>
        {
            [SmartListField.SeriesName] = i => i.Series?.Name ?? string.Empty,

            // Deliberately i.Genre/i.Publisher, not i.Series?.Genre/Publisher (P3 audit,
            // docs/alpha-roadmap.md) - both are real per-issue fields the Issue Properties/Bulk
            // editors actually write to (BulkFieldRegistry), same bug class as the Detail Pills
            // fix. The Series-level columns still exist (populated once at CE-migration time,
            // ContentType/ReadingMode/SeriesComplete have no per-issue equivalent to be stale
            // against) but are no longer the source of truth once an issue has its own value.
            [SmartListField.Genre] = i => i.JoinedGenre() ?? string.Empty,
            [SmartListField.Publisher] = i => i.Publisher ?? string.Empty,
            [SmartListField.ContentType] = i => i.Series?.ContentType.ToString() ?? string.Empty,
            [SmartListField.ReadingMode] = i => i.Series?.ReadingMode.ToString() ?? string.Empty,
            [SmartListField.ReadingStatus] = i => i.Series?.ReadingStatus.ToString() ?? string.Empty,
            // Joined so Contains/ContainsAny operators work when a series is in several continuities.
            [SmartListField.Continuity] = i => i.Series is null ? string.Empty : string.Join("; ", i.Series.Continuities.Select(c => c.Name)),

            [SmartListField.Title] = i => i.Title ?? string.Empty,
            [SmartListField.Number] = i => i.EffectiveNumber() ?? string.Empty,
            [SmartListField.AlternateSeries] = i => i.AlternateSeries ?? string.Empty,
            [SmartListField.AlternateNumber] = i => i.AlternateNumber ?? string.Empty,
            [SmartListField.StoryArc] = i => i.StoryArc ?? string.Empty,
            [SmartListField.StoryArcNumber] = i => i.StoryArcNumber ?? string.Empty,
            [SmartListField.SeriesGroup] = i => i.SeriesGroup ?? string.Empty,
            [SmartListField.Summary] = i => i.Summary ?? string.Empty,
            [SmartListField.Notes] = i => i.Notes ?? string.Empty,
            [SmartListField.Review] = i => i.Review ?? string.Empty,
            [SmartListField.Writer] = i => i.Writer ?? string.Empty,
            [SmartListField.Penciller] = i => i.Penciller ?? string.Empty,
            [SmartListField.Inker] = i => i.Inker ?? string.Empty,
            [SmartListField.Colorist] = i => i.Colorist ?? string.Empty,
            [SmartListField.Letterer] = i => i.Letterer ?? string.Empty,
            [SmartListField.CoverArtist] = i => i.CoverArtist ?? string.Empty,
            [SmartListField.Editor] = i => i.Editor ?? string.Empty,
            [SmartListField.Translator] = i => i.Translator ?? string.Empty,
            [SmartListField.Imprint] = i => i.Imprint ?? string.Empty,
            [SmartListField.Web] = i => i.Web ?? string.Empty,
            [SmartListField.LanguageISO] = i => i.LanguageISO ?? string.Empty,
            [SmartListField.Format] = i => i.Format ?? string.Empty,
            [SmartListField.AgeRating] = i => i.AgeRating ?? string.Empty,
            [SmartListField.Characters] = i => i.Characters ?? string.Empty,
            [SmartListField.Teams] = i => i.Teams ?? string.Empty,
            [SmartListField.Locations] = i => i.Locations ?? string.Empty,
            [SmartListField.MainCharacterOrTeam] = i => i.MainCharacterOrTeam ?? string.Empty,
            [SmartListField.Tags] = i => i.JoinedTags() ?? string.Empty,

            [SmartListField.File] = i => string.IsNullOrEmpty(i.FilePath) ? string.Empty : Path.GetFileName(i.FilePath),
            [SmartListField.FullPath] = i => i.FilePath ?? string.Empty,
            [SmartListField.Directory] = i => string.IsNullOrEmpty(i.FilePath) ? string.Empty : (Path.GetDirectoryName(i.FilePath) ?? string.Empty),
            [SmartListField.FileFormat] = i => string.IsNullOrEmpty(i.FilePath) ? string.Empty : Path.GetExtension(i.FilePath).TrimStart('.'),

            [SmartListField.BookAge] = i => i.BookAge ?? string.Empty,
            [SmartListField.BookCollectionStatus] = i => i.BookCollectionStatus ?? string.Empty,
            [SmartListField.BookCondition] = i => i.BookCondition ?? string.Empty,
            [SmartListField.BookLocation] = i => i.BookLocation ?? string.Empty,
            [SmartListField.BookNotes] = i => i.BookNotes ?? string.Empty,
            [SmartListField.BookOwner] = i => i.BookOwner ?? string.Empty,
            [SmartListField.BookStore] = i => i.BookStore ?? string.Empty,
            [SmartListField.ISBN] = i => i.ISBN ?? string.Empty,
            [SmartListField.ScanInformation] = i => i.ScanInformation ?? string.Empty,
        };

    /// <summary>
    /// Null/unset numeric fields default to -1, matching CE's base
    /// <c>ComicBookValueMatcher&lt;float&gt;.GetInvalidValue()</c> — except Rating/CommunityRating,
    /// which CE explicitly overrides to default 0 (<c>ComicBookRatingMatcher</c>/
    /// <c>ComicBookCommunityRatingMatcher.GetInvalidValue()</c>), confirmed by direct source check.
    /// </summary>
    public static readonly IReadOnlyDictionary<SmartListField, Func<Issue, float>> NumberSelectors =
        new Dictionary<SmartListField, Func<Issue, float>>
        {
            [SmartListField.Year] = i => i.EffectiveYear() ?? -1,
            [SmartListField.Month] = i => i.Month ?? -1,
            [SmartListField.Day] = i => i.Day ?? -1,
            // Volume is a string as of docs/superpowers/specs/2026-08-17-metadata-model-phase1-
            // canonical-metadata-design.md (preserves the original display value) - parsed here so
            // this field's existing numeric smart-list operators (>, <, range) keep working.
            // Reads through EffectiveVolume (Phase 2a) so a filename-inferred value still filters.
            [SmartListField.Volume] = i => float.TryParse(i.EffectiveVolume(), out var vol) ? vol : -1,
            [SmartListField.Count] = i => i.Count ?? -1,
            [SmartListField.PageCount] = i => i.PageCount ?? -1,
            [SmartListField.Rating] = i => i.Rating ?? 0,
            [SmartListField.CommunityRating] = i => i.CommunityRating ?? 0,
            [SmartListField.BookPrice] = i => i.BookPrice ?? -1,
            [SmartListField.FileSize] = i => i.FileSize is long bytes ? bytes / 1024f / 1024f : -1,
            [SmartListField.ReadPercentage] = ReadPercentage.Of,
            [SmartListField.BookmarkCount] = _ => 0, // deferred — no bookmarks feature/backing data yet
            [SmartListField.NewPages] = i => i.NewPages ?? -1,
        };

    public static readonly IReadOnlyDictionary<SmartListField, Func<Issue, bool>> ToggleSelectors =
        new Dictionary<SmartListField, Func<Issue, bool>>
        {
            [SmartListField.SeriesComplete] = i => i.Series?.IsComplete ?? false,
            [SmartListField.Checked] = i => i.Checked,
            [SmartListField.IsMissing] = i => i.FileIsMissing,
            [SmartListField.IsLinked] = i => !string.IsNullOrEmpty(i.FilePath),
            [SmartListField.BlackAndWhite] = i => i.ColorMode == ColorMode.BlackAndWhite,
            [SmartListField.HasCustomValues] = i => i.CustomValues.Count > 0,
        };

    public static readonly IReadOnlyDictionary<SmartListField, Func<Issue, DateTime?>> DateSelectors =
        new Dictionary<SmartListField, Func<Issue, DateTime?>>
        {
            [SmartListField.Added] = i => i.AddedTime,
            [SmartListField.Opened] = i => i.OpenedTime,
            [SmartListField.Released] = i => i.ReleasedTime,
            [SmartListField.Modified] = i => i.FileModifiedTime,
            [SmartListField.Created] = i => i.FileCreationTime,
        };
}

/// <summary>
/// CE's exact formula (<c>ComicBook.ReadPercentage</c>, confirmed via direct source check):
/// 0 if <c>PageCount &lt;= 0</c> or <c>LastPageRead &lt;= 0</c> (0-indexed page, so 0 means "on the
/// first page" = not started); otherwise <c>((LastPageRead + 1) * 100 / PageCount)</c> clamped to
/// <c>[1, 100]</c>.
/// </summary>
internal static class ReadPercentage
{
    public static float Of(Issue issue)
    {
        if (issue.PageCount is not > 0 || issue.LastPageRead is not > 0)
        {
            return 0;
        }

        int pct = (issue.LastPageRead.Value + 1) * 100 / issue.PageCount.Value;
        return Math.Clamp(pct, 1, 100);
    }
}
