using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One copy within a Needs Review "Duplicate Files" group (docs/superpowers/specs/2026-09-05-
/// duplicate-files-review-design.md) - a file name, size, date added, and a small cover thumbnail so
/// the file-size default isn't the only signal before deleting something. <see cref="GroupKey"/>
/// feeds <c>RadioButton.GroupName</c> in XAML so mutual exclusion works per-group even though every
/// group's row template shares one visual tree.
/// </summary>
public partial class DuplicateCandidateViewModel : ViewModelBase
{
    public DuplicateCandidateViewModel(Issue issue, string groupKey)
    {
        IssueId = issue.Id;
        GroupKey = groupKey;
        FileName = string.IsNullOrEmpty(issue.FilePath) ? "(no file)" : Path.GetFileName(issue.FilePath);
        SizeLabel = FormatFileSize(issue.FileSize);
        DateLabel = issue.AddedTime?.ToString("MMM d") ?? string.Empty;
    }

    public int IssueId { get; }

    public string GroupKey { get; }

    public string FileName { get; }

    public string SizeLabel { get; }

    public string DateLabel { get; }

    [ObservableProperty]
    private bool _isKeep;

    /// <summary>Human-readable byte size, same units/style as <c>IssueListFieldCatalog.FormatFileSize</c> (not shared - that method is private to an unrelated file).</summary>
    private static string FormatFileSize(long? bytes)
    {
        if (bytes is not { } b || b < 0)
        {
            return string.Empty;
        }

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = b;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}
