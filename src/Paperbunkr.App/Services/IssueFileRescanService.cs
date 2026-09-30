using System;
using System.IO;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>What one <see cref="IssueFileRescanService.Rescan"/> did.</summary>
public enum IssueRescanOutcome
{
    /// <summary>The file's ComicInfo.xml replaced the book's metadata.</summary>
    Updated,

    /// <summary>The file is fine but carries no ComicInfo.xml - the metadata was left alone (only the file size was refreshed).</summary>
    NoEmbeddedInfo,

    /// <summary>No file, a missing file, or one that couldn't be read.</summary>
    Failed,
}

/// <summary>
/// "Re-read info from file" for one book (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §2) - CE's Ctrl+Refresh
/// (<c>ComicBook.RefreshInfoFromFile</c> with <c>onlyUpdateEmpty: false</c>, <c>ComicBook.cs:2483-2504</c>): the file's embedded
/// ComicInfo.xml overwrites what the library has, blanks included, the same full mapping the import path uses
/// (<see cref="CeLibraryMigrator.MapStoryFields"/> without <c>onlyIfBlank</c>). A file with no ComicInfo.xml never wipes anything.
/// Mutates the tracked <paramref name="issue"/> only; the caller saves. The issue must have <c>Tags</c> loaded (Genre/Tags diff against them).
/// </summary>
public static class IssueFileRescanService
{
    public static IssueRescanOutcome Rescan(Issue issue)
    {
        string? path = issue.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return IssueRescanOutcome.Failed;
        }

        try
        {
            issue.FileSize = new FileInfo(path).Length;
            var info = EmbeddedComicInfoReader.TryRead(path);
            if (info is null)
            {
                return IssueRescanOutcome.NoEmbeddedInfo;
            }

            CeLibraryMigrator.MapStoryFields(info, issue);
            return IssueRescanOutcome.Updated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return IssueRescanOutcome.Failed;
        }
    }
}
