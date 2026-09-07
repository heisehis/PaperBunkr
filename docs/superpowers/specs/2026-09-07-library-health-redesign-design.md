# Library Health Dashboard Redesign — Design (Phase 2 of Preferences)

**Sub-project 1 of "Whole UI Re-Architecture," continuing the Preferences track** — Phase 1
(`2026-09-07-preferences-tile-hub-redesign-design.md`) explicitly deferred this area in its §4
item 2 ("Library Health dashboard — stat tiles + missing-files/recently-removed repeaters") as
needing "its own genuine redesign, not a reskin." This doc is that redesign.

## Background

Library Health lives inside `Views/Preferences/LibrarySection.axaml`, as one `groupBox` among six
in the "Library" sidebar section (Comic Library Folders, Scanning, **Library Health**, Book
Folders, Migration, Virtual Tags — see `LibrarySection.axaml:40-406`). It surfaces
`LibraryHealthService`'s live re-verification of missing comic files: a 4-column plain-text stat
row (Issues checked / Currently missing / Confirmed missing / Last verified), a Verify Now +
Remove All Confirmed action row with a bespoke inline confirm panel, and two always-or-never
repeaters (Missing Files, Recently Removed) that vanish entirely when empty.

A background fact-check against `_reference/ComicRackCE` (2026-09-07) confirmed **no CE
precedent** exists for any of this — no verify action, no confirmed-after-N-checks logic, no
restore list, no summary tile. CE's closest equivalent is a single silent `File.Exists` sweep
during scan gated by one checkbox. Library Health is a deliberate Paperbunkr deviation, so this
redesign answers only to the app's own design language, not CE parity.

Reached via `/grilling` (2026-09-07): 7 rounds of questions plus 3 iterations in the visual
companion converged on the design below. Key inputs: no CE precedent to anchor to; the shared
`IDialogService`/`ConfirmDialogView` and toast system (`ToastRequest`/`PbToastView`) shipped
2026-09-06 specifically naming this screen's bulk-remove flow as an intended consumer
(`IDialogService.cs:11`); and the reverted Phase-1 shell means this still lives in a
sidebar + hard-switch pane, not a single-scroll page.

## Goals (this phase)

- Real stat tiles (bordered cards, per-stat icon, semantic color) replacing the plain-text grid.
- Reorder: Library Health becomes the first block in the Library page (currently third), on the
  reasoning that a health/status summary is what you want to see first when opening library
  management — Comic Library Folders/Scanning/Book Folders/Migration/Virtual Tags keep their
  existing relative order, shifted down.
- Scanning's 2 toggles (`AutoRemoveMissingOnScan`, `DontReimportRemovedFiles`) convert to
  `SettingsRow`, matching Phase 1's primitive (they were static, XAML-authored toggles the Phase 1
  sweep simply missed being outside the sections it surveyed).
- Bulk-remove confirmation moves from the bespoke inline panel to `IDialogService.ShowAsync` with
  `ConfirmDialogRequest.Items = BulkRemovePreview`.
- Missing Files and Recently Removed rows get icon+text action buttons, matching Comic Folders'
  row treatment directly above them in the same tab.
- Both list sections become always-visible with an explicit empty/healthy state, instead of
  disappearing when empty.
- Recently Removed becomes a collapsed-by-default disclosure (space-saving — it's an audit trail,
  not an actionable list like Missing Files).
- `LibraryHealthService.ConfirmedMissingThreshold` (currently a hardcoded `const int = 2`) becomes
  a real, user-facing setting.
- Ephemeral operation feedback (verify completion, bulk-relink outcome) moves to the app's toast
  system instead of a lingering inline status `TextBlock`.

## Non-goals

- No new Preferences sidebar entry — Library Health stays nested under "Library."
- No CE-parity constraints (none exist to satisfy — see Background).
- Comic Library Folders, Book Folders, Migration, and Virtual Tags are unaffected (separate future
  phases per the parent doc's §4 items 1 and 3).
- No change to `LibraryHealthService`'s verification logic itself (the two-strikes algorithm, the
  30-day Recently Removed retention) — only how its threshold is configured and how its output is
  presented.

## Approaches considered

1. **Minimal reskin** — apply tile chrome and icons but keep everything else (inline confirm
   panel, always-text status, conditional section visibility) as-is. Rejected: this is exactly the
   "reskin, not redesign" the parent doc's §4 explicitly ruled out for this area, and it leaves the
   inline confirm panel duplicating logic the shared `IDialogService` was built to replace.
2. **Full re-architecture as a stat-first sub-dashboard with its own navigation** (e.g., a
   dedicated modal or its own Preferences sidebar entry). Rejected: nothing in this project calls
   for that scope — it's still fundamentally "settings about your library," and a new top-level
   destination invents navigation decisions nobody asked for (confirmed in grilling Q2).
