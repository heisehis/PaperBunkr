using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Paperbunkr.App.Plugins;
using Paperbunkr.Data;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.Services;

/// <summary>Result of a completed <see cref="LibraryFolderScanner"/> run.</summary>
public record LibraryFolderScanResult(int IssuesAdded, int SeriesTouched, IReadOnlyList<int> AddedIssueIds);

/// <summary>Result of a completed <see cref="LibraryFolderScanner.SyncMetadataAsync"/> run.</summary>
public record LibraryMetadataSyncResult(int IssuesUpdated);

/// <summary>Result of a completed <see cref="LibraryFolderScanner.ResyncSeriesFromFileAsync"/> run.</summary>
public record LibrarySeriesResyncResult(int IssuesReassigned);

/// <summary>
/// On-demand folder scan-and-import (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md
/// §2, embedded-metadata follow-up in docs/superpowers/specs/
/// 2026-08-09-embedded-metadata-and-migration-relocation-design.md §1) - Paperbunkr's first
/// non-migration way to add comics to the library. Reads embedded ComicInfo.xml when present (via
/// the same <see cref="IInfoStorage"/> cast <c>ComicBook.RefreshInfoFromFile</c> uses internally) -
/// embedded metadata wins per-field over <see cref="ComicNameInfo.FromFilePath(string)"/> filename
/// parsing, which stays as the fallback for files with no embedded info or an unsupported format.
/// <see cref="ImportNewFilesAsync"/> is the live-watch entry point
/// (docs/superpowers/specs/2026-08-23-live-folder-watch-scanning-design.md) added later - it shares
/// the same <see cref="ImportFiles"/> logic as a full <see cref="ScanAllAsync"/> pass.
/// </summary>
public class LibraryFolderScanner
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    /// <summary>
    /// Real ParseComicPath-hook anchor (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-
    /// hooks-plan.md §7). This service has no natural "attach point" the way a long-lived screen
    /// ViewModel does - it's freshly constructed in many places (<c>DragImportService</c>,
    /// <c>LiveFolderWatchService</c>, <c>LibraryScreenViewModel</c>, <c>MainViewModel</c>'s own
    /// constructor, all of which run before <c>PluginHostService.Initialize</c> exists), so unlike
    /// <c>LibraryScreenViewModel.AttachHost</c> this is a settable static, set once from
    /// <c>App.axaml.cs</c> alongside the other <c>AttachHost</c> calls. Null in every test (no test
    /// exercises a live plugin against a real scan) and in any scan that runs before the app has
    /// finished starting up.
    /// </summary>
    public static PluginHostService? PluginHost { get; set; }

    /// <summary>Proactive scan-alert check (docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-scan-alerts-design.md §4) - same "no natural attach point" reasoning as <see cref="PluginHost"/> above.</summary>
    public static PluginScanAlertService? ScanAlertService { get; set; }

    public LibraryFolderScanner()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal LibraryFolderScanner(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<LibraryFolderScanResult> ScanAllAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        return await Task.Run(() => ScanAll(progress, ct), ct);
    }

    private LibraryFolderScanResult ScanAll(IProgress<(int Done, int Total)> progress, CancellationToken ct)
    {
        using var context = _contextFactory();

        var supportedExtensions = new HashSet<string>(Providers.Readers.GetFileExtensions(), StringComparer.OrdinalIgnoreCase);
        var existingPaths = new HashSet<string>(
            context.Issues.Where(i => i.FilePath != null).Select(i => i.FilePath!),
            StringComparer.OrdinalIgnoreCase);

        var candidateFiles = new List<string>();
        foreach (string folder in context.WatchedFolders.Select(w => w.Path).ToList())
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            candidateFiles.AddRange(
                Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f)) && !existingPaths.Contains(f)));
        }

        return ImportFiles(context, candidateFiles, progress, ct);
    }

    /// <summary>
    /// Live-watch entry point (docs/superpowers/specs/2026-08-23-live-folder-watch-scanning-design.md
    /// §3) - imports a specific, already-known set of file paths (a debounced batch of
    /// <c>FileSystemWatcher</c> <c>Created</c> events) rather than enumerating an entire watched
    /// folder. Applies the same extension/not-already-in-library filtering <see cref="ScanAll"/>
    /// does before handing off to the shared <see cref="ImportFiles"/> import logic, so a
    /// live-watched new file gets identical embedded-metadata/proposal/series-matching treatment to
    /// one picked up by a manual "Scan Now".
    /// </summary>
    public async Task<LibraryFolderScanResult> ImportNewFilesAsync(IReadOnlyCollection<string> files, IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        return await Task.Run(
            () =>
            {
                using var context = _contextFactory();

                var supportedExtensions = new HashSet<string>(Providers.Readers.GetFileExtensions(), StringComparer.OrdinalIgnoreCase);
                var existingPaths = new HashSet<string>(
                    context.Issues.Where(i => i.FilePath != null).Select(i => i.FilePath!),
                    StringComparer.OrdinalIgnoreCase);

                var candidateFiles = files
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f)) && !existingPaths.Contains(f))
                    .ToList();

                return ImportFiles(context, candidateFiles, progress, ct);
            },
            ct);
    }

    /// <summary>
    /// Shared per-file import body for both <see cref="ScanAll"/> (an entire watched folder's new
    /// files) and <see cref="ImportNewFilesAsync"/> (a live-watch debounced batch) - same embedded
    /// metadata handling, filename-parsing fallback, series find-or-create, and metadata-proposal
    /// creation regardless of which caller found the files.
    /// </summary>
    private LibraryFolderScanResult ImportFiles(PaperbunkrDbContext context, List<string> candidateFiles, IProgress<(int Done, int Total)> progress, CancellationToken ct)
    {
        int total = candidateFiles.Count;
        int done = 0;
        progress.Report((0, total));

        // Read once, not per-file - the policy doesn't change mid-scan.
        var appSettings = context.GetOrCreateAppSettings();

        // Loaded once and updated in-memory as new series are created within this run, so multiple
        // new issues for the same not-yet-existing series in one scan land on the same Series row
        // instead of creating a duplicate per file.
        var seriesByName = context.Series.ToList().ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);
        var seriesTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedIssues = new List<Issue>();
        int issuesAdded = 0;

        // Series-mismatch proposals that were auto-Accepted this scan (docs/superpowers/specs/
        // 2026-08-17-metadata-model-phase2b-series-reassignment-design.md) - actually applying the
        // reassignment has to wait until after context.SaveChanges() below gives every new Issue and
        // Series a real, persisted Id. SeriesReassignmentResolver.Apply resolves the source series
        // via issue.SeriesId, which reads as 0 (meaningless) on an unsaved entity.
        var autoAcceptedSeriesProposals = new List<MetadataProposal>();

        foreach (string file in candidateFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var nameInfo = ComicNameInfo.FromFilePath(file);
                ApplyParseComicPathOverride(nameInfo, file);
                var embeddedInfo = EmbeddedComicInfoReader.TryRead(file);

                // Series mismatch detection: embedded and filename can disagree about the series
                // name. The issue is always attached to the embedded-derived name first, exactly as
                // before this feature existed (embedded wins per-field, unconditionally) - a
                // mismatch only ever produces a reviewable proposal alongside that, never changes
                // which series the issue is attached to at creation time. Automatic policy still
                // reassigns it, just as a genuine second step after the initial attachment (below,
                // post-save), the same way an Accepted Number/Volume/Year proposal takes effect on
                // top of a real (if here, non-blank) starting value rather than pre-empting it.
                string? embeddedSeriesName = string.IsNullOrWhiteSpace(embeddedInfo?.Series) ? null : embeddedInfo.Series.Trim();
                string? filenameSeriesName = string.IsNullOrWhiteSpace(nameInfo.Series) ? null : nameInfo.Series.Trim();
                bool seriesMismatch = embeddedSeriesName is not null && filenameSeriesName is not null
                    && !string.Equals(embeddedSeriesName, filenameSeriesName, StringComparison.OrdinalIgnoreCase);
                bool autoAcceptSeriesProposal = seriesMismatch && appSettings.MetadataResolutionPolicy == MetadataResolutionPolicy.Automatic;

                string seriesName = embeddedSeriesName ?? filenameSeriesName ?? "Unknown";

                bool isNewSeries = !seriesByName.TryGetValue(seriesName, out var series);
                if (isNewSeries)
                {
                    // TPB folding (docs/superpowers/specs/2026-08-31-series-identity-scan-fixes-
                    // design.md §1): a trade paperback's embedded Series name often carries
                    // collection wording ("Batman: Court of Owls", "Batman Vol. 1") an ongoing
                    // single-issue series' own Series field doesn't - an exact-name miss here would
                    // otherwise spawn a spurious second series for what's really just a collected
                    // volume of the same story. Only attempted on the exact-name miss (never
                    // pre-empts a real hit above), and only ever attaches to an EXISTING series -
                    // the stripped name is never used to name a brand-new series, so a standalone
                    // TPB-only work with no ongoing single-issue counterpart still gets its own
                    // distinct series under its full raw name, same as before this feature existed.
                    if (IsTradePaperback(embeddedInfo?.Format))
                    {
                        string stripped = StripCollectionWording(seriesName);
                        if (!string.Equals(stripped, seriesName, StringComparison.OrdinalIgnoreCase)
                            && seriesByName.TryGetValue(stripped, out var folded))
                        {
                            series = folded;
                            isNewSeries = false;
                        }
                    }
                }

                if (isNewSeries)
                {
                    series = new Series { Name = seriesName };
                    context.Series.Add(series);
                    seriesByName[seriesName] = series;
                }

                var issue = new Issue
                {
                    Series = series,
                    FilePath = file,
                    AddedTime = DateTime.UtcNow,
                };

                if (embeddedInfo is not null)
                {
                    CeLibraryMigrator.MapStoryFields(embeddedInfo, issue);

                    // Classification/reading-direction detection (docs/superpowers/specs/2026-08-16-
                    // manga-content-type-classification-design.md §4) - guarded to a brand-new series
                    // only, never an existing one. Series.ContentType's own property initializer
                    // (Entities/Series.cs) is Unknown, so checking "still Unknown" would look like a
                    // valid not-yet-classified signal at first glance - but Unknown is itself one of
                    // the selectable Options in Bulk Edit/the series picker (docs/superpowers/specs/
                    // 2026-08-16-manga-content-type-classification-design.md §1), so a user can
                    // deliberately set a series back to Unknown; guarding on that value instead of
                    // "isNewSeries" would silently override a real, intentional choice on a later
                    // scan. "isNewSeries" is the guard that actually matches "never overwrite a value
                    // from a prior scan, migration, or manual edit on a series that already exists."
                    // Reuses CeLibraryMigrator.MapMangaField exactly as CE migration itself does, so a
                    // book classified via migration and one classified via a fresh scan land on
                    // identical values.
                    if (isNewSeries && embeddedInfo.Manga != MangaYesNo.Unknown)
                    {
                        var (contentType, readingMode) = CeLibraryMigrator.MapMangaField(embeddedInfo.Manga);
                        series.ContentType = contentType;
                        series.ReadingMode = readingMode;
                    }
                    // Falls back to Publisher when the Manga field itself is absent/Unknown
                    // (docs/superpowers/specs/2026-08-30-publisher-content-type-classification-
                    // design.md) - checked before LanguageISO since a publisher match (Marvel, Viz,
                    // etc.) is at least as confident a signal and this codebase already treats the
                    // Manga field as strictly more authoritative than either. Same isNewSeries guard
                    // and rationale as above.
                    else if (isNewSeries && PublisherContentTypeClassifier.TryClassify(issue.Publisher, out var publisherContentType, out var publisherReadingMode))
                    {
                        series.ContentType = publisherContentType;
                        series.ReadingMode = publisherReadingMode;
                    }
                    // Falls back to LanguageISO when neither the Manga field nor Publisher matched
                    // (docs/superpowers/specs/2026-08-23-language-iso-content-type-heuristic-design.md)
                    // - the embedded Manga field always wins when present since it's a deliberate
                    // classification, not an inference. Same isNewSeries guard and rationale as above.
                    else if (isNewSeries && LanguageIsoClassifier.TryClassify(issue.LanguageISO, out var languageContentType, out var languageReadingMode))
                    {
                        series.ContentType = languageContentType;
                        series.ReadingMode = languageReadingMode;
                    }
                }

                // Filename parsing fills in only what embedded metadata left blank - embedded wins
                // per-field, not all-or-nothing. Unlike before (docs/superpowers/specs/2026-08-17-
                // metadata-model-phase2a-metadata-proposals-design.md), these are no longer direct
                // writes to the Issue field itself - they become MetadataProposal rows, resolved via
                // IssueMetadataExtensions.Effective*(this Issue) everywhere those fields are read.
                if (issue.Number is null && !string.IsNullOrWhiteSpace(nameInfo.Number))
                {
                    AddFilenameProposal(context, issue, MetadataProposalField.Number, nameInfo.Number, appSettings);
                }

                if (issue.Volume is null && nameInfo.Volume > 0)
                {
                    AddFilenameProposal(context, issue, MetadataProposalField.Volume, nameInfo.Volume.ToString(), appSettings);
                }

                if (issue.Year is null && nameInfo.Year > 0)
                {
                    AddFilenameProposal(context, issue, MetadataProposalField.Year, nameInfo.Year.ToString(), appSettings);
                }

                if (seriesMismatch)
                {
                    var now = DateTime.UtcNow;
                    var seriesProposal = new MetadataProposal
                    {
                        Issue = issue,
                        Field = MetadataProposalField.Series,
                        CurrentValue = embeddedSeriesName,
                        ProposedValue = filenameSeriesName,
                        Source = MetadataProposalSource.FilenameParser,
                        Confidence = 0.6m,
                        Status = autoAcceptSeriesProposal ? MetadataProposalStatus.Accepted : MetadataProposalStatus.Pending,
                        CreatedAt = now,
                        ResolvedAt = autoAcceptSeriesProposal ? now : null,
                    };
                    issue.MetadataProposals.Add(seriesProposal);
                    context.MetadataProposals.Add(seriesProposal);

                    if (autoAcceptSeriesProposal)
                    {
                        autoAcceptedSeriesProposals.Add(seriesProposal);
                    }
                }

                series.Issues.Add(issue);
                context.Issues.Add(issue);

                addedIssues.Add(issue);
                issuesAdded++;
                seriesTouched.Add(seriesName);
            }
            catch
            {
                // One bad file doesn't stop the batch - same contract as CoverThumbnailService.GenerateAllAsync.
            }

            progress.Report((++done, total));
        }

        context.SaveChanges();

        // Now that every new Issue/Series has a real, persisted Id, apply this scan's auto-accepted
        // Series reassignments - each moves its issue off the embedded-derived series it was just
        // attached to above and onto the filename-derived one, deleting the source series if this
        // was its only issue (SeriesReassignmentResolver.Apply, shared with NeedsReviewViewModel's
        // manual accept path).
        foreach (var proposal in autoAcceptedSeriesProposals)
        {
            SeriesReassignmentResolver.Apply(context, proposal);
        }

        // Proactive scan alerts (docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-scan-
        // alerts-design.md §4) - every real scan/import path (ScanAllAsync, ImportNewFilesAsync,
        // drag import, live folder watch) funnels through this one method, so this is the single
        // place to re-check, gated on issuesAdded > 0 to skip the work on a no-op scan. Blocking is
        // safe here for the same reason ApplyParseComicPathOverride's blocking call already is -
        // this runs on a background thread.
        if (issuesAdded > 0)
        {
            ScanAlertService?.CheckForNewGroupsAsync().GetAwaiter().GetResult();
        }

        return new LibraryFolderScanResult(issuesAdded, seriesTouched.Count, addedIssues.Select(i => i.Id).ToList());
    }

    /// <summary>
    /// "Sync Metadata" (docs/superpowers/specs/2026-08-09-embedded-metadata-and-migration-relocation-design.md
    /// follow-up) - unlike <see cref="ScanAllAsync"/>, which only ever touches newly-discovered
    /// files, this re-reads embedded ComicInfo.xml for every already-linked issue and fills in
    /// currently-blank fields (<see cref="CeLibraryMigrator.MapStoryFields"/> with
    /// <c>onlyIfBlank: true</c> - never overwrites a value that's already there, from migration or
    /// a manual edit). Presence-based and safe to re-run, same "fills gaps, one bad file doesn't
    /// stop the batch" contract as <see cref="CoverThumbnailService.GenerateAllAsync"/>.
    /// </summary>
    public async Task<LibraryMetadataSyncResult> SyncMetadataAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        return await Task.Run(() => SyncMetadata(progress, ct), ct);
    }

    private LibraryMetadataSyncResult SyncMetadata(IProgress<(int Done, int Total)> progress, CancellationToken ct)
    {
        using var context = _contextFactory();

        // Include(Tags) - MapStoryFields' onlyIfBlank guard and its diff-not-replace MergeFrom both
        // read Issue.Tags (docs/superpowers/specs/2026-08-23-weighted-categorized-tags-design.md);
        // without it every issue looks like it has no existing Genre/Tags at all, so the guard would
        // never fire and every run would re-add every value as a brand-new duplicate row.
        var issues = context.Issues.Include(i => i.Tags).Where(i => i.FilePath != null).ToList();
        int total = issues.Count;
        int done = 0;
        progress.Report((0, total));

        foreach (var issue in issues)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(issue.FilePath))
                {
                    var embeddedInfo = EmbeddedComicInfoReader.TryRead(issue.FilePath!);
                    if (embeddedInfo is not null)
                    {
                        CeLibraryMigrator.MapStoryFields(embeddedInfo, issue, onlyIfBlank: true);
                    }
                }
            }
            catch
            {
                // One bad file doesn't stop the batch - same contract as ScanAllAsync.
            }

            progress.Report((++done, total));
        }

        int issuesUpdated = context.ChangeTracker.Entries<Issue>().Count(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Modified);
        context.SaveChanges();
        return new LibraryMetadataSyncResult(issuesUpdated);
    }

    public async Task<LibrarySeriesResyncResult> ResyncSeriesFromFileAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        return await Task.Run(() => ResyncSeriesFromFile(progress, ct), ct);
    }

    /// <summary>
    /// "Resync Series from File" (docs/superpowers/specs/2026-08-31-ce-migration-embedded-metadata-
    /// precedence-design.md follow-up) - unlike <see cref="SyncMetadata"/> (which only ever fills
    /// currently-blank fields, never reassigns which <see cref="Series"/> an issue belongs to), this
    /// re-reads embedded <c>ComicInfo.xml</c> for every already-linked issue and, when its embedded
    /// <c>Series</c> disagrees with the issue's current one, moves the issue there (creating that
    /// series if needed) via the same <see cref="SeriesReassignmentResolver.Apply"/> move the Needs
    /// Review/auto-accepted-proposal paths already use - no new reassignment mechanism, just a new
    /// trigger for the existing one. Needed because neither a folder rescan (only touches genuinely
    /// new files, <see cref="ScanAllAsync"/>) nor re-running CE migration (idempotent - skips issues
    /// already present) ever revisits an already-imported issue's series assignment, even after the
    /// migration-time precedence fix stops the bug for new imports going forward.
    /// </summary>
    private LibrarySeriesResyncResult ResyncSeriesFromFile(IProgress<(int Done, int Total)> progress, CancellationToken ct)
    {
        using var context = _contextFactory();

        var issues = context.Issues.Include(i => i.Series).Where(i => i.FilePath != null).ToList();
        int total = issues.Count;
        int done = 0;
        progress.Report((0, total));

        int reassigned = 0;
        foreach (var issue in issues)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(issue.FilePath))
                {
                    var embeddedInfo = EmbeddedComicInfoReader.TryRead(issue.FilePath!);
                    string? embeddedSeriesName = string.IsNullOrWhiteSpace(embeddedInfo?.Series) ? null : embeddedInfo!.Series.Trim();
                    if (embeddedSeriesName is not null
                        && !string.Equals(issue.Series?.Name, embeddedSeriesName, StringComparison.OrdinalIgnoreCase))
                    {
                        SeriesReassignmentResolver.Apply(context, new MetadataProposal { Issue = issue, ProposedValue = embeddedSeriesName });
                        reassigned++;
                    }
                }
            }
            catch
            {
                // One bad file doesn't stop the batch - same contract as SyncMetadata/ScanAllAsync.
            }

            progress.Report((++done, total));
        }

        return new LibrarySeriesResyncResult(reassigned);
    }

    /// <summary>
    /// Periodic background sweep (docs/superpowers/specs/2026-08-30-publisher-content-type-
    /// classification-design.md) - retroactively classifies series still at
    /// <see cref="ContentType.Unknown"/> via <see cref="PublisherContentTypeClassifier"/>, catching
    /// libraries scanned before this feature existed or files whose <see cref="Issue.Publisher"/>
    /// was only filled in by a later <see cref="SyncMetadataAsync"/> run - the scan-time hook in
    /// <see cref="ImportFiles"/> only ever helps issues discovered after this shipped. Mirrors
    /// <c>BackupService.RunAutoBackupIfDue</c>'s shape exactly: best-effort, silent, swallows its
    /// own failures, called fire-and-forget from <c>App.axaml.cs</c> startup. Gated by
    /// <see cref="ShouldRunContentTypeSweep"/> against <see cref="AppSettings.LastContentTypeSweepUtc"/>,
    /// which only advances on full completion so an interrupted pass retries next launch.
    /// </summary>
    public void RunContentTypeSweepIfDue()
    {
        try
        {
            using var context = _contextFactory();
            if (!ShouldRunContentTypeSweep(context.GetOrCreateAppSettings().LastContentTypeSweepUtc, DateTime.UtcNow))
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            RunContentTypeSweepCore(CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// The content-type sweep body, gate-free - retroactively classifies <see cref="ContentType.Unknown"/>
    /// series and stamps <see cref="AppSettings.LastContentTypeSweepUtc"/>. The scheduler calls this
    /// directly (it owns the gate); <see cref="RunContentTypeSweepIfDue"/> wraps it with the 7-day
    /// check for the legacy startup path. Returns how many series it reclassified.
    /// </summary>
    public int RunContentTypeSweepCore(CancellationToken ct)
    {
        using var context = _contextFactory();
        var unclassifiedSeries = context.Series
            .Where(s => s.ContentType == ContentType.Unknown)
            .Include(s => s.Issues)
            .ToList();

        int changed = 0;
        foreach (var series in unclassifiedSeries)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var issue in series.Issues)
            {
                if (PublisherContentTypeClassifier.TryClassify(issue.Publisher, out var contentType, out var readingMode))
                {
                    series.ContentType = contentType;
                    series.ReadingMode = readingMode;
                    changed++;
                    break;
                }
            }
        }

        context.GetOrCreateAppSettings().LastContentTypeSweepUtc = DateTime.UtcNow;
        context.SaveChanges();
        return changed;
    }

    /// <summary>Pure gate for <see cref="RunContentTypeSweepIfDue"/> - 7-day interval, unit-testable
    /// without waiting on real elapsed time.</summary>
    public static bool ShouldRunContentTypeSweep(DateTime? lastRunUtc, DateTime nowUtc) =>
        lastRunUtc is null || (nowUtc - lastRunUtc.Value).TotalDays >= 7;

    /// <summary>Mirrors <c>Assets/Marks/format-aliases.tsv</c>'s "Trade Paper Back" row (canonical
    /// "TPB", aliases "trade paperback"/"tpb"/"trade") - a small, self-contained check rather than
    /// routing through <c>MarkResolver</c>'s alias-table/SVG-asset machinery, which exists to
    /// resolve UI badges, not gate scan logic. Kept in sync with that tsv row by convention, not by
    /// sharing code with it.</summary>
    /// <summary>ParseComicPath-hook anchor (docs/superpowers/specs/2026-09-05-plugin-api-v2-
    /// remaining-hooks-plan.md §7) - mutates <paramref name="nameInfo"/> in place, overriding only
    /// the fields a plugin actually returned a value for. Blocking (<c>GetAwaiter().GetResult()</c>)
    /// is safe here: <see cref="ImportFiles"/> already runs on a background thread via
    /// <see cref="ScanAllAsync"/>'s <c>Task.Run</c>, same as <c>PluginHostService</c>'s own
    /// synchronous lifecycle-hook wrapper for Startup/Shutdown.</summary>
    private static void ApplyParseComicPathOverride(ComicNameInfo nameInfo, string path)
    {
        if (PluginHost is null)
        {
            return;
        }

        ParsedComicPath? overrideResult = PluginHost.RunParseComicPathHookAsync(path).GetAwaiter().GetResult();
        if (overrideResult is null)
        {
            return;
        }

        if (overrideResult.Series is not null)
        {
            nameInfo.Series = overrideResult.Series;
        }

        if (overrideResult.Number is not null)
        {
            nameInfo.Number = overrideResult.Number;
        }

        if (overrideResult.Volume is int volume)
        {
            nameInfo.Volume = volume;
        }

        if (overrideResult.Year is int year)
        {
            nameInfo.Year = year;
        }
    }

    private static readonly HashSet<string> TpbFormatAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "tpb", "trade paperback", "trade",
    };

    private static bool IsTradePaperback(string? format)
        => !string.IsNullOrWhiteSpace(format) && TpbFormatAliases.Contains(format.Trim());

    private static readonly Regex TrailingVolumeRegex = new(@"\s*,?\s*(vol(ume)?\.?)\s*\d+\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TrailingParentheticalOrHashRegex = new(@"\s*(\(\d+\)|#\d+)\s*$", RegexOptions.Compiled);

    /// <summary>Strips a TPB's collection wording down to a candidate base-series name for the fold
    /// lookup above - e.g. "Batman: Court of Owls" or "Batman Vol. 1" both strip to "Batman". Never
    /// used to name a newly-created series, only to look one up (see the fold-attempt comment at its
    /// call site).</summary>
    private static string StripCollectionWording(string seriesName)
    {
        string result = seriesName;

        int colonIndex = result.IndexOf(':');
        if (colonIndex >= 0)
        {
            result = result[..colonIndex];
        }

        result = TrailingVolumeRegex.Replace(result, string.Empty);
        result = TrailingParentheticalOrHashRegex.Replace(result, string.Empty);

        return result.Trim().TrimEnd(',', '-').Trim();
    }

    /// <summary>
    /// Creates a <see cref="MetadataProposal"/> for a filename-inferred field value (docs/
    /// superpowers/specs/2026-08-17-metadata-model-phase2a-metadata-proposals-design.md), resolved
    /// immediately per <see cref="AppSettings.MetadataResolutionPolicy"/> - <see cref="MetadataResolutionPolicy.Automatic"/>
    /// (the default) creates it already <see cref="MetadataProposalStatus.Accepted"/>, matching the
    /// pre-existing scan UX exactly; <see cref="MetadataResolutionPolicy.Prompt"/> leaves it
    /// <see cref="MetadataProposalStatus.Pending"/> until a human accepts it in the Needs Review
    /// queue. Confidence is a fixed constant, not computed - filename parsing is deterministic
    /// pattern-matching, not a scored signal.
    /// </summary>
    private static void AddFilenameProposal(PaperbunkrDbContext context, Issue issue, MetadataProposalField field, string proposedValue, AppSettings appSettings)
    {
        bool automatic = appSettings.MetadataResolutionPolicy == MetadataResolutionPolicy.Automatic;
        var now = DateTime.UtcNow;
        var proposal = new MetadataProposal
        {
            Issue = issue,
            Field = field,
            CurrentValue = null,
            ProposedValue = proposedValue,
            Source = MetadataProposalSource.FilenameParser,
            Confidence = 0.6m,
            Status = automatic ? MetadataProposalStatus.Accepted : MetadataProposalStatus.Pending,
            CreatedAt = now,
            ResolvedAt = automatic ? now : null,
        };

        issue.MetadataProposals.Add(proposal);
        context.MetadataProposals.Add(proposal);
    }
}
