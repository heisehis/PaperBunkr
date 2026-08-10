# Preferences: Reader Tab Additions

*Date: 2026-08-10. Closes out the Beta backlog's "Preferences: Reader tab (last of 5 tabs)" entry
— which was stale: a Reader tab already exists (shipped 2026-08-07 alongside RTL navigation, with
Right-to-Left/Display/Keyboard Shortcuts groups), the backlog entry just hadn't been updated to
say so. This spec covers what's actually still missing from it, not a from-scratch tab.*

## 1. Scope

CE's actual Preferences → Reader section (`_reference/ComicRackCE/ComicRack/Config/Settings.cs`)
was checked directly rather than assumed, per the standing CE-parity rule. Most of it gates
capabilities Paperbunkr doesn't have yet — magnifier zoom, on-screen overlays, auto-scroll,
continuous-scroll smoothing, hardware-acceleration toggles (Avalonia already hardware-accelerates
by default; no equivalent toggle needed) — adding checkboxes for those now would be dead controls,
the same anti-pattern the P6 pass already hunted down and fixed elsewhere in this app. `Settings.
HardwareFiltering` already has a real Paperbunkr equivalent (`HighQualityPageDisplay`, shipped).

Ships, because each backs a real, already-shipped capability:
- **Reset zoom on page change** — CE: `Settings.ResetZoomOnPageChange`, default `false`. Both CE's
  default and Paperbunkr's own pre-existing zoom behavior (persists across page turns within a
  session) already agree, so this only changes anything for someone who deliberately turns it on.
- **Mouse wheel scroll speed** — CE: `Settings.MouseWheelSpeed` ("lines per mouse scrolling"),
  default 2.0, UI range 0.5–5.0 (CE's own trackbar min/max). Confirmed from source
  (`ComicDisplay`'s `scrollLines = ... * MouseWheelSpeed`) that this governs plain-wheel *pan*
  speed, not Ctrl+wheel zoom — replaces `PageCanvas`'s previously-fixed `WheelPanStep` constant.
- **Default fit mode / default auto-rotate** — not a CE setting at all; closes a TODO from this
  session's own earlier work. docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-
  controls-design.md §3 deliberately left these as fixed code constants "since no Reader
  Preferences surface exists yet to edit one" — it exists, this is that surface.

## 2. Data model

Four new `AppSettings` columns (one migration, `AddReaderPreferencesTabSettings`):
`ResetZoomOnPageChange` (bool, default false), `MouseWheelSpeed` (double, default 2.0),
`DefaultPageFitMode` (`ImageFitMode`, default `FitWidth` — matches the constant it replaces),
`DefaultAutoRotate` (bool, default false).

`DefaultPageFitMode`'s enum-as-string column needed both `HasDefaultValue` (unlike other enum
columns in this context, e.g. `Series.ContentType` — this `ALTER TABLE` runs against a table with
an existing row, the `AppSettings` singleton, and without a DB-level default EF's own fallback is
an empty string, not a parseable `ImageFitMode`) and an explicit `HasSentinel` (silences EF's
separate warning about `Original`, being the enum's CLR default, being ambiguous with "unset" on
insert — inert in practice since `AppSettings` is only ever inserted once, via
`GetOrCreateAppSettings`, with a real C# default that isn't `Original`).

## 3. Wiring

- `ReaderScreenViewModel.Load` reads all four from `AppSettings` instead of fixed constants;
  `GoToPage` resets `ZoomLevel` when `ResetZoomOnPageChange` is on.
- `PageCanvas` gains a `WheelPanStep` bindable property (default 1.0, reproducing the constant it
  replaces) feeding its existing plain-wheel pan handler — the Novels PDF reader, which shares this
  control but never binds this property, is unaffected.
- `PreferencesScreenViewModel` follows this class's own two established patterns rather than
  inventing a third: the three non-enum settings are plain `[ObservableProperty]` + `On*Changed`
  hooks (matching `HighQualityPageDisplay`); `DefaultPageFitMode` is a `ComboBox` +
  `SelectedItem` two-way binding + changed-hook (matching the existing font-family picker) rather
  than the Reader screen's own flyout-of-buttons (that shape fits a toolbar button, not a
  Preferences row).

## 4. Testing

Mirrors this file's own established shape exactly: `EnsureLoaded_Populates*FromAppSettings` +
`Toggling*_PersistsToAppSettings` pairs per setting in `PreferencesScreenViewModelTests`, plus
`ReaderScreenViewModelTests` coverage for `ResetZoomOnPageChange`'s actual page-turn behavior and
the default-fit-mode/auto-rotate read path. 400 tests pass across the whole solution.

**Manual-only, not done:** on-screen verification of the new controls — no unattended desktop GUI
automation available for this project, same caveat as every other Preferences/Reader addition.
