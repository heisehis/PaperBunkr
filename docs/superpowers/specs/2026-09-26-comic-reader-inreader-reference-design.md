# Comic reader — In-reader reference (slice E1) design

Date: 2026-09-26. Status: **built 2026-09-26** (uncommitted; on-screen check by the user pending, see "Implementation notes" at the end). Design settled in one grilling round ("recommended" on all questions but Q10, which the user challenged and which is corrected below).
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`, slice E "Info & compare", first half: **#28 in-reader info panel**, **#29 pinned reference page**, **#7 page notes and region clips**. The second half, **#11 two-edition compare**, is its own spec
(`2026-09-26-comic-reader-compare-design.md`), built after this one.
Slices A, B, C, D, F and G are built and uncommitted; this builds on the same shared working tree.

## Facts this design rests on (verified 2026-09-26)

- **#28 has no CE counterpart in the reader.** CE's info panel is the library sidebar (`MainForm.ToggleInfoPanel`, `ISidebar.HasInfoPanels`); `ComicInfoUI` is a plugin script type for it
  (`PluginEngine.ScriptTypeComicInfoUI`). CE's reader (`ComicRack.Engine.Display.Forms`: `NavigationOverlay`, `TextOverlay`, `GestureOverlay`) shows no metadata panel. The panel is Paperbunkr-original; a plugin hook for it is out of scope.
- **Everything the panel shows is already on `Issue`:** `Title`, `Number`, `Count`, `Volume`, `Summary`, `Writer`/`Penciller`/`Inker`/`Colorist`/`Letterer`/`CoverArtist`/`Editor`/`Translator`, `Publisher`, `Imprint`, `Characters`/`Teams`/`Locations`
  (semicolon or comma separated strings), `StoryArc`/`StoryArcNumber`, `SeriesGroup`, `Tags`, `Rating`, `AgeRating`, `Year`. Where the issue sits in an Event or Reading List comes from slice A's `ReadingOrderResolver.ResolveContext`
  (label, position, total, previous and next issue ids), already loaded by `ReaderScreenViewModel.RefreshContextStrip`.
- **Comic pages already have named bookmarks with a note:** `IssueBookmark` (issue id, 0-based page number, `Label`, `Note`, `CreatedTime`), listed in the reader as `Bookmarks`. A page note is different: it needs no bookmark.
- **The PDF reader has the clip machinery** (books-reader-ergonomics design): `CaptureOverlay` (drag a rectangle over the page, `Views/CaptureOverlay.cs`), `BookAnnotationCaptureService.CropAndSave(Bitmap, fractions, directory)` (Skia crop to PNG), a `BookAnnotationImage` entity
  and a captures drawer. Slice F3 added the clipboard (`ClipboardHelper`) and page/spread export the reader can reuse.
- **The reader's display bitmap is not a stable handle.** `ReaderImagePipeline` keeps display-tier bitmaps only inside its virtualization window and evicts the rest (a dropped reference, never a `Dispose`), and since slice C the bitmap is cropped and
  levels-stretched when those settings are on. A pinned page therefore holds its own small scaled copy.
- **Drawer and chrome.** The right-hand "Reader Tools" drawer has PAGE, ADJUST and TRANSITION sections; the Ctrl+K palette (`ReaderPaletteCatalog`, `ReaderScreenViewModel.Palette.cs`) and the page right-click menu (`ReaderPageContextMenuBuilder`) take new
  commands cheaply. Toasts go through `ToastRequested`. Remote (mirrored) issues store bookmarks and progress on the client's own row, opened with `PaperbunkrDb.CreateContext(includeRemote: true)`.
- **Shared working tree:** `ReaderScreenViewModel*.cs`, `ReaderScreen.axaml(.cs)`, `PageCanvas.cs`, `KeyboardCommandRegistry.cs`, `KeyOption.cs`, `AppSettings.cs`, `PaperbunkrDbContext.cs`, the model snapshot, `PreferencesScreenViewModel*.cs`, `ReaderProfiles.cs`
  and `MainViewModel.cs` are already modified by earlier slices; edits stay narrow.

## Scope

In: a read-only info panel (#28); a floating pinned page (#29); page notes, region clips and a Markdown export (#7).
Out: editing metadata from the panel (Issue Properties owns that), a plugin hook for the panel, pinning a page of an issue you are not reading, note search, note sync, and anything for the Books/PDF reader.

## Design

### #28 — Info panel

A left-hand slide-in panel (the tools drawer stays on the right), read-only, opened by `I` (`Reader.ToggleInfoPanel`, remappable, `ConflictContext.Always`), a palette entry "Info panel", and a button in the tools drawer. It works in paged and continuous mode, closes on `I`, Escape
or a click outside, and does not move the page (it overlays). Sections, each hidden when empty: series and issue line (`Series #3 of 12 · Title`), **Summary**, **Credits** (role: names, one line per role that has any), **Characters**, **Teams**, **Locations**
(as chips), **Story arc** (name and number, and the reading context line and previous/next buttons from the context strip), **Tags**, rating and age rating, publisher/imprint/year. A footer button "Open details" goes to the issue's Detail screen (a normal drill navigation) and
"Edit properties" opens Issue Properties, the same callbacks the reader's breadcrumb already uses.

