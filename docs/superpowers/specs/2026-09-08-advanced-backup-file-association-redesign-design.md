# Advanced: Backup Manager + File Association Redesign — Design (Phase 8 of Preferences)

Preferences Tile-Hub Redesign's §4 deferred item 7 — the last 2 of the original 9-item deferred list
besides About (picked up separately). Reached via `/grilling` + one round in the visual companion
(backup-row content, Restore's confirm styling, File Association's row shape, header style).

## Background

Today's `AdvancedSection.axaml`: Rendering and Comic File Metadata already got `pref:SettingsRow`
treatment in Phase 1 (unchanged, out of scope here). File Association and Backup Manager are still
`Border.groupBox` cards.

**File Association**: a plain `CheckBox`-per-row `ItemsControl` over `FileAssociationSummary`
(`Name`/`ExtensionList`/`IsAssociated`), sourced from `FileAssociationService.GetAvailableFormats()`
— which groups `Providers.Readers.GetSourceFormats()` by `Name`. This is the **one remaining
`CheckBox`** anywhere in the redesigned Preferences; every other boolean setting uses `ToggleSwitch`.

**Backup Manager**: a `Border.groupBox` mixing ad-hoc `TextBlock`+control pairs (Backup Location
TextBox+Browse, Backups to Keep NumericUpDown, an Auto-backup `Grid` toggle row + conditional
interval NumericUpDown, a Backup Now button+status) with a dynamic `ItemsControl` of existing
backup files, each row a raw monospace filename (`paperbunkr_backup_20260907_203906.db`) plus a
two-click-confirm Restore button (`BackupRowViewModel.RestoreLabel` flips "Restore" →
"Confirm restore?" with a 3-second revert timer, no color change today).

**Re-classification found during exploration:** the original tile-hub deferral treated all of Backup
Manager as one "doesn't fit `SettingsRow`" unit. That's only true of the backup-file list itself —
Location/Backups-to-Keep/Auto-backup-toggle/interval/Backup-Now are each a small, fixed, known-at-
authoring-time row, exactly what `SettingsRow` is for (the same dividing line Phase 1 itself drew).

**CE-parity check** (`_reference/ComicRackCE`, per the standing rule): CE has real in-app backup
(`BackupManagerOptions`: `Location`, `BackupsToKeep`, `OnStartup`/`OnExit` booleans — no interval-
based scheduling) and real in-app file association (a per-extension `CheckedListBox` plus a shared
elevate-and-relaunch "Associate" action, one ProgID for every comic format rather than per-extension
elevation). **Findings that inform this design:** Paperbunkr's "Backups to Keep" has a direct CE
analog (keep as-is); "Minimum Hours Between Automatic Backups" has no CE equivalent — a deliberate,
already-shipped Paperbunkr deviation from CE's simpler startup/exit-only trigger, not something to
walk back. Paperbunkr's `ToggleFileAssociation` is already a simpler, non-elevating per-extension
toggle (catches failures into a toast) — a reasonable, already-correct deviation from CE's shared-
ProgID/elevation dance; nothing here needs to change to align with CE.

**Format-icon investigation (led to a scope correction):** the app already has a `BrandMark`/
`MarkResolver.ResolveFormat` mechanism used elsewhere for comic-format badges (e.g. "CBZ", "PDF").
Checking whether it could give each File Association row a real per-format icon: the actual `Name`
values `FileAssociationService` produces are CE's own verbose provider-registration labels —
`"eComic (ZIP)"`, `"ZIP Archive"`, `"PDF Document (PDF)"`, `"eComic (RAR)"`, `"RAR Archive"`, etc.
(confirmed via `[FileFormat(...)]` attributes in `Paperbunkr.Engine/IO/Provider/{Readers,Writers}`)
— not the clean tokens `MarkResolver`'s alias table expects. Building a new mapping layer just for
this row's icon is real added scope for a cosmetic detail; not worth it this pass.

## Goals

1. **File Association's `CheckBox` → `ToggleSwitch`**, for consistency with every other boolean
   setting in Preferences. Row template also gains a plain generic document icon (not per-format
   branding, per the investigation above) — `fi:SymbolIcon Symbol="Document"` in a small chip,
   ahead of the Name/extension text.
