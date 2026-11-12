using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

public partial class DetailTabsViewModel
{
    private IReadOnlyList<CreatorRun> _creatorRuns = Array.Empty<CreatorRun>();
    private IReadOnlyList<SeriesArc> _seriesArcs = Array.Empty<SeriesArc>();
    public bool HasCreatorRuns => CreatorRunDetector.ShouldOffer(_creatorRuns, _issueEntities.Values.Where(i => !i.IsSpecial()));

    private void LoadStructure(Series series)
    {
        var issues = series.Issues.Where(i => !i.IsSpecial()).ToList();
        using var context = _contextFactory();
        var credits = context.CreatorCredits.AsNoTracking().Include(c => c.Creator).Where(c => c.Issue!.SeriesId == series.Id).ToList();
        _creatorRuns = CreatorRunDetector.FindAll(issues, credits);
        _seriesArcs = SeriesArcDetector.Find(issues, series.ContentType);
        OnPropertyChanged(nameof(HasCreatorRuns));
    }

    private IEnumerable<IssueRunGroup> StructureGroups(string grouping, IReadOnlyList<IssueCardSample> cards)
    {
        var byId = cards.ToDictionary(c => c.Id);
        var used = new HashSet<int>();
        bool first = true;
        var sections = grouping == DetailGrouping.CreatorRuns
            ? _creatorRuns.Select(r => (Name: r.CreatorName, Subtitle: $"{r.Role} · {r.Range}", Issues: r.Issues))
            : _seriesArcs.Select(r => (Name: r.Name, Subtitle: $"{r.Range} · {r.Source}", Issues: r.Issues));
        foreach (var section in sections)
        {
            var issues = section.Issues.Where(i => byId.ContainsKey(i.Id)).ToList();
            if (issues.Count == 0) continue;
            var ids = issues.Select(i => i.Id).ToList();
            string label = section.Name;
            used.UnionWith(ids);
            yield return new IssueRunGroup
            {
                Header = label, Subtitle = section.Subtitle, ReadCount = issues.Count(i => i.HasBeenRead()), TotalCount = issues.Count,
                ReadLabel = grouping == DetailGrouping.CreatorRuns ? "Read this run" : "Read this arc",
                IsFirstSection = first, Items = new ObservableCollection<IssueCardSample>(ids.Select(id => byId[id])),
                ReadCommand = new RelayCommand(() => ReadStructure(ids, label)), SaveCommand = new RelayCommand(() => SaveStructure(ids, label)),
            };
            first = false;
        }
        var other = cards.Where(c => !used.Contains(c.Id)).ToList();
        if (other.Count > 0) yield return new IssueRunGroup { Header = "Other issues", Items = new ObservableCollection<IssueCardSample>(other) };
    }

    private void ReadStructure(IReadOnlyList<int> ids, string name)
    {
        int? next = ids.Where(id => _issueEntities.TryGetValue(id, out var issue) && !issue.FileIsMissing && !issue.IsPlaceholder && !issue.HasBeenRead()).Select(id => (int?)id).FirstOrDefault();
        next ??= ids.Where(id => _issueEntities.TryGetValue(id, out var issue) && !issue.FileIsMissing && !issue.IsPlaceholder).Select(id => (int?)id).FirstOrDefault();
        if (next is int issueId) { if (ReadQueue is { } open) open(issueId, new ReadingQueue(ids.ToArray(), name)); else _openInReader(issueId); }
    }
    private void SaveStructure(IReadOnlyList<int> ids, string name)
    {
        using var context = _contextFactory();
        int listId = OrderedIssueLists.Save(context, name, ids);
        OpenReadingList?.Invoke(listId);
    }
}
