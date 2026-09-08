# Automation Task List Redesign — Implementation Plan
*Implements: [docs/superpowers/specs/2026-09-08-automation-tasks-redesign-design.md](2026-09-08-automation-tasks-redesign-design.md)*

Real current shapes confirmed by direct read before planning: `AutomationSection.axaml` (single
`groupBox` card, flat `ItemsControl` over `ScheduledTasks`, no icons, no running/queued rendering);
`Models/ScheduledTaskRow.cs` (required `TaskId`/`DisplayName`/`Description`, `[ObservableProperty]`
`Enabled`/`Mode`/`IntervalHours`/`DailyAtTime`/`LastRunUtc`/`LastRunStatus`/`IsRunning`/`IsQueued`,
computed `IsIntervalMode`/`IsDailyMode`/`LastRunLabel` with matching `OnXChanged` partials — the
pattern this plan's new computed properties mirror); `Services/Scheduling/ScheduledTaskDescriptor.cs`
(`ActivityKind` property, not yet copied onto `ScheduledTaskRow`);
`Services/Scheduling/SchedulerService.cs` lines 315-362 (`RebuildRows()` — the one place
`ScheduledTaskRow` instances are constructed, lines 343-357); `Services/Scheduling/ISchedulerService.cs`
(`Tasks`, `Changed`, `Start`/`Stop`, `RunNowAsync`, `NotifyRan`, `SetEnabled`, `SetSchedule` — no
pause concept); `Views/ActivityConverters.cs` (`ActivityKindIconConverter.Instance`, reusable as-is);
`PreferencesScreenViewModel.cs` lines 2586-2710ish (`ScheduledTasks`, `AttachScheduler`,
`RebuildScheduledTasks` — reuses row instances by `TaskId` so an init-only `ActivityKind` on a
reused row is never stale); `LibrarySection.axaml` (the `Classes="headerAction primary"` "Verify Now"
button placement precedent, and the bordered-card recipe `BorderBrush={DynamicResource
PbBorderBrush} BorderThickness="1" CornerRadius="6" Padding="12,10" Margin="0,0,0,8"` this plan
reuses for task cards). `PreferencesScreenViewModelTests.cs`'s `CreateViewModel` factory never wires
a scheduler — every existing test leaves `ScheduledTasks` empty, so this plan's new tests need their
own fake `ISchedulerService` plus an explicit `AttachScheduler` call.

**Two things the design doc left for this plan to pin down, now decided:**
1. **Run now button's disabled condition**: disable while *either* `IsRunning` or `IsQueued` (not
   just `IsRunning`) — re-clicking a task that's already queued would otherwise risk double-queuing
   the same `TaskId`. A new computed `IsActive` property on `ScheduledTaskRow` (`IsRunning ||
   IsQueued`) backs both this and the card's accent-tint class, avoiding two independent
   `Classes.x` bindings trying to OR together.
2. **Run now button's label** while active: a new computed `RunButtonLabel` property on
   `ScheduledTaskRow` (`"Running…"` / `"Queued"` / `"Run now"`), same computed-property-with-
   `OnXChanged`-hookup shape `LastRunLabel` already uses in this class — no new converter needed.

## Step 1: `ScheduledTaskRow` — `ActivityKind`, `IsActive`, `RunButtonLabel`

**Files:** `src/Paperbunkr.App/Models/ScheduledTaskRow.cs` (edit)

**What:**
- New `public required ActivityJobKind ActivityKind { get; init; }` (init-only, set once at
  construction like `TaskId`/`DisplayName`/`Description` — `Paperbunkr.Data.Entities` is already
  `using`d in this file).
- New computed `public bool IsActive => IsRunning || IsQueued;`.
- New computed `public string RunButtonLabel => IsRunning ? "Running" : IsQueued ? "Queued" : "Run now";`.
- Extend the existing `partial void OnIsRunningChanged`/`OnIsQueuedChanged` (add these two partials —
  currently only `OnModeChanged`/`OnLastRunUtcChanged`/`OnLastRunStatusChanged` exist) to raise
  `OnPropertyChanged(nameof(IsActive))` and `OnPropertyChanged(nameof(RunButtonLabel))`.

**Depends on:** none
**Verify:** compiles; behavior covered by Step 6's tests (constructing a row with mixed
`IsRunning`/`IsQueued` and asserting `IsActive`/`RunButtonLabel`).

## Step 2: `SchedulerService` — populate `ActivityKind`

