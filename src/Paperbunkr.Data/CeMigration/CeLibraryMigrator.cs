using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.CeMigration;

/// <summary>
/// Raw parse-and-write path for docs/onboarding.md §14 steps 2 ("Dry-run scan") and 4 ("Commit"):
/// load a ComicRackCE <c>ComicDb.xml</c> via the ported <see cref="ComicDatabase.LoadXml(string, Action{int})"/>,
/// walk the resulting object graph, and write <see cref="Series"/>/<see cref="Issue"/> records into
/// a <see cref="PaperbunkrDbContext"/>.
///
/// Explicitly out of scope here (deferred to the UI layer per the task brief): fuzzy series-name
/// conflict detection/merge UI (§14 step 3) and the "Needs Review" queue (§14 step 5). This class
/// only does the mechanical grouping-and-write; <see cref="MigrationResult"/> exposes enough
/// (series/issue counts, how many series landed with a guessed ContentType) to build that UI on
/// top of it later without re-walking the source data.
/// </summary>
public class CeLibraryMigrator
{
    /// <summary>
    /// Loads a CE <c>ComicDb.xml</c> file using the ported engine parser. This is genuinely
    /// portable engine code (confirmed by reading src/Paperbunkr.Engine/Database/ComicDatabase.cs
    /// directly, not assumed from docs/onboarding.md §5's claim) - <c>LoadXml</c> itself is a
    /// plain XmlSerializer-based deserialization with no WinForms dependency. The one Engine method
    /// this migrator deliberately does NOT call is <c>ComicDatabase.FinalizeLoading()</c>: it's
    /// real per-book file-validation work (stats each book's file on disk, etc.) that doesn't
    /// belong in a schema migration and isn't needed to read the fields this migrator maps.
    /// </summary>
    public static ComicDatabase LoadFromXml(string comicDbXmlPath, Action<int>? progress = null)
    {
        return ComicDatabase.LoadXml(comicDbXmlPath, progress);
    }

    /// <summary>
    /// Dry-run scan (§14 step 2): walks the library and reports what would be written, without
    /// touching the target <see cref="PaperbunkrDbContext"/>.
    /// </summary>
    public MigrationPreview Preview(ComicDatabase database)
    {
        var seriesNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int guessedContentType = 0;
        int issueCount = 0;

        foreach (var group in GroupBySeries(database))
        {
            seriesNames.Add(group.Key);
            issueCount += group.Count();
            if (group.Any(b => b.Manga == MangaYesNo.Unknown))
            {
                guessedContentType++;
            }
        }

        return new MigrationPreview(seriesNames.Count, issueCount, guessedContentType);
    }

    /// <summary>
    /// Commit (§14 step 4): writes Series/Issue records into <paramref name="context"/> and saves.
    /// Series identity: CE has no first-class Series entity (§6) - issues are grouped by the flat
    /// <c>ComicBook.Series</c> string, deduped case-insensitively, into one <see cref="Series"/>
    /// row per distinct name. Blank/whitespace series names are grouped under "Unknown".
    /// </summary>
    public MigrationResult Migrate(ComicDatabase database, PaperbunkrDbContext context)
    {
        int seriesCreated = 0;
        int issuesCreated = 0;
        int guessedContentType = 0;

        foreach (var group in GroupBySeries(database))
        {
            var books = group.ToList();
            var series = new Series
            {
                Name = group.Key,
                SortName = group.Key,
            };

            // Series-level facts: take from the first book that has a non-default value, since CE
            // duplicates them per-issue for lack of anywhere else to store them (§6).
            series.Publisher = books.Select(b => b.Publisher).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            series.Genre = books.Select(b => b.Genre).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            series.Summary = books.Select(b => b.Summary).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            series.IsComplete = books.Any(b => b.SeriesComplete == YesNo.Yes);

            // Exact migration mapping table from docs/onboarding.md §6. All books in a CE "series"
            // grouping are expected to share the same Manga value in practice; if they disagree,
            // the first book's value wins (surfacing that disagreement is a §14-step-5 "Needs
            // Review" concern, out of scope here).
            var (contentType, readingMode) = MapMangaField(books[0].Manga);
            series.ContentType = contentType;
            series.ReadingMode = readingMode;
            if (books[0].Manga == MangaYesNo.Unknown)
            {
                guessedContentType++;
            }

            context.Series.Add(series);
            seriesCreated++;

            foreach (var book in books)
            {
                var issue = MapIssue(book);
                issue.Series = series;
                series.Issues.Add(issue);
                context.Issues.Add(issue);
                issuesCreated++;
            }
        }

        context.SaveChanges();
        return new MigrationResult(seriesCreated, issuesCreated, guessedContentType);
    }

