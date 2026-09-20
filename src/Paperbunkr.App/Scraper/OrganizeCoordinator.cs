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
    Action<int>? enqueueWriteBack = null)
{
    private readonly LibraryOrganizerService _service = CreateService(createContext);

    public OrganizerProfileStore Profiles { get; } = new(createContext);

    /// <summary>The organizer service with the app's rules engine resolving profile exclude rules, and the core undo log.</summary>
    public static LibraryOrganizerService CreateService(Func<PaperbunkrDbContext> createContext) =>
        new(ResolveExcludedIds, new OrganizeUndoLog(createContext));

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

        var profile = profiles.Count == 1
            ? profiles[0]
            : await modalHost.ShowAsync<OrganizerProfile?>(resolve => new ProfileSelectDialogView { DataContext = new ProfileSelectDialogViewModel(profiles, resolve) });
        if (profile is null)
        {
            return "Organize cancelled.";
        }

        return await RunAsync(issueIds, profile, isInteractive: true, cancellationToken);
    }

    /// <summary>The scheduled task: every comic with a file, the chosen profile, never asking anything.</summary>
    public async Task<string> OrganizeLibraryAsync(int profileId, CancellationToken cancellationToken)
    {
        var profile = Profiles.Get(profileId);
        if (profile is null)
        {
            return "The automatic organize profile no longer exists.";
        }

        List<int> ids;
        using (var context = createContext())
        {
            ids = context.Issues.Where(i => i.FilePath != null).Select(i => i.Id).ToList();
        }

        return await RunAsync(ids, profile, isInteractive: false, cancellationToken);
    }

    private async Task<string> RunAsync(IReadOnlyList<int> issueIds, OrganizerProfile profile, bool isInteractive, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.BaseFolder))
        {
            return $"Choose a base folder for the profile \"{profile.Name}\" first.";
        }

        List<Issue> books;
        using (var context = createContext())
        {
            books = context.Issues.Include(i => i.Series).Where(i => issueIds.Contains(i.Id) && i.FilePath != null).ToList();
        }

        if (books.Count == 0)
        {
            return "Nothing to organize.";
        }

        using var job = activity.StartJob(ActivityJobKind.Import, $"Organizing {books.Count} comic{(books.Count == 1 ? string.Empty : "s")} ({profile.Name})",
            cancellable: true, trigger: isInteractive ? ActivityTrigger.Manual : ActivityTrigger.Scheduled);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, job.CancellationToken);

        try
        {
            var plan = await _service.PlanAsync(books, profile, createContext).ConfigureAwait(false);
            var result = await _service.ExecuteAsync(
                plan, profile, isInteractive,
                isInteractive ? (incoming, existingPath, ct) => ShowCollisionAsync(incoming, existingPath) : null,
                createContext,
                (done, total, label) => job.Report(done, total, label),
                linked.Token).ConfigureAwait(false);

            if (enqueueWriteBack is not null)
            {
                foreach (var move in result.Succeeded)
                {
                    enqueueWriteBack(move.Issue.Id);
                }
            }

            var summary = $"Organized {result.Succeeded.Count} comic{(result.Succeeded.Count == 1 ? string.Empty : "s")}; {result.Skipped.Count} skipped; {result.Failed.Count} failed.";
            job.Succeed(summary, itemsProcessed: result.Succeeded.Count, itemsFailed: result.Failed.Count);
            return summary;
        }
        catch (OperationCanceledException)
        {
            job.Fail("Organize cancelled.");
            return "Organize cancelled.";
        }
    }

    private Task<(CollisionResolution Resolution, bool ApplyToAllRemaining)> ShowCollisionAsync(Issue incoming, string existingPath) =>
        modalHost.ShowAsync<(CollisionResolution, bool)>(resolve => new FileConflictDialogView
        {
            DataContext = new FileConflictDialogViewModel($"{incoming.Series?.Name} #{incoming.Number}", Path.GetFileName(existingPath), existingPath, resolve),
        });
}
