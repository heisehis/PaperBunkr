# Automation Task List Redesign — Design (Phase 9 of Preferences)

## Background

`AutomationSection.axaml` is the last of the 9 areas Phase 1's tile-hub design doc (§4 item 6)
deferred as "needing a genuine redesign, not a reskin." It currently renders as a single
`groupBox` card ("Scheduled Tasks") containing a notification-level `ComboBox`, an explanatory
paragraph, and a flat `ItemsControl` of the 7 catalog tasks — each row is a `Grid` with a bottom
border divider, no icon, and mode-dependent controls (`NumericUpDown` for Interval, `TimePicker`
for Daily-at) toggled via `IsVisible`.

Facts gathered before designing:

- **No CE precedent.** ComicRack CE has no scheduled-task system at all — its only
  automation-adjacent setting is a two-checkbox "back up on startup / on exit" pair in the Advanced
  tab's Backup Manager (`ComicRack/Dialogs/PreferencesDialog.Designer.cs`, `grpBackupManager`), with
  no interval or time-of-day fields anywhere in its model. Paperbunkr's whole
  interval/daily-at/enable/run-now scheduling concept, applied uniformly across 7 tasks, is a
  Paperbunkr-original feature — this redesign is unconstrained by CE parity.
- **`ScheduledTaskRow` already tracks `IsRunning`/`IsQueued`**, but nothing in the current view
  renders them — clicking "Run now" gives no feedback until the task finishes. Confirmed real gap,
  not a stylistic one.
- **`ActivityKindIconConverter`** (`Views/ActivityConverters.cs`) already maps `ActivityJobKind` →
  `Symbol` for the Activity Center, and every task descriptor in `ScheduledTaskCatalog` carries an
  `ActivityJobKind`. Reusable directly for per-task icons — no new icon-mapping needed.
  `db-backup`/`content-type-sweep` are both `ActivityJobKind.Other`, which the converter's fallback
  maps to `Symbol.Info`.
