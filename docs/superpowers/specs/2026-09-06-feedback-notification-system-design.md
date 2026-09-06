# Feedback & notification system — toasts, dialogs, indicators, badges

**Status:** Approved design, not yet implemented.
**Branch:** `claude/library-health` (current working branch; this work is independent of the
Library Health feature but the two intersect — see "Interaction with Library Health" below).

## Problem

Paperbunkr has a real, solid background-job/alert system (Activity Center, shipped 2026-09-04:
`IActivityService` / `ActivityJob` / `ActivityAlert`, surfaced via a status-bar chip → peek
popover → drawer). Everything *around* it grew ad hoc:

- **Dialogs**: no shared confirm/prompt mechanism. ~15 property-editor overlays in
  `MainWindow.axaml` each hand-copy the same `<Border Background="#B0000000">` scrim + close
  button. `PluginQuestionDialog.axaml` is a second, parallel one-off "simple prompt" mechanism
  built as a native `Window`. A third, `ConfirmedMissingCleanupConfirmDialog`, is proposed
  (not yet built) in the Library Health design and would have become a fourth if left alone.
- **Toasts**: functional (`WindowNotificationManager`, wired in `MainWindow.axaml.cs`), but every
  call site builds its content ad hoc — plain title+message strings via `MainViewModel.ShowToast`,
  versus `UpdateReadyToastView`, a fully bespoke view with its own action buttons.
- **Progress/busy indicators**: no shared control. Each screen (`BookDetailScreen`,
  `MangaDetailScreen`, `ReaderChrome`, `ReadingScreen`, `InsightsScreen`, `MigrationOverlay`) rolls
  its own `ProgressBar` usage. Activity Center's own job rows have a reasonable look, but it's
  inline markup in `ActivityTemplates.axaml`, not a reusable piece.
- **Status/content badges**: the audit for this design found **9 visually distinct badge/pill
  shapes**, several of which represent the exact same underlying state (read/unread/in-progress
  alone has 6 independent treatments — checkmark icon, `CircleHalfFill` icon, an accent bar, a
  progress bar, an overlay dot, and 3 different unread-count pills — spread across `DetailTabs`,
  `LibraryScreen`, `MangaDetailScreen`, `BookDetailScreen`, and `StatusBar`).
- **No documented rule** for when something should be a toast vs. an Activity Center alert vs. a
  blocking dialog vs. a tracked job — 32 existing call sites make this decision independently
  today, with predictable drift.

This design does not treat CE parity as a constraint: CE's own edit dialogs are true blocking
modal Windows, and its `TasksDialog` is a modal, non-persisted, queue-only job monitor — Paperbunkr
already deliberately diverges from both (non-blocking overlays + unsaved-changes tracking instead
of modality; Activity Center is a persisted, generalized expansion of `TasksDialog`). This pass
continues that divergence rather than reconciling with CE.

## Non-goals

- Rewriting the content of any of the 15 existing property-editor overlays (Migration,
  CollectionProperties, BulkIssueProperties, etc.) — only the shared chrome around them changes.
- Touching `NativeMessageBox.cs` or the freeze-watchdog crash path — it deliberately bypasses
  Avalonia entirely because the UI thread may be hung, and must stay that way.