3. **Targeted redesign within the existing shell** (chosen) — real stat tiles, shared dialog/toast
   adoption, consistent row-action styling, and a disclosure for the low-priority list, all within
   the current sidebar + hard-switch pane, at the top of the Library page. Matches what Phase 1 did
   for the areas it touched: keep the shell, redesign the content that lives inside it.

## Architecture

### 1. Layout & scope

`LibrarySection.axaml`'s content stack reorders to: **Library Health**, Scanning, Comic Library
Folders, Book Folders, Migration, Virtual Tags. Scanning's two toggle rows convert from raw
`Grid`+`ToggleSwitch` to `SettingsRow`, unchanged in behavior.

### 2. Stat tiles

Four `Border`-based tiles in a `Grid`/`UniformGrid` (was a bare 4-column `Grid` of `TextBlock`s):
each tile gets a small icon "chip" (28px rounded square, ~18% opacity tint of its semantic color
behind a stroked icon) above the number:

| Stat | Icon | Chip tint |
|---|---|---|
| Issues checked | list | amber (`PbAccentColor` family) |
| Currently missing | alert | gold (`PbBadgeColor`) |
| Confirmed missing | alert | danger (`PbDangerColor`) — number itself also renders in danger when >0, matching the existing plain-text behavior |
| Last verified | clock | success (`PbSuccessColor`) |

No new color tokens — this uses only what's already in `App.axaml`. Chip tints are new
low-opacity brush resources derived from the existing color tokens (e.g.
`PbDangerColor` at ~18% alpha), added alongside the existing brushes.

### 3. Action row

`Verify Now` stays the primary (amber) button, gains a refresh icon. `Remove All Confirmed
Missing` becomes a danger-tinted ghost button (border/text in `PbDangerColor` at reduced opacity),
gains a trash icon — it's a destructive action, and this treatment already exists elsewhere
(`Button.rowAction.destructive`). The inline "Verifying…"/status caption is removed from this row
entirely (see §6, Toasts) since it's redundant with the Last Verified tile and moves to
transient feedback instead.

While a verify is running, `Verify Now` shows the existing `BusyIndicator` control (shipped
2026-09-06, `Controls/BusyIndicator.cs`) instead of just disabling — visible in-progress feedback
without a running text caption or a toast-per-progress-tick (which would be spammy: verify can
process hundreds of issues).

### 4. Bulk-remove confirmation

`OpenBulkRemoveConfirmCommand` stops setting `IsBulkRemoveConfirmOpen = true` to reveal the inline
panel. Instead it awaits `IDialogService.ShowAsync(new ConfirmDialogRequest(
Message: "Remove every confirmed-missing issue below? ...", Items: BulkRemovePreview,
PrimaryLabel: "Confirm Remove All", IsDestructive: true))` and, on primary, runs the existing
`ConfirmBulkRemoveCommand` logic directly. This removes `IsBulkRemoveConfirmOpen`,
`CancelBulkRemoveConfirmCommand`, and the inline `Border` panel from the view entirely — the
now-shared `ConfirmDialogView`/`OverlayShell` renders it instead.

### 5. Missing Files section

Row template (`MissingFileRowTemplate`) changes from plain-text `rowAction` buttons to icon+text
(link icon/Relink, x icon/Dismiss, trash icon/destructive Delete), matching Comic Folders' row
buttons in the same file.

The section container stops being conditionally hidden
(`IsVisible="{Binding HasMissingFileItems}"` on the whole block). It's always visible; when
`HasMissingFileItems` is false, the `ItemsControl` is replaced by a healthy empty state (checkmark
icon in a success-tinted chip + "No missing files" text) instead of the section disappearing.

### 6. Recently Removed section (disclosure)

Also always-visible, but collapsed by default via a new bool (`IsRecentlyRemovedExpanded`,
default `false`) toggled by clicking its header. No `Expander`/`SettingsExpander` exists anywhere
else in the app yet, so this follows the codebase's existing show/hide-toggle idiom (the same
shape as the bulk-remove panel's now-removed `IsBulkRemoveConfirmOpen`) rather than introducing a
new control type:

- **0 items**: header reads "Recently Removed · All clear" with a success checkmark, no chevron,
  not expandable (nothing to reveal).
- **N > 0 items**: header reads "Recently Removed · N" with a chevron (▸ collapsed / ▾ expanded);
  clicking toggles `IsRecentlyRemovedExpanded`, revealing the existing `RemovedLibraryEntryRowTemplate`
  list (also gets the icon+text button treatment on its Restore action).

### 7. New setting: confirmed-missing threshold