*Spoilers.* The **Summary starts collapsed** behind "Show summary" (a summary or character list can spoil); the choice lasts for the visit. Preferences → Reader → a new **Info panel** group has "Show the summary by default" (off). Characters are shown as they are: they are part
of the cover of nearly every issue.

*Data.* `ReaderInfoPanelViewModel` (new, owned by the reader view model, rebuilt on `Load` and on a live change of the issue's metadata) reads the `Issue` and the `ReadingContext` and exposes plain strings and lists; no database work happens on the UI thread beyond what `Load` already does.
Comma or semicolon separated names are split with the app's existing token splitting (`TokenSplitter` or its equivalent, checked at build time), not a new parser.

### #29 — Pinned reference page

*Pinning.* "Pin this page" on the page right-click menu, the palette ("Pin this page as a reference") and a key (`Reader.PinPage`, default `Shift+P`, remappable). Pinning again replaces the pin; "Unpin" (same places, plus clicking the pin's X) removes it. The pin is a **scaled copy of the
displayed page bitmap, about 600 px on its long side**, taken at pin time, so page-window eviction, later crop or levels changes and reading on cannot affect it. It carries a caption ("Page 5 of Series #3").

*Where it lives.* A floating panel over the reader (a `Border` in `ReaderScreen.axaml` with an `Image`), draggable, snapping to the nearest corner on release, three sizes (small 160 px, medium 260 px, large 400 px wide; a cycle button, and the mouse wheel over it) and 85% opaque until hovered. It stays
visible while paging, zooming and in either reading mode, ignores clicks that are not on it (`IsHitTestVisible` only on the panel itself), keeps out of the way of the chrome clusters (corner snap positions are inset past them), and is hidden while the info panel or a modal overlay is open.

*Across issues (correcting the round's Q10 answer).* The pin **survives moving to another issue during the visit** (the next issue, the context strip's previous/next, the end card's "Next issue"), because it is an independent copy: that is the recap-page use case, pin the "previously..." page and read on.
It is cleared when the reader is left (`GoBack`) or unpinned. What stays out of scope is pinning a page of an issue you are not currently reading. Not persisted: a pin is a reading aid, not data.

### #7 — Page notes and region clips

*Notes.* New `PageNote` (`Id`, `IssueId`, `PageNumber` 0-based, `Text` up to 2000 chars, `CreatedTime`, `ModifiedTime`; unique on issue + page, cascade with the issue). Add or edit from the tools drawer's new **NOTES** section (a text box for the current page with Save and Delete),
the palette ("Note on this page") and the page menu ("Add note…"). A page with a note gets a small marker on the page-dot strip and the thumbnail rail. The section lists every note and clip of the issue by page; clicking a row jumps to that page. Bookmarks keep their own `Note`; the section shows a bookmark's note
read-only next to the page so nothing is lost or duplicated.

*Clips.* "Clip a region" (palette, page menu, drawer button, `Reader.ClipRegion`, default `Ctrl+Shift+C`, remappable) turns on `CaptureOverlay`; dragging a rectangle saves a `PageClip` (`Id`, `IssueId`, `PageNumber`, the rectangle as page fractions, `ImagePath`, `Caption`, `CreatedTime`). The PNG is cut from the **displayed page bitmap** (up to 2560 px wide, so it includes
auto-crop and levels but not the colour sliders) with `BookAnnotationCaptureService` into `%AppData%\Paperbunkr\annotations\clips\{issueId}\`. Each clip in the NOTES section has a thumbnail, an editable caption, **Copy** (`ClipboardHelper`), **Save as PNG…** and **Delete** (file and row). Paged single-page reading only; in
continuous or spread layout the command toasts why, like guided view does.

*Export.* "Export notes and clips…" in the NOTES section and the palette writes `{Series} #{Number} - notes.md` with a heading per page, the note text, and clip images copied to a sibling `notes-images` folder and linked relatively, through the existing file picker service.

*Storage details.* Migration `AddPageNotesAndClips` (two tables, no-op `Down()`), both keyed on the issue's own row so a mirrored remote issue works exactly like bookmarks do. Deleting an issue removes its notes and clips through the cascade; the PNG files are removed by `LibraryDeletionHelper.RemoveIssue`
(a small addition) so nothing is orphaned. `RemoteRowIsolationTests`' allowlist is reviewed for any new file that uses `includeRemote: true`.

### Settings, keys, profiles

`AppSettings.InfoPanelShowSummary` (bool, off). Keys `Reader.ToggleInfoPanel` (`I`), `Reader.PinPage` (`Shift+P`), `Reader.ClipRegion` (`Ctrl+Shift+C`), all checked against the registry's conflict rules at build time. No profile fields (these are actions and content, not display settings).

### Notifications

Feedback is toasts (pinned, note saved, clip saved, exported) via `ToastRequested`; nothing goes through the Activity Center because none of it is a job or an alert.

## Build order

1. Data: `PageNote`, `PageClip`, `AppSettings.InfoPanelShowSummary`, migration, `LibraryDeletionHelper` file cleanup. 2. `ReaderInfoPanelViewModel` and the panel, key, palette, Preferences group. 3. Pinned page (copy, panel, snap, sizes, survive-issue-change, clear on leave). 4. Notes (table, NOTES
section, marker, jump). 5. Clips (overlay, save, thumbnails, copy/save/delete) and Markdown export. 6. Docs, wiki, memory, review checklist.

## Testing

- Pure: token splitting for the panel lines, the corner-snap and size math for the pin, the note/clip list ordering and the Markdown writer (against a temp folder).
- View model: the panel's sections show and hide with the data, the summary is collapsed by default and the setting flips it, previous/next use the context strip's ids, "Open details" and "Edit properties" call their callbacks; a pin is a copy, survives `LoadIssue` of another issue and is cleared by `GoBack`; note add/edit/delete and the unique-per-page rule; clip save and delete (file and row), continuous-mode toast.
- Headless canvas/screen: `I` toggles the panel, `Shift+P` pins, the pinned panel does not take clicks meant for the page, dragging snaps to a corner.
- Data: migration defaults and cascade; `RemoteRowIsolationTests` allowlist.

## Not verified by design

How the panel, the floating pin and the clip flow look and feel on screen, the pin's drag and snap with a real mouse and touch, and clip quality on real pages; those are the user's on-screen checks.

## Implementation notes (2026-09-26)

Built in the order given under "Build order". Where the code differs from the design above:

- **One shared key-binding property.** `PageCanvas.ExtraKeyBindings` (`PageCanvas.KeyBindings.cs`, a list of `KeyCommandBinding(gestures, command)`) carries `Reader.ToggleInfoPanel` (`I`), `Reader.PinPage` (`Shift+P`) and `Reader.ClipRegion` (`Ctrl+Shift+C`, paged only) instead of a gesture and a command property per key on the canvas. All three are in the remappable registry.
- **Info panel.** `ReaderInfoBuilder` (pure) makes a `ReaderInfoModel`; `ReaderInfoPanelViewModel` holds it; the panel closes on Escape through `MainViewModel.Escape`, and "Open details" and "Edit properties" are `OpenDetailsRequested`/`EditPropertiesRequested` events the shell maps to the Detail screen and Issue Properties. The pin is hidden while the panel is open. `Genres` and `Tags` (issue tag rows) are chips too. Characters are split with `SmartListLeafEvaluator.SplitValues`, the app's existing splitter, then de-duplicated.
- **Pin.** `ReaderPinMath` (sizes 160/260/400, corner from the release point, copy at most 600 px on the long side never enlarged, chrome-clearing margins) and `ReaderScreenViewModel.Pin.cs`. It rests in the top-right by default, is a copy taken with `CreateScaledBitmap` (never disposed: a dropped reference), survives loading another issue and is cleared by `GoBack`, exactly as corrected in the design. Drag, corner snap and wheel resize are in the code-behind; a press on its buttons is the button's own.
- **Notes.** The NOTES section sits after ADJUST in the drawer. A page dot with a note is tinted (`ReaderThumbnailSample.HasNote`, carried through every thumbnail rebuild). A bookmark's own note is read from its database row (the reader's bookmark summary does not carry it) and listed read-only.
- **Clips.** `CaptureOverlay` (the PDF reader's drag rectangle) needed one fix to work over a canvas in a plain grid: a bare `Control` is only hit-testable where it draws, so while capture mode is on it now fills itself with a transparent brush (this also makes the PDF reader's overlay reliable). Deleting a clip defers the list rebuild one dispatcher tick (CLAUDE.md, "don't remove a control from inside the event it is raising"). The export is `NotesMarkdownExporter` (pure, tested against a temp folder).
- **Deleting an issue** removes its clip PNGs through `LibraryDeletionHelper.RemoveIssue` (rows go by cascade).
- **Settings:** `AppSettings.InfoPanelShowSummary`, migration `AddPageNotesAndClips` (no-op `Down()`), tables `PageNotes` and `PageClips`, a Preferences → Reader → Info panel group.
- **Not verified:** how the panel, the pin (drag and snap with a real mouse and touch) and the clip flow look and feel, and clip quality on real pages.
