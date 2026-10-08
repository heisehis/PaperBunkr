using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia.Input;
using FluentIcons.Common;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels.LibraryActions;

/// <summary>Which kinds of target an action applies to.</summary>
[Flags]
public enum LibraryActionTargets
{
    None = 0,
    Issue = 1,
    Series = 2,
    RemoteIssue = 4,
    RemoteSeries = 8,
    Local = Issue | Series,
}

/// <summary>
/// One Library action (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §1): a stable <see cref="Id"/> (the future
/// shortcut-remapping key), the kinds it <see cref="AppliesTo"/>, and <see cref="Build"/>, which turns a context into the menu/bar entry
/// (label with the target's count, enabled state, ✓, children) or null when it doesn't apply.
/// </summary>
public sealed record LibraryAction(string Id, LibraryActionTargets AppliesTo, Func<LibraryActionContext, ContextMenuEntry?> Build);

/// <summary>One slot of the selection bar: an action's entry, or a group separator. <see cref="IsTrailing"/> slots sit at the right edge.</summary>
public sealed record LibraryBarItem(ContextMenuEntry? Entry, bool IsSeparator, bool IsTrailing, string? ActionId)
{
    /// <summary>Tooltip: the label, plus the shortcut when there is one.</summary>
    public string ToolTip => Entry is null ? string.Empty
        : string.IsNullOrEmpty(Entry.InputGesture) ? Entry.Header ?? string.Empty : $"{Entry.Header} ({Entry.InputGesture})";

    public bool HasChildren => Entry?.Children is { Count: > 0 };

    /// <summary>Flattened for the bar's compiled bindings (an icon-only button's accessible name is its label).</summary>
    public string Label => Entry?.Header ?? string.Empty;

    public Symbol Icon => Entry?.Icon ?? Symbol.Circle;

    public bool IsEnabled => Entry?.IsEnabled ?? false;

    public bool IsDanger => Entry?.IsDanger ?? false;

    public string AutomationId => ActionId is null ? string.Empty : $"LibraryBarAction_{ActionId}";
}

/// <summary>
/// Every Library action, defined once and read by the three surfaces: the right-click menus (<see cref="BuildMenu"/>), the selection bar
/// (<see cref="BuildBar"/>) and the keyboard (<see cref="TryGetKeyCommand"/>), so a label, its icon and its shortcut can't drift apart.
/// Menu and bar order are plain id lists below; <c>"|"</c> is a separator and, on the bar, <c>"&gt;"</c> starts the right-aligned part.
/// </summary>
public sealed class LibraryActionCatalog
{
    private const string Sep = "|";
    private const string Trail = ">";

    private static readonly string[] IssueMenuOrder =
    {
        "open", Sep,
        "edit", "rating", "mark", "add-list", "add-collection", "show-in-list", Sep,
        "go-series", "series-setters", Sep,
        "scrape", "organize", Sep,
        "copy-data", "paste-data", "clear-data", "write-files", "refresh", Sep,
        "reveal", "copy-paths", "compare", Sep,
        "plugins", Sep,
        "select-all", "invert", "clear-selection", Sep,
        "delete",
    };

    private static readonly string[] RemoteIssueMenuOrder =
    {
        "open", Sep, "mark", "go-series", "copy-data", Sep, "select-all", "invert", "clear-selection",
    };

    private static readonly string[] SeriesMenuOrder =
    {
        "open", "edit", "mark", "add-list", "add-collection", Sep,
        "content-type", "reading-direction", "publication-status", "reading-status", "classify", Sep,
        "scrape", "organize", "write-files", "refresh", "merge", Sep,
        "reveal", "copy-paths", Sep,
        "plugins", Sep,
        "select-all", "invert", "clear-selection", Sep,
        "delete",
    };

    private static readonly string[] RemoteSeriesMenuOrder =
    {
        "open", Sep, "select-all", "invert", "clear-selection",
    };

    private static readonly string[] IssueBarOrder =
    {
        "edit", "rating", Sep,
        "bar-mark-read", "bar-mark-unread", "add-to", Sep,
        "scrape", "organize", "write-files", "paste-data", "clear-data", "refresh", Sep,
        "reveal", "copy-paths", "plugins",
        Trail, "delete", "clear-selection",
    };

