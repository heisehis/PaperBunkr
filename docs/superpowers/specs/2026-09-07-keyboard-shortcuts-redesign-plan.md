# Keyboard Shortcuts Redesign — Implementation Plan
*Implements: [docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md](2026-09-07-keyboard-shortcuts-redesign-design.md)*

Real current shapes confirmed by direct read before planning: `KeyBinding.cs` (single `CommandId`+
`Key` string, no navigation properties), migration `20260809031940_AddKeyBindings.cs` (confirms the
**unique index on `CommandId` alone**, not just an application-level assumption), `KeyBindingService.cs`
(60 lines, `GetKey`/`SetKey`/`GetAllBindings`), `KeyBindingIO.cs` (flat JSON list, `SetKey`-per-entry
import), `KeyBindingRowViewModel.cs` (single `SelectedKey` `[ObservableProperty]`),
`KeyboardShortcutsSection.axaml` (3 identical `ItemsControl` blocks, hardcoded `#D96C6C` conflict
banner), and `PreferencesScreenViewModel.cs` lines 704-803 (`RefreshKeyBindings`/`ImportKeyBindings`/
`ExportKeyBindings`/`RecomputeKeyBindingConflict`). All existing test coverage for this area lives in
`KeyBindingServiceTests.cs` (7 tests, all single-gesture assertions) and inline in
`PreferencesScreenViewModelTests.cs` (lines 950-1096, 8 tests spanning load/import/export/conflict).

## Step 1: EF migration — allow multiple rows per `CommandId`

**Files:** `src/Paperbunkr.Data/Migrations/<timestamp>_AddMultipleKeyBindingsPerCommand.cs` (new,
via `dotnet ef migrations add`), `src/Paperbunkr.Data/Migrations/PaperbunkrDbContextModelSnapshot.cs`
(auto-updated by the same command).

**What:** Drop `IX_KeyBindings_CommandId` (unique), create a new index on `(CommandId, Key)`
(unique). `Down()` drops only the new composite index — per this project's standing migration-safety
rule, it does **not** recreate the old single-column unique index, since that could fail against
real post-migration data (a command with 2+ bindings). Generate via:
```bash
dotnet ef migrations add AddMultipleKeyBindingsPerCommand --project src/Paperbunkr.Data --startup-project src/Paperbunkr.App
```
then hand-edit the generated `Down()` to the safe no-op-ish form described above (the scaffolded
`Down()` will otherwise try to recreate the old unique index — replace that with just dropping the
new one). Never re-scaffold `20260809031940_AddKeyBindings.cs` itself.

**Depends on:** none
**Verify:** `dotnet ef database update --project src/Paperbunkr.Data --startup-project src/Paperbunkr.App`
against a scratch/test DB applies cleanly; inspect the generated migration file's `Up()`/`Down()` by
hand before moving on.

## Step 2: `KeyBindingService` — list-valued API

**Files:** `src/Paperbunkr.App/Services/KeyBindingService.cs` (edit)

**What:**
- `GetKey(commandId)` / `GetKey(context, commandId)` → `GetKeys(commandId)` / `GetKeys(context,
  commandId)` returning `IReadOnlyList<KeyGesture>`: query `context.KeyBindings.Where(k =>
  k.CommandId == commandId)` (not `FirstOrDefault`); zero rows → `[descriptor.DefaultGesture]`;
  one-or-more rows → each row's gesture (skipping any that fail `KeyGesture.Parse`, same
  stale/corrupt-row fallback philosophy as today — if *every* stored row is unparseable, fall back
  to `[descriptor.DefaultGesture]` the same way zero rows does).
- `SetKey` → `AddKey(commandId, gesture)`: no-ops if a row with that exact `(CommandId,
  gesture.ToString())` already exists (mirrors the new unique index — checked in code first so the
  no-op doesn't rely on catching a DB constraint exception); otherwise inserts a new row.
- New `RemoveKey(commandId, gesture)`: deletes the matching row if present (no-op if not found).
- New `ResetToDefaults()`: `context.KeyBindings.RemoveRange(context.KeyBindings); context.SaveChanges();`.
- `GetAllBindings()` → `IReadOnlyList<(KeyboardCommandDescriptor Command, IReadOnlyList<KeyGesture> Keys)>`.

**Depends on:** Step 1
**Verify:** `KeyBindingServiceTests.cs` (Step 6 rewrites these) must pass.

## Step 3: `KeyBindingRowViewModel` — chip list

**Files:** `src/Paperbunkr.App/ViewModels/KeyBindingRowViewModel.cs` (edit)

**What:**
- Constructor signature changes from `(command, KeyGesture currentGesture, service, onChanged)` to
  `(command, IReadOnlyList<KeyGesture> currentGestures, service, onChanged)`. For each gesture,
  resolve to a `KeyOption` the same way today's single-gesture constructor does (curated match, or a
  synthetic fallback `KeyOption` for an unrecognized stored gesture) and populate
  `BoundKeys` (`ObservableCollection<KeyOption>`, replaces `SelectedKey`).