2. **Backup Manager's fixed fields become real `SettingsRow`s:** Backup Location (icon +
   `TextBox`(read-only)+Browse button as the row content), Backups to Keep (`NumericUpDown`),
   Automatically back up on startup/shutdown (`ToggleSwitch`), Minimum Hours Between Automatic
   Backups (`NumericUpDown`, `IsVisible` bound to the auto-backup toggle, same conditional-visibility
   pattern already used elsewhere e.g. Comic File Metadata's `IsEnabled` rows), Backup Now (button +
   status description). Only the backup-file `ItemsControl` stays a dynamic list below these rows.
3. **Backup file rows show a formatted date + file size**, not the raw filename — e.g.
   "Sep 7, 2026 — 8:39 PM · 42.3 MB" — confirmed via the visual companion over keeping the raw
   monospace filename.
4. **Restore's "Confirm restore?" state gets danger-soft styling** (`PbDangerBrush`/
   `PbDangerSoftBrush` border+text, same recipe as every other destructive-action confirm this
   session) instead of the plain neutral button it uses today — confirmed via the visual companion.
5. **A small "requires a restart" note** appears near the backup list (or per-row, see Architecture)
   — `RestoreBackup`'s own doc comment already states this; nothing in the UI communicates it today.
6. **Both groups' headers move to the `settingsGroupCaption` style**, dropping the `Border.groupBox`
   card — confirmed via the visual companion, and the stronger argument here (vs. Connections, which
   kept its card) is that Rendering and Comic File Metadata sit right above/below in this exact same
   file already using caption style; keeping these two as cards would be visually inconsistent within
   one file, not just with the rest of Preferences.

## Non-goals

- No changes to `BackupService`/`FileAssociationService`'s actual backup/restore/registration logic
  — presentation and information architecture only, except Goal 3/5's new read-only display data
  (date/size), which is computed from already-available file metadata, not a new persisted field.
- No per-format branded icons (per the investigation above) — a plain generic icon only.
- No elevation/UAC flow for file association, matching CE's own real behavior conceptually but not
  literally — Paperbunkr's existing simpler non-elevating toggle is kept exactly as-is.
- No change to "Minimum Hours Between Automatic Backups" or any other auto-backup *behavior* —
  confirmed as a deliberate, already-correct CE deviation, not a gap.
- No confirmation dialog added anywhere that doesn't already have one — Restore already has its own
  two-click inline confirm; this phase only styles its armed state, doesn't change the interaction.

## Architecture

### 1. `AdvancedSection.axaml` — File Association