    private static readonly string[] SeriesBarOrder =
    {
        "edit", Sep,
        "bar-mark-read", "bar-mark-unread", "add-to", Sep,
        "scrape", "organize", "write-files", "merge", "refresh", "classify", Sep,
        "reveal", "copy-paths", "plugins",
        Trail, "delete", "clear-selection",
    };

    /// <summary>
    /// Which catalog action each Library input-service action runs on the current selection, plus which child to run for a submenu action (the rating's star count). The
    /// bindings themselves live in the input service (<see cref="InputActions"/>) and are remappable; this only says what an action means for the selection at hand.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string ActionId, string? Child)> KeyActions = new Dictionary<string, (string, string?)>
    {
        [InputActionIds.LibraryEdit] = ("edit", null),
        [InputActionIds.LibraryRate0] = ("rating", "None"),
        [InputActionIds.LibraryRate1] = ("rating", "1 Star"),
        [InputActionIds.LibraryRate2] = ("rating", "2 Stars"),
        [InputActionIds.LibraryRate3] = ("rating", "3 Stars"),
        [InputActionIds.LibraryRate4] = ("rating", "4 Stars"),
        [InputActionIds.LibraryRate5] = ("rating", "5 Stars"),
        [InputActionIds.LibraryMarkRead] = ("bar-mark-read", null),
        [InputActionIds.LibraryMarkUnread] = ("bar-mark-unread", null),
        [InputActionIds.LibraryReveal] = ("reveal", null),
        [InputActionIds.LibraryCopyData] = ("copy-data", null),
        [InputActionIds.LibraryPasteData] = ("paste-data", null),
        [InputActionIds.LibraryCopyPaths] = ("copy-paths", null),
    };

    private readonly LibraryScreenViewModel _vm;
    private readonly Dictionary<string, LibraryAction> _actions;

    private readonly IInputService _input;

    /// <param name="input">Supplies the shortcut each entry shows; null means the application's input service.</param>
    public LibraryActionCatalog(LibraryScreenViewModel vm, IInputService? input = null)
    {
        _vm = vm;
        _input = input ?? InputServiceLocator.Current;
        _actions = CreateActions().ToDictionary(a => a.Id);
    }

    /// <summary>
    /// The shortcut text for an entry: the first keyboard binding of <paramref name="inputActionId"/> as it is bound <em>now</em>, so a remap in Preferences shows in the menu and the bar.
    /// An input service that does not know the action at all (the do-nothing one a test or design-time host has) falls back to the action's shipped default, so a menu built without
    /// a live service still reads as it always did; an action the user has deliberately unbound shows no shortcut.
    /// </summary>
    private string? Hint(string inputActionId) => _input.ShortcutText(inputActionId);

    /// <summary>"Alt+Shift+0…5" for the rating submenu when the star shortcuts still share a prefix, otherwise just the first one.</summary>
    private string? RatingRangeHint()
    {
        string? first = Hint(InputActionIds.LibraryRate0);
        string? last = Hint(InputActionIds.LibraryRate5);
        return first is not null && last is not null && first.Length > 1 && last.Length == first.Length && first[..^1] == last[..^1] ? $"{first}…{last[^1]}" : first;
    }

    private static string RateAction(int stars) => stars switch
    {
        0 => InputActionIds.LibraryRate0,
        1 => InputActionIds.LibraryRate1,
        2 => InputActionIds.LibraryRate2,
        3 => InputActionIds.LibraryRate3,
        4 => InputActionIds.LibraryRate4,
        _ => InputActionIds.LibraryRate5,
    };

    public IReadOnlyCollection<LibraryAction> All => _actions.Values;

    // ===================== Contexts =====================

    /// <summary>The context for a right-click on <paramref name="target"/> (an issue row or series card): the selection ∪ that tile - the
    /// same set the union-based commands act on - so the label's count always matches what runs.</summary>
    public LibraryActionContext? ForMenu(object? target)
    {
        switch (target)
        {
            case IssueListRow row:
            {
                var ids = _vm.Selection.UnionForAction(row.Id);
                var rows = RowsFor(ids);
                return new LibraryActionContext
                {
                    Surface = LibraryActionSurface.Menu,
                    Target = LibraryTarget.Issues(ids),
                    Row = row,
                    AnchorId = row.Id,
                    IsRemote = row.IsRemote,
                    HasFile = ids.Count == 1 ? row.HasFile : rows.Any(r => r.HasFile) || row.HasFile,
                    SeriesIds = rows.Select(r => r.SeriesId).Append(row.SeriesId).Distinct().ToList(),
                };
            }

            case SeriesCardSample card:
            {
                var ids = _vm.SeriesSelection.UnionForAction(card.SeriesId);
                var cards = CardsFor(ids);
                return new LibraryActionContext
                {
                    Surface = LibraryActionSurface.Menu,
                    Target = LibraryTarget.Series(ids),
                    Card = card,
                    AnchorId = card.SeriesId,
                    IsRemote = card.IsRemote,
                    HasFile = ids.Count == 1 ? card.HasFile : cards.Any(c => c.HasFile) || card.HasFile,
                    SeriesIds = ids,
                };
            }

            default:
                return null;
        }
    }