- `AddKeyCommand` (`[RelayCommand]`, takes `KeyOption`): no-ops if `BoundKeys.Contains(option)`;
  otherwise `_service.AddKey(CommandId, option.Gesture)`, `BoundKeys.Add(option)`,
  `OnPropertyChanged(nameof(AvailableKeyOptions))`, `_onChanged()`.
- `RemoveKeyCommand` (`[RelayCommand]`, takes `KeyOption`): no-ops if `BoundKeys.Count <= 1`
  (defense in depth — the View also hides the remove control in this case, per the design's
  never-zero rule); otherwise `_service.RemoveKey(...)`, `BoundKeys.Remove(option)`,
  `OnPropertyChanged(nameof(AvailableKeyOptions))`, `_onChanged()`.
- New `AvailableKeyOptions` (computed `IReadOnlyList<KeyOption>`): `KeyOptions.All.Where(o =>
  !BoundKeys.Contains(o)).ToList()` — no stored backing field, recomputed on each access; manual
  `OnPropertyChanged` calls above are what make the UI's trailing ComboBox re-filter after every
  add/remove (Avalonia doesn't auto-recompute a property from an unrelated collection's own
  `CollectionChanged`).
- New `CanRemove(KeyOption)` no longer needed as a separate method — the View binds the remove
  button's `IsVisible` directly to `BoundKeys.Count` via a small converter (Step 5).

**Depends on:** Step 2
**Verify:** compiles; behavior verified through `PreferencesScreenViewModelTests` (Step 7) since this
class has no dedicated test file today and doesn't need one introduced for a change this size — its
only two real states (no-op vs. mutate) are already exercised through the ViewModel's existing
integration-style tests.

## Step 4: `KeyBindingIO` — multi-entry-per-command import/export

**Files:** `src/Paperbunkr.App/Services/KeyBindingIO.cs` (edit)

**What:**
- `Export`: `service.GetAllBindings()` now returns a list of gestures per command —
  `.SelectMany(b => b.Keys.Select(k => new ExportedBinding(b.Command.Id, k.ToString())))` instead of
  one `Select`. A command with 2 bound gestures exports 2 JSON objects sharing the same `CommandId`
  (no format/schema change — `ExportedBinding` record is untouched).
- `Import`: group the deserialized list by `CommandId` (`.GroupBy(e => e.CommandId)`); for each
  group whose id is in `knownIds`, first remove every existing `KeyBinding` row for that command
  (new `KeyBindingService.ReplaceKeys(commandId, IEnumerable<KeyGesture>)` — see below — or do it
  inline as `RemoveKey` for each of `GetKeys(commandId)` followed by `AddKey` per valid entry in the
  group; either way, only entries that parse count toward `applied` and a `KeyGesture.Parse`
  failure on one entry in a group doesn't block the others in the same group, preserving today's
  per-entry tolerance). Simplest: add `KeyBindingService.ReplaceKeys(string commandId,
  IReadOnlyList<KeyGesture> gestures)` (deletes existing rows for that command, inserts the given
  ones) and have `Import` call it once per group with the successfully-parsed gestures from that
  group (unparseable entries within a group are dropped before the call, still counted individually
  toward the `applied` total for parity with today's per-entry counting).

**Depends on:** Step 2 (add `ReplaceKeys` there alongside the others)
**Verify:** covered by the rewritten `ExportThenImportKeyBindings_RoundTripsARemappedBinding` and
`ImportKeyBindings_WithOneCorruptEntry_StillAppliesTheValidOnes` (Step 7), plus one new multi-binding
round-trip case.

## Step 5: `KeyboardShortcutsSection.axaml` — chips, caption headers, conflict tint, Reset button

**Files:** `src/Paperbunkr.App/Views/Preferences/KeyboardShortcutsSection.axaml` (edit)

**What:**
1. **Group headers:** replace all 3 `Border.groupBox` + `Border.groupHeader` wrappers with a plain
   `StackPanel` and a caption-style `TextBlock` (uppercase, `PbTextFaintBrush`, no border/card) —
   same visual recipe the main tile-hub design doc's §2 "Grouping" section specifies and Virtual
   Tags/Library Health already use elsewhere in Preferences.
2. **Row template** (currently duplicated 3× per group — keep 3× since each `ItemsControl` binds a
   different collection, but make each instance's template identical, extracted to one
   `UserControl.Resources` `DataTemplate x:Key="KeyBindingRowTemplate" x:DataType="vm:KeyBindingRowViewModel"`
   referenced by all 3 `ItemsControl.ItemTemplate="{StaticResource KeyBindingRowTemplate}"` — removes
   the current copy-paste-3× duplication as a real, in-scope cleanup, same reasoning as Connections'
   template consolidation):
   ```
   Label (TextBlock, unchanged)
   WrapPanel:
     - one chip Border per BoundKeys entry (text + a small "✕" Button,
       Command="{Binding #Row.((vm:PreferencesScreenViewModel)DataContext)...}" — no, RemoveKeyCommand
       lives on the row itself: Command="{Binding RemoveKeyCommand}" CommandParameter="{Binding}"
       inside a nested DataTemplate keyed on the KeyOption, IsVisible bound to a converter comparing
       the parent row's BoundKeys.Count > 1)
     - a trailing ComboBox, ItemsSource="{Binding AvailableKeyOptions}", PlaceholderText="Add shortcut…",
       SelectedItem two-way bound to a small per-row transient property OR simplest: handle
       SelectionChanged in code-behind to call AddKeyCommand.Execute(selected item) then reset
       SelectedIndex to -1 (matches "immediate commit, no separate confirm" - see note below)
   ```
   **Binding note:** the chip's remove button needs access to both the `KeyOption` (its
   `CommandParameter`) and the parent row's `RemoveKeyCommand` — since the chip's own `DataContext`
   inside the `WrapPanel`'s `ItemsControl` (bound to `BoundKeys`) is the `KeyOption`, not the row,
   the remove `Button.Command` binds via `{Binding $parent[ItemsControl].((vm:KeyBindingRowViewModel)DataContext).RemoveKeyCommand}`
   (walking up to the row-level `ItemsControl` whose own `DataContext` is the row) — mirrors the
   existing `#Root.((vm:...)DataContext).X` indirection pattern used elsewhere in this codebase, just
   via `$parent[Type]` instead of a named element since there's no natural single root name at this
   nesting depth.
