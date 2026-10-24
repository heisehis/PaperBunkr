using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Paperbunkr.Data.Library;

/// <summary>One swap/scratch file whose original is missing or unreadable: kept, so the user can restore from it.</summary>
public sealed record SwapRestoreCandidate(string LeftoverPath, string OriginalPath, long Size, DateTime ModifiedUtc, bool LeftoverIsValidArchive, bool OriginalExists);

public sealed record SwapSweepResult(int Deleted, int Skipped, IReadOnlyList<SwapRestoreCandidate> RestoreCandidates, IReadOnlyList<string> Errors)
{
    public static readonly SwapSweepResult Empty = new(0, 0, Array.Empty<SwapRestoreCandidate>(), Array.Empty<string>());
}

/// <summary>
/// Finds the copies a metadata write or an organize left beside a comic and removes them when that is safe
/// (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md Part 2):
/// <list type="bullet">
/// <item><c>X.cbz.crswap</c> - reported by the user after Scrape then Organize (2026-10-20); a full copy of the comic.</item>
/// <item><c>X.cbz.pbwrite-&lt;guid&gt;.tmp</c> - <c>MetadataFileWriteBackService</c>'s scratch copy.</item>
/// <item><c>X~RF&lt;hex&gt;.TMP</c> - the backup Windows' <c>ReplaceFile</c> makes for a moment during <c>File.Replace</c>.</item>
/// </list>
/// A leftover is deleted only when its original is there and opens; otherwise it is a <see cref="SwapRestoreCandidate"/> and is never
/// touched. Anything newer than <see cref="MinimumAge"/> or locked is skipped: a write may still be using it.
/// </summary>
public static class SwapFileSweeper
{
    public static readonly TimeSpan MinimumAge = TimeSpan.FromMinutes(2);

    private static readonly Regex PbWrite = new(@"^(?<orig>.+)\.pbwrite-[0-9a-fA-F]+\.tmp$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CrSwap = new(@"^(?<orig>.+)\.crswap$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReplaceBackup = new(@"^(?<stem>.+)~RF[0-9a-fA-F]+\.TMP$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] Patterns = { "*.crswap", "*.pbwrite-*.tmp", "*~RF*.TMP" };

    /// <summary>Test seam for "now".</summary>
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>True when <paramref name="fileName"/> is one of the leftover kinds this sweeper knows.</summary>
    public static bool IsLeftoverName(string fileName) =>
        CrSwap.IsMatch(fileName) || PbWrite.IsMatch(fileName) || ReplaceBackup.IsMatch(fileName);

    /// <summary>Sweeps every folder under each of <paramref name="roots"/> (hidden and system folders are skipped).</summary>
    public static SwapSweepResult Sweep(IEnumerable<string> roots, CancellationToken cancellationToken = default)
    {
        int deleted = 0, skipped = 0;
        var candidates = new List<SwapRestoreCandidate>();
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var leftover in EnumerateLeftovers(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(leftover))
                {
                    continue;
                }

                switch (Handle(leftover, out var candidate, out var error))
                {
                    case Outcome.Deleted:
                        deleted++;
                        break;
                    case Outcome.Skipped:
                        skipped++;
                        if (error is not null)
                        {
                            errors.Add(error);
                        }

                        break;
                    case Outcome.Restore:
                        candidates.Add(candidate!);
                        break;
                }
            }
        }

        return new SwapSweepResult(deleted, skipped, candidates, errors);
    }

