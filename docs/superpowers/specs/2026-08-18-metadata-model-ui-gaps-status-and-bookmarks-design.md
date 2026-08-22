# Metadata Model UI Gaps: Series.Status Editing + Reader Bookmarks

**Date:** 2026-08-18
**Status:** Approved, pending implementation

## Context

Prompted by the user asking how the Metadata Model work (Phases 1-6a) actually surfaces in the UI.
Auditing every phase found most of it wired up (Proposals → Needs Review, Media Relations/Continuity
→ Related tab, Story Events → its own screen, ContentType → Bulk Edit + Library context menu), but
two Phase 1 entities shipped with real schema and **zero UI**:

- `Series.Status` (`SeriesStatus`: Unknown/Ongoing/Completed/Cancelled/Hiatus) - nothing anywhere
  lets a user set it.
- `IssueBookmark` (`IssueId`/`PageNumber`/`Label`/`Note`/`CreatedTime`) - the comic Reader has zero
  bookmark UI. A *different* entity, `BookBookmark`, already has working UI, but only for the
  Novels/EPUB reader.

These are also 2 of the 5 concepts that paused "pluggable sort/group strategies" on 2026-08-17
(alongside OpenCount and Proposed-metadata, both already usable since their schema exists with no UI
gap blocking them, and AlternateCount/variant tracking, which has no schema at all and stays out of
scope here - a separate, unscoped `IssueEdition` design, not a "missing UI" fix). Closing these two
gaps is the user's explicit prerequisite before resuming that work.

## Part 1: `Series.Status` editing

No CE precedent exists for this specific concept (`SeriesStatus.cs`'s own doc comment, confirmed via
source search - CE only ever had the per-issue `SeriesComplete` Yes/No/Unknown flag, already ported
separately as `Issue.IsFinalIssue`). The template to follow instead is this codebase's own precedent
for `Series.ContentType` - another Series-level enum made editable through two write paths:

### Bulk Edit field

New row in `BulkFieldRegistry.All` (`src/Paperbunkr.App/Models/BulkFieldDescriptor.cs`), same shape
as the existing `ContentType` row: `FieldKind.Enum`, `Get`/`Set` reaching through `i.Series.Status`
(not `Issue`), `Options: Enum.GetNames<SeriesStatus>()`. `BulkIssuePropertiesScreenViewModel.Save()`
already does `context.Issues.Include(i => i.Series)` for `ContentType`'s sake, so `Status` needs no
new `Include`. No new XAML - the flyout-of-buttons control is already data-driven off `Options`.

### Library grid per-card context menu

New `MenuItem Header="Set Status"` submenu (`src/Paperbunkr.App/Views/LibraryScreen.axaml`, all 6
card-size template sites, same place as the existing `Set Content Type` submenu) with one `MenuItem`
per `SeriesStatus` value, each its own `[RelayCommand]` in `LibraryScreenViewModel.cs` (mirroring
`SetSeriesContentType*`) - one-command-per-value because `CommandParameter` can only carry the card,
not both card and value, same reasoning already documented for Content Type. Unlike
`SetSeriesContentType`, **no `LoadFromDatabase()` reload** - Library doesn't sort/group/filter by
Status yet (that's the follow-on sort/group work), so nothing downstream needs a refresh.

**Explicitly not built**: a "+Add" flow picker (ContentType needed one because it immediately drives
reading-direction defaulting; Status drives nothing else yet) and any dedicated "Series Properties"
screen (doesn't exist for anything else either - out of scope beyond this one field).

## Part 2: Reader bookmarks (`IssueBookmark`)

### CE precedent vs. this codebase's own `BookBookmark` precedent

Checked both, per the standing CE-verification rule - they point in different directions:

- **CE** (`ComicRack/MainForm.cs`, `IEditBookmark`/`ComicDisplay`): one plain string label per page
  (`ComicPageInfo.Bookmark`), set via a small text-input dialog (pre-filled with a proposed label),
  reachable from 3 duplicated menu locations (main menu/right-click/toolbar), with real keyboard
  commands for **Previous Bookmark** (`Ctrl+PageUp`) and **Next Bookmark** (`Ctrl+PageDown`) - but
  notably **no** keyboard shortcut for *setting* a bookmark itself, menu-only. Also draws a ribbon
  indicator on the bookmarked page's thumbnail in the pages rail.
- **This codebase's own `BookBookmark`** (`BookReaderScreenViewModel`/`BookReaderScreen.axaml`,
  Novels reader): a toolbar icon toggles the *current position* on/off (auto-excerpted, no user-typed
  label), opens a slide-out drawer listing every bookmark with click-to-navigate + per-row delete.

**Decision**: follow `BookBookmark`'s interaction shape (toggle + list, this codebase's own
established idiom, same tech stack) rather than CE's WinForms dialog-per-bookmark, but adopt CE's
two real keyboard commands (Previous/Next Bookmark) and its thumbnail-ribbon indicator, since the
Reader screen already has the page-thumbnail rail CE's version needs and `BookBookmark`'s
chapter-based Novels reader doesn't. Toggle instead of a typed-label dialog also matches this
project's `IssueBookmark.Label` usage as an auto-generated `"Page {n}"` rather than requiring manual
naming - consistent with `BookBookmark.Excerpt` being auto-derived, not user-typed, in its own
precedent. One bookmark per page (toggle on/off), matching both CE (one string per page) and
`BookBookmark` (one per position) - `IssueBookmark`'s own schema technically allows more via its
`Id` primary key, but nothing in either precedent does that, and toggle semantics don't naturally
support it; a future "multiple named bookmarks per page" pass can revisit if ever needed.