3. **Conflict tint:** row's outer `Border` (wrapping Label + WrapPanel) gets
   `Classes.ksConflict="{Binding IsConflicted}"`, a new local style:
   ```xml
   <Style Selector="Border.ksConflict">
       <Setter Property="BorderBrush" Value="{DynamicResource PbDangerBrush}" />
       <Setter Property="Background" Value="{DynamicResource PbDangerSoftBrush}" />
   </Style>
   ```
4. **Conflict banner:** replace both `Foreground="#D96C6C"` occurrences with
   `{DynamicResource PbDangerBrush}`.
5. **Reset to Defaults button:** third `Button Classes="headerAction ghost"` next to Import/Export,
   `Command="{Binding ResetKeyBindingsCommand}"`, `AutomationProperties.AutomationId="ResetKeyBindingsButton"`.

**Depends on:** Steps 2-3 (needs `BoundKeys`/`AvailableKeyOptions`/`AddKeyCommand`/`RemoveKeyCommand`
to exist), Step 6 (needs `ResetKeyBindingsCommand`)
**Verify:** `dotnet build` (existing `.axaml`/`x:Class`, not a new view — the AVLN2000 fresh-view
gotcha doesn't apply, but if a compile error occurs mid-edit and a later build reports "0 Errors"
suspiciously, force `CoreCompile` stale per `CLAUDE.md` before trusting it: delete
`src/Paperbunkr.App/obj/Debug/net10.0/Paperbunkr.App.dll`/`.pdb` and rebuild). Manual on-screen pass
per Step 8.

## Step 6: `PreferencesScreenViewModel` — Reset command, generalized conflict check

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit, lines ~704-803)

**What:**
- `RefreshKeyBindings()`: `_keyBindingService.GetAllBindings()` now yields list-valued tuples;
  `new KeyBindingRowViewModel(command, currentKeys, ...)` (plural).
- New `[RelayCommand] private void ResetKeyBindings()`: `_keyBindingService.ResetToDefaults();
  RefreshKeyBindings(); _showToast("Keyboard shortcuts reset", "Every shortcut is back to its
  default.");` — mirrors `ImportKeyBindings`'s existing refresh-then-toast shape.
- `RecomputeKeyBindingConflict()`: reset every row's `IsConflicted = false` first
  (`all.ForEach(r => r.IsConflicted = false)` or a plain loop). Nested loop becomes: for each pair
  `(a, b)`, for each `gestureA` in `a.BoundKeys`, for each `gestureB` in `b.BoundKeys` — same
  equality + `Always`/context-compatibility check as today; on a match, set
  `a.IsConflicted = b.IsConflicted = true` and, only if `KeyBindingConflictError` is still null
  (first-found-wins for the single banner line, matching today's early-return behavior for the
  *message*), set it — but do **not** `return` out of the loop, so every row touching any conflict
  still gets `IsConflicted = true` even after the first message is recorded.

**Depends on:** Steps 2-3
**Verify:** Step 7's rewritten/new tests.

