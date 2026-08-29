using System.Collections.Generic;
using System.Linq;
using FluentIcons.Common;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds the Library screen's right-click menus as plain <see cref="ContextMenuEntry"/> data.
/// Split out of <see cref="LibraryScreenViewModel"/> (already ~1900 lines) and reads only that
/// view model's public command surface + selection controllers, so the whole menu for any target
/// is assertable in a plain unit test.
///
/// Right-click semantics come for free from the commands themselves: each already routes through
/// <c>Selection.UnionForAction(id)</c>, so a menu on an unselected tile acts on just that tile and
/// a menu on a tile within a selection acts on the whole selection. This builder only mirrors that
/// into the <em>labels</em> ("Delete 4 comics").
/// </summary>
public sealed class LibraryContextMenuBuilder
{
    private readonly LibraryScreenViewModel _vm;

    public LibraryContextMenuBuilder(LibraryScreenViewModel vm) => _vm = vm;

    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        IssueListRow row => BuildIssueMenu(row),
        SeriesCardSample card => BuildSeriesMenu(card),
        null => BuildEmptyMenu(),
        _ => null,
    };

    private IReadOnlyList<ContextMenuEntry> BuildIssueMenu(IssueListRow row)
    {
        bool multi = _vm.Selection.IsSelected(row.Id) && _vm.Selection.Count > 1;
        int n = _vm.Selection.Count;
        string plural = multi ? $" {n} comics" : "";

        var entries = new List<ContextMenuEntry?>
        {
            ContextMenuEntry.Item("Open", _vm.IssueList.OpenIssueCommand, row, Symbol.Open, inputGesture: "Enter"),
            ContextMenuEntry.Separator,
            ContextMenuEntry.Item("Edit Properties…", _vm.EditIssuePropertiesCommand, row.Id, Symbol.Info, inputGesture: "Ctrl+I"),
            ContextMenuEntry.Item("Quick Rate…", _vm.OpenQuickRateCommand, row.Id, Symbol.Star),
            ContextMenuEntry.SubMenu(
                multi ? $"Mark {n} as" : "Mark as",
                new[]
                {
                    ContextMenuEntry.Item("Read", _vm.MarkIssueReadCommand, row.Id),
                    ContextMenuEntry.Item("Unread", _vm.MarkIssueUnreadCommand, row.Id),
                },
                Symbol.Checkmark),
            ContextMenuEntry.SubMenu(
                multi ? $"Add {n} to Reading List" : "Add to Reading List",
                ReadingListChildren(row.Id),
                Symbol.TextBulletListAdd),
            ContextMenuEntry.Separator,
            ContextMenuEntry.Item("Go to Series", _vm.GoToSeriesCommand, row.SeriesId, Symbol.ArrowForward),
            ContextMenuEntry.SubMenu(
                "Series",
                new[]
                {
                    ContextMenuEntry.SubMenu("Content Type", ContentTypeChildren(row.SeriesId, row.ContentTypeLabel)),
                    ContextMenuEntry.SubMenu(
                        "Reading Direction",
                        ReadingDirectionChildren(row.SeriesId, row.ReadingDirectionLabel),
                        isVisible: row.IsMangaFamily),
                    ContextMenuEntry.SubMenu("Publication Status", PublicationStatusChildren(row.SeriesId, row.SeriesStatusLabel)),
                    ContextMenuEntry.SubMenu("Reading Status", ReadingStatusChildren(row.SeriesId, row.ReadingStatusLabel)),
                },
                Symbol.Library),
            ContextMenuEntry.Separator,
            ContextMenuEntry.Item("Show in Explorer", _vm.RevealIssueCommand, row.Id, Symbol.FolderOpen, isEnabled: row.HasFile),
            _vm.HasPluginHost
                ? ContextMenuEntry.Item("Find Duplicates", _vm.RunLibraryPluginsCommand, row.Id, Symbol.DocumentSearch)
                : null,
            ContextMenuEntry.Separator,
            ContextMenuEntry.Item("Select All", _vm.SelectAllVisibleIssuesCommand, icon: Symbol.SelectAllOn),
            ContextMenuEntry.Item("Clear Selection", _vm.ClearSelectionCommand, icon: Symbol.SelectAllOff, isEnabled: _vm.HasSelection),
            ContextMenuEntry.Separator,
            ContextMenuEntry.SubMenu(
                multi ? $"Delete{plural}…" : "Delete…",
                new[]
                {
                    ContextMenuEntry.Item(_vm.DeleteConfirmLabel, _vm.DeleteIssueCommand, row.Id),
                },
                Symbol.Delete,
                isDanger: true),
        };

        return Compact(entries);
    }

    private IReadOnlyList<ContextMenuEntry> BuildSeriesMenu(SeriesCardSample card)
    {
        bool multi = _vm.SeriesSelection.IsSelected(card.SeriesId) && _vm.SeriesSelection.Count > 1;
        int n = _vm.SeriesSelection.Count;

        var entries = new List<ContextMenuEntry?>
        {
            ContextMenuEntry.Item("Open Series", _vm.SelectCardCommand, card, Symbol.Open),
            ContextMenuEntry.Separator,
            ContextMenuEntry.SubMenu("Content Type", ContentTypeChildren(card.SeriesId, card.ContentTypeLabel)),
            ContextMenuEntry.SubMenu(
                "Reading Direction",
                ReadingDirectionChildren(card.SeriesId, card.ReadingDirectionLabel),
                isVisible: card.IsMangaFamily),
            ContextMenuEntry.SubMenu("Publication Status", PublicationStatusChildren(card.SeriesId, card.SeriesStatusLabel)),
            ContextMenuEntry.SubMenu("Reading Status", ReadingStatusChildren(card.SeriesId, card.ReadingStatusLabel)),
            ContextMenuEntry.Separator,
            ContextMenuEntry.Item("Show in Explorer", _vm.RevealSeriesCommand, card, Symbol.FolderOpen, isEnabled: card.HasFile),
            ContextMenuEntry.Separator,
            ContextMenuEntry.SubMenu(
                multi ? $"Delete {n} Series…" : "Delete Series…",
                new[]
                {
                    ContextMenuEntry.Item(_vm.DeleteSeriesConfirmLabel, _vm.DeleteSeriesCommand, card.SeriesId),
                },
                Symbol.Delete,
                isDanger: true),
        };

        return Compact(entries);
    }

    private IReadOnlyList<ContextMenuEntry>? BuildEmptyMenu()
    {
        var entry = _vm.IsSeriesGranularity
            ? ContextMenuEntry.Item("Select All", _vm.SelectAllVisibleSeriesCommand, icon: Symbol.SelectAllOn)
            : ContextMenuEntry.Item("Select All", _vm.SelectAllVisibleIssuesCommand, icon: Symbol.SelectAllOn);
        return new[] { entry };
    }

    private IEnumerable<ContextMenuEntry?> ReadingListChildren(int issueId)
    {
        foreach (var list in _vm.ReadingLists)
        {
            yield return ContextMenuEntry.Item(list.Name, _vm.AddIssueToReadingListCommand, (issueId, list.Id));
        }

        if (_vm.ReadingLists.Count > 0)
        {
            yield return ContextMenuEntry.Separator;
        }

        yield return ContextMenuEntry.Item("New List…", _vm.CreateReadingListAndAddIssueCommand, issueId);
    }

    private IEnumerable<ContextMenuEntry?> ContentTypeChildren(int seriesId, string? current) => new[]
    {
        Radio("Comic", "Comic", current, _vm.SetSeriesContentTypeComicCommand, seriesId),
        Radio("Manga", "Manga", current, _vm.SetSeriesContentTypeMangaCommand, seriesId),
        Radio("Manhua", "Manhua", current, _vm.SetSeriesContentTypeManhuaCommand, seriesId),
        Radio("Manhwa", "Manhwa", current, _vm.SetSeriesContentTypeManhwaCommand, seriesId),
    };

    private IEnumerable<ContextMenuEntry?> ReadingDirectionChildren(int seriesId, string? current) => new[]
    {
        Radio("Left to Right", "LeftToRight", current, _vm.SetSeriesReadingModeLeftToRightCommand, seriesId),
        Radio("Right to Left", "RightToLeft", current, _vm.SetSeriesReadingModeRightToLeftCommand, seriesId),
    };

    private IEnumerable<ContextMenuEntry?> PublicationStatusChildren(int seriesId, string? current) => new[]
    {
        Radio("Unknown", "Unknown", current, _vm.SetSeriesStatusUnknownCommand, seriesId),
        Radio("Ongoing", "Ongoing", current, _vm.SetSeriesStatusOngoingCommand, seriesId),
        Radio("Completed", "Completed", current, _vm.SetSeriesStatusCompletedCommand, seriesId),
        Radio("Cancelled", "Cancelled", current, _vm.SetSeriesStatusCancelledCommand, seriesId),
        Radio("Hiatus", "Hiatus", current, _vm.SetSeriesStatusHiatusCommand, seriesId),
    };

    private IEnumerable<ContextMenuEntry?> ReadingStatusChildren(int seriesId, string? current) => new[]
    {
        Radio("Unknown", "Unknown", current, _vm.SetSeriesReadingStatusUnknownCommand, seriesId),
        Radio("Planned", "Planned", current, _vm.SetSeriesReadingStatusPlannedCommand, seriesId),
        Radio("Reading", "Reading", current, _vm.SetSeriesReadingStatusReadingCommand, seriesId),
        Radio("Completed", "Completed", current, _vm.SetSeriesReadingStatusCompletedCommand, seriesId),
        Radio("Paused", "Paused", current, _vm.SetSeriesReadingStatusPausedCommand, seriesId),
        Radio("Dropped", "Dropped", current, _vm.SetSeriesReadingStatusDroppedCommand, seriesId),
        Radio("Re-reading", "ReReading", current, _vm.SetSeriesReadingStatusReReadingCommand, seriesId),
    };

    private static ContextMenuEntry Radio(
        string header, string enumName, string? current, System.Windows.Input.ICommand command, int seriesId) =>
        ContextMenuEntry.Item(header, command, seriesId, isChecked: string.Equals(current, enumName, System.StringComparison.Ordinal));

    private static IReadOnlyList<ContextMenuEntry> Compact(IEnumerable<ContextMenuEntry?> entries)
    {
        var list = entries.Where(e => e is not null).Select(e => e!).ToList();

        // Drop leading/trailing separators and any run of consecutive separators left behind by
        // omitted entries (no plugin host, empty reading-list set, etc.).
        var result = new List<ContextMenuEntry>(list.Count);
        foreach (var entry in list)
        {
            if (entry.IsSeparator && (result.Count == 0 || result[^1].IsSeparator))
            {
                continue;
            }

            result.Add(entry);
        }

        while (result.Count > 0 && result[^1].IsSeparator)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }
}
