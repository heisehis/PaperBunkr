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
    private readonly Action<string>? _sendToRecycleBin;

    /// <param name="resolveExcluded">Turns a profile's exclude-rule JSON into the ids of the issues it matches. Supplied by the app (its rules engine lives above this project); null means "exclude nothing".</param>
    /// <param name="undoLog">Where real moves are recorded for undo; null disables undo.</param>
    /// <param name="sendToRecycleBin">Disposes of a file a Replace overwrites, recoverably. The Recycle Bin helper lives in the app project, so it is injected; null (tests, headless hosts) deletes permanently.</param>
    public LibraryOrganizerService(
        Func<string, IReadOnlyCollection<int>>? resolveExcluded = null,
        OrganizeUndoLog? undoLog = null,
        Action<string>? sendToRecycleBin = null)
    {
        _resolveExcluded = resolveExcluded;
        _undoLog = undoLog;
        _sendToRecycleBin = sendToRecycleBin;
    }

    /// <summary>
    /// Pure planning aside from the one read-only library query below (no writes) - runs the token
    /// engine per book, sanitizes the result, and flags every destination that already exists on disk
    /// or that an earlier book in this same batch already claimed (the plugin's own definition of
    /// "duplicate" - a plain existence check plus in-batch paths, `lobookmover.py:406`; no
    /// hash/metadata comparison). A destination equal to the file's current path is marked
    /// already-in-place instead of being planned as a move. One book that cannot be planned (a bad
    /// template token, an empty file name) becomes a <see cref="PlannedMove.Problem"/> for that book
    /// only - it never aborts the batch.
    /// </summary>
    public Task<OrganizePlan> PlanAsync(IReadOnlyList<Issue> books, OrganizerProfile profile, Func<PaperbunkrDbContext> createDbContext) =>
        PlanCoreAsync(books, profile, createDbContext, restrictLibraryToSeriesIds: null, useExcludeRule: true);

    /// <summary>
    /// What <paramref name="sample"/> (a handful of comics) would look like under <paramref name="profile"/> - the editor's live preview.
    /// The same planning as <see cref="PlanAsync"/>, but the library it reads for series-wide values (start year, first/last issue,
    /// `(series)` credits) is only the sample's own series, so it stays cheap enough to run as you type. The exclude RULE is not evaluated
    /// (it queries the whole library); excluded FOLDERS are.
    /// </summary>
    public Task<OrganizePlan> PreviewAsync(IReadOnlyList<Issue> sample, OrganizerProfile profile, Func<PaperbunkrDbContext> createDbContext) =>
        PlanCoreAsync(sample, profile, createDbContext, sample.Select(i => i.SeriesId).Distinct().ToList(), useExcludeRule: false);

    private async Task<OrganizePlan> PlanCoreAsync(
        IReadOnlyList<Issue> books, OrganizerProfile profile, Func<PaperbunkrDbContext> createDbContext,
        IReadOnlyCollection<int>? restrictLibraryToSeriesIds, bool useExcludeRule)
    {
        if (string.IsNullOrWhiteSpace(profile.BaseFolder))
        {
            throw new InvalidOperationException($"The organizer profile \"{profile.Name}\" has no base folder.");
        }

        IReadOnlyCollection<int> excludedIssueIds = useExcludeRule ? ResolveExcludedIssueIds(profile) : Array.Empty<int>();
        // Tags and custom values are only loaded when a template can read them - they are the heavy part of the query.
        bool Mentions(string text) =>
            profile.FolderTemplate.Contains(text, StringComparison.OrdinalIgnoreCase) || profile.FileTemplate.Contains(text, StringComparison.OrdinalIgnoreCase);
        bool needsTags = Mentions("(series)") || Mentions("genre") || Mentions("tags");
        bool needsCustom = Mentions("custom");
        List<Issue> libraryBooks = await LoadLibraryBooksAsync(createDbContext, needsTags, needsCustom, restrictLibraryToSeriesIds).ConfigureAwait(false);

        // Every value a template reads goes through the Effective* accessors, which look at the issue's accepted filename proposals (a comic
        // whose number, year or format was only ever parsed from its file name has NOTHING in the raw fields). Those collections are not
        // loaded on the issues callers pass in, so each book is evaluated through this fully loaded copy of itself - callers cannot get it wrong.
        var loadedById = libraryBooks.ToDictionary(b => b.Id);
        var aggregateCache = new Dictionary<(int SeriesId, string? Volume, string? Publisher), SeriesAggregate>();

        // One shared context for the whole run - see TemplateContext.AdvanceCounter's own doc comment
        // for why a fresh instance per issue would silently break CE's own running-counter behavior.
        // Aggregate is mutated per issue below rather than rebuilding the whole context each time.
        var context = new TemplateContext
        {
            MonthNames = profile.MonthNames.Count > 0 ? profile.MonthNames : FieldResolvers.DefaultMonthNames,
            EmptyData = profile.EmptyData,
        };
        IReadOnlyList<string> excludedFolders = profile.ExcludeFolders.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        IReadOnlyList<string> requiredTokens = profile.FailEmptyValues ? profile.FailedFields : Array.Empty<string>();

        var moves = new List<PlannedMove>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Issue issue in books)
        {
            if (excludedIssueIds.Contains(issue.Id) || string.IsNullOrEmpty(issue.FilePath)
                || excludedFolders.Any(folder => issue.FilePath.Contains(folder, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Issue evaluated = loadedById.TryGetValue(issue.Id, out Issue? loaded) ? loaded : issue;
            string destination;
            try
            {
                context.Aggregate = GetOrBuildAggregate(evaluated, libraryBooks, aggregateCache);
                context.SeriesBooks = context.Aggregate.Books;
                context.EmptyTokens.Clear();
                destination = BuildDestinationPath(evaluated, issue.FilePath, profile, context);
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException)
            {
                moves.Add(new PlannedMove(evaluated, issue.FilePath, issue.FilePath, false, Problem: ex.Message));
                continue;
            }

            if (requiredTokens.Count > 0 && context.EmptyTokens.Where(t => requiredTokens.Contains(t, StringComparer.OrdinalIgnoreCase)).Distinct().ToList() is { Count: > 0 } missing)
            {
                moves.Add(new PlannedMove(evaluated, issue.FilePath, issue.FilePath, false, SkipReason: $"required field(s) empty: {string.Join(", ", missing)}"));
                continue;
            }

            string fullSource = Path.GetFullPath(issue.FilePath);
            string fullDestination = Path.GetFullPath(destination);
            bool sameFile = string.Equals(fullSource, fullDestination, StringComparison.OrdinalIgnoreCase);
            bool alreadyInPlace = string.Equals(fullSource, fullDestination, StringComparison.Ordinal);
            bool isCollision = !sameFile && File.Exists(destination);
            if (!claimed.Add(fullDestination) && !alreadyInPlace)
            {
                isCollision = true;     // another book in this batch is already headed for this exact path
            }

            moves.Add(new PlannedMove(evaluated, issue.FilePath, destination, isCollision, alreadyInPlace));
        }

        return new OrganizePlan(moves);
    }

    /// <summary>The full library, not just the batch being organized - <c>startyear</c>/<c>EndYear</c>/
    /// <c>startmonth</c>/<c>EndMonth</c>/<c>firstissuenumber</c>/<c>lastissuenumber</c> are real
    /// full-library lookups in CE (`locommon.py`'s <c>get_earliest_book</c>/<c>get_last_book</c>,
    /// verified), not scoped to whatever subset of a series happens to be selected for this one
    /// organize run. <c>AsNoTracking</c> - this is a read-only rollup, never written back.</summary>
    private static async Task<List<Issue>> LoadLibraryBooksAsync(
        Func<PaperbunkrDbContext> createDbContext, bool includeTags, bool includeCustom, IReadOnlyCollection<int>? seriesIds)
    {
        using PaperbunkrDbContext context = createDbContext();
        IQueryable<Issue> query = context.Issues.AsNoTracking().Include(i => i.Series).Include(i => i.MetadataProposals);
        if (seriesIds is not null)
        {
            query = query.Where(i => seriesIds.Contains(i.SeriesId));
        }

        if (includeTags)
        {
            query = query.Include(i => i.Tags);
        }

        if (includeCustom)
        {
            query = query.Include(i => i.CustomValues);
        }

        return await query.ToListAsync().ConfigureAwait(false);
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
            last.EffectiveNumber(),
            last,
            candidates);

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

    /// <summary>The Windows path limit the plugin warns about (`lobookmover.py:627`). Only enforced on Windows.</summary>
    private const int MaxWindowsPathLength = 259;

    private string BuildDestinationPath(Issue issue, string sourcePath, OrganizerProfile profile, TemplateContext context)
    {
        // `UseFolder` / `UseFileName` off keep that half of the current path as it is (`lobookmover.py:1246-1252`).
        string folderPath;
        if (profile.UseFolder)
        {
            string folderPart = Sanitizer.SanitizePath(TemplateEvaluator.Evaluate(profile.FolderTemplate, issue, context), profile.EmptyFolder);
            foreach (string segment in folderPart.Split(Path.DirectorySeparatorChar))
            {
                if (Sanitizer.IsReservedDeviceName(segment))
                {
                    throw new InvalidOperationException($"The folder name \"{segment}\" is reserved by Windows and cannot be created.");
                }
            }

            folderPath = Path.Combine(profile.BaseFolder, folderPart);

            // Defence in depth: whatever the template did, the file must land under the profile's base folder.
            string fullBase = Path.GetFullPath(profile.BaseFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(fullFolder, fullBase, StringComparison.OrdinalIgnoreCase)
                && !fullFolder.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The templates produced a path outside the base folder: {folderPath}");
            }
        }
        else
        {
            folderPath = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        }

        string fileName;
        if (profile.UseFileName)
        {
            string filePart = Sanitizer.SanitizeSegment(TemplateEvaluator.Evaluate(profile.FileTemplate, issue, context));
            if (filePart.Length == 0)
            {
                throw new InvalidOperationException("The file template produced an empty file name for this comic.");
            }

            if (Sanitizer.IsReservedDeviceName(filePart))
            {
                throw new InvalidOperationException($"The file name \"{filePart}\" is reserved by Windows and cannot be created.");
            }

            fileName = filePart + Path.GetExtension(sourcePath);
        }
        else
        {
            fileName = Path.GetFileName(sourcePath);
        }

        string destination = Path.Combine(folderPath, fileName);
        if (OperatingSystem.IsWindows() && Path.GetFullPath(destination).Length > MaxWindowsPathLength)
        {
            throw new InvalidOperationException($"The new path is {Path.GetFullPath(destination).Length} characters, over the {MaxWindowsPathLength} Windows allows: {destination}");
        }

        return destination;
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
        var state = new RunState
        {
            BatchId = profile.Mode == OrganizerMode.Move ? _undoLog?.BeginBatch(profile.Name, profile.BaseFolder) ?? 0 : 0,
        };
        return await ExecuteCoreAsync(plan, profile, isInteractive, interactiveResolver, createDbContext, reportProgress, cancellationToken, state).ConfigureAwait(false);
    }

    /// <summary>What one organize run shares across every profile it executes: the undo batch, the "apply to all remaining" answer, and
    /// every destination already used (or, in Simulate, that would have been) - the plugin's `MovedBooks` (`lobookmover.py:406`), so a later
    /// book aimed at the same path is a real collision even though nothing exists on disk yet.</summary>
    private sealed class RunState
    {
        public int BatchId { get; init; }

        public bool ApplyToAll { get; set; }

        public CollisionResolution Sticky { get; set; } = CollisionResolution.Skip;

        public HashSet<string> Claimed { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<OrganizeResult> ExecuteCoreAsync(
        OrganizePlan plan,
        OrganizerProfile profile,
        bool isInteractive,
        InteractiveCollisionResolver? interactiveResolver,
        Func<PaperbunkrDbContext> createDbContext,
        Action<int, int, string?>? reportProgress,
        CancellationToken cancellationToken,
        RunState state)
    {
        var result = new OrganizeResult();

        int total = plan.Moves.Count;
        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlannedMove move = plan.Moves[i];
            reportProgress?.Invoke(i, total, move.Issue.EffectiveTitle() ?? Path.GetFileName(move.SourcePath));

            if (move.Problem is not null)
            {
                result.Failed.Add((move, move.Problem));
                continue;
            }

            if (move.SkipReason is not null)
            {
                result.Skipped.Add(move);
                continue;
            }

            if (move.IsAlreadyInPlace)
            {
                state.Claimed.Add(Path.GetFullPath(move.DestinationPath));
                result.AlreadyInPlace.Add(move);
                continue;
            }

            try
            {
                string destination = move.DestinationPath;

                // Decided now, not from the plan's flag: earlier items in this run change what exists.
                bool sameFile = string.Equals(Path.GetFullPath(move.SourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);
                bool collides = !sameFile && (File.Exists(destination) || state.Claimed.Contains(Path.GetFullPath(destination)));

                if (collides)
                {
                    CollisionResolution resolution;
                    if (state.ApplyToAll)
                    {
                        resolution = state.Sticky;
                    }
                    else if (isInteractive && interactiveResolver is not null)
                    {
                        (resolution, bool applyAll) = await interactiveResolver(move.Issue, destination, cancellationToken).ConfigureAwait(false);
                        if (applyAll)
                        {
                            state.ApplyToAll = true;
                            state.Sticky = resolution;
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
                        destination = CreateRenamedPath(destination, state.Claimed);
                    }
                    else if (resolution == CollisionResolution.Replace)
                    {
                        DisposeOfReplacedFile(destination, move.Issue.Id, profile.Mode, createDbContext, result);
                    }
                }

                state.Claimed.Add(Path.GetFullPath(destination));
                await PerformMoveAsync(move.Issue, move.SourcePath, destination, profile.Mode, createDbContext, cancellationToken).ConfigureAwait(false);

                if (profile.Mode == OrganizerMode.Move)
                {
                    RecordUndoBestEffort(state.BatchId, move.SourcePath, destination);

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


    /// <summary>Plans one run for several profiles at once, with the plugin's rule for a book several profiles would place
    /// (`create_book_paths`, `lobookmover.py:172-236`): every <b>Copy</b> profile copies it, but only the <b>last Move</b> profile that can
    /// place it moves it - earlier Move profiles skip it ("moved by a later profile"). Books a profile excludes or cannot plan do not count as
    /// placed by it. One profile behaves exactly like <see cref="PlanAsync"/>.</summary>
    public async Task<IReadOnlyList<ProfilePlan>> PlanManyAsync(
        IReadOnlyList<Issue> books, IReadOnlyList<OrganizerProfile> profiles, Func<PaperbunkrDbContext> createDbContext)
    {
        var plans = new List<ProfilePlan>();
        foreach (OrganizerProfile profile in profiles)
        {
            plans.Add(new ProfilePlan(profile, await PlanAsync(books, profile, createDbContext).ConfigureAwait(false)));
        }

        if (profiles.Count < 2)
        {
            return plans;
        }

        static bool CanPlace(PlannedMove m) => m.Problem is null && m.SkipReason is null;

        var winner = new Dictionary<int, int>();
        for (int i = 0; i < plans.Count; i++)
        {
            if (plans[i].Profile.Mode == OrganizerMode.Copy)
            {
                continue;
            }

            foreach (PlannedMove move in plans[i].Plan.Moves.Where(CanPlace))
            {
                winner[move.Issue.Id] = i;      // a later profile overwrites an earlier one
            }
        }

        for (int i = 0; i < plans.Count; i++)
        {
            if (plans[i].Profile.Mode == OrganizerMode.Copy)
            {
                continue;
            }

            int index = i;
            var moves = plans[i].Plan.Moves.Select(m =>
                CanPlace(m) && winner.TryGetValue(m.Issue.Id, out int w) && w != index
                    ? m with { IsCollision = false, IsAlreadyInPlace = false, SkipReason = $"the book is moved by a later profile ({plans[w].Profile.Name})" }
                    : m).ToList();
            plans[i] = plans[i] with { Plan = new OrganizePlan(moves) };
        }

        return plans;
    }

    /// <summary>Executes <see cref="PlanManyAsync"/>'s plans as ONE run: Copy profiles first (a copy must read the file before a Move profile
    /// relocates it), then the Move profiles, all in a single undo batch and sharing the collision answers. Returns each profile's own result.</summary>
    public async Task<IReadOnlyList<ProfileResult>> ExecuteManyAsync(
        IReadOnlyList<ProfilePlan> plans,
        bool isInteractive,
        InteractiveCollisionResolver? interactiveResolver,
        Func<PaperbunkrDbContext> createDbContext,
        Action<int, int, string?>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        List<OrganizerProfile> moveProfiles = plans.Select(p => p.Profile).Where(p => p.Mode == OrganizerMode.Move).ToList();
        var bases = moveProfiles.Select(p => p.BaseFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var state = new RunState
        {
            // The folder-cleanup boundary an undo may go up to is only known when every moving profile shares one base folder.
            BatchId = moveProfiles.Count > 0
                ? _undoLog?.BeginBatch(string.Join(", ", moveProfiles.Select(p => p.Name)), bases.Count == 1 ? bases[0] : null) ?? 0
                : 0,
        };

        List<ProfilePlan> ordered = plans.Where(p => p.Profile.Mode == OrganizerMode.Copy).Concat(plans.Where(p => p.Profile.Mode != OrganizerMode.Copy)).ToList();
        int grandTotal = ordered.Sum(p => p.Plan.Moves.Count);
        int offset = 0;
        var results = new List<ProfileResult>();
        foreach (ProfilePlan item in ordered)
        {
            int start = offset;
            OrganizeResult result = await ExecuteCoreAsync(
                item.Plan, item.Profile, isInteractive, interactiveResolver, createDbContext,
                (done, _, label) => reportProgress?.Invoke(start + done, grandTotal, label),
                cancellationToken, state).ConfigureAwait(false);
            results.Add(new ProfileResult(item.Profile, result));
            offset += item.Plan.Moves.Count;
        }

        return results;
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
        string? batchBaseFolder = _undoLog.GetBatchBaseFolder(entries[0].BatchId);

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

                bool caseOnlyChange = string.Equals(entry.OldPath, entry.NewPath, StringComparison.OrdinalIgnoreCase);
                if (!caseOnlyChange && File.Exists(entry.OldPath))
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

                // The run created these folders (or emptied them by moving into them); take back the ones that are empty now,
                // never going above the base folder the run was organizing into.
                if (!string.IsNullOrWhiteSpace(batchBaseFolder))
                {
                    RemoveEmptyFoldersUpward(Path.GetDirectoryName(entry.NewPath), batchBaseFolder);
                }
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

    /// <summary>The plugin's numeric-suffix algorithm (`lobookmover.py:676-689`): strip an existing
    /// " (N)" suffix, then try " (1)", " (2)", ... up to 100 attempts. A candidate must be free on disk
    /// and not already claimed by an earlier book in this run (which matters in Simulate, where nothing
    /// is really created).</summary>
    private static string CreateRenamedPath(string path, ISet<string> claimed)
    {
        string? directory = Path.GetDirectoryName(path);
        string extension = Path.GetExtension(path);
        string baseName = ExistingSuffixRegex().Replace(Path.GetFileNameWithoutExtension(path), string.Empty);

        for (int i = 1; i <= 100; i++)
        {
            string candidate = Path.Combine(directory ?? string.Empty, $"{baseName} ({i}){extension}");
            if (!File.Exists(candidate) && !claimed.Contains(Path.GetFullPath(candidate)))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Could not find a free renamed path for '{path}' after 100 attempts.");
    }

    // The plugin's regex matches a single digit (`[0-9]`); \d+ additionally strips " (12)" so a name renamed
    // past nine still counts up instead of growing " (12) (1)".
    private static readonly Regex ExistingSuffixRegexInstance = new(@" \(\d+\)$", RegexOptions.Compiled);

    private static Regex ExistingSuffixRegex() => ExistingSuffixRegexInstance;

    /// <summary>Replace: the overwritten file goes to the Recycle Bin (recoverable, as the plugin does -
    /// `lobookmover.py:503`) instead of being deleted outright, and any library entry that pointed at it is left
    /// without a file rather than sharing the incoming book's path. Simulate never gets here in a way that
    /// touches anything.</summary>
    private void DisposeOfReplacedFile(string path, int movingIssueId, OrganizerMode mode, Func<PaperbunkrDbContext> createDbContext, OrganizeResult result)
    {
        if (mode == OrganizerMode.Simulate)
        {
            return;
        }

        if (File.Exists(path))
        {
            if (_sendToRecycleBin is not null)
            {
                _sendToRecycleBin(path);
                if (File.Exists(path))
                {
                    throw new IOException($"Could not move the existing file to the Recycle Bin: {path}");
                }
            }
            else
            {
                File.Delete(path);
            }
        }

        using PaperbunkrDbContext context = createDbContext();
        List<Issue> orphaned = context.Issues.Where(i => i.FilePath == path && i.Id != movingIssueId).ToList();
        if (orphaned.Count > 0)
        {
            foreach (Issue issue in orphaned)
            {
                issue.FilePath = null;
                result.ReplacedIssueIds.Add(issue.Id);
            }

            context.SaveChanges();
        }
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
            try
            {
                using PaperbunkrDbContext context = createDbContext();
                Issue? tracked = await context.Issues.FindAsync(new object[] { issue.Id }, cancellationToken).ConfigureAwait(false);
                if (tracked is not null)
                {
                    tracked.FilePath = destination;
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // The move and the library record must not disagree: put the file back so the record (still
                // the old path) stays true, and say so. If even that fails, the message names both paths.
                try
                {
                    File.Move(destination, source, overwrite: false);
                }
                catch (Exception moveBack)
                {
                    throw new IOException(
                        $"The file was moved to '{destination}' but the library could not be updated ({ex.Message}) and moving it back failed ({moveBack.Message}).", ex);
                }

                throw new IOException($"The library could not be updated ({ex.Message}); the file was moved back to '{source}'.", ex);
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
