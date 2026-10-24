# Comic and Manga Detail List Layouts

Status: approved by user on 2026-10-18. Source implementation is complete; build, tests, and live UI checks remain unverified.

## Summary

Extend the saved list layout feature to Comic and Manga detail screens. Comic Issues and Specials share one layout and view mode, as they do today. Manga Chapters gets its own configurable table layout. Each detail type has one saved default shared by all series of that type, plus a separate catalog of reusable named layouts. Users can save, edit, and select named layouts from controls beside the existing list toolbar controls.

## Context and verified behavior

The existing saved-layout feature serves Library and Books. `ListLayoutService` stores named layouts and JSON assignments by `WorkspaceScreen`; `ListLayoutsViewModel` assumes there can be a different assignment for each selected list and exposes operations such as “Default layout for this list” and “Set on all lists.” Those per-list semantics do not directly fit detail screens, where the user chose one default per detail type.

The Comic detail screen uses `DetailTabsViewModel.IssueViewMode` for both Issues and Specials. Its three modes are Poster, List, and Card. The two collections share templates: Poster shows a cover, issue number, status/edition marks, and arc title; List shows status, issue number, full title, arc, cover date, and rating; Card shows a cover, read status, title/progress label, and reader/property actions. The current list row is fixed, and the mode is currently in memory only.

Manga detail has a chapter row grouped by volume. The current row contains number, title, new/read status, bookmark and missing indicators, scan information, release/added date, and an in-progress indicator. Chapter filtering is All, Unread, Bookmarked, or Missing; sorting is by Number or Date with a direction. Filtering is currently session-only, and `LoadSeries` resets it to All.

ComicRack CE’s `ListLayoutDialog` supports editable ordered Details columns and configurable Thumbnail/Tile text. The named `ListConfiguration` is copied onto a list when applied; it is not a live link. This proposal reuses that layout concept, while keeping Paperbunkr’s detail-specific view structures.

## Goals

- Let users configure the fields shown in Comic Poster, List, and Card modes.
- Let users configure Manga chapter columns and save chapter sorting.
- Reuse the existing named-layout persistence and editing patterns.
- Keep one default layout for every Comic detail screen and one for every Manga detail screen.
- Keep Issues and Specials on the same Comic layout and view mode.
- Preserve current appearance as the initial default for each detail type.

## Out of scope

- Per-series layout assignments.
- A configurable Specials layout separate from Comic Issues.
- Changing Comic sorting, filtering, or grouping; the current detail screen has no such controls.
- Persisting the Manga All/Unread/Bookmarked/Missing filter.
- Changing chapter grouping by volume.
- Changing card action buttons or poster/card cover behavior.
- Adding metadata fields that the current detail rows and cards do not expose.
- Built-in named presets.

## Proposed behavior

### Persistence and named layouts

Add detail-specific layout state records rather than overloading `ListLayoutState`, whose fields describe Library and Books. Store named layouts in the existing `ListLayout` table, with distinct screen keys for Comic Detail and Manga Detail. Store the current defaults in the existing `ListLayoutAssignment` table under the existing `*` default key, one row per detail type. No assignment is keyed by series ID.

On first opening each detail type, capture its current in-code presentation as that type’s default. Any layout-affecting change writes the updated default. Navigating to any other series of that type uses the same saved default. Applying a named layout copies its state into the type default and applies it immediately; later changes do not mutate the named layout. Saving a named layout captures the current default. Reset restores the built-in presentation for that type. The UI does not show per-list actions such as “Set on all lists,” because the single default already applies to every series.

Keep the existing deferred popup-close behavior when a menu item applies a layout, because the clicked row must not be detached while its routed event is still being delivered.

### Comic Issues and Specials

Issues and Specials continue to use the same `IssueViewMode` and the same layout state. The List Layout control is available from both toolbars; the mode selector remains available in both places so the shared mode can be changed from either tab.

The options editor has Poster, List, and Card sections. Each mode has an independent field selection:

- **Poster:** the issue number, current read/in-progress status mark, optional edition mark, and arc title. The cover stays visible. The selected text/marks keep their existing positions and hierarchy.
- **List:** status, issue number, full title, arc, cover date, and rating. Users can toggle visibility, reorder columns, and set column widths. At least one column must remain visible; if a stored layout becomes invalid, use the built-in List columns.
- **Card:** read status, full title, and issue number/progress label. The cover and action buttons stay visible. Selected text/status elements keep their existing positions and hierarchy.

Changing the selected view mode shows that mode’s independently configured fields. The same state applies to both Issues and Specials. A field with no value for an issue renders empty or is omitted according to its existing template behavior; it does not collapse the issue’s interactive hit target.

### Manga Chapters

The chapter options editor supports configurable columns drawn from fields already present on `ChapterRowSample`: number, title, new/read status, bookmark, missing, scan information, date, and progress. Users can toggle visibility, reorder columns, and set widths. The layout also stores `ChapterSortField` and `SortDirection`.

Volume grouping remains as it is today. The active chapter filter remains transient and is not included in a saved layout. Applying a named layout changes the saved default sort and columns, but does not change the current All/Unread/Bookmarked/Missing filter.

