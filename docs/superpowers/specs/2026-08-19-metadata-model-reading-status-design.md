# Metadata Model: Series.ReadingStatus

**Date:** 2026-08-19
**Status:** Approved, implemented

## Context

Prompted by an adversarial architecture review of an external "PaperBunkr Canonical Metadata Model"
proposal document (`docs/alpha-roadmap.md`'s Metadata Model platform section covers Phases 1-6a;
this is unrelated follow-on work the review recommended adopting, not part of that platform doc's
own phase numbering). The review's one clearly-missing, clearly-valuable finding: PaperBunkr tracks
*publisher* status (`SeriesStatus`: Unknown/Ongoing/Completed/Cancelled/Hiatus) but has no explicit
*user* reading-progress status (Planned/Reading/Completed/Dropped/etc.) — `OpenCount`/`LastPageRead`/
`OpenedTime` can infer "started" but nothing represents "dropped" or "plan to read."

## Design

`ReadingStatus` (`src/Paperbunkr.Data/Entities/ReadingStatus.cs`): `Unknown, Planned, Reading,
Completed, Paused, Dropped, ReReading`. No CE precedent (confirmed via source search — CE tracked
read/unread only per-issue, never a series-wide user intent) — same "deliberate new feature, not
parity" footing as `SeriesStatus`, and modeled identically: a `Series`-level enum, `Unknown` as the
default (matches `ContentType`/`SeriesStatus`/`ColorMode`), `HasConversion<string>()` +
`HasDefaultValue`/`HasSentinel` in `PaperbunkrDbContext`.

Deliberately kept **separate** from `SeriesStatus` — the review's "provider status vs. user status"
distinction is real: a series can be `SeriesStatus.Ongoing` (still being published) while the reader
is `ReadingStatus.Dropped`, or `SeriesStatus.Completed` while the reader is `ReadingStatus.Planned`.

### Write paths (mirrors `SeriesStatus`'s own precedent exactly)

- **Bulk Edit** — new "Reading Status" row in `BulkFieldRegistry.All`, `FieldKind.Enum`, `Get`/`Set`
  through `i.Series.ReadingStatus`.
- **Library grid context menu** — new "Set Reading Status" submenu at all 13 card-size template
  sites in `LibraryScreen.axaml`, one `[RelayCommand]` per value in `LibraryScreenViewModel`
  (`SetSeriesReadingStatus*`), no `LoadFromDatabase()` reload (same reasoning as `SetSeriesStatus`:
  Library doesn't sort/group by it).
- **Automatic** — the one exception to "every prior Series-level enum here is 100% user-driven."
  `ReaderScreenViewModel.Load` sets `ReadingStatus.Reading` on an issue's first open, but only when
  currently `Unknown` or `Planned` — never overwrites `Completed`/`Paused`/`Dropped`/`ReReading`.
  Every other transition stays manual, matching this codebase's minimal-automation stance.

### Smart Lists

New `SmartListField.ReadingStatus`, wired as a `SmartListDataType.Text` selector
(`i => i.Series?.ReadingStatus.ToString() ?? string.Empty`) — same shape as `ContentType`/
`ReadingMode`, not a new data type.

### Home screen

`HomeFeedResolver.GetContinueReading` excludes series marked `Dropped` — a dropped series
resurfacing in "Continue Reading" would directly contradict the status the user just set. No other
Home module touches `ReadingStatus` this pass.

## Testing

- `LibraryScreenViewModelTests`: `SetSeriesReadingStatus*` commands persist to the right series only.
- `BulkIssuePropertiesScreenViewModelTests`: Reading Status field round-trips through single- and
  multi-series selections, matching the existing `Status`/`ContentType` test shape.
- `ReaderScreenViewModelTests`: first `LoadIssue` sets `Reading` from `Unknown`; a `Dropped` series
  is not silently reset to `Reading` by opening an issue.
- `HomeFeedResolverTests`: a `Dropped` series with an in-progress issue is excluded from
  `GetContinueReading`.

All new/existing tests pass: 275 `Paperbunkr.Data.Tests`, 668 `Paperbunkr.App.Tests`.