    /// <summary>The context for the selection bar and the keyboard: the current selection, or null when nothing is selected.</summary>
    public LibraryActionContext? ForSelection()
    {
        if (_vm.Selection.Count > 0)
        {
            var ids = OrderedSelection(_vm.Selection.SelectedIds, _vm.VisibleIssueRows.Select(r => r.Id));
            var rows = RowsFor(ids);
            return new LibraryActionContext
            {
                Surface = LibraryActionSurface.Bar,
                Target = LibraryTarget.Issues(ids),
                AnchorId = ids[0],
                HasFile = rows.Any(r => r.HasFile),
                SeriesIds = rows.Select(r => r.SeriesId).Distinct().ToList(),
            };
        }

        if (_vm.SeriesSelection.Count > 0)
        {
            var ids = OrderedSelection(_vm.SeriesSelection.SelectedIds, _vm.VisibleSeriesCards.Select(c => c.SeriesId));
            return new LibraryActionContext
            {
                Surface = LibraryActionSurface.Bar,
                Target = LibraryTarget.Series(ids),
                AnchorId = ids[0],
                HasFile = CardsFor(ids).Any(c => c.HasFile),
                SeriesIds = ids,
            };
        }

        return null;
    }

    // ===================== Surfaces =====================

    public IReadOnlyList<ContextMenuEntry> BuildMenu(LibraryActionContext context)
    {
        string[] order = (context.IsSeries, context.IsRemote) switch
        {
            (false, false) => IssueMenuOrder,
            (false, true) => RemoteIssueMenuOrder,
            (true, false) => SeriesMenuOrder,
            (true, true) => RemoteSeriesMenuOrder,
        };

        var entries = new List<ContextMenuEntry?>();
        foreach (string id in order)
        {
            entries.Add(id == Sep ? ContextMenuEntry.Separator : BuildAction(id, context));
        }

        return ContextMenuEntry.Compact(entries);
    }

    /// <summary>The bar's slots, left to right; separators are collapsed like a menu's (no leading/trailing/doubled ones).</summary>
    public IReadOnlyList<LibraryBarItem> BuildBar(LibraryActionContext context)
    {
        var items = new List<LibraryBarItem>();
        bool trailing = false;
        foreach (string id in context.IsSeries ? SeriesBarOrder : IssueBarOrder)
        {
            if (id == Trail)
            {
                trailing = true;
                continue;
            }

            if (id == Sep)
            {
                if (items.Count > 0 && !items[^1].IsSeparator)
                {
                    items.Add(new LibraryBarItem(null, true, trailing, null));
                }

                continue;
            }

            if (BuildAction(id, context) is { } entry)
            {
                items.Add(new LibraryBarItem(entry, false, trailing, id));
            }
        }

        while (items.Count > 0 && items[^1].IsSeparator)
        {
            items.RemoveAt(items.Count - 1);
        }

        return items;
    }

    /// <summary>The command (and parameter) the input-service action <paramref name="inputActionId"/> runs on the current selection, or false when it isn't a Library selection
    /// action or doesn't apply right now (nothing selected, the entry is disabled, the command can't run).</summary>
    public bool TryGetKeyCommand(string inputActionId, out ICommand? command, out object? parameter)
    {
        command = null;
        parameter = null;
        if (!KeyActions.TryGetValue(inputActionId, out var match) || ForSelection() is not { } context || BuildAction(match.ActionId, context) is not { } entry)
        {
            return false;
        }

        if (match.Child is { } childHeader)
        {
            entry = entry.Children?.FirstOrDefault(c => c.Header == childHeader) ?? entry;
            if (entry.Header != childHeader)
            {
                return false;
            }
        }

        if (!entry.IsEnabled || entry.Command is null || !entry.Command.CanExecute(entry.CommandParameter))
        {
            return false;
        }

        command = entry.Command;
        parameter = entry.CommandParameter;
        return true;
    }