**Files:** `src/Paperbunkr.App/Services/Scheduling/SchedulerService.cs` (edit, line ~346 inside
`RebuildRows()`'s `new ScheduledTaskRow { ... }`)

**What:** Add `ActivityKind = d.ActivityKind,` to the object initializer (`d` is the
`ScheduledTaskDescriptor` already in scope in that `foreach`).

**Depends on:** Step 1
**Verify:** compiles; a `SchedulerServiceTests` case (if one already asserts row shape) still
passes — otherwise covered transitively by Step 6's `PreferencesScreenViewModelTests` since
`AttachScheduler` pulls rows through this exact path in real usage (the plan's own fake scheduler
in Step 6 bypasses `SchedulerService` entirely, so this line's correctness is really only exercised
by the manual on-screen pass in Step 8 — flag this if a `SchedulerServiceTests` file already covers
`RebuildRows()`'s output shape, and add one assertion there for `ActivityKind` if so).

## Step 3: `PreferencesScreenViewModel` — bulk-action commands

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit, near the existing
`AttachScheduler`/`RebuildScheduledTasks` region around line 2614)

**What:**
- New private field: `private Dictionary<string, bool>? _prePauseEnabledSnapshot;`.
- New computed `public bool IsSchedulerPaused => _prePauseEnabledSnapshot is not null;` — since this
  isn't an `[ObservableProperty]`-backed field, add a manual `OnPropertyChanged(nameof(IsSchedulerPaused))`
  call at both places the snapshot field is set (see below) so the button's label/icon binding
  updates.
- New `[RelayCommand] private void RunAllScheduledTasksNow()`: `if (_scheduler is null) return;
  foreach (var row in _scheduler.Tasks) { _ = _scheduler.RunNowAsync(row.TaskId); }` — iterates
  `_scheduler.Tasks` (the scheduler's own live list), **not** this VM's `ScheduledTasks` cache.
  Found while writing Step 6's tests: `ScheduledTasks` only refreshes via a dispatcher round-trip
  off the scheduler's `Changed` event, so reading it immediately (e.g. right after
  `AttachScheduler`, or synchronously within the same call as a `SetEnabled` that hasn't finished
  its round-trip yet) can see a stale or empty snapshot. `_scheduler.Tasks` has no such lag. Same
  reasoning for `TogglePauseAllScheduledTasks` below. Fire-and-forget per task (matches how a
  single row's own `RunNowCommand` is already an `IAsyncRelayCommand` fired from the UI without the
  VM awaiting it; the scheduler's own queue serializes actual execution).
- New `[RelayCommand] private void TogglePauseAllScheduledTasks()`:
  ```csharp
  if (_scheduler is null) return;
  if (_prePauseEnabledSnapshot is null)
  {
      _prePauseEnabledSnapshot = _scheduler.Tasks.ToDictionary(r => r.TaskId, r => r.Enabled);
      foreach (var row in _scheduler.Tasks) { _scheduler.SetEnabled(row.TaskId, false); }
  }
  else
  {
      foreach (var (taskId, wasEnabled) in _prePauseEnabledSnapshot) { _scheduler.SetEnabled(taskId, wasEnabled); }
      _prePauseEnabledSnapshot = null;
  }
  OnPropertyChanged(nameof(IsSchedulerPaused));
  ```
  `SetEnabled` triggers the scheduler's own `Changed` event → `RebuildScheduledTasks()` → each
  row's `Enabled` reflects the new state without this method touching `ScheduledTasks` directly.

**Depends on:** none (doesn't need Steps 1-2 to compile, but Step 4's XAML binds to these commands)
**Verify:** Step 6's new tests.

## Step 4: `AutomationSection.axaml` — full restructure

**Files:** `src/Paperbunkr.App/Views/Preferences/AutomationSection.axaml` (edit, full rewrite of the
body)

**What:**

1. **Header**: keep `<TextBlock Classes="pbTextHeading" Text="Automation" />` at the top (this
   phase doesn't add an identity header the way About's did — Automation has no version/branding
   content to anchor one).

2. **Notification level → `SettingsRow`**, replacing the current `StackPanel Orientation="Horizontal"`
   + explanatory paragraph:
   ```xml
   <pref:SettingsRow Icon="Alert" Title="Notify when a scheduled task finishes"
                      Description="Maintenance tasks run in the background — on launch for anything overdue, then every 15 minutes while Paperbunkr is open. Up to two run at once. Every run shows in the Activity Center.">
       <pref:SettingsRow.SettingsContent>
           <ComboBox ItemsSource="{x:Static vm:PreferencesScreenViewModel.NotificationLevelOptions}"
                     SelectedItem="{Binding ScheduledTaskNotificationLevel}" MinWidth="150"
                     AutomationProperties.AutomationId="ScheduledTaskNotificationLevel" />
       </pref:SettingsRow.SettingsContent>
   </pref:SettingsRow>
   ```
   (needs `xmlns:pref="using:Paperbunkr.App.Views.Preferences"` added to this file's namespace
   imports — not currently present.) Confirm `Symbol="Alert"` compiles (confirmed to exist on
   `FluentIcons.Common.Symbol` before writing this plan); fall back to `Symbol="Info"` if not.

3. **"SCHEDULED TASKS" group** — caption + bulk-action row + card list, replacing the
   `Border Classes="groupBox"` wrapper entirely:
   ```xml
   <StackPanel Tag="automation.tasks">
       <TextBlock Classes="settingsGroupCaption" Text="SCHEDULED TASKS" />
       <StackPanel Orientation="Horizontal" Spacing="10" Margin="0,0,0,10">
           <Button Classes="headerAction ghost" Command="{Binding RunAllScheduledTasksNowCommand}">
               <StackPanel Orientation="Horizontal" Spacing="6">
                   <fi:SymbolIcon Symbol="Play" FontSize="{StaticResource PbIconSizeXs}" />
                   <TextBlock Text="Run all now" />
               </StackPanel>
           </Button>
           <Button Classes="headerAction ghost" Command="{Binding TogglePauseAllScheduledTasksCommand}">
               <StackPanel Orientation="Horizontal" Spacing="6">
                   <fi:SymbolIcon Symbol="{Binding IsSchedulerPaused, Converter={x:Static views:PauseResumeIconConverter.Instance}}"
                                  FontSize="{StaticResource PbIconSizeXs}" />
                   <TextBlock Text="{Binding IsSchedulerPaused, Converter={x:Static views:PauseResumeLabelConverter.Instance}}" />
               </StackPanel>
           </Button>
       </StackPanel>
       <!-- one card per ScheduledTaskRow, see below -->
   </StackPanel>
   ```
   `xmlns:views="using:Paperbunkr.App.Views"` needs adding for the two new converters (Step 5).
   Two tiny bool-keyed converters rather than one `IMultiValueConverter` since each drives an
   unrelated property (`Symbol` vs `string`) off the same single bool — a `BoolToObject`-style
   generic converter would need two instances configured differently anyway, so two purpose-named
   converters are no more code and clearer at the call site.

4. **Per-task card**, replacing the current `Border BorderBrush=... BorderThickness="0,0,0,1"` flat
   row template:
   ```xml
   <ItemsControl ItemsSource="{Binding ScheduledTasks}">
       <ItemsControl.ItemTemplate>
           <DataTemplate x:DataType="models:ScheduledTaskRow">
               <Border Classes="scheduledTaskCard" Classes.scheduledTaskActive="{Binding IsActive}"
                       BorderBrush="{DynamicResource PbBorderBrush}" BorderThickness="1" CornerRadius="6"
                       Padding="14,12" Margin="0,0,0,10">
                   <Grid ColumnDefinitions="Auto,*,Auto" RowDefinitions="Auto,Auto,Auto">
                       <fi:SymbolIcon Grid.Row="0" Grid.Column="0" Grid.RowSpan="3"
                                      Symbol="{Binding ActivityKind, Converter={x:Static views:ActivityKindIconConverter.Instance}}"
                                      FontSize="{StaticResource PbIconSizeSm}" Foreground="{DynamicResource PbTextMutedBrush}"
                                      VerticalAlignment="Top" Margin="0,2,12,0" />

                       <StackPanel Grid.Row="0" Grid.Column="1" Orientation="Horizontal" Spacing="8">
                           <TextBlock Text="{Binding DisplayName}" FontSize="13" FontWeight="SemiBold"
                                      Foreground="{DynamicResource PbTextBrush}" />
                           <Border Classes="scheduledTaskBadge" IsVisible="{Binding IsRunning}">
                               <TextBlock Text="Running…" FontSize="10.5" Foreground="{DynamicResource PbAccentBrush}" />
                           </Border>
                           <Border Classes="scheduledTaskBadge" IsVisible="{Binding IsQueued}">
                               <TextBlock Text="Queued" FontSize="10.5" Foreground="{DynamicResource PbAccentBrush}" />
                           </Border>
                       </StackPanel>
                       <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding Description}" FontSize="11.5"
                                  Foreground="{DynamicResource PbTextFaintBrush}" TextWrapping="Wrap" Margin="0,2,0,8" />

                       <StackPanel Grid.Row="2" Grid.Column="1" Orientation="Horizontal" Spacing="8">
                           <ComboBox ItemsSource="{x:Static vm:PreferencesScreenViewModel.ScheduleModeOptions}"
                                     SelectedItem="{Binding Mode}" MinWidth="110" />
                           <NumericUpDown Value="{Binding IntervalHours}" Minimum="1" Maximum="720" Width="100"
                                          IsVisible="{Binding IsIntervalMode}" />
                           <TextBlock Text="hours" VerticalAlignment="Center" IsVisible="{Binding IsIntervalMode}"
                                      FontSize="12" Foreground="{DynamicResource PbTextFaintBrush}" />
                           <TimePicker SelectedTime="{Binding DailyAtTime}" ClockIdentifier="24HourClock"
                                       IsVisible="{Binding IsDailyMode}" />
                           <TextBlock Text="{Binding LastRunLabel, StringFormat='Last run: {0}'}"
                                      FontSize="11" Foreground="{DynamicResource PbTextFaintBrush}" Margin="12,0,0,0" VerticalAlignment="Center" />
                           <TextBlock Text="{Binding NextRunLabel, StringFormat='Next: {0}'}"
                                      FontSize="11" Foreground="{DynamicResource PbTextFaintBrush}" VerticalAlignment="Center" />
                       </StackPanel>

                       <StackPanel Grid.Row="0" Grid.RowSpan="3" Grid.Column="2" Spacing="10" VerticalAlignment="Top">
                           <ToggleSwitch IsChecked="{Binding Enabled}" HorizontalAlignment="Right" />
                           <Button Content="{Binding RunButtonLabel}" Command="{Binding RunNowCommand}"
                                   IsEnabled="{Binding !IsActive}" HorizontalAlignment="Right" />
                       </StackPanel>
                   </Grid>
               </Border>
           </DataTemplate>
       </ItemsControl.ItemTemplate>
   </ItemsControl>
   ```

5. **New local styles** (`UserControl.Styles`, this file):
   ```xml
   <Style Selector="Border.scheduledTaskBadge">
       <Setter Property="Background" Value="{DynamicResource PbAccentSoftBrush}" />
       <Setter Property="CornerRadius" Value="{StaticResource PbRadiusSm}" />
       <Setter Property="Padding" Value="6,1" />
   </Style>
   <Style Selector="Border.scheduledTaskCard.scheduledTaskActive">
       <Setter Property="Background" Value="{DynamicResource PbAccentSoftBrush}" />
   </Style>
   ```
   (mirrors Connections' `Button.sideItemButton.connConnected` "badge plus additional card-level
   weight" reasoning, cited in the design doc.)

**Depends on:** Steps 1 (`ActivityKind`/`IsActive`/`RunButtonLabel`), 3 (the two new commands +
`IsSchedulerPaused`), 5 (`PauseResumeIconConverter`/`PauseResumeLabelConverter`)
**Verify:** `dotnet build` — existing `.axaml`/`x:Class`, AVLN2000 fresh-view gotcha doesn't apply.
Manual pass per Step 8.

## Step 5: Two small converters for the Pause/Resume button

**Files:** `src/Paperbunkr.App/Views/AboutSectionConverters.cs` → **not** here (that file is
About-specific by name); new `src/Paperbunkr.App/Views/AutomationSectionConverters.cs` (new),
same shape as `DetailsCellConverters.cs`/`AboutSectionConverters.cs` (`sealed class` +
`IValueConverter` + `public static readonly X Instance = new();`)

**What:**
- `PauseResumeIconConverter : IValueConverter` — `value is true ? Symbol.Play : Symbol.Pause`.
- `PauseResumeLabelConverter : IValueConverter` — `value is true ? "Resume all" : "Pause all"`.

**Depends on:** none
**Verify:** exercised transitively by Step 4's manual pass; no dedicated unit test for two one-line
ternaries.

## Step 6: Tests

**Files:** `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit — new fake +
new test cases), `src/Paperbunkr.App.Tests/ScheduledTaskRowTests.cs` (new)

**What:**

`ScheduledTaskRowTests` (new file — this model has no dedicated test file today): `IsActive` is
`true` when either `IsRunning` or `IsQueued` is set, `false` when neither; `RunButtonLabel` reflects
`"Running"`/`"Queued"`/`"Run now"` in that priority order; both recompute via the `OnXChanged`
partials (assert via `INotifyPropertyChanged` subscription, matching however
`LastRunLabel`'s recompute-on-change is already tested elsewhere in this codebase, if it is —
otherwise a direct assert-after-set is sufficient, `ScheduledTaskRow` has no async/dispatcher
dependency).

`PreferencesScreenViewModelTests` — new private nested `FakeSchedulerService : ISchedulerService`
(this codebase's existing convention for test doubles declared inline in the file that needs them,
e.g. `RelayStubCommand` in other test files): backing `List<ScheduledTaskRow>` for `Tasks`, a
`Changed` event, no-op `Start`/`Stop`/`NotifyRan`/`SetSchedule`, `RunNowAsync` appends `taskId` to a
public `List<string> RunNowCalls` and returns `Task.CompletedTask`, `SetEnabled` records into a
public `List<(string TaskId, bool Enabled)> SetEnabledCalls` **and** mutates the matching row's
`Enabled` **and** raises `Changed` (so `RebuildScheduledTasks`'s subscription fires, matching real
`SchedulerService` behavior — a fake that doesn't raise `Changed` would let `ScheduledTasks`'
`Enabled` values silently drift from what `SetEnabled` was actually called with).

New test cases:
- `RunAllScheduledTasksNow_CallsRunNowAsyncForEveryTask_RegardlessOfEnabled`: seed the fake with a
  mix of enabled/disabled rows, call the command, assert `RunNowCalls` contains all `TaskId`s
  (order doesn't matter — assert as a set).
- `TogglePauseAll_DisablesEveryTask`: seed a mix, toggle, assert every row's `Enabled` is now
  `false` and `IsSchedulerPaused` is `true`.
- `TogglePauseAll_ThenToggleAgain_RestoresOriginalEnabledStates_NotAllTrue`: the regression test the
  design doc calls for by name — seed an explicit mix (e.g. 3 enabled, 4 disabled, matching the
  catalog's own real default split), pause, assert all disabled, resume, assert **each task's
  `Enabled` matches its original seeded value**, not uniformly `true`. This is the test that would
  fail if a future edit reverted to the naive "resume sets all to true" behavior the design doc
  explicitly rejected.
- `RunAllScheduledTasksNow_WithNoSchedulerAttached_NoOps`: don't call `AttachScheduler`; assert the
  command executes without throwing (covers the same-shape `_scheduler is null` guard every other
  scheduler-touching method in this class already has).

**Depends on:** Steps 1, 3
**Verify:** `dotnet test --filter
"FullyQualifiedName~ScheduledTaskRowTests|FullyQualifiedName~PreferencesScreenViewModelTests"`

## Step 7: `docs/preferences-descriptions-todo.md`

**Files:** `docs/preferences-descriptions-todo.md` (edit)

**What:** No new entry needed — the notification-level `SettingsRow`'s `Description` is written
as real copy directly in Step 4 (carried over from the section's existing explanatory paragraph),
not a placeholder. Confirm there's no stale `## Automation` TODO section already present from an
earlier phase; remove it if so (superseded by the real copy now in the XAML).

**Depends on:** Step 4
**Verify:** visual check of the file only.

## Step 8: Build, review pass, manual on-screen verification

**Files:** none (verification only)

**What:**
1. `dotnet build` — 0 errors.
2. Review-checklist subset: no hardcoded hex colors (everything through `DynamicResource`/existing
   style classes); "Run all now"/"Pause all" share the same `headerAction ghost` class already used
   throughout Preferences; the accent-tint card and badge use the same brush
   (`PbAccentSoftBrush`) the Connections/About phases already established for this exact "badge
   plus card-level weight" pattern, not a new ad hoc color.
3. Manual on-screen pass (standing no-computer-use limitation): each task card shows its icon;
   clicking "Run now" on one task shows "Running…" with the accent tint, the button disables and
   relabels, and re-enables once the task completes; "Run all now" fires all 7 (spot-check the
   Activity Center shows them); "Pause all" disables every toggle and the button relabels "Resume
   all"; clicking "Resume all" brings back exactly the pre-pause mix, not all 7 enabled; the
   notification-level row and its description render correctly as a `SettingsRow`.

**Depends on:** Steps 1-7
**Verify:** build + targeted test filter green; manual pass confirms all 5 design goals plus the
Pause/Resume refinement's actual behavior.