### UI: toolbar flyout, not a drawer

Reader's toolbar (`ReaderScreen.axaml`) already uses the pill-button-plus-flyout idiom for every
other picker (reading mode, fit mode, adjust, page-transition) - a docked drawer would be a new,
inconsistent pattern for this screen specifically (right for the Novels reader, wrong here). New
`Auto` column between the existing pickers and Fullscreen: a "🔖" pill button, `Classes.active` bound
to whether the current page is bookmarked, flyout containing:
- A toggle row: "Bookmark this page" / "✓ Bookmarked - tap to remove" (mirrors `BookBookmark`'s own
  toggle row text exactly).
- A scrollable list of every bookmark for the current issue (`"Page {n}: {label}"`), each row
  click-to-navigate (`GoToPage`) + a small delete button - same `ItemsControl`/row-with-delete-button
  shape as `BookReaderScreen.axaml`'s bookmark list, adapted to a flyout's `StackPanel` instead of a
  docked drawer's `Border`.

### Thumbnail ribbon indicator

`ReaderThumbnailSample` gains `bool IsBookmarked` (currently only `IsSelected`/`CoverBrush`/
`CoverImage`). `UpdateThumbnailSelection`'s existing per-page rebuild loop (already re-creates every
`ReaderThumbnailSample` on each page change, preserving `CoverImage`) sets it from a
`HashSet<int> _bookmarkedPages` loaded alongside the issue in `Load()`. `ReaderScreen.axaml`'s thumb
`Border` gets a small corner indicator (`IsVisible="{Binding IsBookmarked}"`) - a colored triangle/
dot in a corner, not a full ribbon graphic (no existing icon asset for one, and a corner shape is
cheap to draw with a plain `Border`/`Polygon` rather than needing new art).

### Keyboard commands

Two new entries in `KeyboardCommandRegistry` (`NavigationGroup`, `ConflictContext.Always` - bookmark
navigation should work regardless of paged/continuous mode), mirroring CE's actual defaults exactly:

```csharp
public const string ReaderPreviousBookmark = "Reader.PreviousBookmark";
public const string ReaderNextBookmark = "Reader.NextBookmark";
...
new(ReaderPreviousBookmark, NavigationGroup, "Previous bookmark", new KeyGesture(Key.PageUp, KeyModifiers.Control), ConflictContext.Always),
new(ReaderNextBookmark, NavigationGroup, "Next bookmark", new KeyGesture(Key.PageDown, KeyModifiers.Control), ConflictContext.Always),
```

No keyboard shortcut for *setting* a bookmark (matches CE's own choice - menu/toolbar only, confirmed
via source search, not an oversight). `PageCanvas.OnKeyDown` gains the two new command checks
(jump to nearest bookmarked page before/after `_currentPageIndex`, no-op if none exists in that
direction), same dispatch shape every other remappable command already uses there.

### Persistence pattern

No new resolver/service class - every read/write opens a fresh `PaperbunkrDb.CreateContext()` inline
in the ViewModel method that needs it, exactly matching `BookReaderScreenViewModel`'s own established
convention (its class doc comment explicitly calls this out as matching `ReaderScreenViewModel.
GoToPage`'s existing `Issue.LastPageRead` persistence - the same file this feature extends).

## Testing

- `BulkFieldRegistryTests`: new `Status` row round-trips through `Get`/`Set` via `Issue.Series`
  (same pattern as the existing `ContentType` row's test).
- `LibraryScreenViewModelTests`: each `SetSeriesStatus*` command writes the right `SeriesStatus` to
  the right series, doesn't touch other series' rows, doesn't trigger a reload.
- `ReaderScreenViewModelTests`: toggling a bookmark on creates one `IssueBookmark` row with the
  expected auto-label and marks the thumbnail/toolbar state active; toggling again removes it;
  navigating to a bookmark jumps to its page and closes nothing unexpected; Previous/Next Bookmark
  commands find the nearest bookmark in each direction and no-op at the ends; bookmarks are scoped
  to their own issue (switching issues doesn't show another issue's bookmarks).