    private ContextMenuEntry? BuildAction(string id, LibraryActionContext context)
    {
        if (!_actions.TryGetValue(id, out var action))
        {
            return null;
        }

        var kind = (context.IsSeries, context.IsRemote) switch
        {
            (false, false) => LibraryActionTargets.Issue,
            (false, true) => LibraryActionTargets.RemoteIssue,
            (true, false) => LibraryActionTargets.Series,
            (true, true) => LibraryActionTargets.RemoteSeries,
        };

        return (action.AppliesTo & kind) == 0 ? null : action.Build(context);
    }

    // ===================== The actions =====================

    private IEnumerable<LibraryAction> CreateActions()
    {
        const LibraryActionTargets issue = LibraryActionTargets.Issue;
        const LibraryActionTargets series = LibraryActionTargets.Series;
        const LibraryActionTargets local = LibraryActionTargets.Local;

        yield return new("open", local | LibraryActionTargets.RemoteIssue | LibraryActionTargets.RemoteSeries, c => c switch
        {
            { IsMenu: false } => null,
            { Row: { } row } => ContextMenuEntry.Item("Open", _vm.IssueList.OpenIssueCommand, row, Symbol.Open, inputGesture: "Enter"),
            { Card: { } card } => ContextMenuEntry.Item("Open Series", _vm.SelectCardCommand, card, Symbol.Open),
            _ => null,
        });

        yield return new("edit", local, c => c.IsMenu
            ? c.IsSeries
                ? ContextMenuEntry.Item(c.IsMulti ? $"Bulk Edit {c.Count} Series…" : "Bulk Edit…", _vm.BulkEditTargetCommand, c.Target, Symbol.Edit, inputGesture: Hint(InputActionIds.LibraryEdit))
                : ContextMenuEntry.Item("Edit Properties…", _vm.EditIssuePropertiesCommand, c.AnchorId, Symbol.Info, inputGesture: Hint(InputActionIds.LibraryEdit))
            : ContextMenuEntry.Item("Bulk Edit", _vm.BulkEditTargetCommand, c.Target, Symbol.Edit, inputGesture: Hint(InputActionIds.LibraryEdit)));

        yield return new("rating", issue, BuildRating);

        yield return new("mark", local | LibraryActionTargets.RemoteIssue, c =>
        {
            var children = new List<ContextMenuEntry?>
            {
                ContextMenuEntry.Item("Read", _vm.MarkTargetReadCommand, c.Target, Symbol.CheckmarkCircle, inputGesture: Hint(InputActionIds.LibraryMarkRead)),
                ContextMenuEntry.Item("Unread", _vm.MarkTargetUnreadCommand, c.Target, Symbol.Circle, inputGesture: Hint(InputActionIds.LibraryMarkUnread)),
            };
            if (c is { Row: { } row, Count: 1 })
            {
                children.Add(ContextMenuEntry.Item("Read up to here", _vm.MarkReadUpToHereCommand, row.Id, Symbol.ArrowCircleDown,
                    isEnabled: LibraryScreenViewModel.CanMarkReadUpTo(row)));
            }

            return ContextMenuEntry.SubMenu(c.IsMulti ? $"Mark {c.Count} as" : "Mark as", children, Symbol.Checkmark);
        });

        yield return new("bar-mark-read", local, c =>
            ContextMenuEntry.Item(c.IsMulti ? $"Mark {c.Count} as read" : "Mark as read", _vm.MarkTargetReadCommand, c.Target, Symbol.CheckmarkCircle, inputGesture: Hint(InputActionIds.LibraryMarkRead)));

        yield return new("bar-mark-unread", local, c =>
            ContextMenuEntry.Item(c.IsMulti ? $"Mark {c.Count} as unread" : "Mark as unread", _vm.MarkTargetUnreadCommand, c.Target, Symbol.Circle, inputGesture: Hint(InputActionIds.LibraryMarkUnread)));

        yield return new("add-list", local, c =>
            ContextMenuEntry.SubMenu(c.IsMulti ? $"Add {c.Count} to Reading List" : "Add to Reading List", ReadingListChildren(c), Symbol.TextBulletListAdd));

        yield return new("add-collection", local, c =>
            ContextMenuEntry.SubMenu(c.IsMulti ? $"Add {c.Count} to Collection" : "Add to Collection", CollectionChildren(c), Symbol.CollectionsAdd));

        yield return new("add-to", local, c => ContextMenuEntry.SubMenu(
            c.IsMulti ? $"Add {c.Count} to" : "Add to",
            new[]
            {
                ContextMenuEntry.SubMenu("Reading List", ReadingListChildren(c), Symbol.TextBulletListAdd),
                ContextMenuEntry.SubMenu("Collection", CollectionChildren(c), Symbol.CollectionsAdd),
            },
            Symbol.CollectionsAdd));

        yield return new("show-in-list", issue, c =>
        {
            if (c is not { IsMenu: true, Row: { } row, Count: 1 })
            {
                return null;
            }

            var lists = _vm.ListsContaining(row.Id);
            IEnumerable<ContextMenuEntry?> children = lists.Count == 0
                ? new[] { ContextMenuEntry.Item("(Not in any list)", null, isEnabled: false) }
                : lists.Select(l => ContextMenuEntry.Item(l.Name, _vm.ShowInListCommand, l, l.IsCollection ? Symbol.CollectionsAdd : Symbol.TextBulletListAdd));
            return ContextMenuEntry.SubMenu("Show in List", children, Symbol.List);
        });

        yield return new("go-series", issue | LibraryActionTargets.RemoteIssue, c => c is { IsMenu: true, Row: { } row }
            ? ContextMenuEntry.Item("Go to Series", _vm.GoToSeriesCommand, row.SeriesId, Symbol.ArrowForward)
            : null);

        yield return new("series-setters", issue, c => c.IsMenu ? ContextMenuEntry.SubMenu(c.SeriesIds.Count > 1 ? $"{c.SeriesIds.Count} Series" : "Series", SeriesSetters(c), Symbol.Library) : null);
        yield return new("content-type", series, c => c.IsMenu ? SeriesSetters(c)[0] : null);
        yield return new("reading-direction", series, c => c.IsMenu ? SeriesSetters(c)[1] : null);
        yield return new("publication-status", series, c => c.IsMenu ? SeriesSetters(c)[2] : null);
        yield return new("reading-status", series, c => c.IsMenu ? SeriesSetters(c)[3] : null);

        yield return new("classify", series, c => ContextMenuEntry.Item(
            c.IsMulti ? $"Classify {c.Count} series from trackers" : "Classify from trackers", _vm.ClassifyTargetCommand, c.Target, Symbol.Tag));

        yield return new("scrape", local, c => c.IsSeries
            ? ContextMenuEntry.Item(c.IsMulti ? $"Scrape {c.Count} series…" : "Scrape…", _vm.ScrapeSeriesWithComicVineCommand, c.AnchorId, Symbol.ArrowDownload)
            : ContextMenuEntry.Item(c.IsMulti ? $"Scrape {c.Count}…" : "Scrape…", _vm.ScrapeWithComicVineCommand, c.AnchorId, Symbol.ArrowDownload));

        yield return new("organize", local, c => c.IsSeries
            ? ContextMenuEntry.Item(c.IsMulti ? $"Organize {c.Count} series…" : "Organize…", _vm.OrganizeSeriesWithProfileCommand, c.AnchorId, Symbol.FolderArrowRight)
            : ContextMenuEntry.Item(c.IsMulti ? $"Organize {c.Count}…" : "Organize…", _vm.OrganizeWithProfileCommand, c.AnchorId, Symbol.FolderArrowRight));

        yield return new("copy-data", issue | LibraryActionTargets.RemoteIssue, c =>
            ContextMenuEntry.Item("Copy Data", _vm.CopyDataCommand, c.Target, Symbol.Copy, inputGesture: Hint(InputActionIds.LibraryCopyData)));

        yield return new("paste-data", issue, c =>
            ContextMenuEntry.Item(c.IsMulti ? $"Paste Data onto {c.Count}…" : "Paste Data…", _vm.PasteDataCommand, c.Target, Symbol.ClipboardPaste,
                isEnabled: _vm.HasMetadataClipboard, inputGesture: Hint(InputActionIds.LibraryPasteData)));

        yield return new("clear-data", issue, c =>
            ContextMenuEntry.Item(c.IsMulti ? $"Clear Data of {c.Count}…" : "Clear Data…", _vm.ClearDataCommand, c.Target, Symbol.Eraser));

        yield return new("write-files", local, c =>
        {
            if (!_vm.CanWriteMetadataToFiles)
            {
                return null;
            }

            return c.IsSeries
                ? ContextMenuEntry.Item(c.IsMulti ? $"Write metadata to {c.Count} series' files" : "Write metadata to files", _vm.WriteSeriesMetadataToFilesCommand, c.AnchorId, Symbol.Save)
                : ContextMenuEntry.Item(c.IsMulti ? $"Write metadata to {c.Count} files" : "Write metadata to file", _vm.WriteIssueMetadataToFilesCommand, c.AnchorId, Symbol.Save, isEnabled: c.HasFile);
        });

        yield return new("refresh", local, c => ContextMenuEntry.SubMenu(
            "Refresh",
            new[]
            {
                ContextMenuEntry.Item("Refresh thumbnails", _vm.RefreshThumbnailsCommand, c.Target, Symbol.ArrowSync, isEnabled: c.HasFile),
                ContextMenuEntry.Item("Re-read info from file…", _vm.RereadFromFileCommand, c.Target, Symbol.DocumentArrowDown, isEnabled: c.HasFile),
            },
            Symbol.ArrowSync));

        yield return new("merge", series, c => c.Count >= 2
            ? ContextMenuEntry.Item($"Merge {c.Count} series…", _vm.MergeSeriesCommand, c.Target, Symbol.Merge)
            : null);

        yield return new("reveal", local, c =>
            ContextMenuEntry.Item("Show in Explorer", _vm.RevealTargetCommand, c.Target, Symbol.FolderOpen, isEnabled: c.HasFile, inputGesture: Hint(InputActionIds.LibraryReveal)));

        yield return new("copy-paths", local, c =>
            ContextMenuEntry.Item(!c.IsSeries && c.Count == 1 ? "Copy file path" : "Copy file paths", _vm.CopyFilePathsCommand, c.Target, Symbol.DocumentCopy,
                isEnabled: c.HasFile, inputGesture: Hint(InputActionIds.LibraryCopyPaths)));

        // Exactly two selected: put the two files side by side (docs/superpowers/specs/2026-09-26-comic-reader-compare-design.md #11).
        yield return new("compare", issue, c => c is { IsMenu: true, Count: 2 }
            ? ContextMenuEntry.Item("Compare files…", _vm.CompareSelectedIssuesCommand, c.AnchorId, Symbol.ArrowSwap)
            : null);

        yield return new("plugins", local, c => ContextMenuEntry.SubMenu(
            "Plugins",
            _vm.LibraryPluginCommands.Select(p => ContextMenuEntry.Item(p.Name, _vm.RunLibraryPluginOnTargetCommand, (c.Target, p), inputGesture: Hint(Paperbunkr.App.Services.Input.PluginInputActions.IdFor(p)))),
            Symbol.PuzzlePiece,
            isVisible: _vm.HasLibraryPluginCommands));

        yield return new("select-all", local | LibraryActionTargets.RemoteIssue | LibraryActionTargets.RemoteSeries, c => c.IsMenu
            ? ContextMenuEntry.Item("Select All", c.IsSeries ? _vm.SelectAllVisibleSeriesCommand : _vm.SelectAllVisibleIssuesCommand, icon: Symbol.SelectAllOn, inputGesture: Hint(InputActionIds.LibrarySelectAll))
            : null);

        yield return new("invert", local | LibraryActionTargets.RemoteIssue | LibraryActionTargets.RemoteSeries, c => c.IsMenu
            ? ContextMenuEntry.Item("Invert Selection", _vm.InvertSelectionCommand, icon: Symbol.SelectAllOff)
            : null);

        yield return new("clear-selection", local | LibraryActionTargets.RemoteIssue | LibraryActionTargets.RemoteSeries, c => c.IsMenu
            ? ContextMenuEntry.Item("Clear Selection", _vm.ClearAnySelectionCommand, icon: Symbol.SelectAllOff, isEnabled: _vm.HasAnySelection)
            : ContextMenuEntry.Item("Clear selection", _vm.ClearAnySelectionCommand, icon: Symbol.Dismiss, inputGesture: "Esc"));

        yield return new("delete", local, c =>
        {
            int n = c.Count;
            return c.IsSeries
                ? ContextMenuEntry.SubMenu(
                    c.IsMulti ? $"Delete {n} Series…" : "Delete Series…",
                    new[]
                    {
                        ContextMenuEntry.Item(n > 1 ? $"Remove {n} series from the library, keep the files" : "Remove from the library, keep the files", _vm.RemoveSeriesKeepFilesCommand, c.AnchorId),
                        ContextMenuEntry.Item(n > 1 ? $"Yes, delete {n} series" : "Yes, delete this series", _vm.DeleteSeriesCommand, c.AnchorId),
                    },
                    Symbol.Delete,
                    isDanger: true)
                : ContextMenuEntry.SubMenu(
                    c.IsMulti ? $"Delete {n} comics…" : "Delete…",
                    new[]
                    {
                        ContextMenuEntry.Item(n > 1 ? $"Remove {n} from the library, keep the files" : "Remove from the library, keep the file", _vm.RemoveIssueKeepFileCommand, c.AnchorId),
                        ContextMenuEntry.Item(n > 1 ? $"Yes, delete {n} issues" : "Yes, delete this issue", _vm.DeleteIssueCommand, c.AnchorId),
                    },
                    Symbol.Delete,
                    isDanger: true);
        });
    }

