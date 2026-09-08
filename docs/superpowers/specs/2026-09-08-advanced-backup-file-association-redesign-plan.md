# Advanced: Backup Manager + File Association Redesign — Implementation Plan
*Implements: [docs/superpowers/specs/2026-09-08-advanced-backup-file-association-redesign-design.md](2026-09-08-advanced-backup-file-association-redesign-design.md)*

Real current shapes confirmed by direct read before planning: `AdvancedSection.axaml` (159 lines,
full file already read during brainstorming), `BackupRowViewModel.cs` (59 lines, `RestoreLabel`/
`Restore`/`ConfirmWindow`(3s)/`CancelPendingConfirm`), `BackupService.cs` (already read in full —
`GetAvailableBackups()` returns paths newest-first via filename-string ordering, no size/date API of
its own), `FileAssociationSummary.cs` (4 lines, `Name`/`ExtensionList`/`IsAssociated`),
`FileAssociationService.cs` (`GetAvailableFormats()`), and `PreferencesScreenViewModel.cs` lines
2095-2169 (`RefreshBackups`, `OnRestoreBackupConfirmed` — already sets a post-restore `BackupStatus`
message mentioning the restart requirement; the design's new "requires a restart" note is an
*always-visible* addition alongside this, not a replacement for it — property change handlers for
all 4 backup settings). **Byte-size formatting already exists** — `StatusBarViewModel.FormatBytes`
(private static, `StatusBarViewModel.cs:102-117`, MB/GB precision) — but it's private to an
unrelated file. Rather than extracting a shared utility (expanding this phase's blast radius into a
file that has nothing to do with Backup Manager), `BackupRowViewModel` gets its own small duplicate,
matching this codebase's established tolerance for small local static helpers (e.g.
`SkinService.Brush`, `KeyBindingRowViewModel`'s `AnyMatches`-style local helpers). **No existing
`BackupRowViewModelTests.cs`** — confirmed via glob, this class has zero test coverage today.

## Step 1: `BackupRowViewModel` — display properties + armed state

**Files:** `src/Paperbunkr.App/ViewModels/BackupRowViewModel.cs` (edit)

**What:**
- New `DisplayDate` (`string`, computed once in the constructor from `File.GetLastWriteTime(filePath)`,
  formatted `"MMM d, yyyy — h:mm tt"`, e.g. "Sep 7, 2026 — 8:39 PM").
- New `DisplaySize` (`string`, computed once from `new FileInfo(filePath).Length` via a small private
  static `FormatBytes(long bytes)` — same MB/GB-precision shape as `StatusBarViewModel.FormatBytes`,
  duplicated locally per the reasoning above, not extracted).
- New `[ObservableProperty] private bool _isArmed;` set `true` when `Restore()` enters its
  "Confirm restore?" state and `false` in `CancelPendingConfirm()` (covers both the timer-revert and
  the eventual successful-restore path, since `CancelPendingConfirm()` already runs in both).
- `RestoreLabel`'s two literal strings ("Restore"/"Confirm restore?") stay exactly as-is — `IsArmed`
  is a parallel flag for styling, not a replacement for the label text.

**Depends on:** none
**Verify:** Step 6's new `BackupRowViewModelTests`.

## Step 2: `AdvancedSection.axaml` — File Association restyle

**Files:** `src/Paperbunkr.App/Views/Preferences/AdvancedSection.axaml` (edit)

