# CBL Smart Match — review tools

Date: 2026-10-25. Status: approved in conversation; user explicitly requested write-up and implementation.

## Scope and decisions

Part 2 of the October 13 Smart Match design. Part 1 is implemented. Part 3 (scrape/edit/rename healing and preserving manual additions on source refresh) follows separately, before backlog items 2–6. No tracker queue, Continuity/import-gap work, panel performance or alternate-cover work belongs in this delivery.

The user chose a dedicated review screen over a drawer/dialog extension or a wizard. It serves single-entry Find & link, an open list's unresolved entries, selected lists chosen inside the review screen, and import previews. The existing Add issues drawer remains. Requests and candidate metadata/evidence are shown together; search and next/previous navigation remain available. Missing entries can be left unresolved. No network matching or confidence percentages are introduced.

CBL, CSV, arc and folder imports prepare a read-only manifest before showing the preview. Folder import has one combined preview, preserving folder hierarchy and reporting failed/skipped files. CBL smart-list detection remains separate. Fetch an arc once, retaining its source/overview and ordered annotations. Do not create folders, lists, issues or source records until acceptance. Keep unresolved entries defaults on; off excludes unresolved rows with explicit counts/details. Cancel writes nothing. Duplicate source entries remain reported.

Review choices are staged. Cross-list application is explicit, shows all affected entries, and touches only equivalent unresolved entries without a previous manual choice. Same provider issue identity may establish equivalence unless another supplied identity conflicts; otherwise equivalent normalized series/number requires equal requested year, normalized volume/format and all supplied identity fields. Null is not a wildcard. No persistent future-match rule is created.

Apply revalidates affected rows and candidate identities and checks duplicate assignments within each list, inside the write transaction. Any stale row/candidate or duplicate blocks the whole batch, retaining choices for correction. Commit changes and durable undo together. Preserve requested source identity, order, roles, notes and reading progress. One issue per list is an operation rule, not a database unique constraint.

Drafts remain in the retained screen view model across navigation during the process. Starting another operation with pending choices offers Resume or Discard; app exit asks before discarding. Only applied history survives restart.

## Undo

History is scoped to the database and retained for 30 days. Screen History, list/gallery menu entry points and immediate Undo expose it. Each applied review/link batch stores before and expected-after link/source state in the same database transaction as the changes. Capture placeholder information needed to restore a deleted placeholder; never depend on the previous issue id surviving. Do not use the metadata-edit global undo action.

Undo previews eligible rows and conflicts, then restores eligible rows in one transaction. Compare owned link/source state rather than notes, role or position, so unrelated later edits survive. Deleted/changed entries, missing targets and duplicate collisions are conflicts, not silently overwritten. Successfully restored rows are marked undone; conflicting rows remain reportable. Whole-import deletion/undo is excluded. Choices made before a new list exists are part of import, not independently undoable link batches.

## UI and lifecycle

Use existing Paperbunkr heading/type/control/token families and Motion v2. A virtualized entry queue and candidate comparison share the screen; compact widths switch queue/detail rather than squeeze both. Review starts with the relevant entry/list preselected; gallery review offers a checkbox list selector. Counts name resolved, needs choice, missing, duplicate, skipped and explicitly excluded entries. Status/errors use text as well as color. Search has an associated label. Focus returns sensibly after changing rows, Apply, Back and Undo.

Avalonia foundation: `avalonia-pro-max/layout-patterns`, `accessibility`, `avalonia-mvvm`, project `paperbunkr-design-language`; implementation also reads XAML/binding/testing guidance and runs review-checklist. Shortcuts register through IInputService. Defer collection replacement/close beyond routed events. New AXAML and matching code-behind are added together.

## Reference findings

CE `ComicListLibraryBrowser.cs:1488–1545` matches before confirmation, reports missing books and offers Add missing Books to Library; cancel creates no list. CE `ComicIdListItem.cs:154` tries GUID, filename, then metadata and selects first. Rich ambiguity review is a deliberate improvement. Contrary to an older context note, CE does read filenames; changing Paperbunkr filename matching is outside this delivery. Reference CBL Manager imports immediately and refresh clears/rebuilds its source order; no precedent for review undo or remembered choices was found.

Existing `ReadingListResolver.Resolve` is read-only; `Materialize` stages writes. `ReadingListItemSource` keeps original requested identity. `ReadingListItemLinker` currently cleans up unused placeholders, so restoration must be explicit. `ReadingListReconciler` also serves Continuity; leave its semantics alone here.

## Validation

Data tests: preview has no writes; CBL/CSV/arc/folder manifests; failed/skipped files and cancellation; strict equivalence and manual-link protection; duplicate and stale-candidate/entry rejection; atomic rollback; cross-list batch; persistent history; shared placeholder recreation; partial undo conflicts; notes/role/order preservation and 30-day retention. App tests: launch modes, staged state/navigation, list selection, search/candidates, compact layout, Apply/Undo/history and accessibility names. Run matching/import/review targeted suites, shared UI lint and relevant real-window input tests serially. Forced rebuild if XAML weave fails. Real-window/assistive-technology verification is reported separately from headless checks.