    /// <summary>"My Rating ▸" - None and 1-5 stars (✓ only when every targeted book shares the rating, CE's <c>GetRating()</c> -1 rule), then
    /// Quick Rate… for a single book.</summary>
    private ContextMenuEntry? BuildRating(LibraryActionContext c)
    {
        int? common = _vm.CommonRating(c.Target.Ids);
        var children = new List<ContextMenuEntry?>
        {
            ContextMenuEntry.Item("None", _vm.SetRatingCommand, (c.Target, (int?)null), isChecked: common == 0, inputGesture: Hint(InputActionIds.LibraryRate0)),
        };
        for (int stars = 1; stars <= 5; stars++)
        {
            children.Add(ContextMenuEntry.Item(stars == 1 ? "1 Star" : $"{stars} Stars", _vm.SetRatingCommand, (c.Target, (int?)stars),
                Symbol.Star, isChecked: common == stars, inputGesture: Hint(RateAction(stars))));
        }

        if (c.Count == 1)
        {
            children.Add(ContextMenuEntry.Separator);
            children.Add(ContextMenuEntry.Item("Quick Rate…", _vm.OpenQuickRateCommand, c.Target.Ids[0], Symbol.Star));
        }

        var menu = ContextMenuEntry.SubMenu(c.IsMulti ? $"Rate {c.Count}" : "My Rating", children, Symbol.Star);
        return menu is null ? null : menu with { InputGesture = RatingRangeHint() };
    }

