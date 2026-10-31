using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.SmartLists;

/// <summary>
/// Field catalog for <see cref="SmartListTargetKind.Novel"/> lists (docs/superpowers/specs/
/// 2026-08-30-smart-collections-design.md) — <see cref="Book"/> is Paperbunkr's EPUB/PDF novel
/// entity, unrelated to CE's "Book-collection" fields on <see cref="Issue"/> (physical comic-book-
/// collector metadata) already in <see cref="SmartListField"/>. Every field here is prefixed
/// <c>Novel*</c> to keep the two concepts unambiguous in the enum.
/// </summary>
internal static class NovelSmartListCatalog
{
    public static readonly IReadOnlyDictionary<SmartListField, SmartListFieldDefinition> Definitions =
        new Dictionary<SmartListField, SmartListFieldDefinition>
        {
            [SmartListField.NovelTitle] = new(SmartListField.NovelTitle, "Title", SmartListDataType.Text),
            [SmartListField.NovelAuthor] = new(SmartListField.NovelAuthor, "Author", SmartListDataType.Text),
            [SmartListField.NovelSeries] = new(SmartListField.NovelSeries, "Series", SmartListDataType.Text),
            [SmartListField.NovelFormat] = new(SmartListField.NovelFormat, "Format", SmartListDataType.Text),
            [SmartListField.NovelSummary] = new(SmartListField.NovelSummary, "Summary", SmartListDataType.Text),
            [SmartListField.NovelFinished] = new(SmartListField.NovelFinished, "Finished", SmartListDataType.Toggle),
            [SmartListField.NovelChapterCount] = new(SmartListField.NovelChapterCount, "Chapter Count", SmartListDataType.Number),
            [SmartListField.NovelAdded] = new(SmartListField.NovelAdded, "Added", SmartListDataType.Date),
            [SmartListField.NovelOpened] = new(SmartListField.NovelOpened, "Last Opened", SmartListDataType.Date),
            [SmartListField.NovelPublished] = new(SmartListField.NovelPublished, "Published", SmartListDataType.Date),
            [SmartListField.NovelRating] = new(SmartListField.NovelRating, "My rating (0 = unrated)", SmartListDataType.Number),
            [SmartListField.NovelStatus] = new(SmartListField.NovelStatus, "Reading status", SmartListDataType.Text),
            [SmartListField.NovelTag] = new(SmartListField.NovelTag, "Genres and tags", SmartListDataType.Text),
            [SmartListField.NovelPublisher] = new(SmartListField.NovelPublisher, "Publisher", SmartListDataType.Text),
            [SmartListField.NovelLanguage] = new(SmartListField.NovelLanguage, "Language", SmartListDataType.Text),
            [SmartListField.NovelSeriesIndex] = new(SmartListField.NovelSeriesIndex, "Number in series (0 = none)", SmartListDataType.Number),
            [SmartListField.NovelProgress] = new(SmartListField.NovelProgress, "Progress (%)", SmartListDataType.Number),
        };

    public static readonly IReadOnlyDictionary<SmartListField, Func<Book, string>> TextSelectors =
        new Dictionary<SmartListField, Func<Book, string>>
        {
            [SmartListField.NovelTitle] = b => b.Title,
            [SmartListField.NovelAuthor] = b => b.Author ?? string.Empty,
            [SmartListField.NovelSeries] = b => b.BookSeries?.Name ?? string.Empty,
            [SmartListField.NovelFormat] = b => b.Format.ToString(),
            [SmartListField.NovelSummary] = b => b.Summary ?? string.Empty,
            [SmartListField.NovelStatus] = b => b.Status == BookReadingStatus.WantToRead ? "Want to read" : b.Status.ToString(),
            // Every Genre and Tags value in one string, so "contains" finds a single tag.
            [SmartListField.NovelTag] = b => string.Join(", ", b.Tags.Select(t => t.Value)),
            [SmartListField.NovelPublisher] = b => b.Publisher ?? string.Empty,
            [SmartListField.NovelLanguage] = b => b.LanguageISO ?? string.Empty,
        };

    public static readonly IReadOnlyDictionary<SmartListField, Func<Book, bool>> ToggleSelectors =
        new Dictionary<SmartListField, Func<Book, bool>>
        {
            [SmartListField.NovelFinished] = b => b.Finished,
        };

    public static readonly IReadOnlyDictionary<SmartListField, Func<Book, float>> NumberSelectors =
        new Dictionary<SmartListField, Func<Book, float>>
        {
            [SmartListField.NovelChapterCount] = b => b.ChapterCount,
            [SmartListField.NovelRating] = b => b.Rating ?? 0f,
            [SmartListField.NovelSeriesIndex] = b => b.SeriesIndex ?? 0f,
            [SmartListField.NovelProgress] = b => (float)Math.Round(Paperbunkr.Data.Books.BookProgress.Compute(b) * 100d, 1),
        };

    public static readonly IReadOnlyDictionary<SmartListField, Func<Book, DateTime?>> DateSelectors =
        new Dictionary<SmartListField, Func<Book, DateTime?>>
        {
            [SmartListField.NovelAdded] = b => b.AddedTime,
            [SmartListField.NovelOpened] = b => b.LastOpenedTime,
            [SmartListField.NovelPublished] = b => b.PublishedDate,
        };
}