    /// <summary>Sweeps just the folders holding <paramref name="files"/> (after a write-back batch: no full library walk).</summary>
    public static SwapSweepResult SweepBeside(IEnumerable<string> files, CancellationToken cancellationToken = default)
    {
        int deleted = 0, skipped = 0;
        var candidates = new List<SwapRestoreCandidate>();
        var errors = new List<string>();
        foreach (var file in files.Where(f => !string.IsNullOrEmpty(f)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? dir = Path.GetDirectoryName(file);
            if (dir is null || !Directory.Exists(dir))
            {
                continue;
            }

            string name = Path.GetFileName(file);
            string stem = Path.GetFileNameWithoutExtension(file);
            IEnumerable<string> matches;
            try
            {
                matches = Directory.EnumerateFiles(dir, name + ".crswap")
                    .Concat(Directory.EnumerateFiles(dir, name + ".pbwrite-*.tmp"))
                    .Concat(Directory.EnumerateFiles(dir, stem + "~RF*.TMP"))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var leftover in matches)
            {
                switch (Handle(leftover, out var candidate, out var error))
                {
                    case Outcome.Deleted: deleted++; break;
                    case Outcome.Restore: candidates.Add(candidate!); break;
                    default:
                        skipped++;
                        if (error is not null)
                        {
                            errors.Add(error);
                        }

                        break;
                }
            }
        }

        return new SwapSweepResult(deleted, skipped, candidates, errors);
    }

    /// <summary>
    /// Puts a leftover back as the comic: only when the original is missing (never over an existing file). Returns the restored path,
    /// or throws <see cref="IOException"/> saying why not.
    /// </summary>
    public static string Restore(SwapRestoreCandidate candidate)
    {
        if (File.Exists(candidate.OriginalPath))
        {
            throw new IOException($"'{Path.GetFileName(candidate.OriginalPath)}' exists again, so the copy was not moved over it.");
        }

        File.Move(candidate.LeftoverPath, candidate.OriginalPath, overwrite: false);
        return candidate.OriginalPath;
    }

    /// <summary>The comic a leftover belongs to, or null when the name is not a leftover's.</summary>
    public static string? OriginalPathFor(string leftoverPath)
    {
        string dir = Path.GetDirectoryName(leftoverPath) ?? string.Empty;
        string name = Path.GetFileName(leftoverPath);

        var m = CrSwap.Match(name);
        if (!m.Success)
        {
            m = PbWrite.Match(name);
        }

        if (m.Success)
        {
            return Path.Combine(dir, m.Groups["orig"].Value);
        }

        m = ReplaceBackup.Match(name);
        if (!m.Success)
        {
            return null;
        }

        // ReplaceFile's backup drops the extension; the comic is the one sibling with that stem.
        string stem = m.Groups["stem"].Value;
        try
        {
            return Directory.EnumerateFiles(dir, stem + ".*")
                .FirstOrDefault(f => !IsLeftoverName(Path.GetFileName(f)) && string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase))
                ?? Path.Combine(dir, stem + ".cbz");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Path.Combine(dir, stem + ".cbz");
        }
    }

    /// <summary>True when <paramref name="path"/> is a readable comic: a zip whose directory reads, or any other non-empty file.</summary>
    public static bool IsUsableArchive(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
            {
                return false;
            }

            if (!LooksLikeZip(path))
            {
                // CBR/CB7/PDF/EPUB...: no cheap full check here; a non-empty file that is not a zip is left to the scanner.
                return true;
            }

            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private enum Outcome { Deleted, Skipped, Restore }

    private static Outcome Handle(string leftover, out SwapRestoreCandidate? candidate, out string? error)
    {
        candidate = null;
        error = null;

        FileInfo info;
        try
        {
            info = new FileInfo(leftover);
            if (!info.Exists || Clock() - info.LastWriteTimeUtc < MinimumAge)
            {
                return Outcome.Skipped;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Outcome.Skipped;
        }

        string? original = OriginalPathFor(leftover);
        if (original is null)
        {
            return Outcome.Skipped;
        }

        bool originalExists = File.Exists(original);
        if (!originalExists || !IsUsableArchive(original))
        {
            candidate = new SwapRestoreCandidate(leftover, original, info.Length, info.LastWriteTimeUtc, IsUsableArchive(leftover), originalExists);
            return Outcome.Restore;
        }

        try
        {
            File.Delete(leftover);
            return Outcome.Deleted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked (a write in flight, antivirus, the indexer): the next pass tries again.
            error = $"{Path.GetFileName(leftover)}: {ex.Message}";
            return Outcome.Skipped;
        }
    }

    private static IEnumerable<string> EnumerateLeftovers(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };

        foreach (var pattern in Patterns)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, pattern, options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (IsLeftoverName(Path.GetFileName(file)))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool LooksLikeZip(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[4];
            return stream.Read(header) == 4 && header[0] == (byte)'P' && header[1] == (byte)'K';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