    /// <summary>Content Type / Reading Direction / Publication Status / Reading Status over every series of the target - folded under
    /// "Series ▸" on an issue menu, inline on a series menu (its long-standing shape). ✓ when all targeted series share the value. Always four
    /// slots, in that order; Reading Direction is null unless the clicked series is manga-family.</summary>
    private IReadOnlyList<ContextMenuEntry?> SeriesSetters(LibraryActionContext c)
    {
        var seriesIds = c.SeriesIds;
        var labels = SeriesLabels(c);
        string? Common(Func<(string? ContentType, string? Status, string? Reading, string? Direction), string?> pick)
        {
            var values = labels.Select(pick).Distinct().ToList();
            return values.Count == 1 ? values[0] : null;
        }

        bool mangaFamily = c.Row?.IsMangaFamily ?? c.Card?.IsMangaFamily ?? false;
        return new[]
        {
            ContextMenuEntry.SubMenu("Content Type", Radios(Common(l => l.ContentType), _vm.SetSeriesContentTypeForCommand, seriesIds,
                ("Comic", ContentType.Comic), ("Manga", ContentType.Manga), ("Manhua", ContentType.Manhua), ("Manhwa", ContentType.Manhwa))),
            ContextMenuEntry.SubMenu("Reading Direction", Radios(Common(l => l.Direction), _vm.SetSeriesReadingModeForCommand, seriesIds,
                ("Left to Right", ReadingMode.LeftToRight), ("Right to Left", ReadingMode.RightToLeft)), isVisible: mangaFamily),
            ContextMenuEntry.SubMenu("Publication Status", Radios(Common(l => l.Status), _vm.SetSeriesStatusForCommand, seriesIds,
                ("Unknown", SeriesStatus.Unknown), ("Ongoing", SeriesStatus.Ongoing), ("Completed", SeriesStatus.Completed),
                ("Cancelled", SeriesStatus.Cancelled), ("Hiatus", SeriesStatus.Hiatus))),
            ContextMenuEntry.SubMenu("Reading Status", Radios(Common(l => l.Reading), _vm.SetSeriesReadingStatusForCommand, seriesIds,
                ("Unknown", ReadingStatus.Unknown), ("Planned", ReadingStatus.Planned), ("Reading", ReadingStatus.Reading),
                ("Completed", ReadingStatus.Completed), ("Paused", ReadingStatus.Paused), ("Dropped", ReadingStatus.Dropped),
                ("Re-reading", ReadingStatus.ReReading))),
        };
    }

