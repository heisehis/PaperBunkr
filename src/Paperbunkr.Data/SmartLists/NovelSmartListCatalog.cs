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
        };

    public static readonly IReadOnlyDictionary<SmartListField, Func<Book, string>> TextSelectors =
        new Dictionary<SmartListField, Func<Book, string>>
        {
            [SmartListField.NovelTitle] = b => b.Title,
            [SmartListField.NovelAuthor] = b => b.Author ?? string.Empty,
            [SmartListField.NovelSeries] = b => b.BookSeries?.Name ?? string.Empty,
            [SmartListField.NovelFormat] = b => b.Format.ToString(),
            [SmartListField.NovelSummary] = b => b.Summary ?? string.Empty,
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
        };

    public static readonly IReadOnlyDictionary<SmartListField, Func<Book, DateTime?>> DateSelectors =
        new Dictionary<SmartListField, Func<Book, DateTime?>>
        {
            [SmartListField.NovelAdded] = b => b.AddedTime,
            [SmartListField.NovelOpened] = b => b.LastOpenedTime,
            [SmartListField.NovelPublished] = b => b.PublishedDate,
        };
}
