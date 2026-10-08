using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Organizing;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Scraper;

/// <summary>
/// The app-side glue for "Organize…" (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md section 8): picks a profile, plans, runs the moves with the
/// collision dialog for interactive runs, and reports the whole run as one Activity Center job. Also builds the shared <see cref="LibraryOrganizerService"/> (its exclude-rule
/// resolver uses the app's rules engine, which lives above the data project).
/// <para>An unattended run (the scheduled task) never opens a dialog: collisions use the profile's <see cref="OrganizerProfile.AutomationCollisionPolicy"/>.</para>
/// </summary>
public sealed class OrganizeCoordinator(
    NativePluginModalHostViewModel modalHost,
    Func<PaperbunkrDbContext> createContext,
    IActivityService activity,
    Action<int>? enqueueWriteBack = null,
    Func<OrganizePlanSummary, IReadOnlyList<OrganizerProfile>, Task<bool>>? confirmPlan = null,
    string? reportFolder = null)
{
    private readonly LibraryOrganizerService _service = CreateService(createContext);

    public OrganizerProfileStore Profiles { get; } = new(createContext);

    /// <summary>The organizer service with the app's rules engine resolving profile exclude rules, and the core undo log.</summary>
    public static LibraryOrganizerService CreateService(Func<PaperbunkrDbContext> createContext) =>
        new(ResolveExcludedIds, new OrganizeUndoLog(createContext), path => RecycleBinHelper.SendToRecycleBin(path));

    private static IReadOnlyCollection<int> ResolveExcludedIds(string ruleJson)
    {
        try
        {
            var rule = JsonSerializer.Deserialize<PluginConditionGroup>(ruleJson);
            return rule is null ? Array.Empty<int>() : new PaperbunkrRulesEngine().Evaluate(rule).Select(i => i.Id).ToHashSet();
        }
        catch (JsonException)
        {
            return Array.Empty<int>();     // a corrupt rule excludes nothing; the profile editor drops it on the next save
        }
    }

    public const string NoProfilesMessage = "Create an organizer profile under Preferences → Organize & Scrape first.";

    public async Task<string> OrganizeIssuesAsync(IReadOnlyList<int> issueIds, CancellationToken cancellationToken = default)
    {
        var profiles = Profiles.GetAll();
        if (profiles.Count == 0)
        {
            return NoProfilesMessage;
        }

        IReadOnlyList<OrganizerProfile>? chosen = profiles.Count == 1
            ? profiles
            : await modalHost.ShowAsync<IReadOnlyList<OrganizerProfile>?>(resolve => new ProfileSelectDialogView { DataContext = new ProfileSelectDialogViewModel(profiles, resolve) });
        if (chosen is not { Count: > 0 })
        {
            return "Organize cancelled.";
        }

        return await RunAsync(issueIds, chosen, isInteractive: true, cancellationToken);
    }

    /// <summary>The scheduled task: every comic with a file, the chosen profile, never asking anything.</summary>
    public Task<string> OrganizeLibraryAsync(int profileId, CancellationToken cancellationToken, IActivityJobHandle? existingJob = null) =>
        OrganizeLibraryAsync(new[] { profileId }, cancellationToken, existingJob);

    /// <summary>The scheduled task with several profiles marked for it: they run together, in list order, as one run.</summary>
    public async Task<string> OrganizeLibraryAsync(IReadOnlyList<int> profileIds, CancellationToken cancellationToken, IActivityJobHandle? existingJob = null)
    {
        var chosen = profileIds.Select(Profiles.Get).OfType<OrganizerProfile>().ToList();
        if (chosen.Count == 0)
        {
            return "The automatic organize profile no longer exists.";
        }

        List<int> ids;
        using (var context = createContext())
        {
            ids = context.Issues.Where(i => i.FilePath != null).Select(i => i.Id).ToList();
        }

        return await RunAsync(ids, chosen, isInteractive: false, cancellationToken, existingJob);
    }

    private async Task<string> RunAsync(IReadOnlyList<int> issueIds, IReadOnlyList<OrganizerProfile> profiles, bool isInteractive, CancellationToken cancellationToken, IActivityJobHandle? existingJob = null)
    {
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.BaseFolder))
            {
                return $"Choose a base folder for the profile \"{profile.Name}\" first.";
            }
        }

        // A Copy into a watched library folder makes the watcher import every copy as a duplicate comic - refuse before a single file is written.
        List<string> watchedFolders;
        using (var watchedContext = createContext())
        {
            watchedFolders = watchedContext.WatchedFolders.Select(w => w.Path).ToList();
        }

        var unsafeCopies = OrganizerSafety.CopiesIntoWatchedFolders(profiles, watchedFolders);
        if (unsafeCopies.Count > 0)
        {
            return OrganizerSafety.Describe(unsafeCopies);
        }

        var profileLabel = profiles.Count == 1 ? profiles[0].Name : $"{profiles.Count} profiles";

        List<Issue> books;
        using (var context = createContext())
        {
            books = context.Issues.Include(i => i.Series).Where(i => issueIds.Contains(i.Id) && i.FilePath != null).ToList();
        }

        if (books.Count == 0)
        {
            return "Nothing to organize.";
        }

        using var owned = existingJob is null
            ? activity.StartJob(ActivityJobKind.Import, $"Organizing {books.Count} comic{(books.Count == 1 ? string.Empty : "s")} ({profileLabel})",
                cancellable: true, trigger: isInteractive ? ActivityTrigger.Manual : ActivityTrigger.Scheduled,
                startQueued: HeavyJobLane.Shared.WouldWait)
            : null;
        var job = existingJob ?? owned!;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, job.CancellationToken);

        try
        {
            // One heavy job at a time (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md §4.3). A scheduled run
            // arrives already inside the lane with its own job, and passes straight through.
            using var laneSlot = HeavyJobLane.Activate(await HeavyJobLane.Shared.EnterAsync(isInteractive, linked.Token));
            owned?.Begin();

            var plans = await _service.PlanManyAsync(books, profiles, createContext).ConfigureAwait(false);

            // A manual run shows what is about to happen before any file moves. A scheduled run has nobody to ask. A run where nothing
            // would move (everything in place, or only problems) has nothing to confirm - go straight on and report.
            var preview = OrganizePlanSummary.From(plans);
            bool simulationOnly = profiles.All(p => p.Mode == OrganizerMode.Simulate);
            if (isInteractive && (preview.HasWork || simulationOnly))
            {
                var proceed = await (confirmPlan ?? ShowPreviewAsync)(preview, profiles).ConfigureAwait(false);
                if (simulationOnly)
                {
                    var simulated = SimulationSummary(preview, OrganizeReport.TrySave(OrganizeReport.FromPlans(plans), reportFolder));
                    owned?.Succeed(simulated, itemsProcessed: preview.Moving, itemsFailed: preview.Problems);
                    return simulated;
                }

                if (!proceed)
                {
                    owned?.Fail("Organize cancelled at the preview - nothing was moved.");
                    return "Organize cancelled.";
                }
            }

            var results = await _service.ExecuteManyAsync(
                plans, isInteractive,
                isInteractive ? (incoming, existingPath, ct) => ShowCollisionAsync(incoming, existingPath) : null,
                createContext,
                (done, total, label) => job.Report(done, total, label),
                linked.Token).ConfigureAwait(false);

            // Only a real Move changes what the library knows about a file. A Simulate touched nothing and a Copy left
            // the original where it was, so neither has any reason to rewrite an archive.
            if (enqueueWriteBack is not null)
            {
                foreach (var moved in results.Where(r => r.Profile.Mode == OrganizerMode.Move).SelectMany(r => r.Result.Succeeded))
                {
                    enqueueWriteBack(moved.Issue.Id);
                }
            }

            var summary = SummarizeAll(results);
            if (results.Any(r => r.Result.Failed.Count > 0 || r.Result.Skipped.Count > 0))
            {
                // The line above can only name the first few; the whole list goes to a file so nothing is lost.
                if (OrganizeReport.TrySave(OrganizeReport.FromResults(results), reportFolder) is { } reportPath)
                {
                    summary += $" Full report: {reportPath}";
                }
            }

            owned?.Succeed(summary, itemsProcessed: results.Sum(r => r.Result.Succeeded.Count), itemsFailed: results.Sum(r => r.Result.Failed.Count));

            return summary;
        }
        catch (OperationCanceledException)
        {
            owned?.Fail("Organize cancelled.");
            return "Organize cancelled.";
        }
        catch (Exception ex) when (owned is not null)
        {
            // A scheduled run (existingJob) lets the scheduler record the failure; an interactive run ends its own job here
            // instead of leaving it "Cancelled" with no explanation.
            var message = $"Organize failed: {ex.Message}";
            owned.Fail(message, ex: ex);
            return message;
        }
    }

    /// <summary>One profile: <see cref="Summarize"/>. Several: each profile's line, named, since "3 failed" means little without saying where.</summary>
    internal static string SummarizeAll(IReadOnlyList<ProfileResult> results) =>
        results.Count == 1
            ? Summarize(results[0].Profile.Mode, results[0].Result)
            : string.Join(" | ", results.Select(r => $"{r.Profile.Name}: {Summarize(r.Profile.Mode, r.Result)}"));

    /// <summary>The one-line run report. Failed and skipped books are named with the reason - "3 failed" alone tells the user
    /// nothing they can act on.</summary>
    internal static string Summarize(OrganizerMode mode, OrganizeResult result)
    {
        var verb = mode switch { OrganizerMode.Copy => "Copied", OrganizerMode.Simulate => "Would organize", _ => "Organized" };
        var parts = new List<string> { $"{verb} {result.Succeeded.Count} comic{(result.Succeeded.Count == 1 ? string.Empty : "s")}" };
        if (result.AlreadyInPlace.Count > 0)
        {
            parts.Add($"{result.AlreadyInPlace.Count} already in place");
        }

        if (result.Skipped.Count > 0)
        {
            parts.Add($"{result.Skipped.Count} skipped");
        }

        if (result.ReplacedIssueIds.Count > 0)
        {
            parts.Add($"{result.ReplacedIssueIds.Count} library entr{(result.ReplacedIssueIds.Count == 1 ? "y" : "ies")} lost the file that was replaced (it is in the Recycle Bin)");
        }

        if (result.Failed.Count > 0)
        {
            var reasons = result.Failed.Take(3).Select(f => $"{Describe(f.Move)}: {f.Error}");
            var more = result.Failed.Count > 3 ? $" (and {result.Failed.Count - 3} more)" : string.Empty;
            parts.Add($"{result.Failed.Count} failed - {string.Join("; ", reasons)}{more}");
        }

        return string.Join("; ", parts) + ".";
    }

    private static string Describe(PlannedMove move) => $"{move.Issue.Series?.Name} #{move.Issue.Number}".Trim() is { Length: > 2 } label ? label : Path.GetFileName(move.SourcePath);

    internal static string SimulationSummary(OrganizePlanSummary preview, string? reportPath)
    {
        var text = $"Simulation: {preview.Moving} would be processed, {preview.AlreadyInPlace} already in place, {preview.Skipped} skipped, {preview.Problems} would fail. Nothing was moved.";
        return reportPath is null ? text : $"{text} Full report: {reportPath}";
    }

    private Task<bool> ShowPreviewAsync(OrganizePlanSummary summary, IReadOnlyList<OrganizerProfile> profiles) =>
        modalHost.ShowAsync<bool>(resolve => new OrganizePreviewDialogView { DataContext = new OrganizePreviewDialogViewModel(summary, profiles, resolve) });

    private Task<(CollisionResolution Resolution, bool ApplyToAllRemaining)> ShowCollisionAsync(Issue incoming, string existingPath) =>
        modalHost.ShowAsync<(CollisionResolution, bool)>(resolve => new FileConflictDialogView
        {
            DataContext = new FileConflictDialogViewModel($"{incoming.Series?.Name} #{incoming.Number}", Path.GetFileName(existingPath), existingPath, resolve),
        });
}