`LibraryHealthService.ConfirmedMissingThreshold` (currently `const int = 2`,
`LibraryHealthService.cs:29`) becomes instance state read from a new
`AppSettings.LibraryHealthConfirmedMissingThreshold` column (`int`, default `2` — preserves
today's behavior). Requires an EF migration; per this project's standing rule (the 2026-09-06
orphan-column rollback bug — any new `AppSettings` column needs a no-op `Down()`), the generated
migration's `Down()` must be hand-corrected to a no-op for this column.

Surfaced as a `SettingsRow` inside the Library Health block (small numeric stepper, range 1-5,
label "Confirm missing after N checks", description explaining what "confirmed missing" gates —
the Remove All Confirmed action and the auto-remove-on-scan behavior). This is a static,
single-control row, so it fits `SettingsRow` cleanly even though the rest of this area is
deferred from that primitive (per Phase 1's own dividing line: static rows vs. dynamic repeaters —
this one row is static).

### 8. Toasts

`PreferencesScreenViewModel` gains `public event Action<ToastRequest>? ToastRequested`, and
`MainViewModel` wires `Preferences.ToastRequested += ShowToast` alongside its existing
`Activity.CompletionToastRequested += ShowToast` (`MainViewModel.cs:116`) — same pattern, new
source.

Two call sites switch from setting a status string to raising a toast:
- `VerifyLibraryHealthNow`'s completion (today: `LibraryHealthVerifyStatus = summary`) → a
  `ToastSeverity.Success`/`.Info` toast with that same summary text. The failure path (today:
  `LibraryHealthVerifyStatus = $"Verify failed: {ex.Message}"`) → `ToastSeverity.Error`.
- Bulk relink's outcome (today: `LibraryHealthBulkRelinkStatus = ...`) → a toast, same severity
  logic (0 relinked = info, otherwise success).

`LibraryHealthVerifyStatus`/`LibraryHealthBulkRelinkStatus` properties are removed; nothing reads
them once their bindings are gone.

## ViewModel changes (summary)

- New: `IsRecentlyRemovedExpanded` (bool, default false).
- New: `LibraryHealthConfirmedMissingThreshold` (int, bound to the new `AppSettings` column,
  replacing the `LibraryHealthService.ConfirmedMissingThreshold` constant with an instance field
  the service reads at construction/verify time).
- New: `ToastRequested` event on `PreferencesScreenViewModel`; `MainViewModel` subscribes it.
- Removed: `IsBulkRemoveConfirmOpen`, `CancelBulkRemoveConfirmCommand`,
  `LibraryHealthVerifyStatus`, `LibraryHealthBulkRelinkStatus`.
- Changed: `OpenBulkRemoveConfirmCommand` becomes async, awaiting `IDialogService.ShowAsync`
  instead of flipping a bool.
- `LibraryHealthService` constructor/method signatures gain the threshold as a parameter instead
  of reading the `const`.

## Testing

- `PreferencesScreenViewModelTests`: replace the `IsBulkRemoveConfirmOpen`-toggling assertions with
  ones asserting `IDialogService.ShowAsync` is called with the expected `ConfirmDialogRequest`
  (existing tests already mock cross-cutting services this way, e.g. `IFilePickerService`).
  New cases: `IsRecentlyRemovedExpanded` toggles on header click and only appears clickable when
  count > 0; `LibraryHealthConfirmedMissingThreshold` persists through the settings round-trip
  used elsewhere in this file; `ToastRequested` fires on verify completion and on verify failure
  with the expected severities.
- `LibraryHealthServiceTests`: update the 5 existing cases to pass an explicit threshold instead of
  relying on the constant; add one case confirming a non-default threshold changes
  confirmed-missing classification.
- New migration test (`AddLibraryHealthConfirmedMissingThresholdMigrationTests`), same shape as
  the existing `AddMissingVerificationCountAndRemovedLibraryEntryMigrationTests` — confirms the
  column adds with the right default and that `Down()` is a no-op.
- Manual on-screen pass (standing no-unattended-GUI caveat): verify with a healthy library shows
  the "All clear" states on both lists; introduce a missing file, confirm it appears, dismiss/
  relink/delete it via the new icon buttons; trigger Remove All Confirmed and confirm the shared
  modal (not an inline panel) appears with the correct preview list; change the threshold and
  confirm a file needs the new number of checks before appearing in the bulk-remove preview;
  confirm verify-complete and bulk-relink-outcome show as toasts, not inline text; confirm
  Recently Removed collapses/expands correctly and shows "All clear" with no chevron when empty.

## Deliverable: description checklist

The new `SettingsRow` for the confirmed-missing threshold needs its `Description` text written as
part of implementation (same open item Phase 1 tracked in `docs/preferences-descriptions-todo.md`
— add this row to that list rather than duplicating the tracking mechanism here).
