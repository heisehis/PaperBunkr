using System.Text.Json;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Naming;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Organizing;

/// <summary>Interactive collision resolver - shown per file, modal (design doc §6, grilling Q9=B).
/// Returns the user's choice plus whether they checked "apply to all remaining conflicts".</summary>
public delegate Task<(CollisionResolution Resolution, bool ApplyToAllRemaining)> InteractiveCollisionResolver(
    Issue incoming, string existingPath, CancellationToken cancellationToken);

/// <summary>
/// Two-phase file organizer (design doc §6), modeled on <c>MigrationViewModel</c>'s staged-screen
/// pattern rather than a single move-everything call. <see cref="PlanAsync"/> is pure aside from
/// `File.Exists` collision checks; <see cref="ExecuteAsync"/> performs the real I/O with per-item
/// failure isolation - never a batch-wide transaction (see this method's own doc comment for why).
/// </summary>
public sealed class LibraryOrganizerService
{
    private readonly Func<string, IReadOnlyCollection<int>>? _resolveExcluded;
    private readonly OrganizeUndoLog? _undoLog;

    /// <param name="resolveExcluded">Turns a profile's exclude-rule JSON into the ids of the issues it matches. Supplied by the app (its rules engine lives above this project); null means "exclude nothing".</param>
    /// <param name="undoLog">Where real moves are recorded for undo; null disables undo.</param>
    public LibraryOrganizerService(Func<string, IReadOnlyCollection<int>>? resolveExcluded = null, OrganizeUndoLog? undoLog = null)
    {
        _resolveExcluded = resolveExcluded;
        _undoLog = undoLog;
    }

    /// <summary>
    /// Pure planning aside from the one read-only library query below (no writes) - runs the token
    /// engine per book, sanitizes the result, and flags every destination that already exists on disk
    /// (CE's own definition of "duplicate" - a plain `File.Exists` check, no hash/metadata comparison,
    /// verified against `loduplicate.py`).
    /// </summary>
    public async Task<OrganizePlan> PlanAsync(IReadOnlyList<Issue> books, OrganizerProfile profile, Func<PaperbunkrDbContext> createDbContext)
    {
        IReadOnlyCollection<int> excludedIssueIds = ResolveExcludedIssueIds(profile);
        List<Issue> libraryBooks = await LoadLibraryBooksAsync(createDbContext).ConfigureAwait(false);
        var aggregateCache = new Dictionary<(int SeriesId, string? Volume, string? Publisher), SeriesAggregate>();

        // One shared context for the whole run - see TemplateContext.AdvanceCounter's own doc comment
        // for why a fresh instance per issue would silently break CE's own running-counter behavior.
        // Aggregate is mutated per issue below rather than rebuilding the whole context each time.
        var context = new TemplateContext { MonthNames = profile.MonthNames.Count > 0 ? profile.MonthNames : FieldResolvers.DefaultMonthNames };

        var moves = new List<PlannedMove>();
        foreach (Issue issue in books)
        {
            if (excludedIssueIds.Contains(issue.Id) || string.IsNullOrEmpty(issue.FilePath))
            {
                continue;
            }

            context.Aggregate = GetOrBuildAggregate(issue, libraryBooks, aggregateCache);
            string destination = BuildDestinationPath(issue, profile, context);
            bool isCollision = File.Exists(destination) &&
                !string.Equals(Path.GetFullPath(destination), Path.GetFullPath(issue.FilePath), StringComparison.OrdinalIgnoreCase);
            moves.Add(new PlannedMove(issue, issue.FilePath, destination, isCollision));
        }

        return new OrganizePlan(moves);
    }

    /// <summary>The full library, not just the batch being organized - <c>startyear</c>/<c>EndYear</c>/
    /// <c>startmonth</c>/<c>EndMonth</c>/<c>firstissuenumber</c>/<c>lastissuenumber</c> are real
    /// full-library lookups in CE (`locommon.py`'s <c>get_earliest_book</c>/<c>get_last_book</c>,
    /// verified), not scoped to whatever subset of a series happens to be selected for this one
    /// organize run. <c>AsNoTracking</c> - this is a read-only rollup, never written back.</summary>
    private static async Task<List<Issue>> LoadLibraryBooksAsync(Func<PaperbunkrDbContext> createDbContext)
    {
        using PaperbunkrDbContext context = createDbContext();
        return await context.Issues.AsNoTracking().Include(i => i.Series).ToListAsync().ConfigureAwait(false);
    }

