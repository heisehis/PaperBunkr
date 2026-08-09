using System.Xml.Linq;
using cYo.Common.Xml;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.CeMigration;

/// <summary>
/// Parse-and-write path for docs/onboarding.md §14: load a ComicRackCE <c>ComicDb.xml</c> via the
/// ported <see cref="ComicDatabase.LoadXml(string, Action{int})"/>, walk the resulting object
/// graph, and write <see cref="Series"/>/<see cref="Issue"/> records into a
/// <see cref="PaperbunkrDbContext"/>. Full design:
/// docs/superpowers/specs/2026-08-06-migration-ux-design.md.
///
/// Series identity: CE has no first-class Series entity (§6) - issues are grouped by the flat
/// <c>ComicBook.Series</c> string, deduped case-insensitively (<see cref="GroupBySeries"/>).
/// Near-but-not-exact name matches (§14 step 3) are surfaced via <see cref="SeriesNameMatcher"/>
/// as <see cref="SeriesConflictCandidate"/>s for the UI to resolve (merge / keep separate) before
/// or after commit - <see cref="Migrate"/> never blocks on an unresolved one, it just records it
/// as a <see cref="SeriesConflict"/> row for the Needs Review queue.
///
/// Idempotency: an incoming series whose name exactly matches an existing <see cref="Series"/>
/// (case-insensitive) never creates a duplicate row - only issues not already present (matched by
/// Number+Volume) are added. This is what makes re-running migration after adding books in CE a
/// safe "sync new issues in" operation rather than a duplicate-creating one.
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
        return ComicDatabase.LoadXml(comicDbXmlPath, StripComicListsAndDeserialize, progress);
    }

    /// <summary>
    /// Migration only ever reads <see cref="ComicDatabase.Books"/> - it has nothing to do with
    /// CE's serialized Smart Lists/saved searches (a distinct, unrelated concept from Paperbunkr's
    /// own native Smart Lists feature). A real CE install's <c>ComicDb.xml</c> bundles both in one
    /// document, and a Smart List can reference a plugin-contributed matcher type (e.g.
    /// <c>ComicBookPluginMatcher</c>, defined in CE's separate <c>ComicRack.Plugins</c> project -
    /// deliberately not ported here, since plugin API support is Beta-scoped per
    /// docs/onboarding.md §16). <c>XmlSerializer</c> has no partial-tolerance mode - one
    /// unrecognized type anywhere in the document fails the whole deserialize, even though the
    /// Books data itself is perfectly readable. Stripping the <c>&lt;ComicLists&gt;</c> subtree
    /// (<see cref="ComicLibrary.ComicLists"/>) before deserializing keeps migration working
    /// against real-world libraries without needing to port CE's plugin matcher ecosystem just to
    /// read past it.
    /// </summary>
    private static ComicDatabase StripComicListsAndDeserialize(Stream source)
    {
        var document = XDocument.Load(source);
        document.Root?.Element("ComicLists")?.Remove();

        using var strippedXml = new MemoryStream();
        document.Save(strippedXml);
        strippedXml.Position = 0;
        return XmlUtility.Load<ComicDatabase>(strippedXml, compressed: false);
    }

    /// <summary>
    /// Dry-run scan (§14 step 2): walks the library and reports what would be written, without
    /// touching <paramref name="context"/> - including the §14 step 3 fuzzy conflict candidates
    /// (both within the incoming batch and against series already in <paramref name="context"/>),
    /// so the UI can show them before the user chooses to commit.
    /// </summary>
    public MigrationPreview Preview(ComicDatabase database, PaperbunkrDbContext context)
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

        var distinctNames = seriesNames.ToList();
        var existingNames = context.Series.Select(s => s.Name).ToList();

        var conflicts = SeriesNameMatcher.FindIntraGroupCandidates(distinctNames)
            .Concat(SeriesNameMatcher.FindAgainstExistingCandidates(distinctNames, existingNames))
            .ToList();

        return new MigrationPreview(seriesNames.Count, issueCount, guessedContentType, conflicts);
    }

    /// <summary>
    /// Commit (§14 step 4): writes Series/Issue records into <paramref name="context"/> and saves.
    /// <paramref name="options"/> carries the user's §14 step 3 conflict decisions (merges and
    /// explicit keep-separate calls); any candidate conflict left out of it is neither blocking nor
    /// silently dropped - it's written as a <see cref="SeriesConflictStatus.Pending"/>
    /// <see cref="SeriesConflict"/> row for the Needs Review queue.
    /// </summary>
    public MigrationResult Migrate(ComicDatabase database, PaperbunkrDbContext context, MigrationOptions? options = null)
    {
        options ??= new MigrationOptions();

        int seriesCreated = 0;
        int seriesMerged = 0;
        int issuesCreated = 0;
        int guessedContentType = 0;

        var rawGroups = GroupBySeries(database).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var rawNames = rawGroups.Keys.ToList();

        // Canonical name each raw incoming name resolves to, after applying MergeGroups - members
        // of a merge group collapse onto the group's first name.
        var canonicalOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in options.MergeGroups)
        {
            var namesInGroup = group.Where(n => rawGroups.ContainsKey(n)).ToList();
            if (namesInGroup.Count == 0)
            {
                continue;
            }

            string canonical = namesInGroup[0];
            foreach (string name in namesInGroup)
            {
                canonicalOf[name] = canonical;
            }
        }

        var effectiveBooks = new Dictionary<string, List<ComicBook>>(StringComparer.OrdinalIgnoreCase);
        var effectiveMembers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in rawNames)
        {
            string canonical = canonicalOf.TryGetValue(name, out string? c) ? c : name;
            if (!effectiveBooks.TryGetValue(canonical, out var books))
            {
                books = new List<ComicBook>();
                effectiveBooks[canonical] = books;
                effectiveMembers[canonical] = new List<string>();
            }

            books.AddRange(rawGroups[name]);
            effectiveMembers[canonical].Add(name);
        }

        // Loaded once up front: the pre-migration state of the library, used both for the
        // idempotent exact-name path and for resolving the against-existing conflict candidates
        // recomputed below.
        var existingSeriesByName = context.Series.Include(s => s.Issues)
            .AsEnumerable()
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var existingNamesBeforeMigration = existingSeriesByName.Keys.ToList();

        var rawNameToSeries = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);

        foreach (var (canonicalName, books) in effectiveBooks)
        {
            Series? target = null;
            foreach (string memberName in effectiveMembers[canonicalName])
            {
                if (options.MergeIntoExisting.TryGetValue(memberName, out int existingId))
                {
                    target = existingSeriesByName.Values.FirstOrDefault(s => s.Id == existingId)
                        ?? context.Series.Include(s => s.Issues).First(s => s.Id == existingId);
                    break;
                }
            }

            bool isExplicitMerge = target is not null;

            if (target is null && existingSeriesByName.TryGetValue(canonicalName, out var exact))
            {
                target = exact;
            }

            if (target is not null)
            {
                var existingKeys = new HashSet<(string?, int?)>(target.Issues.Select(i => (i.Number, i.Volume)));
                int added = 0;
                foreach (var book in books)
                {
                    var issue = MapIssue(book);
                    if (!existingKeys.Add((issue.Number, issue.Volume)))
                    {
                        continue;
                    }

                    issue.Series = target;
                    target.Issues.Add(issue);
                    context.Issues.Add(issue);
                    issuesCreated++;
                    added++;
                }

                if (isExplicitMerge || added > 0)
                {
                    seriesMerged++;
                }

                foreach (string memberName in effectiveMembers[canonicalName])
                {
                    rawNameToSeries[memberName] = target;
                }

                continue;
            }

            var series = new Series { Name = canonicalName, SortName = canonicalName };

            // Series-level facts: take from the first book that has a non-default value, since CE
            // duplicates them per-issue for lack of anywhere else to store them (§6).
            series.Publisher = books.Select(b => b.Publisher).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            series.Genre = books.Select(b => b.Genre).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            series.Summary = books.Select(b => b.Summary).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            series.IsComplete = books.Any(b => b.SeriesComplete == YesNo.Yes);

            // Exact migration mapping table from docs/onboarding.md §6. All books in a CE "series"
            // grouping are expected to share the same Manga value in practice; if they disagree,
            // the first book's value wins (surfacing that disagreement is a §14-step-5 "Needs
            // Review" concern, handled by the ContentType == Unknown check below, not by this
            // mapping choice itself).
            var (contentType, readingMode) = MapMangaField(books[0].Manga);
            series.ContentType = contentType;
            series.ReadingMode = readingMode;

            if (books.Any(b => b.Manga == MangaYesNo.Unknown))
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

            foreach (string memberName in effectiveMembers[canonicalName])
            {
                rawNameToSeries[memberName] = series;
            }
        }

        int conflictsPending = WriteConflictRows(context, rawNames, existingSeriesByName, existingNamesBeforeMigration, canonicalOf, rawNameToSeries, options);

        context.SaveChanges();
        return new MigrationResult(seriesCreated, issuesCreated, guessedContentType, seriesMerged, conflictsPending);
    }

    /// <summary>
    /// Recomputes the same conflict candidates <see cref="Preview"/> reported, skips any the
    /// caller's <paramref name="options"/> already resolved via merge, and writes a
    /// <see cref="SeriesConflict"/> row for the rest - <see cref="SeriesConflictStatus.KeptSeparate"/>
    /// for ones explicitly dismissed, <see cref="SeriesConflictStatus.Pending"/> for everything
    /// else. Returns the count of rows left <c>Pending</c>.
    /// </summary>
    private static int WriteConflictRows(
        PaperbunkrDbContext context,
        List<string> rawNames,
        Dictionary<string, Series> existingSeriesByName,
        List<string> existingNamesBeforeMigration,
        Dictionary<string, string> canonicalOf,
        Dictionary<string, Series> rawNameToSeries,
        MigrationOptions options)
    {
        int pendingCount = 0;

        foreach (var candidate in SeriesNameMatcher.FindIntraGroupCandidates(rawNames))
        {
            string canonicalA = canonicalOf.TryGetValue(candidate.IncomingName, out string? ca) ? ca : candidate.IncomingName;
            string canonicalB = canonicalOf.TryGetValue(candidate.MatchedName, out string? cb) ? cb : candidate.MatchedName;
            if (string.Equals(canonicalA, canonicalB, StringComparison.OrdinalIgnoreCase))
            {
                // Already merged into the same series via MergeGroups - implicitly resolved.
                continue;
            }

            bool keptSeparate = options.ResolvedKeepSeparate.Contains(candidate);
            context.SeriesConflicts.Add(new SeriesConflict
            {
                IncomingName = candidate.IncomingName,
                MatchedName = candidate.MatchedName,
                Similarity = candidate.Similarity,
                Status = keptSeparate ? SeriesConflictStatus.KeptSeparate : SeriesConflictStatus.Pending,
                SeriesA = rawNameToSeries.GetValueOrDefault(candidate.IncomingName),
                SeriesB = rawNameToSeries.GetValueOrDefault(candidate.MatchedName),
            });

            if (!keptSeparate)
            {
                pendingCount++;
            }
        }

        foreach (var candidate in SeriesNameMatcher.FindAgainstExistingCandidates(rawNames, existingNamesBeforeMigration))
        {
            if (rawNameToSeries.TryGetValue(candidate.IncomingName, out var target)
                && string.Equals(target.Name, candidate.MatchedName, StringComparison.OrdinalIgnoreCase))
            {
                // Explicitly merged into the existing series it fuzzy-matched - resolved.
                continue;
            }

            bool keptSeparate = options.ResolvedKeepSeparate.Contains(candidate);
            context.SeriesConflicts.Add(new SeriesConflict
            {
                IncomingName = candidate.IncomingName,
                MatchedName = candidate.MatchedName,
                Similarity = candidate.Similarity,
                Status = keptSeparate ? SeriesConflictStatus.KeptSeparate : SeriesConflictStatus.Pending,
                SeriesA = rawNameToSeries.GetValueOrDefault(candidate.IncomingName),
                ExistingSeries = existingSeriesByName.GetValueOrDefault(candidate.MatchedName),
            });

            if (!keptSeparate)
            {
                pendingCount++;
            }
        }

        return pendingCount;
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

/// <summary>Result of <see cref="CeLibraryMigrator.Preview"/> - the §14 step 2 "dry-run scan" plus §14 step 3 conflict candidates.</summary>
public record MigrationPreview(int SeriesCount, int IssueCount, int SeriesWithGuessedContentType, List<SeriesConflictCandidate> ConflictCandidates);

/// <summary>Result of <see cref="CeLibraryMigrator.Migrate"/> - what was actually committed.</summary>
public record MigrationResult(int SeriesCreated, int IssuesCreated, int SeriesWithGuessedContentType, int SeriesMerged, int ConflictsPending);

/// <summary>
/// The user's §14 step 3 conflict decisions, passed into <see cref="CeLibraryMigrator.Migrate"/>.
/// Any <see cref="SeriesConflictCandidate"/> not covered by any of these three collections isn't
/// blocked or dropped - it's written as a <see cref="SeriesConflictStatus.Pending"/>
/// <see cref="SeriesConflict"/> row instead (§14 step 5's Needs Review queue).
/// </summary>
public record MigrationOptions
{
    /// <summary>Incoming series name -> existing library Series.Id to fold its issues into.</summary>
    public Dictionary<string, int> MergeIntoExisting { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sets of incoming series names (from the same import) to treat as one series, keyed to the first name in each set.</summary>
    public List<List<string>> MergeGroups { get; init; } = new();

    /// <summary>Candidates the user explicitly dismissed as "not the same series" - written as KeptSeparate rather than Pending.</summary>
    public HashSet<SeriesConflictCandidate> ResolvedKeepSeparate { get; init; } = new();
}
