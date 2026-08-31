using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services;

/// <summary>
/// The app shell's drill-down navigation history (docs/superpowers/specs/2026-08-30-app-shell-
/// navigation-history-design.md) - a list + cursor, the same shape CE's own <c>IBrowseHistory</c>
/// uses (a <c>LinkedList</c> cursor over past library-list selections, confirmed by reading
/// <c>ComicListLibraryBrowser.cs</c>), generalized from "library list selection" to "drill-down
/// screen + entity". Replaces <c>MainViewModel</c>'s old single-slot <c>_screenBeforeReader</c>/
/// <c>_screenBeforeBookReader</c> hacks, which only ever supported exactly one level.
///
/// Deliberately knows nothing about any screen ViewModel - only <see cref="NavigationEntry"/> and a
/// plain root-screen key string. <c>MainViewModel</c> owns translating an entry back into an actual
/// screen navigation.
/// </summary>
public sealed class NavigationHistoryService
{
    private readonly List<NavigationEntry> _entries = new();
    private int _cursor = -1;

    /// <summary>The lateral rail screen the current drill-down chain hangs off - set by
    /// <see cref="ResetRoot"/>, whatever the current chain's first `Back()` past index 0 returns to.</summary>
    public string RootScreenKey { get; private set; } = "home";

    /// <summary>Called by every lateral rail navigation (Home/Library/Books/Smart/Reading/Events/
    /// Preferences) - clears any in-progress drill-down chain and establishes the new root, per the
    /// design doc's "rail moves don't push, but do become the new root" rule.</summary>
    public void ResetRoot(string railScreenKey)
    {
        _entries.Clear();
        _cursor = -1;
        RootScreenKey = railScreenKey;
    }

    /// <summary>Called by every drill-down navigation (GoDetailForSeries, GoReaderForIssue, etc.).
    /// Truncates any forward entries past the cursor first - same as a browser: navigating from a
    /// backed-up position discards the abandoned forward branch.</summary>
    public void Push(NavigationEntry entry)
    {
        if (_cursor < _entries.Count - 1)
        {
            _entries.RemoveRange(_cursor + 1, _entries.Count - _cursor - 1);
        }

        _entries.Add(entry);
        _cursor = _entries.Count - 1;
    }

    public bool CanGoBack => _cursor >= 0;

    public bool CanGoForward => _cursor < _entries.Count - 1;

    /// <summary>Moves the cursor back one step. Returns the entry now at the cursor, or
    /// <see langword="null"/> when the cursor has moved past index 0 to the root - the caller
    /// navigates to <see cref="RootScreenKey"/> in that case. No-ops (returns <see langword="null"/>
    /// without moving the cursor) when <see cref="CanGoBack"/> is already false.</summary>
    public NavigationEntry? Back()
    {
        if (!CanGoBack)
        {
            return null;
        }

        _cursor--;
        return _cursor >= 0 ? _entries[_cursor] : null;
    }

    /// <summary>Moves the cursor forward one step and returns the entry now at the cursor. No-op
    /// (returns <see langword="null"/>) when <see cref="CanGoForward"/> is already false.</summary>
    public NavigationEntry? Forward()
    {
        if (!CanGoForward)
        {
            return null;
        }

        _cursor++;
        return _entries[_cursor];
    }

    /// <summary>Jumps directly to a breadcrumb segment's index (clicking a mid-trail crumb),
    /// truncating anything past it exactly like a fresh <see cref="Push"/> would on the next
    /// navigation. Passing -1 is the root segment - returns <see langword="null"/>, same contract as
    /// <see cref="Back"/> landing past index 0.</summary>
    public NavigationEntry? JumpTo(int index)
    {
        _cursor = index;
        return index >= 0 && index < _entries.Count ? _entries[index] : null;
    }

    /// <summary>Every entry from the start of the current chain up to and including the cursor - the
    /// breadcrumb trail. Entries past the cursor (the abandoned forward branch, if any) are excluded.</summary>
    public IReadOnlyList<NavigationEntry> BreadcrumbTrail => _entries.Take(_cursor + 1).ToList();
}
