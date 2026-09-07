# Virtual Tags Editor Redesign — Design (Phase 4 of Preferences)

**Closes the Library section redesign** — the Preferences Tile-Hub Redesign's §4 deferred list,
item 3, the last of three (after [Library Health](2026-09-07-library-health-redesign-design.md)
and [Library folder management](2026-09-07-library-folder-management-redesign-design.md)).

## Background

`LibrarySection.axaml`'s Virtual Tags block is a master/detail pattern: an `ItemsControl` of tag
names (`VirtualTags`, clickable via `SelectVirtualTagCommand`), and below it an inline detail form
(Name, Caption Format, Enabled toggle, a live Preview, Delete Tag) shown only when
`HasSelectedVirtualTag`. Still uses the pre-redesign plain-text row treatment.

A CE-precedent check confirmed "Virtual Tags" is genuine CE-parity naming
(`cYo.Projects.ComicRack.Engine.VirtualTag`), but CE's actual implementation differs
substantially: a fixed pool of numbered slots selected via a `ComboBox` (not an add/remove list),
a rich token-builder for the caption format (a `RichTextBox` plus Choose-Value/Prefix/Suffix/
Insert controls), and **no live preview at all**. Paperbunkr's dynamic add/delete list and its
Preview field are both pre-existing deliberate deviations, already shipped — nothing here changes
that; it's context, not a finding that requires action.

Reached via `/grilling` + 2 rounds in the visual companion (row treatment, then a color pass).

## Goals (this phase)

- List rows get a tag icon chip (amber, matching the accent-soft tint already used for Comic
  Folders' rows and Library Health's neutral stat), consistent with the visual language
  established in the other two Library blocks this session.
- Selected row gets an amber border plus a soft amber-tinted background (stronger selection
  affordance than a border alone).
- A disabled tag is shown dimmed (muted chip + muted text) instead of an appended "Off" text
  label — the row's own visual weight communicates the state.
- Empty state: "No virtual tags yet" (green check, same shape as Library Health's/folder
  management's empty states) when the list has zero entries.
- Detail form gets color: "Add Virtual Tag" becomes a primary (filled amber) button instead of
  ghost; "Delete Tag" becomes a red-soft-tinted button (matching Library Health's "Remove All
  Confirmed" treatment) instead of plain ghost-with-red-text; the Preview line becomes a small
  gold-tinted chip (icon + monospace text) instead of a bare `TextBlock`; the Enabled toggle gets
  a small status dot (green when on, muted when off) next to its label.

## Non-goals

- No change to the master/detail *structure* itself (list + inline form below, not an overlay) —
  this is a small 2-field-plus-toggle form, genuinely smaller than the editors that became
  floating overlays elsewhere in the app (Issue Properties, Bulk Editing).
- No delete confirmation added to "Delete Tag" — it stays an immediate, unconfirmed delete,
  consistent with the folder rows' own Remove buttons in the same Library page (also
  unconfirmed today). Adding confirmation to one but not the other would be an arbitrary
  inconsistency; if single-item destructive actions in this section should confirm, that's a
  decision to make uniformly, not something to slip in via one component's visual redesign.
- No change to `VirtualTagTemplateEvaluator`, the caption-format template syntax, or CE-parity
  token support — presentation only.
- Not adopting CE's fixed-slot/ComboBox/RichTextBox-token-builder shape — the existing dynamic
  list + plain-text caption field is a deliberate, already-shipped deviation, not something this
  pass revisits.

## Architecture

### 1. Row treatment

`VirtualTags` `ItemsControl`'s `DataTemplate` (currently a `sideItemButton` with just a name
`TextBlock` and a conditional "Off" `TextBlock`) gains a 24-26px tag icon chip (accent-soft tint,
matching the folder rows' chip sizing) ahead of the name. Selected state (`Id ==
SelectedVirtualTagId`, needs a converter or a per-item computed flag - see ViewModel changes)
adds an amber border + soft amber background to the row's `Border`. Disabled state
(`!IsEnabled`) dims both the chip (muted tint) and the name text instead of appending "Off".

### 2. Empty state

Below the `ItemsControl`, when `VirtualTags.Count == 0`: the same green-check + short-message
shape as the other two Library blocks' empty states.

### 3. Detail form color

Within the existing `IsVisible="{Binding HasSelectedVirtualTag}"` panel: "Add Virtual Tag" moves
from `headerAction ghost` to `headerAction primary`. "Delete Tag" moves from
`headerAction ghost` (with inline red foreground) to a red-soft-tinted treatment (border/text in
`PbDangerBrush` at reduced opacity - reuses the same visual recipe as Library Health's "Remove All
Confirmed Missing" button, no new brush needed). The Preview `TextBlock` becomes a small chip
(icon + `PbBadgeBrush`-tinted monospace text, ~gold) inside a soft-tinted `Border` — reuses
`PbBadgeSoftBrush` (already added for Library Health's "Currently missing" stat chip). The Enabled
row gains a small 8px status dot (`PbSuccessBrush` when on, muted when off) before the "Enabled"
label.

## ViewModel changes (summary)

- `VirtualTagSummary` (the row model) needs an `IsSelected`-style bindable flag, or the row's
  `DataTemplate` needs a converter comparing `Id` against `SelectedVirtualTagId` — simplest is
  adding a computed `IsSelected` bool to `VirtualTagSummary` set by `RefreshVirtualTags`/
  `SelectVirtualTag` (whichever row matches `SelectedVirtualTagId`), avoiding a multi-value
  converter for a single comparison. `SelectVirtualTag`/`AddVirtualTag`/`DeleteVirtualTag`
  (whichever changes `SelectedVirtualTagId`) update every row's `IsSelected` after the change.
- New: `HasVirtualTags` (`VirtualTags.Count > 0`), notified from `RefreshVirtualTags` (same
  pattern as `HasWatchedFolders`/`HasBookFolders` from the folder management redesign).
- No other command/property changes - `AddVirtualTagCommand`, `DeleteVirtualTagCommand`,
  `SelectVirtualTagCommand`, `VirtualTagName`/`VirtualTagCaptionFormat`/`VirtualTagIsEnabled`/
  `VirtualTagPreview` all keep their exact current behavior.

## Testing

- `PreferencesScreenViewModelTests`: new case for `HasVirtualTags` reflecting list state (add/
  delete a tag). New case: selecting a tag marks only that row's `IsSelected` true, and switching
  selection updates both the old and new row correctly.
- Manual on-screen pass (standing no-unattended-GUI caveat): empty list shows the prompt; adding a
  tag shows it selected with the amber-tinted row; disabled tags read as dimmed, not "Off"-labeled;
  Delete Tag's red-soft styling reads correctly; the Preview chip updates live as the Caption
  Format field changes.

## Deliverable

No new `SettingsRow`s (same reasoning as the other two Library-section phases — this stays a
dynamic list, not static settings rows) — nothing to add to
`docs/preferences-descriptions-todo.md`.
