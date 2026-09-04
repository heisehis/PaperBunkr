# Preferences — Behavior settings, second batch

*Date: 2026-09-04. Follows docs/superpowers/specs/2026-08-07-preferences-behavior-tab-design.md,
which shipped the first two Behavior toggles (`OpenLastPage`, `AutoNavigateComics`). That spec's §1
triage deliberately deferred most of CE's ~38 `[Category("…")]` `Settings` checkboxes until their
underlying feature existed. Several of those features now exist — this batch adds the four toggles
whose feature has since landed and which meaningfully change behavior.*

## 1. Scope

Four new `bool` columns on `AppSettings`, four new checkboxes in **Preferences → General**, each
gating something that already works in Paperbunkr today. All wired with the existing
immediate-persist pattern (`PreferencesScreenViewModel.PersistBehaviorSetting`) — no Save step, no
new service.

| Setting (CE origin) | Column | Default | Gates |
|---|---|---|---|
| Reopen from last session (`OpenLastFile`, "Starting ComicRack", CE default `true`) | `RestoreSessionOnStartup` | `true` | `MainViewModel.RestoreLastScreen()` |
| Rescan folders on startup (`ScanStartup`, "Starting ComicRack", CE default `false`) | `ScanFoldersOnStartup` | `false` | a new startup background scan job |
| Show Quick Review after finishing (`AutoShowQuickReview`, "Reading", CE default `false`) | `PromptReviewOnFinish` | `false` | auto-open of the existing Quick Rate overlay |
| Disable drag-and-drop (`DisableDragDrop`, "Browser", CE default `false` = drop enabled) | `EnableDragDropImport` | `true` | the Library / Reading List screen drop handlers |

Defaults are chosen so an existing settings row reproduces today's behavior exactly:
`RestoreSessionOnStartup` and `EnableDragDropImport` default `true` because Paperbunkr already
does those unconditionally; the other two default to CE's own `false` (opt-in).

