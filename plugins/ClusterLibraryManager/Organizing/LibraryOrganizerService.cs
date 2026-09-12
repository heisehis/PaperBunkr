using System.Text.Json;
using System.Text.RegularExpressions;
using ClusterLibraryManager.Templating;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Plugins.Automation;

namespace ClusterLibraryManager.Organizing;

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
    private readonly IRulesEngine? _rulesEngine;
    private readonly UndoLog? _undoLog;

    public LibraryOrganizerService(IRulesEngine? rulesEngine = null, UndoLog? undoLog = null)
    {
        _rulesEngine = rulesEngine;
        _undoLog = undoLog;
    }

    /// <summary>
    /// Pure planning - no writes. Runs the token engine per book, sanitizes the result, and flags
    /// every destination that already exists on disk (CE's own definition of "duplicate" - a plain
    /// `File.Exists` check, no hash/metadata comparison, verified against `loduplicate.py`).
    /// </summary>
    public Task<OrganizePlan> PlanAsync(IReadOnlyList<Issue> books, OrganizerProfile profile)
    {
        IReadOnlyCollection<int> excludedIssueIds = ResolveExcludedIssueIds(profile);

        var moves = new List<PlannedMove>();
        foreach (Issue issue in books)
        {
            if (excludedIssueIds.Contains(issue.Id) || string.IsNullOrEmpty(issue.FilePath))
            {
                continue;
            }

            string destination = BuildDestinationPath(issue, profile);
            bool isCollision = File.Exists(destination) &&
                !string.Equals(Path.GetFullPath(destination), Path.GetFullPath(issue.FilePath), StringComparison.OrdinalIgnoreCase);
            moves.Add(new PlannedMove(issue, issue.FilePath, destination, isCollision));
        }

        return Task.FromResult(new OrganizePlan(moves));
    }

    private string BuildDestinationPath(Issue issue, OrganizerProfile profile)
    {
        string folderPart = Sanitizer.SanitizePath(TemplateEvaluator.Evaluate(profile.FolderTemplate, issue));
        string filePart = Sanitizer.SanitizeSegment(TemplateEvaluator.Evaluate(profile.FileTemplate, issue));
        string extension = Path.GetExtension(issue.FilePath ?? string.Empty);
        return Path.Combine(profile.BaseFolder, folderPart, filePart + extension);
    }

    /// <summary>CE's own zero-rules default is "move everything" (`ExcludeMode: Do not` with no
    /// rules) - reuses Paperbunkr's own <see cref="IRulesEngine"/> instead of porting CE's bespoke
    /// exclude-rule engine (design doc §5). No rule configured, or no rules engine available (e.g. a
    /// pure-unit-test caller with neither), both mean "exclude nothing".</summary>
    private IReadOnlyCollection<int> ResolveExcludedIssueIds(OrganizerProfile profile)
    {
        if (_rulesEngine is null || string.IsNullOrEmpty(profile.ExcludeRuleJson))
        {
            return Array.Empty<int>();
        }

        PluginConditionGroup? rule = JsonSerializer.Deserialize<PluginConditionGroup>(profile.ExcludeRuleJson);
        return rule is null ? Array.Empty<int>() : _rulesEngine.Evaluate(rule).Select(i => i.Id).ToHashSet();
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
        int batchId = UndoLog.NewBatchId();
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