    private static IEnumerable<ContextMenuEntry?> Radios<TEnum>(string? current, ICommand command, IReadOnlyList<int> seriesIds,
        params (string Header, TEnum Value)[] options) where TEnum : struct, Enum =>
        options.Select(o => ContextMenuEntry.Item(o.Header, command, (seriesIds, o.Value),
            isChecked: string.Equals(current, o.Value.ToString(), StringComparison.Ordinal)));

    private List<(string? ContentType, string? Status, string? Reading, string? Direction)> SeriesLabels(LibraryActionContext c)
    {
        if (c.IsSeries)
        {
            var cards = CardsFor(c.Target.Ids);
            if (c.Card is { } clicked && cards.All(x => x.SeriesId != clicked.SeriesId))
            {
                cards.Add(clicked);
            }

            return cards.GroupBy(x => x.SeriesId).Select(g => g.First())
                .Select(x => ((string?)x.ContentTypeLabel, x.SeriesStatusLabel, x.ReadingStatusLabel, x.ReadingDirectionLabel)).ToList();
        }

        var rows = RowsFor(c.Target.Ids);
        if (c.Row is { } row && rows.All(r => r.Id != row.Id))
        {
            rows.Add(row);
        }

        return rows.GroupBy(r => r.SeriesId).Select(g => g.First())
            .Select(r => ((string?)r.ContentTypeLabel, r.SeriesStatusLabel, r.ReadingStatusLabel, r.ReadingDirectionLabel)).ToList();
    }