- **`ISchedulerService`** (`Services/Scheduling/ISchedulerService.cs`) exposes `Tasks`, `Changed`,
  `RunNowAsync(taskId)` (works "ignoring its enabled flag and schedule... still goes through the
  queue"), `NotifyRan`, `SetEnabled(taskId, enabled)`, `SetSchedule(...)`. No pause/suspend concept
  exists anywhere in it today.
- **Precedent for a group-level action button**: Library Health's "Verify Now" is a
  `Classes="headerAction primary"` button sitting directly inside its own content group, not in a
  separate page-level header bar (`LibrarySection.axaml`). Automation's own bulk actions follow the
  same placement.

## Goals

1. **Split the page into two groups.** Notification level becomes its own `SettingsRow`
   (icon + title + `ComboBox`), matching every other simple setting in this redesign arc. The 7
   tasks form their own "SCHEDULED TASKS" caption group below it (plain caption, no `groupBox`
   card wrapper — same visual recipe Keyboard Shortcuts already established for a lone group).
2. **Each task becomes its own bordered card**, not a divider-separated flat row: icon (via
   `ActivityKindIconConverter`) + title + description on the left, Enabled toggle + Run now stacked
   in a trailing right column, mode controls and Last-run/Next-run labels below.
3. **Surface `IsRunning`/`IsQueued`.** A small "Running…" / "Queued" badge appears next to the
   title, the card gets a subtle accent-tint background while active (same "badge plus additional
   card-level weight, not badge-replaces-tint" reasoning the Connections redesign used for its
   connected-row tint), and the Run now button disables and relabels to match state.
4. **Mode controls (Interval-hours / Daily-at-time) restyled in place** — same `ComboBox` +
   conditional `NumericUpDown`/`TimePicker` shape, just cleaned up spacing/alignment for the new
   card layout. Not rebuilt as a segmented toggle (real extra work for a control that already
   functions correctly).
5. **Two new bulk actions**, placed as `headerAction` buttons at the top of the "SCHEDULED TASKS"
   group (mirroring Library Health's "Verify Now" placement):
   - **Run all now**: calls `RunNowAsync` for all 7 tasks unconditionally, regardless of each
     task's own `Enabled` state (explicit user choice — this is "run everything right now" as an
     override, not "run whatever's scheduled").
   - **Pause all / Resume all** (one button, label/icon flips on state): bulk-sets every task's
     `Enabled` to `false` via the existing `SetEnabled`, no new scheduler concept. **Refinement
     over the literal ask, flagged explicitly**: instead of Resume blindly setting every task back
     to `true` (which would incorrectly re-enable the 4 tasks that default to `Enabled: false` -
     library scan, book scan, metadata sync, generate covers - even for a user who deliberately
     left them off), Pause snapshots each task's actual pre-pause `Enabled` value in memory first,
     and Resume restores exactly that snapshot. Same mechanism the user chose (bulk-toggling each
     task's own `Enabled` flag, no new global scheduler-pause field), same session-only lifetime
     (the snapshot is a plain in-memory dictionary on `PreferencesScreenViewModel`, cleared/lost on
     app restart, matching the "resets on restart" call) - just without the specific data-loss
     scenario. If a fully literal "always re-enable all 7" Resume is actually wanted instead, say
     so and this reverts to that simpler form.

## Non-goals

- **Persisting pause state across restarts** — explicitly session-only; no `AppSettings` column, no
  migration.
- **A segmented-toggle rebuild of the Interval/Daily-at mode switch** — restyled in place instead.
- **Any change to the 7-task catalog itself, scheduling algorithm, or concurrency/priority rules**
  (`SchedulerResourceClass`, ≤2-at-once) — this redesign is Preferences-tab-only.
- **A new `AppSettings`-level "scheduler paused" flag** — the chosen mechanism reuses per-task
  `SetEnabled`, not a new global switch.

## Architecture

### 1. Notification level → standalone `SettingsRow`

Replace the current `StackPanel Orientation="Horizontal"` (label + `ComboBox`) with one
`SettingsRow` (`Icon="Alert"` or similar bell/notification icon, `Title="Notify when a scheduled
task finishes"`, `SettingsContent` = the existing `ComboBox` bound to
`ScheduledTaskNotificationLevel`). The explanatory paragraph ("Maintenance tasks run in the
background...") becomes the row's `Description`.

### 2. "SCHEDULED TASKS" group: caption + bulk-action header + card list

```
TextBlock Classes="settingsGroupCaption" Text="SCHEDULED TASKS"
StackPanel Orientation="Horizontal" Spacing="10"   <!-- bulk actions -->
    Button Classes="headerAction ghost" Command="{Binding RunAllScheduledTasksNowCommand}"
        Icon: Play, Text: "Run all now"
    Button Classes="headerAction ghost" Command="{Binding TogglePauseAllScheduledTasksCommand}"
        Icon/Text switch on IsSchedulerPaused: Pause/"Pause all" ↔ Play/"Resume all"
ItemsControl ItemsSource="{Binding ScheduledTasks}"   <!-- one card per task, see below -->
```

### 3. Per-task card

```
Border (bordered card, rounded corners, padding - same visual language as Connections/Library
        Health cards elsewhere in this redesign arc)
    Classes.scheduledTaskActive="{Binding IsRunning}" Classes.scheduledTaskActive="{Binding IsQueued}"
      (an OR - both states get the same accent tint; exact binding mechanism, e.g. two independent
      Classes.x bindings both mapping to one shared style selector, or a small computed
      IsActive => IsRunning || IsQueued property on ScheduledTaskRow, left to the plan)
    Grid ColumnDefinitions="Auto,*,Auto"
        fi:SymbolIcon Column 0, Symbol bound via ActivityKindIconConverter over the row's
            ActivityJobKind (needs adding to ScheduledTaskRow - see ViewModel changes)
        StackPanel Column 1:
            StackPanel Orientation="Horizontal": TextBlock DisplayName, then a small badge
                IsVisible bound to IsRunning ("Running…") or IsQueued ("Queued") - two badges,
                mutually exclusive via IsVisible, not a single multi-state badge control (no
                existing shared badge fits this domain - StatusBadge is scoped to reading-status
                semantics, Read/InProgress/New, a different concept; a small local Border+TextBlock
                pill matches how About's Changelog "Current" badge and category tags were done)
            TextBlock Description
            StackPanel Orientation="Horizontal" (mode controls, restyled in place)
            StackPanel Orientation="Horizontal" (Last run / Next run labels)
        StackPanel Column 2 (trailing, VerticalAlignment=Top):
            ToggleSwitch IsChecked="{Binding Enabled}"
            Button Content bound to IsRunning ? "Running" : IsQueued ? "Queued" : "Run now",
                   IsEnabled="{Binding !IsRunning}" (a queued task can still be cancelled-and-
                   rerun conceptually, but simplest and matching today's RunNowAsync contract:
                   disable only while actually running, not while queued - queued still allows a
                   new RunNowAsync call to re-order; exact IsEnabled expression left to the plan
                   if this needs refinement), Command="{Binding RunNowCommand}"
```

## ViewModel changes (summary)

- `AutomationSection.axaml`: full restructure per Architecture above.
- `ScheduledTaskRow` (Models): new `ActivityJobKind` property (populated from the catalog
  descriptor in `RebuildScheduledTasks`/wherever rows are first created) so the view can bind
  `ActivityKindIconConverter` without reaching back into `ScheduledTaskCatalog` from XAML.
- `PreferencesScreenViewModel`:
  - New `[RelayCommand] RunAllScheduledTasksNow` — loops `ScheduledTasks`, calls
    `_scheduler.RunNowAsync(row.TaskId)` for each (fire-and-forget per task, matching how a single
    row's own Run now already behaves - each goes through the existing queue independently).
  - New `[RelayCommand] TogglePauseAllScheduledTasks` and `IsSchedulerPaused` (computed from
    whether a snapshot is currently held): when not paused, capture
    `Dictionary<string,bool>` of `TaskId → Enabled` from `ScheduledTasks`, then call
    `_scheduler.SetEnabled(taskId, false)` for each; when paused, restore each task's `Enabled`
    from the snapshot via `SetEnabled` and clear it. Snapshot is a private field, not persisted -
    lost on VM disposal/app restart per the session-only call.
  - New icon resource for the notification-level `SettingsRow` (`Icon="Alert"` — confirm exact
    `Symbol` name exists at compile time, same fallback-if-missing tolerance as prior phases).

## Testing

- `PreferencesScreenViewModelTests`: new cases —
  `RunAllScheduledTasksNow_CallsRunNowAsyncForEveryTask` (spy/fake `ISchedulerService`, assert 7
  calls, one per `TaskId`, regardless of `Enabled`); `TogglePauseAll_DisablesEveryTask_ThenResume
  RestoresOriginalEnabledStates` (seed a mix of enabled/disabled tasks, pause, assert all
  `SetEnabled(..., false)`, resume, assert each restored to its *original* value, not blindly
  `true` — this is the exact scenario the Pause/Resume refinement targets, so it needs an explicit
  regression test, not just a round-trip-happens-to-work case).
- Manual on-screen pass (standing no-computer-use limitation): each card shows the right icon;
  triggering "Run now" on one task shows "Running…" with the accent tint and a disabled button
  until it completes; "Run all now" fires all 7 (watch the Activity Center for confirmation);
  "Pause all" disables every toggle, "Resume all" brings back exactly the pre-pause mix (not all
  7 on).

## Deliverable

One new `SettingsRow` (notification level) + `docs/preferences-descriptions-todo.md` doesn't need a
new entry since a real description is written directly in Goals §1. No new NuGet dependency, no
schema/migration change. `ActivityKindIconConverter` reused as-is, not modified.
