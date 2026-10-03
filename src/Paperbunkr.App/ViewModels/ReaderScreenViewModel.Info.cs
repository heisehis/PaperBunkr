using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The info panel, and the shared key bindings for the in-reader reference commands, on the reader view model (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #28). Kept in its own file so the
/// already-large reader view model does not grow further.
/// </summary>
public partial class ReaderScreenViewModel
{
    /// <summary>The slide-in info panel (read-only metadata of the open issue).</summary>
    public ReaderInfoPanelViewModel Info { get; } = new();

    /// <summary>Raised with the series id when "Open details" is chosen in the panel; the shell navigates to the Detail screen.</summary>
    public event Action<int>? OpenDetailsRequested;

    /// <summary>Raised with the issue id when "Edit properties" is chosen in the panel; the shell opens Issue Properties.</summary>
    public event Action<int>? EditPropertiesRequested;

    [RelayCommand]
    private void ToggleInfoPanel()
    {
        if (Info.Model is null)
        {
            return;
        }

        Info.IsOpen = !Info.IsOpen;
    }

    [RelayCommand]
    private void OpenInfoDetails()
    {
        if (_loadedSeriesId is int seriesId)
        {
            Info.Close();
            OpenDetailsRequested?.Invoke(seriesId);
        }
    }

    [RelayCommand]
    private void EditInfoProperties()
    {
        if (_loadedIssueId is int issueId)
        {
            Info.Close();
            EditPropertiesRequested?.Invoke(issueId);
        }
    }

    /// <summary>Fills the info panel for the issue that just loaded (called from <c>Load</c>). The panel closes so a new issue never opens with the previous one's panel showing.</summary>
    private void RefreshInfoPanel(PaperbunkrDbContext context, Issue issue, Series series)
    {
        var context0 = ReadingOrderResolver.ResolveContext(context, issue.Id, _activeReadingListId, _activeStoryEventId);
        var genres = context.IssueTags.Where(t => t.IssueId == issue.Id && t.Field == IssueTagField.Genre).Select(t => t.Value).ToList();
        var tags = context.IssueTags.Where(t => t.IssueId == issue.Id && t.Field == IssueTagField.Tags).Select(t => t.Value).ToList();
        Info.IsOpen = false;
        Info.Set(ReaderInfoBuilder.Build(issue, series.Name, context0, genres, tags), context.GetOrCreateAppSettings().InfoPanelShowSummary);
    }
}