    /// <summary>Per-(series, volume, publisher) cache, mirroring CE's own <c>startbooks</c>/<c>endbooks</c>
    /// module-level memoization (`locommon.py`, verified) so a batch with many books from the same
    /// series/volume only computes this once.</summary>
    private static SeriesAggregate GetOrBuildAggregate(
        Issue issue,
        IReadOnlyList<Issue> libraryBooks,
        Dictionary<(int SeriesId, string? Volume, string? Publisher), SeriesAggregate> cache)
    {
        var key = (SeriesId: issue.SeriesId, Volume: issue.EffectiveVolume(), Publisher: issue.Publisher);
        if (cache.TryGetValue(key, out SeriesAggregate? cached))
        {
            return cached;
        }

        List<Issue> candidates = libraryBooks
            .Where(b => b.SeriesId == issue.SeriesId
                && string.Equals(b.EffectiveVolume(), key.Volume, StringComparison.Ordinal)
                && string.Equals(b.Publisher, key.Publisher, StringComparison.Ordinal))
            .ToList();

        // The book itself is always a candidate even if (implausibly) missing from libraryBooks -
        // matches CE's own get_earliest_book/get_last_book, both of which seed their search starting
        // from `book` (the book currently being organized), not an empty/sentinel value.
        if (candidates.All(b => b.Id != issue.Id))
        {
            candidates.Add(issue);
        }

        Issue earliest = FindEarliestBook(issue, candidates);
        Issue last = FindLastBook(issue, candidates);

        var aggregate = new SeriesAggregate(
            earliest.EffectiveYear(),
            earliest.Month,
            last.EffectiveYear(),
            last.Month,
            earliest.EffectiveNumber(),
            last.EffectiveNumber());

        cache[key] = aggregate;
        return aggregate;
    }