- Touching `CrashReportWindow` or `DatabaseRecoveryWindow` — both deliberately use a raw
  `Show()` + blocking `DispatcherFrame` native `Window`, for the same reason (they must survive a
  crash in `MainWindow`'s own visual tree). Only `PluginQuestionDialog` is absorbed, because its
  reason for existing (a simple two-button prompt) doesn't share that crash-survival constraint.
- Unifying tag pills' *editing* interaction (add/remove tag flows) — only their visual style.
- Extending `BusyIndicator` beyond Activity Center's own job rows (see below).
- Auditing every future toast call site forever — the taxonomy governs new code; the 32-site audit
  in this doc is a one-time pass.

## 1. Taxonomy: which bucket does an event fall into?

Every one of the four mechanisms below exists to serve exactly one branch of this decision, made
by whatever ViewModel/service raises the event:

1. **Does it have real duration/progress while it runs?**
   → **`ActivityJob`** (tracked in the drawer regardless). Its existing `ActivityToastPolicy`
   (Always / FailuresOnly / Never) decides whether completion *also* raises a Toast. No further
   decision needed — a Job is always tracked, the toast is a side effect of the policy.

2. **Otherwise: must the user make an explicit decision before the app proceeds — especially if
   destructive/irreversible?**
   → **`ConfirmDialog`** (blocking, in-page, via `OverlayShell`).

3. **Otherwise: would losing this because the user wasn't looking be bad — does it need to survive
   until dismissed, or reference an ongoing/persistent state?**
   → **Activity Center `Alert`** (persists until dismissed; status-bar badge + peek/drawer).

4. **Otherwise** (quick, low-stakes, already-done, no action needed):
   → **Toast** (transient, auto-dismiss, non-blocking).

This is the same rule applied to the 32-site audit in section 6, and is the rule future code
should follow.

## 2. OverlayShell & ConfirmDialog

### OverlayShell

A new custom control (`Controls/OverlayShell.cs` + a `ControlTheme` in `Styles/`), following the
same "real custom control with a `ControlTheme`" precedent already established by
`BrandMark`/`SplitText` (as opposed to a plain `UserControl` or an attached-property helper like
`ContextMenuHost`) — but deriving from `ContentControl` rather than bare `TemplatedControl`, since
unlike `BrandMark`/`SplitText` it needs to host arbitrary child content (the wrapped overlay view).
`ContentControl` is itself a `TemplatedControl` with that content-hosting behavior built in.

```csharp
public class OverlayShell : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty = ...;
    public bool IsOpen { get; set; }

    public static readonly StyledProperty<ICommand?> CloseCommandProperty = ...;
    public ICommand? CloseCommand { get; set; }

    public static readonly StyledProperty<bool> AllowDismissProperty = ...; // default true
    public bool AllowDismiss { get; set; }
}
```

- `IsOpen` drives visibility/scrim, bound to each screen's existing `IsXOverlayOpen` boolean — no
  change to how overlays are opened.
- `CloseCommand` is invoked by scrim-click and Escape (when `AllowDismiss` is true). **It must be a
  command, not a direct `IsOpen = false` flip** — several existing overlays (e.g. `IssueProperties`,
  `BulkIssueProperties`) already run an unsaved-changes check (`TryLeaveCurrentEditor`) from their
  explicit close button. Routing scrim/Escape through the same command they already bind keeps
  that check intact instead of silently bypassing it. As part of migrating each of the 15
  overlays, any that don't yet expose a dedicated `CloseXCommand` get one extracted.
- `AllowDismiss = false` is available for overlays that must be explicitly closed via in-content
  action (e.g. `Welcome`/`WelcomeTour` onboarding).

Migration: each of the 15 `<Border IsVisible="{Binding IsXOverlayOpen}"> ... </Border>` blocks in
`MainWindow.axaml` becomes:

```xml
<controls:OverlayShell IsOpen="{Binding IsMigrationOverlayOpen}" CloseCommand="{Binding CloseMigrationCommand}">
  <views:MigrationOverlay />
</controls:OverlayShell>
```

No ViewModel restructuring — each overlay keeps its own state and its own view. Only the
repeated scrim/close-button/animation chrome moves into the shared control.

### ConfirmDialog

Content shown inside one shared `OverlayShell` instance (mounted once, like the other 15), driven
by a new service:

```csharp
public interface IDialogService
{
    Task<int> ShowAsync(ConfirmDialogRequest request); // 0 = primary, 1 = secondary, -1 = dismissed
    Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
        string cancelLabel = "Cancel", bool isDestructive = false); // convenience: 0 => true, else false
}

public sealed record ConfirmDialogRequest(
    string Message,
    string? Title = null,
    IReadOnlyList<string>? Items = null,       // renders as a scrollable list when present
    string PrimaryLabel = "Confirm",
    string? SecondaryLabel = "Cancel",         // null collapses to a single-button dialog
    bool IsDestructive = false);               // recolors the primary button (error/danger token)
```

This shape covers both real consumers found during design:

- **`ConfirmedMissingCleanupConfirmDialog`** (Library Health, not yet built): `Items` populated
  with each badge-eligible series/issue/path, `IsDestructive = true`, single confirm/cancel.
- **`PluginQuestionDialog`** (existing, absorbed): `IApplication.AskQuestion(question, buttonText,
  optionText)` — a versioned plugin-facing contract (`src/Paperbunkr.Plugins/Automation/
  IApplication.cs:35`) — **keeps its existing synchronous `int`-returning signature**; plugin
  authors are not affected. Its adapter (`PaperbunkrApplication.AskQuestion`,
  `src/Paperbunkr.App/Plugins/PaperbunkrApplication.cs:152`) internally pumps a `DispatcherFrame`
  while awaiting `IDialogService.ShowAsync(...)` — the same blocking-pump technique
  `CrashReportWindow.ShowModal` already uses (`Show()` + `Dispatcher.UIThread.PushFrame`), just
  targeting the new in-page dialog instead of a native `Window`. `PluginQuestionDialog.axaml` and
  `.axaml.cs` are deleted; `optionText` empty/null maps to `SecondaryLabel = null` (single-button).

Every other (non-plugin) call site uses `await dialogService.ConfirmAsync(...)` normally — no
dispatcher pumping needed, since those callers are already on async command handlers.

## 3. BusyIndicator

A small `TemplatedControl` (same `BrandMark`/`SplitText` pattern), scoped **only** to Activity
Center's own job rows:

```csharp
public class BusyIndicator : TemplatedControl
{
    public static readonly StyledProperty<ActivityJob?> JobProperty = ...;
    public ActivityJob? Job { get; set; }
}
```

The control's template reads `Job.Fraction` / `Job.IsIndeterminate` (already computed on
`ActivityJob`, `Models/ActivityJob.cs:61-67` — unchanged). `ActivityTemplates.axaml`'s job-row
template replaces its inline `ProgressBar` + marquee markup with `<controls:BusyIndicator
Job="{Binding}" />`. This is an extraction of the existing look into a named, reusable control —
not a visual change.

