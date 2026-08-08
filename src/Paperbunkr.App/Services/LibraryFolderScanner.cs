using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>Result of a completed <see cref="LibraryFolderScanner"/> run.</summary>
public record LibraryFolderScanResult(int IssuesAdded, int SeriesTouched);

/// <summary>
/// On-demand folder scan-and-import (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md
/// §2) - Paperbunkr's first non-migration way to add comics to the library. v1 scope: filename
/// parsing only (<see cref="ComicNameInfo.FromFilePath(string)"/>, the same parser CE itself uses
/// by default), no embedded ComicInfo.xml reading and no live <c>FileSystemWatcher</c> auto-import
/// - both real, deliberately deferred follow-ups once this on-demand path is proven.
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

        // Loaded once and updated in-memory as new series are created within this run, so multiple
        // new issues for the same not-yet-existing series in one scan land on the same Series row
        // instead of creating a duplicate per file.
        var seriesByName = context.Series.ToList().ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);
        var seriesTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int issuesAdded = 0;

        foreach (string file in candidateFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var nameInfo = ComicNameInfo.FromFilePath(file);
                string seriesName = string.IsNullOrWhiteSpace(nameInfo.Series) ? "Unknown" : nameInfo.Series.Trim();

                if (!seriesByName.TryGetValue(seriesName, out var series))
                {
                    series = new Series { Name = seriesName };
                    context.Series.Add(series);
                    seriesByName[seriesName] = series;
                }

                var issue = new Issue
                {
                    Series = series,
                    Number = string.IsNullOrWhiteSpace(nameInfo.Number) ? null : nameInfo.Number,
                    Volume = nameInfo.Volume > 0 ? nameInfo.Volume : null,
                    Year = nameInfo.Year > 0 ? nameInfo.Year : null,
                    FilePath = file,
                    AddedTime = DateTime.UtcNow,
                };
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
        return new LibraryFolderScanResult(issuesAdded, seriesTouched.Count);
    }
}
