using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Read-only side-by-side of two overlapping reading lists (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md
/// §7, decision Q25): only in this list · in both · only in the other, each in its list's order. Merge is offered from the footer.
/// </summary>
public sealed class ReadingListCompareViewModel
{
    public string ThisName { get; init; } = string.Empty;

    public string OtherName { get; init; } = string.Empty;

    public IReadOnlyList<string> OnlyHere { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> InBoth { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> OnlyThere { get; init; } = Array.Empty<string>();

    public string OnlyHereHeader => $"Only in {ThisName} ({OnlyHere.Count})";

    public string InBothHeader => $"In both ({InBoth.Count})";

    public string OnlyThereHeader => $"Only in {OtherName} ({OnlyThere.Count})";

    public IRelayCommand CloseCommand { get; init; } = null!;

    public IRelayCommand MergeCommand { get; init; } = null!;

    public static ReadingListCompareViewModel Build(PaperbunkrDbContext context, int thisListId, int otherListId, Action close, Action merge)
    {
        var lists = context.ReadingLists.AsNoTracking()
            .Where(l => l.Id == thisListId || l.Id == otherListId)
            .Include(l => l.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .Include(l => l.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
            .ToList();
        var here = lists.First(l => l.Id == thisListId);
        var there = lists.First(l => l.Id == otherListId);

        List<(int IssueId, string Label)> Rows(Paperbunkr.Data.Entities.ReadingList list) =>
            list.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                .Select(i => (i.IssueId, Label: $"{i.Issue?.Series?.Name ?? "Unknown"} #{i.Issue?.EffectiveNumber()}{(i.Issue?.EffectiveYear() is int y ? $" ({y})" : "")}"))
                .ToList();

        var hereRows = Rows(here);
        var thereRows = Rows(there);
        var hereIds = hereRows.Select(r => r.IssueId).ToHashSet();
        var thereIds = thereRows.Select(r => r.IssueId).ToHashSet();

        return new ReadingListCompareViewModel
        {
            ThisName = here.Name,
            OtherName = there.Name,
            OnlyHere = hereRows.Where(r => !thereIds.Contains(r.IssueId)).Select(r => r.Label).ToList(),
            InBoth = hereRows.Where(r => thereIds.Contains(r.IssueId)).Select(r => r.Label).ToList(),
            OnlyThere = thereRows.Where(r => !hereIds.Contains(r.IssueId)).Select(r => r.Label).ToList(),
            CloseCommand = new RelayCommand(close),
            MergeCommand = new RelayCommand(merge),
        };
    }
}