**What:** Replace the `Border.groupBox Tag="advanced.fileAssociation"` wrapper with
`StackPanel Tag="advanced.fileAssociation"` + `TextBlock Classes="settingsGroupCaption" Text="FILE ASSOCIATION"`.
Row `DataTemplate`: replace the `CheckBox` with a row shape matching this file's own established
`sideItemButton`-adjacent pattern — a `Grid ColumnDefinitions="Auto,*,Auto"` containing a small icon
chip (`Border` + `fi:SymbolIcon Symbol="Document"`), the `Name`/`ExtensionList` text stack, and a
`ToggleSwitch IsChecked="{Binding IsAssociated, Mode=OneWay}"`. The toggle itself needs to invoke
`ToggleFileAssociationCommand`/`CommandParameter="{Binding}"` per row — `ToggleSwitch` has no
`Command` property, so wrap the whole row (or just the toggle) in a transparent `Button` carrying
the `Command`/`CommandParameter`, matching how the *current* `CheckBox`'s own `Command` already
achieves per-row dispatch in a repeater (same mechanism, relocated onto the new visual shape, per
the design doc's own note on this).

**Depends on:** none
**Verify:** `dotnet build`; manual pass in Step 7.

## Step 3: `AdvancedSection.axaml` — Backup Manager's 5 fixed fields → `SettingsRow`

**Files:** `src/Paperbunkr.App/Views/Preferences/AdvancedSection.axaml` (edit)

**What:** Inside `StackPanel Tag="advanced.backup"` (replacing the `Border.groupBox` wrapper) +
`TextBlock Classes="settingsGroupCaption" Text="BACKUP"`, add 5 `pref:SettingsRow`s in place of the
current ad-hoc `TextBlock`+control pairs, each reusing its exact existing binding/command
unchanged:
1. `Icon="FolderOpen" Title="Backup location"` — content: `TextBox Text="{Binding BackupLocation}" IsReadOnly="True" Width="280"` + `Button Command="{Binding BrowseBackupLocationCommand}"` (keep the existing "Browse…" icon+label content).
2. `Icon="Archive" Title="Backups to keep"` — content: `NumericUpDown Value="{Binding BackupsToKeep}" Minimum="0" Maximum="99" Width="120"`.
3. `Icon="History" Title="Automatically back up on startup and shutdown"` — content: `ToggleSwitch IsChecked="{Binding AutoBackupEnabled}"`.
4. `Icon="Clock" Title="Minimum hours between automatic backups" IsVisible="{Binding AutoBackupEnabled}"` — content: `NumericUpDown Value="{Binding AutoBackupMinIntervalHours}" Minimum="1" Maximum="168" Width="120"`.
5. `Icon="Save" Title="Backup now" Description="{Binding BackupStatus}"` — content: the existing primary `Button` (icon+"Backup Now" label, `Command="{Binding BackupNowCommand}"`).

No ViewModel changes — `BackupLocation`/`BackupsToKeep`/`AutoBackupEnabled`/
`AutoBackupMinIntervalHours`/`BackupStatus`/`BrowseBackupLocationCommand`/`BackupNowCommand` are all
reused exactly as today.

**Depends on:** none (independent of Step 2, both land in the same file)
**Verify:** `dotnet build`; manual pass in Step 7.

## Step 4: `AdvancedSection.axaml` — backup file list: date/size, danger-armed Restore, restart note

**Files:** `src/Paperbunkr.App/Views/Preferences/AdvancedSection.axaml` (edit)

**What:**
1. New local style: `Style Selector="Button.restoreArmed" { BorderBrush: PbDangerBrush; Foreground: PbDangerBrush }`.
2. Backup-row `DataTemplate` (`x:DataType="vm:BackupRowViewModel"`): replace the monospace `FileName`
   `TextBlock` with `Text="{Binding DisplayDate}"` plus a smaller trailing
   `TextBlock Text="{Binding DisplaySize}" Foreground="{DynamicResource PbTextFaintBrush}" FontSize="11" Margin="8,0,0,0"`.
   Restore `Button` gains `Classes.restoreArmed="{Binding IsArmed}"`.
3. Below the `ItemsControl` (still inside `Tag="advanced.backup"`), add:
   `TextBlock Text="Restoring a backup requires restarting Paperbunkr to take effect." FontSize="11.5" Foreground="{DynamicResource PbTextFaintBrush}" Margin="0,8,0,0" IsVisible="{Binding Backups.Count}"`
   — `IsVisible` bound directly to the collection's `Count` (Avalonia truthiness treats a non-zero
   int as visible... **verify this actually works**; if `IsVisible` requires a real `bool` and
   doesn't accept an `int` via implicit truthy conversion, bind instead to a small new
   `[ObservableProperty] private bool _hasBackups` on `PreferencesScreenViewModel`, set in
   `RefreshBackups()` alongside the collection rebuild — check `Backups.Count > 0` there. Prefer
   the existing `HasWatchedFolders`/`HasVirtualTags`-style computed-bool pattern this codebase
   already uses elsewhere over relying on int-to-bool coercion if that turns out not to work.

**Depends on:** Step 1 (needs `DisplayDate`/`DisplaySize`/`IsArmed` to exist)
**Verify:** `dotnet build`; manual pass in Step 7.

## Step 5: `PreferencesScreenViewModel` — `HasBackups` (only if Step 4's IsVisible check needs it)

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit, only if needed)

