using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// Helpers for driving the Library toolbar: the Covers / List / Details switch and the Display button in row 1, and the
/// "Sort:" and "Group:" chips in row 2, each of which opens its own list (the three-tab "View &amp; Sort" popup was split up
/// on 2026-10-04).
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
        Find(window, "LibraryDisplayButton");
    }

    /// <summary>Clicks a row-1 view switch: <c>LibraryViewSwitch_Covers</c>, <c>_List</c> or <c>_Details</c>.</summary>
    public static void SelectViewMode(Window window, string switchId) => Invoke(window, switchId);

    /// <summary>Opens the popup behind <paramref name="chipId"/> unless the option is already on screen, then picks it.</summary>
    private static void SelectFromChip(Window window, string chipId, string optionId)
    {
        if (TryFind(window, optionId) is null)
        {
            Invoke(window, chipId);
        }

        Invoke(window, optionId);
    }

    public static void SelectSort(Window window, string optionId) => SelectFromChip(window, "LibrarySortChip", optionId);

    public static void SelectGroup(Window window, string optionId) => SelectFromChip(window, "LibraryGroupChip", optionId);

    /// <summary>The "Sort: …" chip's accessible name. The chip is always shown.</summary>
    public static string SortChipText(Window window) => Find(window, "LibrarySortChip").Name;

    /// <summary>The "Group: …" chip's accessible name ("Group: None" when ungrouped).</summary>
    public static string GroupChipText(Window window) => Find(window, "LibraryGroupChip").Name;

    /// <summary>The Display button's accessible name, which carries the active display mode ("Display options: List").</summary>
    public static string DisplayButtonName(Window window) => Find(window, "LibraryDisplayButton").Name;

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