## Step 7: Rewrite and extend tests

**Files:** `src/Paperbunkr.App.Tests/KeyBindingServiceTests.cs` (edit),
`src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit, lines 950-1096)

**What:**

`KeyBindingServiceTests.cs` — rewrite all 7 existing tests for the list-valued API
(`GetKey`→`GetKeys`, assert `Assert.Equal([gesture], service.GetKeys(id))` for the single-binding
cases; `SetKey`→`AddKey`). Add:
- `AddKey_CalledTwiceWithSameGesture_DoesNotDuplicate` (replaces
  `SetKey_CalledTwice_UpdatesExistingRow_DoesNotDuplicate` — new semantics: calling `AddKey` twice
  with the *same* gesture no-ops the second time; calling it with a *different* gesture appends a
  second row instead of replacing).
- `AddKey_DifferentGesture_AddsSecondBinding_BothReturnedByGetKeys`.
- `RemoveKey_DeletesMatchingRow`.
- `ResetToDefaults_ClearsEveryRow_GetAllBindingsReturnsRegistryDefaults`.
- `ReplaceKeys_ClearsExistingThenAddsGiven`.

`PreferencesScreenViewModelTests.cs` (lines 950-1096) — every `row.SelectedKey = row.Options.Single(o
=> ...)` becomes a two-call replace: `row.AddKeyCommand.Execute(newOption);
row.RemoveKeyCommand.Execute(oldOption);` (where `oldOption` is captured as `row.BoundKeys.Single()`
before the add, since a fresh row always starts with exactly one). Every `r.SelectedKey.Gesture`
assertion becomes `r.BoundKeys.Single().Gesture` (still exactly one, since no test in this file adds
a second binding without also removing the first). Add:
- `AddKeyCommand_SecondGesture_BothPersistAndBothReturnedOnReload` — a genuine multi-binding case:
  add a second gesture to `ReaderPageTurnLeft` *without* removing the default, reload
  (`RefreshKeyBindings`-equivalent via a fresh `EnsureLoaded` on a new VM instance against the same
  DB), assert both gestures come back.
- `RemoveKeyCommand_LastRemainingGesture_NoOps` — call `RemoveKeyCommand` on a row's only bound
  `KeyOption`, assert `BoundKeys.Count` is still 1 and unchanged.
- `TwoRowsSharingAGesture_BothMarkedIsConflicted_AndClearingOneUnmarksBoth`.
- `RowConflictingWithTwoOthers_AllThreeMarkedIsConflicted` — exercises the "don't return early"
  change from Step 6.
- `ResetKeyBindingsCommand_RevertsEveryRowToDefault_AndClearsConflictError`.
- `ImportKeyBindings_MultipleEntriesForSameCommand_AppliesBothAsSeparateBindings` — a JSON fixture
  with 2 objects sharing one `CommandId`, assert the row ends up with both gestures in `BoundKeys`.

**Depends on:** Steps 1-6
**Verify:** `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter
"FullyQualifiedName~KeyBindingServiceTests|FullyQualifiedName~PreferencesScreenViewModelTests"` — all
pass (per the project's own full-suite-flake note, use this targeted filter, not the whole suite).

## Step 8: Build, avalonia-pro-max review pass, manual on-screen verification

**Files:** none (verification only)

**What:**
1. `dotnet build` — 0 errors. Apply the force-stale-`CoreCompile` workaround (delete `obj/.../
   Paperbunkr.App.dll`/`.pdb`, rebuild) if any interim build in Step 5 failed on XAML before this
   final one succeeds, per `CLAUDE.md`'s standing gotcha.
2. `dotnet ef database update` against the real dev DB only if/when the user wants to try it live —
   otherwise leave the migration unapplied to the shared dev DB until they've reviewed it, per the
   project's worktree/shared-DB caution (this session isn't in a worktree, but the same care about
   not surprising a running app with a schema change applies).
3. Review-checklist subset (not full-app audit): no hardcoded hex left in the conflict banner; the
   Reset/Import/Export buttons all share the same `headerAction ghost` class; the conflict tint
   pairs with the existing banner text (not color-alone); the remove ✕ has
   `AutomationProperties.Name` (icon-only control).
4. Manual on-screen pass (standing no-unattended-GUI caveat — hand off to the user): add a second
   shortcut to a command and confirm both chips show and survive a reload; trigger a conflict across
   two different groups and confirm both rows tint (not just the banner); confirm the ✕ disappears
   on a single-chip row; click Reset to Defaults and confirm every row reverts and any conflict
   banner clears; Import a previously-exported single-binding-per-command file and confirm it still
   works exactly as before.

**Depends on:** Steps 1-7
**Verify:** build + targeted test filter green; manual pass confirms all 5 design goals plus the
bug-free import backward-compatibility claim.
