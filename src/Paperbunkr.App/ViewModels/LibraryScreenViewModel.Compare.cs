using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.ViewModels;

/// <summary>"Compare files…" on the Library (docs/superpowers/specs/2026-09-26-comic-reader-compare-design.md #11): with exactly two comics selected, opens them on the Compare screen.</summary>
public partial class LibraryScreenViewModel
{
    /// <summary>Raised with the first comic and the other(s) to compare it with; the shell navigates to the Compare screen.</summary>
    public event Action<int, IReadOnlyList<int>>? CompareRequested;

    [RelayCommand]
    private void CompareSelectedIssues(int issueId)
    {
        var ids = Selection.UnionForAction(issueId).Distinct().ToList();
        if (ids.Count != 2)
        {
            _showToast("Select two comics to compare", "Compare puts two copies of one comic side by side.");
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            // A mirrored remote comic is filtered out of the default context, so a missing row means "not a local file".
            var issues = context.Issues.Where(i => ids.Contains(i.Id)).ToList();
            if (issues.Count != 2 || issues.Any(i => string.IsNullOrEmpty(i.FilePath) || i.FileIsMissing || !System.IO.File.Exists(i.FilePath)))
            {
                _showToast("Can't compare these", "Both comics must be files on this computer.");
                return;
            }
        }

        CompareRequested?.Invoke(ids[0], [ids[1]]);
    }
}