**Explicitly out of scope**: `BookDetailScreen`, `MangaDetailScreen` (`chapterProgress`),
`ReaderChrome`, `ReadingScreen`, `InsightsScreen`, and `MigrationOverlay` keep their own separate
`ProgressBar` usages, untouched. `BusyIndicator` is typed directly to `ActivityJob`, which carries
real job semantics (`Title`, a `Queued`/`Running`/`Succeeded`/`Failed`/`Cancelled` `Status`,
`IsUpkeep`) that don't fit a book's reading-progress percentage or a manga chapter's read
fraction — forcing those into fake `ActivityJob` instances would be worse than leaving them alone.
The status bar's pulsing `IndicatorPulse` dot is a different, simpler "is anything running"
visual, not a progress display, and is unrelated to this control.

## 4. Badge/Pill system

The audit found 9 shapes falling into three genuinely different jobs, so this is three
constructs sharing one set of visual tokens (corner radius, padding scale, and a consistent
color mapping — success/accent/muted) rather than one component forced to do three jobs:

### StatusBadge (new `TemplatedControl`)

Icon-driven state, `Variant` = `Read` | `InProgress` | `New`. Replaces:

- `DetailTabs.axaml:114,134,168` — the three tile templates' (poster/list/card) read-checkmark
  icon, and `:138` (list row `CircleHalfFill` "in progress" icon). The poster tile's separate
  accent-bar treatment for in-progress (`:112-113`) is dropped — `InProgress` converges on the
  icon variant everywhere, since the bar-only-fits-a-poster-tile approach doesn't generalize to
  list/card rows anyway.
- `BookDetailScreen.axaml:97` — book read-checkmark icon.
- `MangaDetailScreen.axaml:105,224-225` — the "NEW" chapter pill becomes `Variant="New"`.

### CountPill (shared `Style` on `Border`, not a new control)

Plain number-in-a-pill — no icon logic, so a `Style` selector is enough (matches the existing
`Button.toolbarPill` precedent rather than introducing a control for something this simple).
Replaces:

- `LibraryScreen.axaml:246,423` (dot overlay), `:313,501,617-682` (pill+count), `:536` (plain
  text) — three divergent unread-count treatments in the same file collapse to one.
- `StatusBar.axaml:89-93` — alert-count pill (its separate pulsing dot at `:80-85` is unrelated
  and unaffected).
- `MainWindow.axaml:124,444,464,530,564` — search `countBadge` (match count).

### TagPill (shared `Style`, not a new control)

Content/metadata tags — genuinely different from status (user-editable, not machine-computed) —
so it stays its own style rather than pretending to be a `StatusBadge`, but uses the same
radius/padding/typography tokens so it still reads as the same family. Applies to
`ReadingScreen.axaml:147-149` and `DetailBand.axaml:159-186`'s existing `TagPillViewModel`-backed
markup; only the visual style changes, not the click/remove interaction logic.

**Explicitly not touched**: `MangaDetailScreen.axaml:240`'s `chapterProgress` `ProgressBar` shows a
real fraction (% of a chapter read), not a boolean state. It fits neither `StatusBadge` (icon/
boolean-driven) nor `BusyIndicator` (Activity-Center-only, per section 3). It keeps its current
plain `ProgressBar` exactly as-is.

## 5. Shared toast template

One content shape, rendered by a new `PbToastView`, shown through the existing
`WindowNotificationManager` (which already accepts arbitrary content — this is how
`UpdateReadyToastView` works today):

```csharp
public sealed record ToastRequest(
    string Title,
    string? Message = null,
    ToastSeverity Severity = ToastSeverity.Info,   // Info / Success / Warning / Error
    IReadOnlyList<ToastAction>? Actions = null);

public sealed record ToastAction(string Label, ICommand Command);

public enum ToastSeverity { Info, Success, Warning, Error }
```

