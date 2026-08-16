# Reveal-in-Explorer & manual fileless book entries

**Date:** 2026-08-16
**Status:** Approved, pending implementation plan
**Backlog ref:** `docs/alpha-roadmap.md`'s "Library browsing extras" bundle — first of several planned
sub-projects (decomposed this session; the remaining 9 items — filesystem folder browsing mode,
browse history, saved Workspaces/List Layouts, pluggable sort/group, drag-and-drop import,
Recent/MRU + Quick Open, live folder-watch, file metadata write-back — are each their own future
spec, not covered here).

## Context

Two of the eleven "Library browsing extras" backlog items turn out to be nearly infrastructure-
complete already, verified against CE's actual implementation per this project's standing rule:

- **Reveal-in-Explorer**: CE's shell-API helper (`FileExplorer.cs` — `SHOpenFolderAndSelectItems`
  P/Invoke, no explorer.exe process spawned) is already ported verbatim to
  `src/Paperbunkr.Common/Win32/FileExplorer.cs`, but has zero call sites anywhere in the app.
- **Fileless book entries**: CE's `IsLinked`/`FileIsMissing` distinction (a book can be "never had a
  file" vs. "was linked, file went missing") is already fully modeled — `Issue.FilePath` is
  nullable, `IsPlaceholder`/`FileIsMissing` are real migrated columns, and
  `ReadingListMatcher.ResolveOrCreatePlaceholder` (`src/Paperbunkr.Data/ReadingLists/
  ReadingListMatcher.cs`) already creates exactly this kind of entry today — just only reachable via
  CBL/CSV reading-list import, not a manual "add a physical book" UI flow.

Both are picked as the first slice specifically because they close two backlog items with mostly
wiring/reuse rather than new architecture — see this session's decomposition discussion for why the
other nine items were deferred (each is either a larger, more independent subsystem, or — for file
metadata write-back — deliberately sequenced last as the one item CE itself gates behind explicit
opt-in settings, matching the roadmap doc's own "real risk surface" flag).

A real architectural fork surfaced during design: `LibraryScreen`'s grid (all 7 view modes) shows
**series-level** cards (`SeriesCardSample`), not individual issues — so "reveal in Explorer" can't
mean CE's "select this exact file" there (a series can span multiple files/folders). The Detail
screen's issue-tile list (`DetailTabs.axaml`, `src/Paperbunkr.App/Views/DetailTabs.axaml`) already
shows individual issues with a per-issue `ContextMenu` (the existing "Edit Properties" item) — that
surface maps cleanly onto CE's exact per-file behavior.

A second risk surfaced during design: naively wiring "delete this placeholder if the user cancels
without editing anything" onto the existing edit-buffer's `_isDirty` flag would be dangerous — if a
user's entered Series+Number happens to match an **existing, fully-detailed real book**,
`_isDirty` would still read `false` (nothing changed *this session*), and a naive rule would delete
real library data. The fix (§3 below) only allows deletion when the row was actually newly created
by this exact flow, never an existing match.

## 1. Reveal-in-Explorer

New static helper, `RevealInExplorerHelper` (`src/Paperbunkr.App/Services/RevealInExplorerHelper.cs`),
wrapping `cYo.Common.Win32.FileExplorer` calls with the folder-resolution rules below — kept out of
the ViewModels so the same logic isn't duplicated across three call sites:

- `RevealIssue(Issue issue)` — `FileExplorer.OpenFolderAndSelect(issue.FilePath)` if `FilePath` is
  non-empty, else no-op (returns `false`).
- `RevealIssues(IEnumerable<Issue> issues)` — dedupes to unique containing folders
  (`Path.GetDirectoryName`) across every issue with a non-empty `FilePath`, calls
  `FileExplorer.OpenFolder` once per unique folder (not `OpenFolderAndSelect` — no single file to
  select once more than one file's involved).
- `RevealSeries(Series series)` — orders `series.Issues` via the existing `IssueOrdering.OrderByNumber()`
  extension (`src/Paperbunkr.App/Models/IssueOrdering.cs`), takes the first issue with a non-empty
  `FilePath`, opens its containing folder via `FileExplorer.OpenFolder` (again no selection — a
  series has no single file).

Wired at three points:
- **`DetailTabs.axaml`** issue tile: new `MenuItem` "Show in Explorer" in the existing
  `Border.ContextMenu`, same `Command="{Binding #IssuesList.((vm:DetailTabsViewModel)DataContext).X}"
  CommandParameter="{Binding}"` pattern the "Edit Properties" item already uses. `IsEnabled` bound to
  the issue row's `FilePath` non-empty.
- **`DetailTabsViewModel`**: new command using `SelectedIssueIds` (the same set bulk-editing already
  populates) when more than one issue is selected, calling `RevealIssues`.
- **`LibraryScreen`**'s series card: new context-menu item calling `RevealSeries`, `IsEnabled` when
  the series has at least one issue with a `FilePath`.

## 2. Manual "add a physical book" entry point

New "+ Add" button in `LibraryScreen`'s toolbar, opening a small `Flyout` (same weight as the
existing Zoom/Adjust-style flyouts elsewhere in this app) with two fields: a Series-name text box
with typeahead against existing `Series.Name` values, and a Number text box (the minimum CE itself
requires to identify a `ComicBook` — matches `ResolveOrCreatePlaceholder`'s own minimum argument
set). A "Create" button confirms.

## 3. Wiring into the existing editor, safely

`ReadingListMatcher` gains a **new, additive overload** — the existing 5-argument
`ResolveOrCreatePlaceholder` (used by CBL/CSV import today) is untouched, so reading-list import
behavior doesn't change at all:

```csharp
public static Issue ResolveOrCreatePlaceholder(
    PaperbunkrDbContext context, string seriesName, string number,
    int? volume, int? year, string? format, out bool wasCreated)
```

`wasCreated` is `true` only when no existing match was found and a brand-new row was inserted;
`false` when `FindExisting` matched something already in the library (a real book or an existing
placeholder) — the new UI flow navigates to that existing entry either way (useful — "you already
have this" instead of a silent duplicate), but only treats the newly-created case as deletable.

`IssuePropertiesScreenViewModel.Load` gains a new parameter:
`Load(int issueId, bool deleteIfUnedited = false)` — stored as a field, defaulting to `false` for
every existing call site (Detail screen's "Edit Properties", etc.), so nothing else changes
behavior. `Cancel()` becomes:

```csharp
[RelayCommand]
private void Cancel()
{
    if (_deleteIfUnedited && !HasUnsavedChanges())
    {
        using var context = _contextFactory();
        var issue = context.Issues.Find(_issueId);
        if (issue is not null)
        {
            context.Issues.Remove(issue);
            context.SaveChanges();
        }
    }

    _goBack();
}
```

`HasUnsavedChanges()` (the existing public accessor over `_isDirty`) is reused exactly as-is — the
danger case from this spec's Context section (deleting a real pre-existing book) can't happen
because `deleteIfUnedited` is only ever `true` when the caller's `wasCreated` was `true`, i.e. this
exact flow inserted the row a moment ago.

## Explicitly out of scope

Series-picker autocomplete beyond a simple typeahead (no fuzzy matching, no "did you mean" —
exact/prefix match against `Series.Name` is enough for a first pass). Editing volume/year/format at
creation time (left to the editor screen the flow immediately opens, matching how CE's own minimum
`ComicBook` identity is just series+number). The other nine "Library browsing extras" items, per
this spec's Context section.