Replace `Border.groupBox` + `Border.groupHeader` with `StackPanel Tag="advanced.fileAssociation"` +
`TextBlock Classes="settingsGroupCaption" Text="FILE ASSOCIATION"`. Row template: replace `CheckBox`
with a `Button`-hosted row (same `sideItemButton`-adjacent shape used elsewhere, or a plain `Grid`)
containing a small icon chip (`fi:SymbolIcon Symbol="Document"`), `Name`/`ExtensionList` text, and a
`ToggleSwitch IsChecked="{Binding IsAssociated, Mode=OneWay}"` whose own toggle (not a row-level
`Command`) invokes `ToggleFileAssociationCommand` — matching how every other Preferences
`ToggleSwitch` is wired directly (`IsChecked="{Binding X}"` on a simple bool), except this one still
needs the existing `CommandParameter`-based dispatch since it's a per-row command in a repeater, not
a flat property — so the `ToggleSwitch` binds `IsChecked` `OneWay` and a wrapping clickable row (or
the toggle's own click) still calls `ToggleFileAssociationCommand`/`CommandParameter="{Binding}"`,
the same mechanism the current `CheckBox`'s `Command` already uses, just relocated onto the new
control shape.

### 2. `AdvancedSection.axaml` — Backup Manager fixed fields

Replace the ad-hoc `TextBlock`+control pairs with `pref:SettingsRow`s inside
`StackPanel Tag="advanced.backup"` + `TextBlock Classes="settingsGroupCaption" Text="BACKUP"`:
- `SettingsRow Icon="FolderOpen" Title="Backup location"` — content: the existing read-only
  `TextBox` + Browse `Button`, unchanged bindings (`BackupLocation`, `BrowseBackupLocationCommand`).
- `SettingsRow Icon="Archive" Title="Backups to keep"` — content: the existing `NumericUpDown`
  (`BackupsToKeep`, `Minimum="0" Maximum="99"`).
- `SettingsRow Icon="History" Title="Automatically back up on startup and shutdown"` — content: the
  existing `ToggleSwitch` (`AutoBackupEnabled`).
- `SettingsRow Icon="Clock" Title="Minimum hours between automatic backups" IsVisible="{Binding AutoBackupEnabled}"`
  — content: the existing `NumericUpDown` (`AutoBackupMinIntervalHours`, `Minimum="1" Maximum="168"`).
- `SettingsRow Icon="Save" Title="Backup now" Description="{Binding BackupStatus}"` — content: the
  existing primary `Button` (`BackupNowCommand`).

No ViewModel changes — every bound property/command above is reused exactly as today, only their
View placement/wrapper changes.

### 3. Backup file list: formatted date/size + danger-styled Restore + restart note

`BackupRowViewModel` gains two new computed display properties (built once from `FilePath` in the
constructor, since the file's own metadata doesn't change after listing):
- `DisplayDate` (`string`) — `File.GetLastWriteTime(FilePath).ToString("MMM d, yyyy — h:mm tt")`.
  Uses the file's actual last-write time rather than re-deriving from the filename's embedded
  timestamp (`BackupService.TryParseBackupTimestamp` is `private` and this class has no reason to
  duplicate that parsing — the file's own OS-level write time is equivalent for a file this class
  never modifies after creation, and is simpler to get here).
- `DisplaySize` (`string`) — `new FileInfo(FilePath).Length`, formatted as "42.3 MB" (reuse an
  existing size-formatting helper if one exists in this codebase — e.g. wherever library file sizes
  are already displayed elsewhere; otherwise a small local `FormatBytes` static helper, MB-precision
  is enough here, no need for a general-purpose byte-formatting utility).

`AdvancedSection.axaml`'s backup-row `DataTemplate`: replace the monospace `FileName` `TextBlock`
with `Text="{Binding DisplayDate}"` + a smaller trailing `TextBlock Text="{Binding DisplaySize}"
Foreground="{DynamicResource PbTextFaintBrush}"`. The Restore `Button` gains
`Classes.ksConflict`-style conditional styling (new local class, e.g. `Button.restoreArmed`) bound
to `RestoreLabel != "Restore"` via a small converter (or a new `bool IsArmed` computed property on
`BackupRowViewModel`, simpler than a converter and consistent with how other rows already expose
computed display state) — `BorderBrush="{DynamicResource PbDangerBrush}"` +
`Foreground="{DynamicResource PbDangerBrush}"` when armed, same recipe as every other danger-state
button this session.

**Restart note:** a `TextBlock` below the backup-file `ItemsControl` (or above it, whichever reads
better once implemented), `Text="Restoring a backup requires restarting Paperbunkr to take effect."`,
`Foreground="{DynamicResource PbTextFaintBrush}"`, always visible when the list is non-empty (not
conditional on anything — it's general information about the Restore action, not a live status).

## Testing

- `BackupRowViewModelTests` (new or extended): `DisplayDate`/`DisplaySize` reflect a fixture file's
  real last-write-time/length; `IsArmed` is `false` initially, `true` after the first `Restore`
  click, `false` again after the confirm window elapses or after a completed restore.
- `PreferenceIndexTests`: unaffected — `advanced.fileAssociation`/`advanced.backup` Tags are
  unchanged (only their content restructures, not their anchor), so no entry changes needed here
  (unlike the Appearance phase's Motion/Developer/Install-Skin merges).
- Manual on-screen pass (standing no-unattended-GUI caveat): File Association rows show the toggle
  + generic icon and still correctly reflect/change registration state; Backup Manager's 5 fixed
  rows behave identically to today (just restyled); backup file rows show a real formatted date and
  a plausible size; clicking Restore once shows the danger-styled "Confirm restore?" state, a second
  click within 3 seconds actually restores, and the restart note is visible.

## Deliverable

Backup Manager's 5 relocated rows (Location/Backups to Keep/Auto-backup toggle/interval/Backup Now)
are new `SettingsRow`s with **no** `Description` filled in beyond Backup Now's status binding —
each gets an entry in `docs/preferences-descriptions-todo.md` under Advanced → Backup, per the
tile-hub design's own "every row ships with Description unset, tracked separately" convention.