`PbToastView` renders a small `FluentIcons.Avalonia` `SymbolIcon` (matching the icon set used
everywhere else in the app — e.g. `CheckmarkCircle` for `Success`) next to the title, alongside a
left-border accent color, both driven by `Severity`; `Message` and `Actions` are optional.

- `MainViewModel.ShowToast(title, message)` becomes a thin wrapper constructing
  `new ToastRequest(title, message)`.
- `UpdateReadyToastView` and `UpdateReadyToastViewModel` are deleted; the update-ready toast
  becomes `new ToastRequest("Update ready", "Version X will install on restart", Severity: Info,
  Actions: [Restart, Later, What's New])`.
- Activity Center's `CompletionToastRequested` picks `Severity` from the job's own
  `Succeeded`/`Failed` outcome.

## 6. Reclassifying existing call sites (one-time audit)

32 existing `ShowToast`/`ShowToastForPlugin`/`RaiseAlert`/`UpdateReadyToastRequested` call sites
were checked against the taxonomy in section 1. 27 are already correct (quick foreground
confirmations correctly left as Toasts — undo/redo, plugin install feedback, bulk mark-read,
add-to-collection, shortcut import/export, etc.; background/unattended events correctly already
Alerts — files-missing/reconnected, duplicate detection, scheduled-task failures). 5 are
reclassified, each because an existing same-family sibling event is already classified
differently, and the sibling is the one that's right:

| Site | Today | Reclassified to | Why |
|---|---|---|---|
| `ReadingScreenViewModel.cs:376` (drag-and-drop import) | Toast | **Job-tracked** | Real duration; should be an `ActivityJob` from the start, with completion following the normal toast-policy path, rather than a fire-and-forget toast at the end |
| `LibraryScreenViewModel.cs:2245` (drag-and-drop import) | Toast | **Job-tracked** | Same operation, same reasoning |
| `MetadataWriteBackQueue.cs:212` (write-back batch finished) | Toast | **Job-tracked** | A batch across many items with possible per-item errors — the same shape as any other tracked job |
| `PluginCommandRowViewModel.cs:92` (plugin scan finds issues) | Toast | **Alert** | `PluginScanAlertService.cs:69` already treats the same kind of event ("scan found matches") as an Alert; this one was missed |
| `LiveFolderWatchService.cs:268` (watcher found new comics) | Toast | **Alert** | Its own siblings in the same watcher family (`MainViewModel.cs:180,203` — missing/reconnected files) already use Alert |

## Interaction with Library Health

`ConfirmedMissingCleanupConfirmDialog` (proposed, not yet built, in
`docs/superpowers/specs/2026-09-06-missing-files-library-health-design.md:240-244`) becomes the
first real consumer of `ConfirmDialog` rather than a 16th one-off overlay — implemented as
`dialogService.ShowAsync(new ConfirmDialogRequest(..., Items: affectedItems, IsDestructive:
true))`. That feature's implementation plan should build on this design rather than scaffolding
its own dialog.

## Testing

- `OverlayShell`: unit tests for `CloseCommand` invocation on scrim-click/Escape (vs. a plain
  `IsOpen` flip), and `AllowDismiss = false` suppressing both.
- `IDialogService`/`ConfirmDialog`: unit tests for `ShowAsync` resolving 0/1/-1 correctly, the
  `ConfirmAsync` convenience wrapper's bool mapping, and `PaperbunkrApplication.AskQuestion`'s
  dispatcher-pump bridge returning the correct synchronous `int` (mirroring the existing
  `PluginApiV3Tests.cs` write-confirmation-gate assertions, now against the new dialog).
  Regression-test that plugin command tests (`TestPluginEnvironment.cs`,
  `PluginScreenViewModelTests.cs`) still pass unchanged, since the plugin-facing contract doesn't
  change shape.
- `BusyIndicator`: unit tests for `Fraction`/`IsIndeterminate` reflecting the bound `ActivityJob`.
- `StatusBadge`: unit tests per `Variant` rendering the right icon/color.
- Toast: unit test that `MainViewModel.ShowToast` and the update-ready flow both produce a correct
  `ToastRequest`; existing `ActivityService` completion-toast tests updated to assert `Severity`.
- The 5 reclassified call sites get their existing tests updated to assert the new behavior
  (job started instead of/alongside a toast, or `RaiseAlert` instead of `ShowToast`) rather than
  new tests being added from scratch.
- Manual/GUI verification (per project convention, this doc doesn't claim it done until actually
  clicked through): migrate 2-3 of the 15 overlays first as a smoke test before doing the
  remaining ones in bulk; visually confirm `StatusBadge` variants across all affected screens;
  confirm `ConfirmDialog` for both the plugin `AskQuestion` path and the Library Health path once
  that feature lands.
