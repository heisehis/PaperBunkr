# Issue Properties Editor

*Date: 2026-08-07. First slice of docs/ce-feature-inventory.md §A ("Comic metadata editing" —
flagged as the single biggest gap: the Detail screen only edits `Series.ContentType` today, despite
`Issue.cs` already carrying nearly the full ComicInfo.xml field set with zero edit UI anywhere).
Scoped after directly reading CE's real `Dialogs/ComicBookDialog.cs` (the single-book editor —
bulk editing is a separate `MultipleComicBooksDialog` class, out of scope here), not the audit
summary. §A also lists bulk edit, copy/paste, templated text, Quick Rating+Review popup, Undo/Redo,
and Reader-canvas-territory items (per-page tagging, per-page rotation, bookmarks) — all deliberately
separate future specs; this one covers only the single-book properties editor, the foundation the
others build on.*

## 1. Scope, vs. CE's real dialog

CE's `ComicBookDialog` has five tabs: Summary, Details, Plot & Notes, Catalog, Custom (Pages/Colors
tabs excluded — those are Reader-canvas/cover-art territory, already tracked separately). This spec
builds **Summary, Details, and Plot & Notes only**:

- **Catalog** (ISBN, Released/Opened/Added dates, Book Location/Price/Condition/Store/Owner/Notes/
  Collection Status) is excluded — not a new cut, `Issue.cs`'s own code comments already defer
  exactly this field set ("Book\*" fields) to "a dedicated Book Collection panel... as a separate
  future feature." Consistent, not coincidental: same collector-fields concept CE itself groups
  together.
- **Custom** (free-form name/value grid) is excluded — `IssueCustomValue` schema exists but this is
  a genuinely separate small feature (its own add/edit/remove grid UI), not core ComicInfo editing.
- CE's two Comic-Vine-Scraper-automation toggles on the Details tab ("Include in Updates"/"Proposed
  Values") are excluded — no scraper integration exists in Paperbunkr.
- CE's Plot & Notes tab nests Summary/Notes/Review in their own 3-tab sub-control; this spec
  flattens that to three stacked multi-line text boxes on one tab — same fields, less UI nesting,
  no loss of function.
- No autocomplete/suggestion lists for Publisher/Imprint/Format/AgeRating/etc. (CE has them via
  `AutoCompleteMode=SuggestAppend` combos) — plain text fields for v1, a small deferred fast-follow.
- No numeric spinners — CE itself uses plain textboxes with only a `MaxLength` for Number/Volume/
  Count/Year/Month/Day, so Paperbunkr matches that exactly (see §4).
- **Verified via grep against CE's real source**: the single-book dialog has zero "mixed value"
  logic anywhere — that only exists in the separate bulk-edit dialog. Nothing to replicate here.

## 2. Entry point and routing

Issue cover tiles on the Detail screen's Issues tab currently have no click behavior at all — not
even an `Id` on `IssueCardSample`. This spec:

- Adds `Id` (int) to `IssueCardSample`.
- Adds an Avalonia `ContextMenu` (new to this codebase — first use) to each tile in
  `DetailTabs.axaml`'s issue-tile `DataTemplate`, with one item: "Edit Properties" → a new
  `EditIssuePropertiesCommand` (`RelayCommand<IssueCardSample>`) on `DetailTabsViewModel`.
  Left-click stays a no-op for now (deliberately not wired to anything else in this spec — e.g.
  "click opens Reader" is a separate decision for a future spec, not bundled in here).
- New full-screen route, `"issueProperties"`, following the exact same pattern as
  `"reader"`/`"detail"` in `MainViewModel`: a new `IssuePropertiesScreenViewModel` with
  `Load(int issueId)` and a `goBack` callback. `DetailTabsViewModel` gains a
  `goToProperties: Action<int>` constructor parameter (alongside its existing context-factory
  param), threaded from `DetailScreenViewModel` (gains the same parameter) from `MainViewModel`
  (`GoIssuePropertiesForIssue(int issueId)` → `IssueProperties.Load(issueId); CurrentScreen =
  "issueProperties"`). Back returns to `"detail"` (`GoDetail`, the same target `Reader`'s back
  button already uses) — always correct since the Detail screen's Issues tab is the only entry
  point.
- `IsIssueProperties` computed flag + `OnCurrentScreenChanged` wiring, same as every other screen.

## 3. Data flow — edit buffer, not a live tracked entity