    private static IEnumerable<IGrouping<string, ComicBook>> GroupBySeries(ComicDatabase database)
    {
        return database.Books
            .Where(b => b != null)
            .GroupBy(b => string.IsNullOrWhiteSpace(b.Series) ? "Unknown" : b.Series.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Exact mapping table from docs/onboarding.md §6.</summary>
    public static (ContentType ContentType, ReadingMode ReadingMode) MapMangaField(MangaYesNo manga) => manga switch
    {
        MangaYesNo.YesAndRightToLeft => (ContentType.Manga, ReadingMode.RightToLeft),
        MangaYesNo.Yes => (ContentType.Manga, ReadingMode.LeftToRight),
        MangaYesNo.No => (ContentType.Comic, ReadingMode.LeftToRight),
        _ => (ContentType.Unknown, ReadingMode.LeftToRight),
    };

    private static Issue MapIssue(ComicBook book)
    {
        return new Issue
        {
            Title = NullIfEmpty(book.Title),
            Number = NullIfEmpty(book.Number),
            Count = book.Count > 0 ? book.Count : null,
            Volume = book.Volume != 0 ? book.Volume : null,
            AlternateSeries = NullIfEmpty(book.AlternateSeries),
            AlternateNumber = NullIfEmpty(book.AlternateNumber),
            StoryArc = NullIfEmpty(book.StoryArc),
            // StoryArcNumber has no CE source field - confirmed absent entirely (§6) - left null.
            SeriesGroup = NullIfEmpty(book.SeriesGroup),
            Summary = NullIfEmpty(book.Summary),
            Notes = NullIfEmpty(book.Notes),
            Review = NullIfEmpty(book.Review),
            Year = book.Year > 0 ? book.Year : null,
            Month = book.Month > 0 ? book.Month : null,
            Day = book.Day > 0 ? book.Day : null,
            Writer = NullIfEmpty(book.Writer),
            Penciller = NullIfEmpty(book.Penciller),
            Inker = NullIfEmpty(book.Inker),
            Colorist = NullIfEmpty(book.Colorist),
            Letterer = NullIfEmpty(book.Letterer),
            CoverArtist = NullIfEmpty(book.CoverArtist),
            Editor = NullIfEmpty(book.Editor),
            Translator = NullIfEmpty(book.Translator),
            Publisher = NullIfEmpty(book.Publisher),
            Imprint = NullIfEmpty(book.Imprint),
            Genre = NullIfEmpty(book.Genre),
            Web = NullIfEmpty(book.Web),
            PageCount = book.PageCount > 0 ? book.PageCount : null,
            LanguageISO = NullIfEmpty(book.LanguageISO),
            Format = NullIfEmpty(book.Format),
            AgeRating = NullIfEmpty(book.AgeRating),
            Characters = NullIfEmpty(book.Characters),
            Teams = NullIfEmpty(book.Teams),
            Locations = NullIfEmpty(book.Locations),
            Tags = NullIfEmpty(book.Tags),
            FilePath = NullIfEmpty(book.FilePath),
            AddedTime = book.AddedTime != DateTime.MinValue ? book.AddedTime : null,
            ReleasedTime = book.ReleasedTime != DateTime.MinValue ? book.ReleasedTime : null,
            OpenedTime = book.OpenedTime != DateTime.MinValue ? book.OpenedTime : null,
            LastPageRead = book.LastPageRead,
            FileIsMissing = book.FileIsMissing,
            CustomThumbnailKey = NullIfEmpty(book.CustomThumbnailKey),
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Result of <see cref="CeLibraryMigrator.Preview"/> - the §14 step 2 "dry-run scan".</summary>
public record MigrationPreview(int SeriesCount, int IssueCount, int SeriesWithGuessedContentType);

/// <summary>Result of <see cref="CeLibraryMigrator.Migrate"/> - what was actually committed.</summary>
public record MigrationResult(int SeriesCreated, int IssuesCreated, int SeriesWithGuessedContentType);