    private IEnumerable<ContextMenuEntry?> ReadingListChildren(LibraryActionContext c)
    {
        foreach (var list in _vm.ReadingLists)
        {
            yield return ContextMenuEntry.Item(list.Name, _vm.AddTargetToReadingListCommand, (c.Target, list.Id));
        }

        if (_vm.ReadingLists.Count > 0)
        {
            yield return ContextMenuEntry.Separator;
        }

        yield return ContextMenuEntry.Item("New List…", _vm.CreateReadingListAndAddTargetCommand, c.Target);
    }

    private IEnumerable<ContextMenuEntry?> CollectionChildren(LibraryActionContext c)
    {
        var add = c.IsSeries ? _vm.AddSeriesToCollectionCommand : _vm.AddIssueToCollectionCommand;
        var create = c.IsSeries ? _vm.CreateCollectionAndAddSeriesCommand : _vm.CreateCollectionAndAddIssueCommand;
        foreach (var collection in _vm.Collections)
        {
            yield return ContextMenuEntry.Item(collection.Name, add, (c.AnchorId, collection.Id));
        }

        if (_vm.Collections.Count > 0)
        {
            yield return ContextMenuEntry.Separator;
        }

        yield return ContextMenuEntry.Item("New collection…", create, c.AnchorId);
    }

    private List<IssueListRow> RowsFor(IReadOnlyList<int> ids)
    {
        var set = ids.ToHashSet();
        return _vm.VisibleIssueRows.Where(r => set.Contains(r.Id)).ToList();
    }

    private List<SeriesCardSample> CardsFor(IReadOnlyList<int> ids)
    {
        var set = ids.ToHashSet();
        return _vm.VisibleSeriesCards.Where(c => set.Contains(c.SeriesId)).ToList();
    }

    /// <summary>The selected ids in on-screen order (any not on screen - there shouldn't be, after pruning - go last).</summary>
    private static IReadOnlyList<int> OrderedSelection(IReadOnlySet<int> selected, IEnumerable<int> visibleOrder) =>
        visibleOrder.Where(selected.Contains).Concat(selected).Distinct().ToList();
}
