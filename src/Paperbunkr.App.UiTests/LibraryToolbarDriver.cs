using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// Helpers for driving Library's Phase 4b toolbar (docs/superpowers/specs/2026-08-27-library-
/// browsing-4b-toolbar-rework-design.md): the Filter/Sort/Group/Display pills collapsed into one
/// "View &amp; Sort" tabbed popup, with live sort/group state surfaced as removable chips instead of
/// on the buttons themselves.
/// </summary>
internal static class LibraryToolbarDriver
{
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(8);

    /// <summary>Find by AutomationId, retrying until it appears (screen/popup transitions aren't instant).</summary>
    public static AutomationElement Find(Window window, string id) =>
        Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId(id)),
            FindTimeout, throwOnTimeout: true,
            timeoutMessage: $"No element with AutomationId '{id}' appeared within {FindTimeout}.").Result!;

    public static AutomationElement? TryFind(Window window, string id) =>
        window.FindFirstDescendant(cf => cf.ByAutomationId(id));

    public static void Invoke(Window window, string id) => Find(window, id).AsButton().Invoke();

    public static void GoToLibrary(Window window)
    {
        Invoke(window, "LibraryRailButton");
        // Wait for the toolbar to actually render before the caller starts poking it.
        Find(window, "LibraryViewSortButton");
    }

    /// <summary>Opens the View &amp; Sort popup (if closed) and switches it to the given tab.</summary>
    private static void OpenViewSortTab(Window window, string tabId)
    {
        if (TryFind(window, tabId) is null)
        {
            Invoke(window, "LibraryViewSortButton");
        }

        Invoke(window, tabId);
    }

    public static void OpenViewTab(Window window) => OpenViewSortTab(window, "LibraryViewSortTab_View");
    public static void OpenSortTab(Window window) => OpenViewSortTab(window, "LibraryViewSortTab_Sort");
    public static void OpenGroupTab(Window window) => OpenViewSortTab(window, "LibraryViewSortTab_Group");

    public static void SelectViewMode(Window window, string optionId)
    {
        OpenViewTab(window);
        Invoke(window, optionId);
    }

    public static void SelectSort(Window window, string optionId)
    {
        OpenSortTab(window);
        Invoke(window, optionId);
    }

    public static void SelectGroup(Window window, string optionId)
    {
        OpenGroupTab(window);
        Invoke(window, optionId);
    }

    /// <summary>The "Sorted: …" chip's accessible name, retrying until it appears (a non-default
    /// sort makes it visible). Throws if it never shows.</summary>
    public static string SortChipText(Window window) => Find(window, "LibrarySortChip").Name;

    /// <summary>The "Grouped: …" chip's accessible name, retrying until it appears.</summary>
    public static string GroupChipText(Window window) => Find(window, "LibraryGroupChip").Name;

    /// <summary>The View &amp; Sort button's accessible name carries the active display mode (the
    /// visible label stays "View &amp; Sort"; the chips carry sort/group state).</summary>
    public static string ViewSortButtonName(Window window) => Find(window, "LibraryViewSortButton").Name;

    // --- Saved Workspaces (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md) ---

    /// <summary>The workspace switcher pill's accessible name, e.g. "Workspace: Manga" or "Workspace: Workspace".</summary>
    public static string WorkspaceButtonName(Window window) => Find(window, "LibraryWorkspaceButton").Name;

    /// <summary>Opens the workspace dropdown (if closed) and applies the built-in workspace with the given name.</summary>
    public static void ApplyWorkspace(Window window, string name)
    {
        string rowId = $"LibraryWorkspaceRow_{name}";
        if (TryFind(window, rowId) is null)
        {
            Invoke(window, "LibraryWorkspaceButton");
        }

        Invoke(window, rowId);
    }
}
