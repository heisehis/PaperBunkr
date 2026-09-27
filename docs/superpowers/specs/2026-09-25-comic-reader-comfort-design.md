# Comic reader — Comfort (slice F3) design

Date: 2026-09-25. Status: **built 2026-09-25** (uncommitted; on-screen check by the user pending, see "Implementation notes" at the end).
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`, items #17 (session stats HUD), #22 (copy and share) and #18 (eye-care reminders). Third of three
specs split from slice F: F1 "Reach" (`2026-09-25-comic-reader-reach-design.md`), F2 "Profiles" (`2026-09-25-comic-reader-profiles-design.md`), **F3 "Comfort"**
(this one). **Build order is F1 → F2 → F3.** F3 reuses F1's "reader is visible and the window is active" gate and command palette, and F2's
`ReaderScreenViewModel.ToastRequested` event and `ReaderProfileState`. Slices A, B, G are built and uncommitted; this builds on the same shared working tree.

## Facts this design rests on (verified 2026-09-25)

- **The pitch's HUD data source is wrong.** #17 says the HUD "reads from the `ReadingEvent` log". The log holds only `Opened`/`Finished` rows with a page delta
  (`Data/Entities/ReadingEvent.cs`); it has no per-page timing, and `IReadingEventRecorder` only fills `PagesRead` on teardown. Live pace has to come from an in-memory
  session tracker in the reader. The log is left untouched (no new rows, no new columns).
- **Where the clock lives.** `CurrentTimeLabel`/`BatteryStatusLabel` are refreshed by a 60 s `_clockTimer` (`ReaderScreenViewModel.cs` ~1006, ~2087) and shown in the
  chrome's **actions cluster**, which hides with the chrome (auto-hide, fullscreen). A HUD "beside the clock" would vanish exactly when it is wanted.
- **Page-progress hooks.** `TrackSessionProgress` (~2392) runs on page changes and computes `_sessionMaxPage - _sessionStartPage`; `OnCurrentContinuousPageIndexChanged`
  (~561) is the continuous-mode per-boundary handler and is **timed** by slice G's overlay, so anything added there must be O(1) and allocation-free. `_storyEndIndex`
  (slice B) is the true end of the story when known.
- **Save Page As** (`SavePageAsAsync`, ~538) decodes the current page with `PageDecodeCore.DecodeSinglePage(filePath, index)` (an Avalonia `Bitmap`), asks
  `FilePickerService.PickSaveFileAsync`, and writes with `PageExportService.TryExport(bitmap, path, format)`. In spread mode it exports **only the current single page**
  (its own comment says a stitched composite was left for later). The context menu (`ReaderPageContextMenuBuilder`) has "Save Page as PNG…/JPEG…".
- **Spread geometry exists.** `SpreadLayoutMath.ComputeCombinedSize(a, b)` is CE's common-height formula (both pages scaled to the taller height, widths summed) and
  `IsPairEligible` is CE's portrait-or-square pairing test; the view model already resolves the paired secondary page (~2293-2334) and the RTL flip is
  `EffectiveReadingMode == ReadingMode.RightToLeft`.
- **Clipboard.** `ClipboardHelper.CopyTextAsync` reaches the main window's clipboard for view models with no view. Avalonia 12.1.1 has
  `ClipboardExtensions.SetBitmapAsync(IClipboard, Bitmap)` (`DataFormat.Bitmap`).
- **No region selection in the reader.** There is no rectangle-drag tool on the comic canvas (that is pitch #7, "page notes and region clips", a later slice).
- **Toasts.** `ToastRequest(Title, Message, Severity, Actions)`; toasts with `Actions` are persistent, others close after 5 s (`MainWindow.axaml.cs:534-548`); a persistent
  toast is closed with `ToastCloseRequested`. The feedback taxonomy (`2026-09-06-feedback-notification-system-design.md` §1): Job (has duration) > ConfirmDialog
  (decision) > **Activity Center alert** (must survive until dismissed / references persistent state) > **Toast** (quick, low-stakes, no action needed). A break reminder
  is worthless once you looked away, so it is a Toast, not an alert (the pitch line "nudges go through the Activity Center" is read as "through the feedback system").
- **Adjustment pipeline for the warm shift.** Brightness/contrast/saturation are one 4x5 Skia colour matrix (`ImageAdjustmentMath.CreateColorMatrix`), gamma a second
  LUT pass, delivered to the render thread as `AdjustmentVisualData(Brightness, Contrast, Saturation, Gamma)` (`ReaderPageVisualHandler.cs` ~117, ~487) with the filter
  cached by record equality. A warmth term is one more diagonal scale on that matrix.
- **Key defaults free in the registry:** `P`, `H`, `W`, and `Ctrl+C` are unused by `KeyboardCommandRegistry` (the duplicate-default test guards this at build time;
  `MainWindow` global shortcuts are checked when the keys are wired).

## Scope

In: a live session tracker with a corner HUD (session time, pages, pages/min, estimated time left); copy page/spread to the clipboard and Save Spread as PNG/JPEG
(stitched); optional 20-20-20 break nudges as toasts; an optional schedule-based warm shift for comic pages; Preferences group, keys, palette entries.

Out (decided): copy of a selected region (deferred to the #7 region-clips slice); the Windows Share sheet (WinRT window interop for little over Copy and Save As);
HUD history or charts (Insights already has pace/streak tiles); enforcing or detecting an actual break; warm shift for the EPUB/PDF readers or the app chrome (Windows
Night Light does the whole screen); a colour-temperature slider; ramped warm shift.

## 1. Reading session clock and HUD (#17)

- **`ReadingSessionClock`** (pure, injectable time, new): `Tick(now, present)` accrues **active time** only while the user is present (reader visible and window active,
  F1's gate) **and** the last input was less than **2 minutes** ago; `NoteInput(now)`; `NotePageViewed(issueId, pageIndex)` adds to a set of distinct
  `(issueId, index)` pairs seen this visit; `Reset()`. A visit is the time between entering the reader and `GoBack`, surviving "continue to next issue"; a gap of more than
  30 minutes without presence also starts a new visit. It exposes `ActiveTime`, `PagesViewed`, `ActiveSinceBreak` (section 3) and a rolling `PagesPerMinute`
  (`PagesViewed` ÷ active minutes, `null` until at least 3 pages and 2 active minutes, so a first glance never shows a wild number).
- **Estimate.** `RemainingPages` = pages after the current one up to the story end (`_storyEndIndex` when known, else the last page); time left = `RemainingPages` ÷
  `PagesPerMinute`, shown as "~12 min left" ("<1 min left" under a minute, "—" while the pace is null or the issue is finished).
- **`SessionHudFormatter`** (pure): "24 min · 18 pages · 0.8/min · ~12 min left"; pieces drop from the right when a value is not available or the chip is narrow.
- **Placement:** a small non-interactive chip drawn in a canvas corner (bottom-left; one constant to move it after the on-screen look), visible whether or not the chrome
  is shown, at reduced opacity so it does not compete with the page. It hit-tests as transparent, so it never eats clicks or tap zones.
- **Visibility:** `AppSettings.ShowSessionHud` (default **off**) is the value at reader open; `Reader.ToggleSessionHud` (remappable, `ConflictContext.Always`,
  default `H`) and a palette entry flip it for the current visit. A profile may carry it (F2 `ReaderProfileState.ShowSessionHud`).
- **Wiring:** `NotePageViewed`/`NoteInput` are called from the same points that already run on a page change (`GoToPage`, the continuous handler, `Load`) and from the
  canvas's key/pointer handlers through one `NoteReaderInput()` method; the clock is ticked every 10 s by a dedicated `DispatcherTimer` that runs **only while the reader
  is present**, and its text is rebuilt at most every 10 s (not per frame, not per page boundary), so slice G's boundary handler stays O(1).

## 2. Copy and stitched-spread export (#22)

- **What is copied/exported:** the real decoded page content, like Save Page As (not a screenshot of zoom/pan/rotation and not the colour adjustments).
- **`Reader.CopyPage`** (remappable, `ConflictContext.Always`, default `Ctrl+C`) copies **what you see**: the stitched spread when spread mode is on and the current page is
  paired, otherwise the current page (in continuous mode, the nearest page). Context menu (`ReaderPageContextMenuBuilder`): "Copy Page", and, only when a spread is showing,
  "Copy Spread" and "Save Spread as PNG…/JPEG…"; the palette gets "Copy page", "Copy spread", "Save page as…", "Save spread as…" entries.
- **`SpreadComposer`** (new): `Compose(Bitmap first, Bitmap second, bool rightToLeft)` returns one bitmap using `SpreadLayoutMath.ComputeCombinedSize` (common height =
  the taller page, widths scaled and summed, no gap), the first page on the left for left-to-right and on the right for right-to-left, exactly the on-screen order. The
  pairing decision is the view model's existing one; the composer never decides eligibility.
- **`ClipboardHelper.CopyBitmapAsync(Bitmap)`** (`SetBitmapAsync` through the main window's clipboard). Decode and compose run off the UI thread; the clipboard call is on
  it. Success shows a 5 s toast ("Page copied" / "Spread copied"); a failure (no clipboard, decode failed) shows an error toast. Save Spread reuses
  `PickSaveFileAsync`/`PageExportService.TryExport` with the name "<series> - Pages 12-13".
- **Reuse, not duplication:** `SavePageAsAsync`'s decode-and-export core is factored so page, spread, copy and save share it (one `TryGetExportBitmap(includeSpread)`).

## 3. Break nudges (#18)

- **Rule:** with `AppSettings.BreakNudgesEnabled` on (default **off**), after **`BreakNudgeIntervalMinutes`** (default 20, range 10-60) of **active reading since the last
  break**, the reader raises one toast. A break is any pause long enough to drop presence for 2 minutes (or leaving the reader); it resets `ActiveSinceBreak`. The reader
  cannot tell that you actually looked away for 20 seconds, so the nudge is a prompt, not enforcement.
- **`BreakNudgePolicy`** (pure): given `ActiveSinceBreak`, the interval and the next-due mark, returns "nudge now" once and moves the mark by one interval (so it repeats
  every interval while you keep reading); **Snooze** moves the mark to now plus 10 minutes; a break resets it. It also refuses to nudge while a previous nudge toast is
  still open, and closes an unanswered one when a new one is due or the reader is left.
- **Channel:** a toast through `ReaderScreenViewModel.ToastRequested` (F2) with an action button **"Snooze 10 min"** (so it is persistent until dismissed, per
  `MainWindow`), title "Time for a break", message "Look at something 20 feet away for 20 seconds." Closed through a new `ToastCloseRequested` event wired to `CloseToast`.
  No Activity Center alert, no job. Nothing is persisted or logged.
- **Not while paused/away:** the clock's presence gate means no nudge fires while the window is in the background or the reader is not showing.

## 4. Warm shift (#18, second half)

- **Settings:** `WarmShiftEnabled` (default off), `WarmShiftStartMinutes` (default 1260 = 21:00), `WarmShiftEndMinutes` (default 420 = 07:00), `WarmShiftStrength`
  (0-100, default 40). Times are minutes after local midnight; the schedule may wrap midnight. Strength can be laid in by a profile (`WarmShiftEnabled`,
  `WarmShiftStrength` in `ReaderProfileState`; the schedule times stay global).
- **`WarmShiftSchedule`** (pure): `IsActive(TimeOnly now, int startMinutes, int endMinutes)` (start equal to end = never active; start after end wraps midnight).
  The view model evaluates it at `Load`, on the existing 60 s `_clockTimer` tick and when settings refresh; **constant strength inside the window, no ramp**.
- **`Reader.ToggleWarmShift`** (remappable, default `W`) and a palette entry force the tint on or off for the current visit (`bool?` override, cleared by `GoBack`).
- **Rendering:** `ImageAdjustmentMath.CreateColorMatrix` gains an optional `warmth` (0-1, default 0) that scales the output channels after saturation: R ×1, G
  ×(1 − 0.10·k), B ×(1 − 0.45·k) (constants named and unit-tested; tuned by eye on the user's screen). At 0 the returned matrix is **identical to today's**, asserted by
  test. `AdjustmentVisualData` gains `Warmth` (default 0, `None` updated), `PageCanvas` a `Warmth` styled property pushed the same way as brightness, and the "all zero =
  no filter" short-circuit in `ReaderPageVisualHandler` includes it. The tint affects comic pages only, not chrome, thumbnails or the palette.

## Settings and migration

One migration `AddReaderComfortSettings`, **no-op `Down()`**, `HasDefaultValue` on every column (the `PropertyBuilder`/HasDefaultValue gotcha from earlier slices):
`ShowSessionHud` (false), `BreakNudgesEnabled` (false), `BreakNudgeIntervalMinutes` (20), `WarmShiftEnabled` (false), `WarmShiftStartMinutes` (1260),
`WarmShiftEndMinutes` (420), `WarmShiftStrength` (40). Preferences → Reader gains a **COMFORT** group (`Tag="reader.comfort"` plus its `PreferenceIndex` entry): HUD toggle;
nudge toggle and interval picker; warm-shift toggle, start/end pickers (`SuggestBox IsStrict` with half-hour entries, not a `TimePicker`, keeping the dropdown-freeze
lesson) and a strength slider. Setting changes raise `ReaderDisplaySettingsChanged` so an open reader picks them up.

## Build order

1. Settings + migration + Preferences COMFORT group (+ `ReaderProfileState` fields and overlay lines); tests.
2. `ReadingSessionClock`, `SessionHudFormatter`, view model wiring, HUD chip, `Reader.ToggleSessionHud`; tests.
3. `BreakNudgePolicy`, `ToastCloseRequested`, nudge wiring; tests.
4. `WarmShiftSchedule`, matrix `warmth`, `AdjustmentVisualData`/`PageCanvas`/handler plumbing, `Reader.ToggleWarmShift`; tests.
5. `SpreadComposer`, `CopyBitmapAsync`, `TryGetExportBitmap`, `Reader.CopyPage`, context-menu and palette entries; tests.
6. Docs, wiki (`Reading.md`, `Preferences.md`, `Keyboard-Shortcuts.md`), memory, review checklist over the new XAML.

The user checks on screen after steps 2, 4 and 5 (the HUD chip and its numbers, the tint at night/forced, pasting a copied page and a spread into another app) and reports
what needs tuning (corner, opacity, tint constants).

## Testing

- **Pure:** `ReadingSessionClock` (presence gating, 2-minute idle pause, distinct pages, pace threshold, 30-minute visit reset, break reset); `SessionHudFormatter`;
  `BreakNudgePolicy` (first nudge, repeat, snooze, break reset, no stacking); `WarmShiftSchedule` (midnight wrap, equal start/end, boundaries); the colour matrix at
  `warmth = 0` equals the old matrix and at `k > 0` moves R/G/B as specified; `SpreadComposer` size and left/right placement (two solid-colour bitmaps, LTR and RTL);
  spread-name formatting.
- **View model:** HUD text appears after enough pages and time (injected clock); `ToggleSessionHud`/`ToggleWarmShift` are visit-scoped; the nudge raises a toast with the
  snooze action and closing on `GoBack`; `Reader.CopyPage` chooses spread vs page correctly (fake clipboard seam like `PositionWriter`); settings round-trip through
  Preferences; registry duplicate-default test with the four new keys.
- **Data:** migration defaults and round trip.
- **Not testable here:** the real clipboard paste into other applications, the tint's look, the chip's readability over bright and dark pages, and whether the 2-minute idle
  rule feels right. Those are the user's on-screen checks.

## Open risks

- **Boundary handler budget.** The HUD must add nothing measurable to `OnCurrentContinuousPageIndexChanged`; `NotePageViewed` is a hash-set add and a stopwatch read, and
  the text is rebuilt on the 10 s tick. The Ctrl+Shift+P overlay's boundary line is the check.
- **Idle rule.** Dense pages can take longer than 2 minutes to read, which would pause the clock and slightly under-count; the constant is one named value.
- **Clipboard format.** Some receiving apps want PNG bytes rather than a DIB; `SetBitmapAsync` chooses the formats. If pasting fails in a target the user cares about, the
  fallback is a `DataTransfer` with an explicit PNG item.
- **Shared working tree:** `ReaderScreenViewModel.cs`, `PageCanvas.cs`, `ReaderPageVisualHandler.cs`, `ReaderScreen.axaml`, `KeyboardCommandRegistry.cs`,
  `ReaderPageContextMenuBuilder.cs`, `AppSettings.cs`, the model snapshot and `MainViewModel.cs` are already modified by earlier slices, F1 and F2; edits stay narrow and every
  new migration's `Up()` is read before it is kept.

## Implementation notes (2026-09-25)

Built in the order given under "Build order". Where the code differs from the design:

- **Presence gate:** F1 kept its gate inside the screen's supervisor timer, so the same 500 ms tick now also calls `ReaderScreenViewModel.SetUserPresent` (visible and window active). Key presses, clicks, wheel
  turns and gamepad frames call `NoteReaderInput`. One 10 s `DispatcherTimer` (`OnSessionTick`) drives the clock, the chip text, the nudge check and the warm-shift schedule; page views are recorded by
  `GoToPage` and by the continuous handler (`NotePageViewed`, a hash-set add), so the timed boundary handler stays O(1).
- **A nudge never fires while the reader is away** (found by a test: the accumulated time was already past the interval when the user left). Whether a nudge toast is still open is a two-minute heuristic,
  because the toast host does not report a dismissal; leaving the reader or a break closes it through the new `ToastCloseRequested` event (wired to `MainViewModel.CloseToast`).
- **Warm shift** is `ImageAdjustmentMath.CreateColorMatrix(..., warmth)` (row scales R x1, G x0.90, B x0.55 at full warmth, exposed as `WarmGreenLoss`/`WarmBlueLoss`; identical matrix at 0, asserted by test),
  `AdjustmentVisualData.Warmth`, `PageCanvas.Warmth`. A session profile can switch it on and set its strength; toggling with **W** forces it for the visit and shows a toast.
- **Copy:** `SpreadComposer` (layout math reuses `SpreadLayoutMath.ComputeCombinedSize`; Skia compose; the Avalonia wrapper round-trips through PNG), `ClipboardHelper.TryCopyBitmapAsync` (keeps the last
  bitmap alive until the next copy), `ReaderScreenViewModel.Export.cs` (`BuildExportBitmap`, `CopyPage`/`CopySpread`/`CopyWhatYouSee`, `SaveSpreadAs…`, and Save Page As now shares the same flow). The clipboard
  is a test seam (`BitmapClipboardWriter`). The context menu gained Copy Page (always) and Copy/Save Spread (only while a spread shows).
- **Settings and migration** `AddReaderComfortSettings` (no-op `Down()`), Preferences group `reader.comfort` (start and end are strict half-hour pickers, not a `TimePicker`); values are clamped when saved.
- **Deferred as decided:** region copy (pitch #7), the Windows Share sheet.
- **Not verified:** pasting into other apps, the chip's look, the tint's look and whether the two-minute idle rule feels right; all on-screen checks.
- **Chip corner:** moved from bottom-left to **bottom-right** after the first on-screen look: the View cluster (bottom-left) drew over it whenever the chrome showed; no cluster uses the bottom-right corner.