If a stored field is removed from the chapter or issue catalog, ignore it on load. If no visible columns remain, use the built-in columns. Clamp persisted widths to a safe range, following the existing `ListLayoutStateJson` normalization pattern.

## UI

**Placement (corrected 2026-10-19, user direction):** the detail layouts live in the Library toolbar's Workspace popup, in a "Detail screen layouts" section under the Library's own "List layouts", where every list layout already lives. The detail screens get no layout button of their own. An earlier build put a "Layouts" button beside the view controls on Comic Issues/Specials and the filter/sort pills on Manga Chapters; that was removed as the wrong place. The section has a Comic | Manga switch and offers the current named layouts, List Options, Save List Layout, Edit Layouts, and Reset to Default for the chosen type. Named layouts are separate between Comic and Manga. It binds to the detail screens' own `ListLayoutsViewModel`s (long-lived on `MainViewModel`), so a layout applied from the Library shows the next time a detail screen opens.

The detail List Options overlay presents the current detail’s sections: Poster/List/Card for Comic, and Chapters for Manga. Comic Poster/Card offer field checkboxes while retaining their current arrangement; Comic List and Manga Chapters offer an ordered, checkable column list with width controls. The overlay supports Apply, OK, and Cancel, consistent with the existing List Options editor.

Reuse the existing layout name prompt, rename/delete/reorder patterns, and Edit Layouts presentation where practical. Detail layout management must not expose per-series operations. Every icon-only control has an accessible name, and the UI uses the app’s dynamic `Pb*` theme resources and standard focus-ring behavior.

## Data shape

Proposed JSON state is version-tolerant and screen-specific:

```text
ComicDetailLayoutState
  ViewMode: Poster | List | Card
  PosterFields: set of supported poster text/status fields
  ListColumns: ordered list of { Field, Visible, Width }
  CardFields: set of supported card text/status fields

MangaDetailLayoutState
  ChapterColumns: ordered list of { Field, Visible, Width }
  SortField: Number | Date
  SortDirection: Ascending | Descending
```

Missing fields load built-in defaults, unknown fields are dropped, and malformed JSON falls back to the built-in layout. Enum values should follow the tolerant enum-as-string serializer used by workspace state. This does not change `ListLayoutState` JSON or existing Library/Books assignments.

## Implementation approach

1. Add detail-specific state records, field catalogs, normalization, and capture/apply behavior.
2. Add a detail layout host/view model that uses the existing `ListLayoutService` named catalogs and type-default assignment rows without exposing per-list assignment actions.
3. Extend the List Options UI for mode-specific Comic fields and Manga chapter columns; wire its open/close and edit-layout overlays through `MainViewModel`.
4. Bind the Comic and Manga toolbar controls to the detail layout view models. Preserve deferred popup closure and existing routed input handling.
5. Add focused persistence and view-model coverage for default reuse across series, named-layout CRUD/apply, mode-specific fields, chapter column normalization, and transient Manga filtering.

## Avalonia constraints

Follow `avalonia-pro-max/components`, `layout-patterns`, `accessibility`, and `review-checklist` for the implementation. Use existing `Button`/popup and overlay patterns, app theme tokens, and keyboard-accessible move-up/move-down controls alongside any drag interaction. Do not add screen-level key handlers for hotkeys; any shortcut must use `IInputService` and `InputActionIds`. Preserve the existing delayed-detach rule for popup menu rows.

## Verification plan

- Unit coverage: JSON round-trip/defaults, unknown-field removal, width clamping, and the no-visible-column fallback for both detail types.
- View-model coverage: per-type defaults persist across different series; named layout save/apply/edit behavior; Comic mode-specific field state; Manga sort persists while the filter remains unchanged by layout application.
- View coverage: toolbar layout controls exist on Comic Issues, Specials, and Manga Chapters; the correct options section appears; all controls remain keyboard reachable.
- Manual UI check in light and dark skins: field/column reordering and resizing, popup closure from a layout row, switching series and detail tabs, and reset/apply behavior.

## Design self-review

- **Scope:** Covers all three requested surfaces, preserves shared Comic Issues/Specials behavior, and avoids per-series assignments as requested.
- **Data ownership:** Keeps detail state separate from Library/Books state and gives each detail type its own named-layout namespace and default.
- **Invalid state:** Defines fallback behavior for stale fields, malformed JSON, and empty visible-column sets.
- **Interaction risk:** Calls out deferred popup close and keyboard alternatives for reordering.
- **Storage key:** Reuse `WorkspaceScreen` for the existing layout service’s screen namespace and append `ComicDetail` and `MangaDetail`. Those values are used only in list-layout rows, not in `Workspace` rows. Both layout tables already persist the enum as a string with enough length for these names, so no database migration is needed.

## CE source checked

`_reference/ComicRackCE/ComicRack/Dialogs/ListLayoutDialog.cs`,
`_reference/ComicRackCE/ComicRack/Database/DisplayListConfig.cs`, and
`_reference/ComicRackCE/ComicRack/Views/ComicBrowserControl.cs` were checked through the existing List Layouts design audit. CE provides configurable list columns and thumbnail/tile text fields; Paperbunkr’s new per-mode detail state is a deliberate adaptation to its current detail templates.