The `EnableDragDropImport` column stores the *positive* sense (CE's is negative, `DisableDragDrop`)
so the value reads the same way the checkbox label does — matches how this codebase already
inverted `LeftRightMovementReversed` → `ReverseRtlNavigation`.

## 2. Data model

`AppSettings` gains four columns, one migration (`AddBehaviorSettingsBatch2`):

```csharp
/// <summary>Whether launch restores the screen active when the app last closed
/// (<see cref="LastScreenKey"/>), or always opens Home. CE: Settings.OpenLastFile
/// ("Reopen Books from last session"), default true. A --open CLI deep link still wins
/// regardless (App.axaml.cs checks it first).</summary>
public bool RestoreSessionOnStartup { get; set; } = true;

/// <summary>Whether a full library folder scan (comics + book folders) runs in the
/// background on launch. CE: Settings.ScanStartup ("Rescan the Book Folders for new
/// Books"), default false. LiveFolderWatchService only catches changes made while the
/// app is running; this covers changes made while it was closed.</summary>
public bool ScanFoldersOnStartup { get; set; }

/// <summary>Whether reaching the end of a comic (paging past the last page with no
/// next issue to advance to) auto-opens the Quick Rate overlay for that issue. CE:
/// Settings.AutoShowQuickReview ("Show Quick Review Dialog after finishing Book"),
/// default false. Fires at most once per reader-load session.</summary>
public bool PromptReviewOnFinish { get; set; }

/// <summary>Whether files/folders/.cbl can be imported by dragging them onto the
/// Library or Reading List screens. CE: Settings.DisableDragDrop (inverted sense),
/// default false there = drop enabled, so default true here.</summary>
public bool EnableDragDropImport { get; set; } = true;
```

Migration adds all four with the CE-parity defaults. Enum/HasSentinel treatment: none needed,
these are plain bools with literal defaults (same as every other bool on this entity).

## 3. Behavior wiring

### 3.1 `RestoreSessionOnStartup`

`App.axaml.cs` startup already branches: CLI deep link → `OpenDeepLink`, else
`mainViewModel.RestoreLastScreen()`. Change the `else` branch:

```csharp
else if (settings.RestoreSessionOnStartup)
{
    mainViewModel.RestoreLastScreen();
}
else
{
    mainViewModel.GoHome();
}
```

`App.axaml.cs` already opens a context for the deep-link check region; read `AppSettings` there
(or pass the flag). `RestoreLastScreen()` itself is unchanged — its own "no usable last screen →
Home" fallback still covers the fresh-install case.

### 3.2 `ScanFoldersOnStartup`

New fire-and-forget block in `App.axaml.cs` startup, alongside the existing auto-backup /
content-type-sweep `Task.Run` blocks (all of which already run there, gated on their own
`AppSettings` flag):

```csharp
if (settings.ScanFoldersOnStartup)
{
    _ = Task.Run(async () =>
    {
        var activity = mainViewModel.Activity;
        using var job = activity.StartJob(ActivityJobKind.LibraryScan, "Startup folder scan");
        try
        {
            await new LibraryFolderScanner().ScanAllAsync(progress: null, job.CancellationToken);
            await new BookFolderScanService().ScanAllAsync(progress: null, job.CancellationToken);
            job.Succeed("Startup scan complete");
        }
        catch (Exception ex) { job.Fail("Startup scan failed", ex: ex); }
    });
}
```

This reuses the exact scan pipeline the Preferences "Scan now" / "Scan books now" buttons call
(`PreferencesScreenViewModel.ScanNow` / `ScanBooksNow`). It does **not** also run the cover-thumbnail
generation pass those buttons do — the Library screen already generates missing covers lazily on
first display, and a blocking cover pass on every launch is exactly the startup-latency cost this
setting is opt-in to avoid. New books still get covers, just on first view rather than up front.

`ActivityJobKind.LibraryScan` already exists. If `ScanAllAsync`'s `progress` parameter is
non-nullable, pass `new Progress<...>(_ => { })` or a small no-op — confirm at implementation time
and match whichever the two callers above already do.

Deliberately **not** gated behind `RestoreSessionOnStartup` — the two are orthogonal (you can want
a fresh Home screen *and* a startup scan, or neither).

### 3.3 `PromptReviewOnFinish`

`ReaderScreenViewModel` already has a natural "finished the book" signal: `NextPage()` at the last
page calls `TriggerChapterTransition(forward: true)`, which no-ops when there is no adjacent issue
(true end of series / end of a reading list). Hook there.

- New constructor callback `Action<int>? promptReviewForIssue` (issue id), threaded from
  `MainViewModel` the same way `NavigateBack` already is. `MainViewModel` points it at its existing
  private `OpenQuickRateOverlay(int issueId)` (today only reachable from the Library / Detail
  right-click "Quick Rate…" item).
- In `TriggerChapterTransition(forward: true)`, when `TryGetAdjacentIssuePreview` returns `false`
  (no next issue): if `AppSettings.PromptReviewOnFinish` is on, `_loadedIssueId` is set, and a
  per-load guard `_reviewPromptShown` is still `false`, set the guard and invoke
  `promptReviewForIssue(_loadedIssueId.Value)`.
- `_reviewPromptShown` resets to `false` in `Load(...)` (every issue open, including an
  auto-navigate cross-issue jump), so re-reading the same issue later can prompt again, but bouncing
  against the last page repeatedly in one sitting only prompts once.
- Only the forward end triggers it. Backward under-run (`TriggerChapterTransition(forward: false)`
  at page 0) never prompts.
- Continuous-scroll mode reaches the same boundary through
  `ChapterBoundaryOverscroll` → `TryGetAdjacentIssuePreview`; apply the same guard there so the
  prompt fires in both reading modes.

The prompt is the full existing Quick Rate overlay (star rating + free-text review), opened over
the Reader. Dismissing it (Save or Cancel) returns to the Reader on the last page — no auto-advance,
no screen change. This matches CE, whose `AutoShowQuickReview` likewise just shows the dialog and
leaves you where you were.

### 3.4 `EnableDragDropImport`

The drop handlers live in the code-behind of `LibraryScreen.axaml.cs` and `ReadingScreen.axaml.cs`
(`OnDragOver` / `OnDrop`). Both `OnDragOver` methods already set
`e.DragEffects = … ? Copy : None`. Add the settings check to that condition so the drag shows the
"no" cursor and `OnDrop` early-returns when the feature is off:

```csharp
private void OnDragOver(object? sender, DragEventArgs e)
{
    bool enabled = /* AppSettings.EnableDragDropImport */;
    e.DragEffects = enabled && e.DataTransfer.Formats.Contains(DataFormat.File)
        ? DragDropEffects.Copy : DragDropEffects.None;
}
```

Reading `AppSettings` from a View code-behind is not a pattern this codebase loves, but these two
files already reach into their `DataContext` ViewModel in `OnDrop`. Cleanest: expose a
`bool DragDropImportEnabled` computed property on `LibraryScreenViewModel` /
`ReadingScreenViewModel` (each reads `GetOrCreateAppSettings()` — both VMs already open contexts
freely) and have the code-behind check `vm.DragDropImportEnabled`. `OnDrop` also re-checks it before
calling `ImportDroppedPathsAsync`, in case the drag started before the toggle flipped.

## 4. Preferences UI

`Views/Preferences/GeneralSection.axaml` today has two `groupBox` cards: "Reading", "Window". After
this batch:

- **Startup** (new card, `Tag="general.startup"`, placed first) —
  - ☐ "Reopen the screen I was on last time" → `RestoreSessionOnStartup`
  - ☐ "Scan library folders for new files at startup" → `ScanFoldersOnStartup`
    - caption line (`PbTextFaintBrush`, 11.5): "Picks up files added or removed while Paperbunkr
      wasn't running. Runs in the background — check the activity center for progress."
- **Reading** (existing card) — add:
  - ☐ "Ask me to rate a comic when I finish it" → `PromptReviewOnFinish`
- **Library** (new card, `Tag="general.library"`) —
  - ☐ "Allow importing files by dragging them into the window" → `EnableDragDropImport`

Each checkbox is the exact markup already in this file: `CheckBox IsChecked="{Binding …}"
Foreground="{DynamicResource PbTextBrush}" FontSize="13" Content="…"`. Every new `ObservableProperty`
on `PreferencesScreenViewModel` follows the block at lines 320–345 and gets an `On…Changed` partial
calling `PersistBehaviorSetting(s => s.X = value)`; all four are hydrated in `Reload()` alongside
the existing `OpenLastPage` / `AutoNavigateComics` reads inside the `_suppressBehaviorApply` guard.

### 4.1 Search index

`PreferenceIndex.Entries` gets the two new group cards and updated keywords for "Reading":

```csharp
new(PreferencesSection.General, "Startup", "Startup",
    new[] { "startup", "launch", "reopen", "last session", "restore screen", "scan on startup", "rescan folders" },
    "general.startup"),
new(PreferencesSection.General, "Library", "Library",
    new[] { "drag and drop", "drag drop", "import files", "drop files" },
    "general.library"),
```

and the existing `"general.reading"` entry's keyword list gains `"rate on finish"`,
`"quick review"`, `"review prompt"`. `PreferenceIndexTests` already asserts every `AnchorKey`
resolves to a real `Border.Tag` — the two new tags must exist in the .axaml.

## 5. What this batch deliberately still defers

Added to `docs/Paperbunkr-Roadmap.md`'s Beta backlog as a single "Preferences: Behavior/CE-parity toggle
remainder" entry:

- **`AddToLibraryOnOpen`** ("Opened Files are added to the Library") — blocked: Paperbunkr has no
  shell-open-a-loose-file path yet. `RegisterFileOpen` registers `"…\Paperbunkr.exe" "%1"` but
  `App.axaml.cs` only parses `--open <kind>:<id>`; a bare file-path arg is ignored and startup
  falls through to restore-on-launch. Needs: handle a path arg → open it in the Reader (import-on-
  demand or transient), *then* this toggle has something to gate.
- **`HideCursorFullScreen` / `AutoMinimalGui`** — Paperbunkr already auto-hides both chrome and the
  cursor after 3s idle in *every* reading mode (`ReaderScreenViewModel.ShowChrome` +
  `NotifyCursorActivity`, no `IsFullscreen` gate). A toggle would gate already-unified behavior;
  only worth adding if someone wants the pre-unification "cursor always visible in windowed mode"
  split back.
- **Cosmetic browser micro-toggles** — `FadeInThumbnails`, `CoverThumbnailsSameSize` (largely
  already the PosterGrid-vs-Panorama view-mode choice), `DogEarThumbnails`, `ShowToolTips`
  (tooltips aren't built), `NumericRatingThumbnails`, `ExportedListsContainFilenames`. Low value;
  revisit only if a user asks.

## 6. Testing

- **`AppSettingsTests`** (or the migration test): four new columns exist with the stated defaults;
  round-trip via `GetOrCreateAppSettings`.
- **`PreferencesScreenViewModelTests`**: toggling each of the four checkboxes persists to
  `AppSettings`; `Reload()` hydrates each without firing its persist hook (the
  `_suppressBehaviorApply` guard).
- **`PreferenceIndexTests`**: passes with the two new `general.startup` / `general.library` anchors
  (the test asserts anchor↔`Border.Tag` correspondence — the .axaml tags must land in the same
  change).
- **`ReaderScreenViewModelTests`** (new cases): with `PromptReviewOnFinish` on, paging past the
  last page of the last issue in a series invokes the review callback with that issue's id;
  it does **not** fire mid-book, does **not** fire when the setting is off, does **not** fire on
  backward under-run, and fires at most once per `Load`. Use the existing test seam pattern
  (inject the callback, assert it was called with the right id).
- **`MainViewModelTests`**: a startup-path test that `RestoreSessionOnStartup = false` results in
  Home rather than the persisted `LastScreenKey` screen. (If the existing restore tests call
  `RestoreLastScreen()` directly, add the branch check at whatever seam `App.axaml.cs` uses —
  keep the gate testable, don't bury it in `App.axaml.cs`.)
- **Drag-drop gate**: `LibraryScreenViewModel.DragDropImportEnabled` (and the Reading List VM's)
  reflects `AppSettings.EnableDragDropImport`.
- **Manual, on-screen (user)**: flip all four in the running app — confirm a fresh launch opens
  Home with restore off; a file dropped in a folder while closed shows up after launch with startup
  scan on; finishing a comic pops the rating overlay; dragging a file in does nothing with import
  off. Same no-GUI-automation posture as every prior spec.

## 7. Avalonia review

Trivial surface — four `CheckBox`es and two `Border Classes="groupBox"` cards copied verbatim from
the two already in `GeneralSection.axaml`, all colors via `DynamicResource`. Run
`avalonia-pro-max/review-checklist` before calling the UI done anyway (project standing rule), but
no layout/animation/theming risk is anticipated.
