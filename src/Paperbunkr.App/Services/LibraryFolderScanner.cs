using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Paperbunkr.Data;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>Result of a completed <see cref="LibraryFolderScanner"/> run.</summary>
public record LibraryFolderScanResult(int IssuesAdded, int SeriesTouched);

/// <summary>Result of a completed <see cref="LibraryFolderScanner.SyncMetadataAsync"/> run.</summary>
public record LibraryMetadataSyncResult(int IssuesUpdated);

/// <summary>
/// On-demand folder scan-and-import (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md
/// §2, embedded-metadata follow-up in docs/superpowers/specs/
/// 2026-08-09-embedded-metadata-and-migration-relocation-design.md §1) - Paperbunkr's first
/// non-migration way to add comics to the library. Reads embedded ComicInfo.xml when present (via
/// the same <see cref="IInfoStorage"/> cast <c>ComicBook.RefreshInfoFromFile</c> uses internally) -
/// embedded metadata wins per-field over <see cref="ComicNameInfo.FromFilePath(string)"/> filename
/// parsing, which stays as the fallback for files with no embedded info or an unsupported format.
/// No live <c>FileSystemWatcher</c> auto-import - still a real, deliberately deferred follow-up.
/// </summary>
public class LibraryFolderScanner
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

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
                var embeddedInfo = TryReadEmbeddedInfo(file);

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

        return new LibraryFolderScanResult(issuesAdded, seriesTouched.Count);
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

        var issues = context.Issues.Where(i => i.FilePath != null).ToList();
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
                    var embeddedInfo = TryReadEmbeddedInfo(issue.FilePath!);
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

    /// <summary>
    /// Reads embedded ComicInfo.xml via the archive reader's own <see cref="IInfoStorage"/>
    /// implementation (the same one <c>ComicBook.RefreshInfoFromFile</c> uses internally) - the
    /// same provider <see cref="PageImageDecoder"/> opens for page decoding, just a separate
    /// short-lived open here since only metadata is needed. Returns null - never throws - for
    /// anything that doesn't pan out: unsupported/dynamic formats, no embedded ComicInfo.xml,
    /// or a malformed one. Callers fall back to filename parsing in every case.
    /// </summary>
    private static ComicInfo? TryReadEmbeddedInfo(string file)
    {
        try
        {
            using var provider = Providers.Readers.CreateSourceProvider(file);
            if (provider is not IInfoStorage infoStorage)
            {
                return null;
            }

            provider.Open(async: false);
            return infoStorage.LoadInfo(InfoLoadingMethod.Complete);
        }
        catch
        {
            return null;
        }
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