`IssuePropertiesScreenViewModel.Load(issueId)` opens a context, reads the `Issue`, copies every
editable field into plain `[ObservableProperty]` fields on the ViewModel, and **disposes the
context immediately** — nothing stays open across the edit session.

- **Save**: opens a fresh context, re-fetches the `Issue` by id, writes every buffered field across,
  `SaveChanges()`, disposes, then invokes `goBack`.
- **Cancel**: just invokes `goBack`. No context is ever created by Cancel — the buffer is discarded
  by simply navigating away and `Load` overwriting it next time the screen opens. This makes Cancel
  unambiguous by construction (chosen over keeping a tracked entity open and relying on "dispose
  without `SaveChanges`" as the only thing preventing a write — more explicit, easier to test:
  "Cancel touches the database zero times" is directly assertable).
- Numeric fields (`Volume`, `Count`, `Year`, `Month`, `Day` — all `int?` on `Issue`) are buffered as
  `string` (matching CE's plain-textbox-with-`MaxLength` treatment, no numeric spinner) and parsed
  with `int.TryParse` on Save; empty or non-numeric input parses to `null`. `Number` stays `string?`
  end-to-end (already free text on the entity, e.g. "12", not a real number).
- `Rating`/`CommunityRating` (`float?`) are buffered as plain `int?` (0–5) — see §4.

## 4. Fields per tab

**Summary** (read-only info + the two rating widgets):
- Read-only: cover preview (`CoverImageCache.Get`, same source as everywhere else), `Type` (fixed
  text, "Comic Book" — Paperbunkr has no other book type), `FilePath`, `PageCount`.
- **My Rating** / **Community Rating**: a 5-star click widget, new but small — five `Button`s per
  row (Content = "★"/"☆" per position via a converter keyed on `ConverterParameter` vs. the bound
  int?), `SetMyRatingCommand`/`SetCommunityRatingCommand` (`RelayCommand<int>`, star position 1–5).
  Clicking the currently-set star clears the rating to `null` (click star 3 when already at 3 →
  unrated) — standard toggle-to-clear star-widget behavior. Whole-star only, no half-star support
  in v1 (CE's `RatingControl` may support finer granularity; Paperbunkr's `Issue.Rating` only drives
  a `> 3` Favorites-smart-list threshold today, so whole-star precision is sufficient and simpler).

**Details** (plain `TextBox` unless noted): Number, Volume, Count, Title, AlternateSeries,
AlternateNumber, StoryArc, StoryArcNumber, SeriesGroup, Publisher, Imprint, Format, Year, Month,
Day, Genre, Tags, Writer, Penciller, Inker, Colorist, Letterer, CoverArtist, Editor, Translator,
AgeRating, LanguageISO (all plain text), BlackAndWhite (`CheckBox`).

**Plot & Notes** (plain `TextBox` unless noted): Characters, Teams, MainCharacterOrTeam, Locations,
Web, ScanInformation (single-line), Summary, Notes, Review (three stacked multi-line `TextBox`es,
`AcceptsReturn="True"`, per §1's flattening of CE's nested sub-tabs).

Not editable here (out of scope, see §1): `SeriesId`/series name (a move-to-different-series
operation, its own future feature), `ReadingModeOverride` (existing per-issue escape hatch, no UI
yet — small, separate, not bundled here), all `Book*`/`ISBN` Catalog fields, `CustomValues`,
`FileIsMissing`/`Checked`/`LastPageRead`/timestamps (read-state, not metadata).

## 5. Testing

- `IssuePropertiesScreenViewModelTests` (new, own context-factory seam like every other DB-touching
  ViewModel): `Load` populates every buffered field correctly from a seeded `Issue`; `Save` writes
  every field back and calls `goBack`; **Cancel never calls `SaveChanges`** — assert the underlying
  row is byte-for-byte unchanged after a Cancel that followed real buffer edits; numeric fields
  round-trip correctly including the empty-string→`null` case; star-rating toggle-to-clear behavior
  for both `Rating` and `CommunityRating`.
- `DetailTabsViewModelTests`: `EditIssuePropertiesCommand` invokes the `goToProperties` callback
  with the right issue id.
- `MainViewModelTests` if one exists, otherwise a light manual check: `IsIssueProperties` flag
  flips correctly, back returns to Detail.
- Manual verification: same no-GUI-automation approach as every prior spec — build + run real
  tests, then ask the user to right-click a real issue tile, edit a handful of fields across all
  three tabs, Save, and confirm the Detail/Reader screens reflect the change; separately confirm
  Cancel truly discards edits (edit a field, Cancel, reopen, old value still there).