**What:** If Step 4 determined `IsVisible="{Binding Backups.Count}"` doesn't work directly: add
`[ObservableProperty] private bool _hasBackups;` and set it in `RefreshBackups()`
(`HasBackups = Backups.Count > 0;`, after the `foreach` populates the collection). Skip this step
entirely if the direct binding works.

**Depends on:** none
**Verify:** compiles; covered by Step 6/7.

## Step 6: Tests

**Files:** `src/Paperbunkr.App.Tests/BackupRowViewModelTests.cs` (new)

**What:** First test file for this class. Cases:
- `DisplayDate_ReflectsFileLastWriteTime` — create a temp file, set its `LastWriteTime` explicitly,
  construct a `BackupRowViewModel`, assert `DisplayDate` matches the expected formatted string.
- `DisplaySize_ReflectsFileLength` — a temp file with known byte content, assert `DisplaySize`
  shows the right MB value (or "0 MB" for an empty/tiny file, matching `FormatBytes`'s own `<= 0`
  floor behavior... actually a real small file is `> 0` bytes, so assert it rounds to "0 MB" for a
  file under ~500KB, matching `{mb:0} MB"`'s rounding, not the zero-guard branch).
- `IsArmed_FalseInitially_TrueAfterFirstRestoreClick_FalseAfterConfirm` — call `RestoreCommand`
  once, assert `IsArmed == true` and `RestoreLabel == "Confirm restore?"`; call it again, assert
  `_onRestore` fired and (via `CancelPendingConfirm`) `IsArmed` is back to `false`.
- `IsArmed_RevertsAfterConfirmWindowElapses` — only if easily testable without a real 3-second
  sleep (check whether `ConfirmWindow`/the `DispatcherTimer` are test-seamed at all; if not, skip
  this specific timing case rather than adding a real-time sleep to the test suite — the manual
  pass in Step 7 covers the timer-revert behavior either way).

**Depends on:** Step 1
**Verify:** `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter "FullyQualifiedName~BackupRowViewModelTests"` — all pass.

## Step 7: Build, review-checklist pass, manual verification

**Files:** none (verification only)

**What:**
1. `dotnet build` — 0 errors. If the real `Paperbunkr.App.exe` is running (the MSB3027 file-lock
   error seen earlier this session), ask the user to close it — never force-kill. Per `CLAUDE.md`'s
   standing gotcha, force `CoreCompile` stale (delete `obj/Debug/net10.0/Paperbunkr.App.dll`/`.pdb`)
   before trusting a suspicious "0 Errors" after any interim XAML-compile failure.
2. Review-checklist subset: File Association's toggle+icon row and Backup's 5 `SettingsRow`s use
   only `DynamicResource`/bound values, no hardcoded hex; the danger-armed Restore button pairs
   color with its own text change ("Confirm restore?"), not color alone; the per-row toggle's
   effective hit target stays reasonably sized after wrapping in a transparent `Button`.
3. Manual on-screen pass (standing no-unattended-GUI caveat — hand off to the user): File
   Association rows toggle correctly and reflect real registration state; all 5 Backup fields work
   identically to before, just restyled; backup file rows show a real formatted date and a
   plausible size; clicking Restore once arms the danger-styled confirm state, a second click
   within 3 seconds actually restores (and `BackupStatus` shows its existing post-restore message),
   letting it sit unclicked reverts after 3 seconds; the "requires a restart" note is visible
   whenever the list is non-empty and absent when it's empty.

**Depends on:** Steps 1-6
**Verify:** build + targeted test filter green; manual pass confirms all 6 design goals.
