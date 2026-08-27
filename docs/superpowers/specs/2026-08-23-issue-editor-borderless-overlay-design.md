# Issue Properties / Bulk Editing: Borderless Overlay (Not a Screen Swap)

**Date:** 2026-08-23
**Status:** Approved, pending implementation

## Context

`IssuePropertiesScreenViewModel`/`BulkIssuePropertiesScreenViewModel` are currently reached by
setting `MainViewModel.CurrentScreen` to `"issueProperties"`/`"bulkIssueProperties"` - a full
screen-swap that replaces whatever was on screen (Detail, MangaDetail, or Library, for the "add a
physical book" placeholder flow) with a full-page editor. User feedback from the original ship
(`[[project-paperbunkr-issue-properties-editor]]`) flagged the layout as "too boxed," deliberately
deferred at the time. Revisiting now: make both editors a **borderless floating panel over a dimmed
backdrop** instead of a full-screen route - a "child window" look without introducing a real second
OS `Window`.

This exact pattern already exists twice in this codebase - `MigrationOverlay` and, more recently,
`ReadingListPropertiesOverlay` (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md): a
`Border Background="#B0000000"` scrim in `MainWindow.axaml`, centered `Grid` containing a
`CornerRadius` panel with no title bar, and a small round Close button pinned outside its top-right
corner. `MainViewModel.Escape()`'s own doc comment already lists Issue Properties/Bulk Editing
alongside Migration as screens with no native dialog-Escape behavior "because none of them are real
Avalonia Windows/Popups" - this change makes Issue Properties/Bulk Editing follow the *overlay* half
of that comment instead of the *screen-swap* half, matching Reading List Properties exactly.

## What changes

### 1. Routing model: booleans, not `CurrentScreen`

Add `IsIssuePropertiesOverlayOpen`/`IsBulkIssuePropertiesOverlayOpen` to `MainViewModel` (same shape
as `IsReadingListPropertiesOverlayOpen`). Remove `"issueProperties"`/`"bulkIssueProperties"` as
`CurrentScreen` values entirely - `IsIssueProperties`/`IsBulkIssueProperties` become aliases for the
new booleans (kept as property names so `Escape()`'s existing branches and any other binding sites
don't need renaming, only re-pointing).

The underlying screen is **never left**. Concretely:

- `GoIssuePropertiesForIssue`/`GoBulkIssuePropertiesForIssues` (called from Detail/MangaDetail's
  right-click menu and Edit button): drop the `CurrentScreen = "issueProperties"` assignment, set
  the overlay flag instead. Detail/MangaDetail stay the visible screen underneath.
- `GoNewIssuePropertiesForPlaceholder` (Library's "add a physical book" flow): today it calls
  `LoadDetailSeries(seriesId)` *before* switching screens, purely so the old `_goBack` had somewhere
  correct to land. With an overlay, that ordering becomes the real navigation: `LoadDetailSeries`
  now actually assigns `CurrentScreen` to the resolved Detail/MangaDetail screen (it already knows
  which one), then the overlay opens on top of it. Opening the editor from Library now visibly
  transitions to Detail-with-the-editor-open-on-top, rather than a silent screen change hidden
  behind the full-screen editor - arguably clearer, and it's what the code already intended.

### 2. Save/Cancel no longer "navigate back"

`GoDetailAfterIssueEdit` (the current shared `_goBack` for both editors) exists solely to force a
reload of Detail's now-possibly-stale data (`Issue.Number`, `ContentType`, etc. - see its own doc
comment). That reload need doesn't go away, but "navigating back" does, since the overlay closing
doesn't change `CurrentScreen` at all. Replace `_goBack` with two steps on Save (Cancel skips the
reload, matching today):

1. Close the overlay (flag → `false`).
2. If `_currentDetailSeriesId` is set, call the existing `GoDetailForSeries(seriesId)` reload path
   (unchanged logic - still re-resolves Comic-vs-Manga routing in case `ContentType` itself was
   edited). If no series was ever routed to (the same edge case the current fallback branch
   handles), skip the reload - nothing to refresh.

### 3. Visual chrome

New `IssuePropertiesOverlay.axaml`/`BulkIssuePropertiesOverlay.axaml` wrapper views (or a straight
rename of the existing `IssuePropertiesScreen.axaml`/`BulkIssuePropertiesScreen.axaml`, whichever
keeps the diff smaller) hosted the same way as `ReadingListPropertiesOverlay`:

```xml
<Border IsVisible="{Binding IsIssuePropertiesOverlayOpen}" Background="#B0000000">
    <Grid HorizontalAlignment="Center" VerticalAlignment="Center">
        <views:IssuePropertiesOverlay DataContext="{Binding IssueProperties}" />
        <Button Classes="rail" Width="28" Height="28" HorizontalAlignment="Right" VerticalAlignment="Top"
                Margin="0,-14,-14,0" Command="{Binding CloseIssuePropertiesOverlayCommand}" ... />
    </Grid>
</Border>
```

(and the same for Bulk Editing). The existing full-page content gets re-hosted inside a
`CornerRadius="10"` panel `Border` with a bounded size (`MigrationOverlay`'s `Width="580"
MaxHeight="640"` is the smallest of the three; Issue Properties has three tabs and more fields, so
its panel will likely need to be wider/taller - exact sizing is an implementation-time visual call,
not a spec decision) with internal `ScrollViewer` for overflow, matching the existing Migration/
Reading-List panels' treatment. Both close buttons wire to a plain `Close*OverlayCommand` that calls
each ViewModel's existing `CancelCommand` (mirroring `CloseReadingListPropertiesOverlay`'s shape) -
clicking the corner X is a Cancel, not a silent discard-without-confirmation change from today.

### 4. Escape key + unsaved-changes guard

`MainViewModel.Escape()`: replace the `IsIssueProperties`/`IsBulkIssueProperties` branches' meaning
(same property names, now backed by the new booleans) - no structural change needed there since
they already call `CancelCommand.Execute(null)`, which is exactly right for an overlay too.

The unsaved-changes guard (`TryLeaveCurrentEditor`'s `hasUnsavedChanges` check, `MainViewModel.cs`
around line 338) currently only matters when navigating *away* from the editor's own `CurrentScreen`
value. Once the editor is an overlay, the underlying screen the user might navigate away from is
Detail/MangaDetail/Library **while the overlay is open on top of it** - the rail nav (Library,
Reader, Preferences, etc.) needs to keep refusing to navigate while either overlay is open, exactly
as it does today. No logic change, just re-pointing the same two `HasUnsavedChanges()` checks at the
new boolean flags instead of `CurrentScreen` equality.

### 5. Tests

`MainViewModelTests`/`DetailScreenViewModelTests` assertions on `CurrentScreen == "issueProperties"`
/`"bulkIssueProperties"` change to asserting the new boolean flags; assertions that Detail/
MangaDetail's `CurrentScreen` value is unchanged after opening an editor are new coverage this change
specifically enables (today that assertion would be meaningless - the screen really did change).

## Explicitly not changing

- The editors' own internal tab structure, field layout, edit-buffer Save/Cancel pattern, and
  dirty-tracking (`IssuePropertiesScreenViewModel`/`BulkIssuePropertiesScreenViewModel` internals) -
  this is purely a hosting/chrome change.
- No real Avalonia `Window` - per the earlier discussion, the in-app overlay keeps this change
  isolated to XAML + `MainViewModel` routing, with zero new multi-window test-harness or
  focus-management concerns.
- Library's placeholder-creation delete-if-unedited behavior (`_deleteIfUnedited` in
  `IssuePropertiesScreenViewModel`) - unaffected by how the screen is hosted.
