# Keyboard Shortcuts Redesign — Design (Phase 6 of Preferences)

Preferences Tile-Hub Redesign's §4 deferred item 5 — "Keyboard Shortcuts — 3 key-binding repeaters"
— following the same phased pattern as Library Health, Library Folder Management, Virtual Tags, and
Connections. Unlike those, this phase also closes a real CE-parity gap (multi-binding) found during
the mandated CE-source check, not just a visual pass — confirmed and scoped via `/grilling` + one
round in the visual companion (header style, conflict highlighting).

## Background

Today's `KeyboardShortcutsSection.axaml`: 27 commands (`KeyboardCommandRegistry.Commands`) split
into 3 static groups (Navigation 17, Zoom & Fit 7, Display 3), each rendered as a flat
`Label / ComboBox` row inside a `Border.groupBox` card. Each row (`KeyBindingRowViewModel`) holds
exactly **one** `SelectedKey` (a curated `KeyOption` from `KeyOptions.All`, ~25 entries — a
deliberate, already-documented deviation from CE's free "press any key" capture widget). A pairwise
conflict check (`RecomputeKeyBindingConflict`) surfaces one shared-gesture message in a banner at
the top of the page, in a hardcoded `#D96C6C` red rather than the app's `PbDangerBrush` token.
Import/Export (JSON, one `{CommandId, Gesture}` object per command) already exists; there is no
Reset to Defaults.

**CE-parity check** (`_reference/ComicRackCE`, per the standing rule): CE's own
`cYo.Common.Windows.Forms.KeyboardShortcutEditor` (embedded in `PreferencesDialog.cs`) uses a
grouped `ListView` (coarse, ad-hoc group strings like `"Library"`/`"Display Options"` — not a richer
taxonomy, so Paperbunkr's 3 groups are already fine as-is) with a side panel of **4 independent key
slots per command** (`KeyboardCommand.NumberOfKeys = 4`, `cbKey1..4` + modifier checkboxes, or a
"press your key combination" capture dialog per slot) — real simultaneous multi-binding, confirmed
via `KeyboardCommand.Handles()` checking all 4. CE has **no conflict detection anywhere** (grepped
the whole `cYo.Common.Windows` folder — zero hits; `KeyboardShortcuts.HandleKey` just invokes
whichever command it finds first, silently shadowing). CE's fixed action roster and whole-layout-
only import/export/reset (`btExportKeyboard_Click`/`btLoadKeyboard_Click`/
`miDefaultKeyboardLayout_Click` — no per-command reset) already match what Paperbunkr does or is
adding here.

**Net finding:** single-binding-per-command was an undocumented deviation from CE (CE supports 4
per command); Paperbunkr's own conflict detection is a deliberate improvement over CE (which has
none). This phase closes the multi-binding gap while keeping the conflict-detection improvement.

## Goals

1. **Group headers** switch from the `Border.groupBox` card to the lighter caption-style label
   (uppercase, no border/background) — more breathing room across 27+ rows, matches the direction
   the rest of Preferences is moving.
2. **Conflict highlighting:** in addition to today's banner text, both rows actually involved in a
   conflict get a red-soft-tinted border/background (reusing the same `PbDangerBrush`/
   `PbDangerSoftBrush` recipe as Virtual Tags/Library Health's other danger treatments), wherever
   they land in their own group — no more hunting through 27 rows to find the two the banner names.
3. **Fix the hardcoded `#D96C6C`** in the conflict banner → `PbDangerBrush`/`PbDangerSoftBrush`.
4. **Multi-binding:** a command can have more than one bound gesture simultaneously (closing the CE-
   parity gap above), with **no fixed cap** — CE's 4-slot limit was an artifact of its fixed-size
   panel UI, not a real product constraint, so Paperbunkr's dynamic chip list drops it deliberately.
5. **Reset to Defaults** button next to Import/Export — clears every stored `KeyBinding` row,
   reverting every command to its registry default gesture. Matches CE's whole-layout restore.

## Non-goals

- No capture-any-key widget — the curated `KeyOptions.All` list stays exactly as-is; this phase adds
  *more than one* curated selection per row, not a different selection mechanism.
- No per-command reset (only the whole-layout button in Goal 5) — CE doesn't have one either.
- **A command can never have zero bound gestures.** The last remaining chip on a row cannot be
  removed (its remove control is hidden/disabled when it's the only one left) — you can still
  *rebind* a single-chip row by adding a new key then removing the old one, or the reverse, but a
  row is never left with nothing. This sidesteps a real ambiguity the current schema has no clean
  way to represent (zero explicit rows already means "never customized, use the registry default" —
  reusing that same empty state to also mean "customized to intentionally nothing" would make those
  two states indistinguishable without a sentinel row, which is more machinery than this feature
  needs).
- No change to `KeyboardCommandRegistry` itself — same 27 commands, same 3 groups, same defaults.
- No version bump to the Import/Export JSON shape — see Architecture §4.

## Architecture

### 1. Schema: multi-row-per-command

`KeyBindings` currently has a **unique index on `CommandId`** (migration
`20260809031940_AddKeyBindings.cs`), which is what actually enforces single-binding today — not
just application code. New migration `AddMultipleKeyBindingsPerCommand`:
- Drop `IX_KeyBindings_CommandId`.
- Create a new unique index on `(CommandId, Key)` — still prevents literally adding the same
  gesture twice to one command, but allows several distinct gestures per command.
- `Down()` drops the new composite index only — it does **not** attempt to recreate the old
  single-column unique index, since doing so could fail against real data once a user has more than
  one binding for a command (the same reasoning as this project's standing no-op-`Down()` rule for
  orphaned-column migrations: a rollback that can fail against legitimate post-migration data is
  worse than a rollback that does less).

### 2. `KeyBindingService`

- `GetKey(commandId)` → `GetKeys(commandId)` returning `IReadOnlyList<KeyGesture>`: zero stored rows
  → `[descriptor.DefaultGesture]` (unchanged fallback semantics); one or more stored rows → exactly
  those, in insertion order.
- `SetKey` → `AddKey(commandId, gesture)` (inserts a new row; no-ops if that exact `(CommandId,
  Key)` pair already exists — mirrors the new unique index) and `RemoveKey(commandId, gesture)`
  (deletes the matching row; the ViewModel enforces the "never zero" rule before calling this, so
  the service itself doesn't need to reject the last-row case, but it's a harmless no-op if
  something ever did call it with only one row left — SQLite just deletes it and the command falls
  back to its registry default until re-bound, which is a reasonable failure mode even if the UI is
  supposed to prevent reaching it).
- New `ResetToDefaults()`: deletes every row in `KeyBindings`.
- `GetAllBindings()` → returns `(KeyboardCommandDescriptor Command, IReadOnlyList<KeyGesture> Keys)`
  tuples instead of a single gesture each.

### 3. `KeyBindingRowViewModel`

- `SelectedKey` (single `KeyOption`) → `BoundKeys` (`ObservableCollection<KeyOption>`), constructed
  from `GetKeys()`'s result the same way today's constructor resolves a stale/unrecognized gesture
  to a synthetic `KeyOption` (per-gesture, not just once).
- `AddKeyCommand(KeyOption)`: calls `_service.AddKey`, appends to `BoundKeys` if not already
  present, calls `_onChanged` (same conflict-recompute hook as today).
- `RemoveKeyCommand(KeyOption)`: no-ops if `BoundKeys.Count == 1` (enforces the "never zero" rule
  from the View side too, not just by hiding the button — defense in depth); otherwise calls
  `_service.RemoveKey`, removes from `BoundKeys`, calls `_onChanged`.
- `AvailableKeyOptions` (computed): `KeyOptions.All` minus `BoundKeys` — feeds the trailing "Add
  shortcut…" `ComboBox` so it never offers a gesture this row already has.
- New `[ObservableProperty] bool _isConflicted` — set by the (generalized) conflict recompute below,
  drives the row's tinted-border style.

### 4. Conflict detection (generalized)

`RecomputeKeyBindingConflict` resets every row's `IsConflicted = false` first, then for each pair of
rows `(a, b)` where `a != b`, checks every gesture in `a.BoundKeys` against every gesture in
`b.BoundKeys` using the exact same `Always`/context-compatibility rule as today. On any match, sets
`a.IsConflicted = b.IsConflicted = true` and records the first such message for the banner (unchanged
single-line banner text) — but does **not** return early, so a row conflicting with more than one
other row (now more likely with multi-binding) still ends up correctly flagged even though only one
message shows.

### 5. Import/Export (`KeyBindingIO`) — no format version bump

The JSON shape stays a flat list of `{CommandId, Gesture}` objects — multiple objects may now share
the same `CommandId`. Export: one object per bound gesture per command (a command with 2 bindings
exports 2 objects). Import: group entries by `CommandId`; for each group, **replace** that command's
entire binding set — delete its existing rows, then `AddKey` each gesture in the file that parses
and is a known command id (same per-entry skip-on-error philosophy as today). A pre-existing
single-binding export (at most one object per `CommandId`) imports identically to today's behavior —
fully backward compatible, no migration of exported files needed.

### 6. View

- **Group headers:** `Border.groupHeader`'s bold `TextBlock` → the caption-style treatment (`TextBlock.pbTextCaption`-equivalent, uppercase, no border/card) — `Border.groupBox` itself is dropped for this section the same way, since the card was only ever there to frame the header.
- **Row template:** two-line layout — `Label` on its own line, then a `WrapPanel` below holding one
  removable chip per `BoundKeys` entry (`Border` + text + a small ✕ `Button`, `IsVisible="{Binding
  #Row.((vm:KeyBindingRowViewModel)DataContext).BoundKeys.Count, Converter=...GreaterThanOne}"` on
  the ✕ — enforcing Goal/Non-goal "never zero" visually) followed by a trailing `ComboBox` bound to
  `AvailableKeyOptions` with a "Add shortcut…" placeholder, whose `SelectionChanged` calls
  `AddKeyCommand` then resets its own selection to null (mirrors today's immediate-commit
  interaction, just appending instead of replacing).
- **Conflict tint:** the row's root `Border` gets `Classes.ksConflict="{Binding IsConflicted}"`
  → `PbDangerSoftBrush` background + `PbDangerBrush` border, same recipe used elsewhere.
- **Conflict banner:** replace the hardcoded `#D96C6C` on both the icon and text with
  `{DynamicResource PbDangerBrush}`.
- **Reset to Defaults:** a third `Button Classes="headerAction ghost"` alongside Import/Export,
  bound to a new `ResetKeyBindingsCommand`. No confirmation dialog — matches this section's existing
  immediate-persist philosophy (every ComboBox change here already applies instantly with no
  Save/Cancel step), and it's a fully recoverable action (Import can restore a previously-exported
  layout).

## ViewModel changes (`PreferencesScreenViewModel`) — summary

- `RefreshKeyBindings()`: unchanged shape, just constructs each `KeyBindingRowViewModel` from
  `GetAllBindings()`'s new list-valued tuples.
- New `ResetKeyBindingsCommand`: calls `_keyBindingService.ResetToDefaults()`, then
  `RefreshKeyBindings()` (same rebuild-from-scratch pattern `ImportKeyBindings` already uses),
  then a toast ("Keyboard shortcuts reset", matching the Import/Export toast style).
- `RecomputeKeyBindingConflict`: generalized per Architecture §4; still the single
  `KeyBindingConflictError`/`HasKeyBindingConflictError` pair for the banner.

## Testing

- `KeyBindingServiceTests`: replace single-gesture assertions with list-based ones; new cases for
  `AddKey` (appends, no-ops on an exact duplicate `(CommandId, Key)` pair), `RemoveKey` (deletes the
  matching row), `ResetToDefaults` (clears the table, `GetAllBindings()` reports every command back
  at its registry default).
- `KeyBindingIOTests` (or wherever existing Import/Export tests live): a multi-binding round-trip
  (export a command with 2 gestures, re-import, confirm both come back); confirm a legacy
  single-entry-per-command file still imports exactly as before.
- `PreferencesScreenViewModelTests`: `IsConflicted` gets set on both rows in a genuine conflict and
  stays false otherwise; a row conflicting with two different other rows ends up `IsConflicted` on
  all three; `ResetKeyBindingsCommand` reverts every row to its default and clears
  `KeyBindingConflictError`.
- `KeyBindingRowViewModelTests` (new or extended): `RemoveKeyCommand` no-ops when `BoundKeys.Count
  == 1`; `AddKeyCommand` is idempotent against an already-present `KeyOption`.
- Manual on-screen pass (standing no-unattended-GUI caveat): add a second shortcut to a command,
  confirm both chips show and both persist across a reload; create a conflict across two different
  groups and confirm both rows tint, not just the banner; confirm the ✕ disappears on a single-chip
  row; Reset to Defaults confirms every row goes back to its shipped default and the conflict banner
  (if any was showing) clears.

## Deliverable

No new `SettingsRow`s — stays a dynamic per-command list, same reasoning as every other Library/
Connections-shaped phase. Nothing to add to `docs/preferences-descriptions-todo.md`.