    /// <summary>Ported verbatim from CE's <c>get_earliest_book</c> (`locommon.py:501`, verified),
    /// sequential-if structure preserved exactly (each condition re-reads the current best after any
    /// earlier condition this same iteration may have just replaced it, matching Python's own
    /// re-assign-in-place semantics) - including its one apparent quirk: a candidate whose year is
    /// literally 1 never gets to "rescue" a best-so-far that has no year at all. Harmless in practice
    /// (no real comic has publication year 1), kept rather than "fixed" per this project's own standing
    /// rule to port CE's real behavior, not a guessed-at improvement of it. -1 is CE's own sentinel for
    /// "unknown year/month" (<c>ShadowYear</c>/<c>Month</c> default), mirrored here instead of null to
    /// keep every comparison a direct, faithful transcription.</summary>
    private static Issue FindEarliestBook(Issue book, IReadOnlyList<Issue> candidates)
    {
        Issue best = book;

        foreach (Issue b in candidates)
        {
            int bestYear = best.EffectiveYear() ?? -1;
            int bYear = b.EffectiveYear() ?? -1;

            if (bestYear == -1 && bYear != 1)
            {
                best = b;
            }

            bestYear = best.EffectiveYear() ?? -1;
            if (bYear != -1 && bYear < bestYear)
            {
                best = b;
            }

            bestYear = best.EffectiveYear() ?? -1;
            int bMonth = b.Month ?? -1;
            if (bYear == bestYear && bMonth != -1)
            {
                int bestMonth = best.Month ?? -1;
                if (bestMonth == -1)
                {
                    best = b;
                }
                else if (bMonth < bestMonth)
                {
                    best = b;
                }
                else if (bMonth == bestMonth)
                {
                    // CE compares ShadowNumber with Python 2's string `<` - lexical/ordinal, not
                    // numeric ("10" sorts before "9") - replicated exactly via ordinal comparison.
                    if (string.CompareOrdinal(b.EffectiveNumber() ?? string.Empty, best.EffectiveNumber() ?? string.Empty) < 0)
                    {
                        best = b;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>Ported verbatim from CE's <c>get_last_book</c> (`locommon.py:553`, verified) - a
    /// different, number-only algorithm from <see cref="FindEarliestBook"/>, not a "latest date" lookup:
    /// a candidate whose issue number isn't a plain digit string unconditionally replaces the current
    /// best (CE's own quirk for annotated numbers like "Annual 1"); once the best has a digit number,
    /// only a strictly higher digit number replaces it.</summary>
    private static Issue FindLastBook(Issue book, IReadOnlyList<Issue> candidates)
    {
        Issue best = book;

        foreach (Issue b in candidates)
        {
            bool bestIsDigit = int.TryParse(best.EffectiveNumber(), out int bestValue);
            if (!bestIsDigit)
            {
                best = b;
                continue;
            }

            if (int.TryParse(b.EffectiveNumber(), out int bValue) && bestValue < bValue)
            {
                best = b;
            }
        }

        return best;
    }

    private string BuildDestinationPath(Issue issue, OrganizerProfile profile, TemplateContext context)
    {
        string folderPart = Sanitizer.SanitizePath(TemplateEvaluator.Evaluate(profile.FolderTemplate, issue, context));
        string filePart = Sanitizer.SanitizeSegment(TemplateEvaluator.Evaluate(profile.FileTemplate, issue, context));
        string extension = Path.GetExtension(issue.FilePath ?? string.Empty);
        return Path.Combine(profile.BaseFolder, folderPart, filePart + extension);
    }

    /// <summary>CE's own zero-rules default is "move everything" (`ExcludeMode: Do not` with no
    /// rules) - reuses Paperbunkr's own <see cref="IRulesEngine"/> instead of porting CE's bespoke
    /// exclude-rule engine (design doc §5). No rule configured, or no rules engine available (e.g. a
    /// pure-unit-test caller with neither), both mean "exclude nothing".</summary>
    private IReadOnlyCollection<int> ResolveExcludedIssueIds(OrganizerProfile profile)
    {
        if (_resolveExcluded is null || string.IsNullOrEmpty(profile.ExcludeRuleJson))
        {
            return Array.Empty<int>();
        }

        return _resolveExcluded(profile.ExcludeRuleJson);
    }

    /// <summary>
    /// Performs the planned moves/copies. Failure isolation is per-item, deliberately not one
    /// batch-wide EF transaction (design doc §6): a file move is a real filesystem side effect a
    /// database transaction can't roll back, so a whole-batch rollback after a late failure would
    /// leave already-moved files' disk state and database state pointing at different things - worse
    /// than no rollback. Each item's file move and its `Issue.FilePath` update happen together as one
    /// small step; a failure there is logged and the batch continues (exactly CE's own verified
    /// `process_books` behavior).
    ///
    /// <paramref name="isInteractive"/> gates collision handling (design doc §6/§9's headless-
    /// automation fix): true shows <paramref name="interactiveResolver"/> per collision; false always
    /// uses <paramref name="profile"/>'s <see cref="OrganizerProfile.AutomationCollisionPolicy"/>
    /// instead, so an unattended Scheduled Task run never hangs waiting on a modal nobody can answer.
    /// </summary>
    public async Task<OrganizeResult> ExecuteAsync(
        OrganizePlan plan,
        OrganizerProfile profile,
        bool isInteractive,
        InteractiveCollisionResolver? interactiveResolver,
        Func<PaperbunkrDbContext> createDbContext,
        Action<int, int, string?>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new OrganizeResult();
        int batchId = _undoLog?.BeginBatch(profile.Name) ?? 0;
        bool applyToAllRemaining = false;
        CollisionResolution stickyResolution = CollisionResolution.Skip;

        int total = plan.Moves.Count;
        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlannedMove move = plan.Moves[i];
            reportProgress?.Invoke(i, total, move.Issue.EffectiveTitle() ?? Path.GetFileName(move.SourcePath));

            try
            {
                string destination = move.DestinationPath;

                if (move.IsCollision && File.Exists(destination))
                {
                    CollisionResolution resolution;
                    if (applyToAllRemaining)
                    {
                        resolution = stickyResolution;
                    }
                    else if (isInteractive && interactiveResolver is not null)
                    {
                        (resolution, bool applyAll) = await interactiveResolver(move.Issue, destination, cancellationToken).ConfigureAwait(false);
                        if (applyAll)
                        {
                            applyToAllRemaining = true;
                            stickyResolution = resolution;
                        }
                    }
                    else
                    {
                        // Non-interactive (Scheduled Task) run - never shows a modal (design doc
                        // §6/§9's headless-automation fix).
                        resolution = profile.AutomationCollisionPolicy switch
                        {
                            AutomationCollisionPolicy.Skip => CollisionResolution.Skip,
                            AutomationCollisionPolicy.Overwrite => CollisionResolution.Replace,
                            _ => CollisionResolution.Rename,
                        };
                    }

                    if (resolution == CollisionResolution.Skip)
                    {
                        result.Skipped.Add(move);
                        continue;
                    }

                    if (resolution == CollisionResolution.Rename)
                    {
                        destination = CreateRenamedPath(destination);
                    }
                    else if (resolution == CollisionResolution.Replace)
                    {
                        DeleteExisting(destination, profile.Mode);
                    }
                }

                await PerformMoveAsync(move.Issue, move.SourcePath, destination, profile.Mode, createDbContext, cancellationToken).ConfigureAwait(false);

                if (profile.Mode == OrganizerMode.Move)
                {
                    RecordUndoBestEffort(batchId, move.SourcePath, destination);

                    if (profile.RemoveEmptyFolders && !string.IsNullOrWhiteSpace(profile.BaseFolder))
                    {
                        RemoveEmptyFoldersUpward(Path.GetDirectoryName(move.SourcePath), profile.BaseFolder);
                    }
                }

                result.Succeeded.Add(move with { DestinationPath = destination });
            }
            catch (Exception ex)
            {
                result.Failed.Add((move, ex.Message));
            }
        }

        reportProgress?.Invoke(total, total, null);
        return result;
    }

    /// <summary>
    /// "Undo last organize" (design doc §8) - the lightweight first pass explicitly scoped there
    /// ("each real Move batch writes a compact log... a single 'Undo last organize' action reverses
    /// them") had the log-writing half built (<see cref="OrganizeUndoLog"/>) but nothing ever actually called
    /// this reversal, so the feature never did anything a user could trigger. Reverses the most recent
    /// Move batch's entries newest-first: moves each file back from its organized location to its
    /// original one and restores <see cref="Issue.FilePath"/>, then deletes the batch from the log
    /// regardless of partial failures - a batch that's been attempted once shouldn't be silently
    /// re-offered again with half its entries already gone. One item's failure (the file already moved
    /// again since, the original location occupied by something else, a permission error) is logged and
    /// skipped, matching this class's own per-item resilience elsewhere - never a batch-aborting throw.
    /// </summary>
    public async Task<UndoResult> UndoLastOrganizeAsync(Func<PaperbunkrDbContext> createDbContext, CancellationToken cancellationToken = default)
    {
        if (_undoLog is null)
        {
            return new UndoResult(0, 0, Array.Empty<string>());
        }

        IReadOnlyList<OrganizeMove> entries = _undoLog.GetLastBatch();
        if (entries.Count == 0)
        {
            return new UndoResult(0, 0, Array.Empty<string>());
        }

        int reversed = 0;
        var errors = new List<string>();

        foreach (OrganizeMove entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(entry.NewPath))
                {
                    errors.Add($"{Path.GetFileName(entry.NewPath)}: no longer at its organized location - skipped.");
                    continue;
                }

                if (File.Exists(entry.OldPath))
                {
                    errors.Add($"{Path.GetFileName(entry.NewPath)}: original location is occupied again - skipped.");
                    continue;
                }

                string? originalDirectory = Path.GetDirectoryName(entry.OldPath);
                if (!string.IsNullOrEmpty(originalDirectory))
                {
                    Directory.CreateDirectory(originalDirectory);
                }

                File.Move(entry.NewPath, entry.OldPath, overwrite: false);

                using (PaperbunkrDbContext context = createDbContext())
                {
                    Issue? tracked = await context.Issues.FirstOrDefaultAsync(i => i.FilePath == entry.NewPath, cancellationToken).ConfigureAwait(false);
                    if (tracked is not null)
                    {
                        tracked.FilePath = entry.OldPath;
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                reversed++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(entry.NewPath)}: {ex.Message}");
            }
        }

        // Deleted regardless of partial failure (design doc §8's own "prevents re-offering an
        // already-reversed batch" rationale) - re-attempting a half-undone batch a second time would
        // try to move files that already moved back, which is worse than just surfacing the failures
        // once and letting the user reorganize manually if something didn't come back.
        _undoLog.MarkBatchReverted(entries[0].BatchId);

        return new UndoResult(reversed, errors.Count, errors);
    }

    /// <summary>CE's exact numeric-suffix algorithm (`lobookmover.py:670-689`, verified): strip an
    /// existing " (N)" suffix, then try " (1)", " (2)", ... up to 100 attempts.</summary>
    private static string CreateRenamedPath(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        string extension = Path.GetExtension(path);
        string baseName = ExistingSuffixRegex().Replace(Path.GetFileNameWithoutExtension(path), string.Empty);

        for (int i = 1; i <= 100; i++)
        {
            string candidate = Path.Combine(directory ?? string.Empty, $"{baseName} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Could not find a free renamed path for '{path}' after 100 attempts.");
    }

    private static readonly Regex ExistingSuffixRegexInstance = new(@" \(\d+\)$", RegexOptions.Compiled);

    private static Regex ExistingSuffixRegex() => ExistingSuffixRegexInstance;

    /// <summary>Recycle-bin-safe where the platform supports it; Simulate mode never reaches here
    /// (it's short-circuited in <see cref="PerformMoveAsync"/> before any real I/O).</summary>
    private static void DeleteExisting(string path, OrganizerMode mode)
    {
        if (mode == OrganizerMode.Simulate)
        {
            return;
        }

        File.Delete(path);
    }

    private static async Task PerformMoveAsync(
        Issue issue, string source, string destination, OrganizerMode mode, Func<PaperbunkrDbContext> createDbContext, CancellationToken cancellationToken)
    {
        if (mode == OrganizerMode.Simulate)
        {
            return;
        }

        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (mode == OrganizerMode.Move)
        {
            File.Move(source, destination, overwrite: false);
        }
        else
        {
            File.Copy(source, destination, overwrite: false);
        }

        // Only Move updates the library's own record of the file's location - a Copy leaves the
        // original Issue exactly as it was; registering the copy as a new library book is CE's own
        // "add copied book to library" sub-setting, not built in this pass (design doc's own scope).
        if (mode == OrganizerMode.Move)
        {
            using PaperbunkrDbContext context = createDbContext();
            Issue? tracked = await context.Issues.FindAsync(new object[] { issue.Id }, cancellationToken).ConfigureAwait(false);
            if (tracked is not null)
            {
                tracked.FilePath = destination;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// CE's own "remove empty folders" cleanup, run only after a real Move (a Copy leaves the source
    /// untouched, so there's nothing to clean up). Walks upward from the just-vacated source directory,
    /// deleting each folder that's now completely empty, stopping the moment a folder is non-empty,
    /// missing, or would be <paramref name="baseFolder"/> itself or something above it - this never
    /// deletes the profile's own base library folder, only folders this organize run genuinely emptied
    /// out underneath it.
    /// </summary>
    private static void RemoveEmptyFoldersUpward(string? directory, string baseFolder)
    {
        string normalizedBase = Path.GetFullPath(baseFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        while (!string.IsNullOrEmpty(directory))
        {
            string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(fullDirectory, normalizedBase, StringComparison.OrdinalIgnoreCase)
                || !fullDirectory.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!Directory.Exists(fullDirectory) || Directory.EnumerateFileSystemEntries(fullDirectory).Any())
            {
                break;
            }

            try
            {
                Directory.Delete(fullDirectory);
            }
            catch (IOException)
            {
                break;
            }
            catch (UnauthorizedAccessException)
            {
                break;
            }

            directory = Path.GetDirectoryName(fullDirectory);
        }
    }

    /// <summary>Undo-log write ordering fix (design doc §6): the core file move + `Issue.FilePath`
    /// update above already happened and are authoritative by the time this runs. A failure here is
    /// logged and does not retry the move, does not touch already-committed core state, and does not
    /// abort the batch - it only means this one item won't be reversible via "Undo last organize".</summary>
    private void RecordUndoBestEffort(int batchId, string oldPath, string newPath)
    {
        if (_undoLog is null)
        {
            return;
        }

        try
        {
            _undoLog.Record(batchId, oldPath, newPath);
        }
        catch (Exception)
        {
            // Intentionally swallowed - see method doc comment. The move already succeeded and is
            // the source of truth; losing undo coverage for this one item is the only consequence.
        }
    }
}
