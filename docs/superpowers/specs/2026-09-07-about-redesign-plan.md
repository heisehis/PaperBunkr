# About Redesign — Implementation Plan
*Implements: [docs/superpowers/specs/2026-09-07-about-redesign-design.md](2026-09-07-about-redesign-design.md)*

Real current shapes confirmed by direct read before planning: `AboutSection.axaml` (Updates group
already `SettingsRow`'d; Changelog is a flat `ItemsControl` over `ChangelogEntries`; Legal is a
`groupBox`/`WrapPanel`/`Button` block calling `OpenLegalDocumentCommand` with a file-name
`CommandParameter`); `ChangelogParser.cs`/`ChangelogEntry` record (`Version`, `Date`, `Body`) shared
with the update-available overlay; `PreferencesScreenViewModel.cs` lines 222-253 (`ChangelogEntries`,
`CurrentVersion`, `CheckForUpdatesAsync`) and lines 858-879 (`OpenLegalDocument` — `Process.Start`,
no existing test coverage); `SettingsRow.axaml.cs` (`Icon: Symbol`, `Title`/`Description: string?`,
`SettingsContent: object?`); `OverlayShell.cs` (generic `ContentControl`, `IsOpen`/`CloseCommand`).

**One correction to the design doc's wiring assumption, found during this survey:** every existing
`OverlayShell` instance is registered globally in `MainWindow.axaml` (18 of them, lines 900-1054),
but Preferences' own precedent for a section-scoped modal — the Connections dialog
(`ConnectionsSection.axaml` line 77) — is a hand-rolled `Border Background="#B0000000"` declared
*inline inside the section*, not registered in `MainWindow.axaml`. Registering the new legal-doc
viewer globally would mean threading `PreferencesScreenViewModel`'s state up through
`MainViewModel` for no benefit, since About only ever opens its own viewer. This plan uses
`<controls:OverlayShell>` **declared inline inside `AboutSection.axaml`** — same scoping precedent
as Connections, but the actual shared control instead of Connections' older hand-rolled scrim (no
regression to a pattern `OverlayShell` was built to retire).

Also confirmed: no markdown-rendering package exists in the project or its curated-package list;
`PRIVACY.md`/`TERMS.md`/`COMICVINE_NOTICE.md` use only `#`/`##` headers, `> ` blockquote, `**bold**`,
`- ` bullets, backtick inline code, and two plain-text URLs (`LICENSE` is unformatted plain text).
Avalonia's `TextBlock.Inlines` supports statically-declared `Run`s in XAML but has no direct
data-binding path for a runtime-sized collection of runs — so each parsed line's runs render as a
nested `WrapPanel`-hosted `ItemsControl` of small `TextBlock`s.

**Two more corrections, found while checking this codebase's actual converter conventions (there is
no `Converters/` folder):** Bold/code emphasis doesn't need a new converter at all — this codebase's
established idiom for a bool-driven visual switch is a `Classes.x="{Binding Bool}"` binding plus a
`<Style Selector="T.x">` setter (e.g. `Classes.ksConflict` in the Keyboard Shortcuts plan), not a
`bool→object` `IValueConverter`; Step 4 below uses that instead. Where a real converter *is*
unavoidable (the cross-scope `Version == CurrentVersion` compare, and the `Body`-to-category-groups
transform), this codebase's actual precedent is `DetailsCellConverter`/`DetailsSortGlyphConverter`
in `src/Paperbunkr.App/Views/DetailsCellConverters.cs` — a `sealed class` implementing
`IValueConverter`/`IMultiValueConverter` with a `public static readonly X Instance = new();`, placed
in `Views/`, not a dedicated converters folder. The two converters below follow that exact shape.

## Step 1: `LegalDocumentParser` + `LegalDocumentBlock` model

**Files:** `src/Paperbunkr.App/Services/LegalDocumentParser.cs` (new),
`src/Paperbunkr.App.Tests/LegalDocumentParserTests.cs` (new)

**What:**
- `LegalBlockKind` enum: `Heading1, Heading2, Quote, Bullet, Paragraph`.
- `LegalInlineRun(string Text, bool Bold, bool Code)` record.
- `LegalDocumentBlock(LegalBlockKind Kind, IReadOnlyList<LegalInlineRun> Runs)` record.
- `LegalDocumentParser.Parse(string markdown) -> IReadOnlyList<LegalDocumentBlock>` (static class,
  mirrors `ChangelogParser`'s shape): split into lines; classify each non-blank line by its leading
  token (`# ` → `Heading1`, `## ` → `Heading2`, `> ` → `Quote`, `- ` → `Bullet`, else `Paragraph`,
  stripping the matched token before further parsing); within a line's remaining text, split on
  `**...**` and `` `...` `` spans into `LegalInlineRun`s (a run can't be both bold and code — these
  docs never nest them). Blank lines separate paragraphs but produce no block of their own.
- Test cases: one per `LegalBlockKind`, one for a line with mixed bold+plain text (multiple runs),
  one for inline code, one smoke test per real bundled file (`LICENSE`, `PRIVACY.md`, `TERMS.md`,
  `COMICVINE_NOTICE.md` — read via `File.ReadAllText` from the repo root, same "real content, not
  just synthetic fixtures" reasoning `ChangelogParserTests` documents its own restraint against, but
  deliberately inverted here since these 4 files change far less often than `CHANGELOG.md` and
  catching a parser gap against real content matters more than fixture independence) asserting a
  non-empty, non-throwing result.

**Depends on:** none
**Verify:** `dotnet test --filter FullyQualifiedName~LegalDocumentParserTests`

## Step 2: `PreferencesScreenViewModel` — legal viewer state

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit, constructor ~line
133 and the `OpenLegalDocument` method at lines 858-879)

**What:**
- New `[ObservableProperty] private string? _selectedLegalDocumentTitle;`
- New `[ObservableProperty] private IReadOnlyList<LegalDocumentBlock> _selectedLegalDocumentBlocks
  = Array.Empty<LegalDocumentBlock>();`
- New `[ObservableProperty] private bool _isLegalDocumentViewerOpen;`
- New `[RelayCommand] private void CloseLegalDocumentViewer() => IsLegalDocumentViewerOpen = false;`
- `OpenLegalDocument(string fileName)` body replaced: resolve the same title each `CommandParameter`
  maps to today (`LICENSE` → "License", `PRIVACY.md` → "Privacy notice", `TERMS.md` → "Terms of
  use", `COMICVINE_NOTICE.md` → "ComicVine API notice" — a small local `switch`), read the file
  (same `File.Exists` tolerance as today — missing file leaves `IsLegalDocumentViewerOpen` false and
  does nothing else, no exception), `SelectedLegalDocumentBlocks =
  LegalDocumentParser.Parse(File.ReadAllText(path))`, `SelectedLegalDocumentTitle = title`,
  `IsLegalDocumentViewerOpen = true`. Drop the `Process.Start`/`ProcessStartInfo`/try-catch entirely
  — no external-launch path is kept (design doc Non-goals).
- Remove the now-unused `System.Diagnostics` using **only if** nothing else in the file still uses
  `Process`/`ProcessStartInfo` — check before removing (the file is large and shared with other
  in-flight work on this branch; a mechanical grep for `Process` in the file first).

**Depends on:** Step 1
**Verify:** compiles; behavior covered by Step 5's new tests.

## Step 3: `AboutSection.axaml` — identity header, Legal rows + viewer, Changelog accordion

**Files:** `src/Paperbunkr.App/Views/Preferences/AboutSection.axaml` (edit — full rewrite of the
Legal and Changelog blocks; Updates block and root `ScrollViewer`/`StackPanel` structure unchanged)

**What:**

1. **Identity header** — replace the `<TextBlock Classes="pbTextHeading" Text="About" />` line with:
   ```xml
   <StackPanel Orientation="Horizontal" Spacing="14">
       <Image Source="avares://Paperbunkr.App/Assets/paperbunkr-logo-source.png" Width="48" Height="48" />
       <StackPanel VerticalAlignment="Center" Spacing="2">
           <TextBlock Text="Paperbunkr" Classes="pbTextHeading" />
           <TextBlock FontSize="13" Foreground="{DynamicResource PbTextFaintBrush}">
               <Run Text="Version" /><Run Text="{Binding CurrentVersion}" /><Run Text=" · Comic and manga library" />
           </TextBlock>
       </StackPanel>
   </StackPanel>
   ```
   (confirm `paperbunkr-logo-source.png`'s actual `Build Action` in the csproj is `AvaloniaResource`
   or add it if it's currently plain `Content`/unreferenced — the `.ico` is referenced via
   `ApplicationIcon` only, which doesn't imply the PNG is already packed as an Avalonia resource).

2. **Legal → `SettingsRow` list**, replacing the entire `Border Classes="groupBox" Tag="about.legal"`
   block:
   ```xml
   <StackPanel Tag="about.legal">
       <TextBlock Classes="settingsGroupCaption" Text="LEGAL" />
       <pref:SettingsRow Icon="DocumentText" Title="License"
                          Description="AGPLv3, the terms this project is released under">
           <pref:SettingsRow.SettingsContent>
               <Button Classes="headerAction ghost" Command="{Binding OpenLegalDocumentCommand}" CommandParameter="LICENSE" Content="Open" />
           </pref:SettingsRow.SettingsContent>
       </pref:SettingsRow>
       <!-- same shape for Privacy notice (PRIVACY.md, Icon="Shield"), Terms of use (TERMS.md,
            Icon="DocumentText"), ComicVine API notice (COMICVINE_NOTICE.md, Icon="Warning") -->
   </StackPanel>
   ```
   Confirm `Shield`/`Warning` exist on `FluentIcons.Common.Symbol` at compile time (`dotnet build`
   will fail loudly with an invalid-member error if not — fall back to `DocumentText` for all four
   if either doesn't exist, matching today's single-icon treatment).

3. **In-app legal viewer**, added inline (per the wiring correction above — not in
   `MainWindow.axaml`):
   ```xml
   <controls:OverlayShell IsOpen="{Binding IsLegalDocumentViewerOpen}"
                           CloseCommand="{Binding CloseLegalDocumentViewerCommand}"
                           CloseButtonAutomationId="LegalDocumentViewerCloseButton">
       <Border Classes="groupBox" Width="480" MaxHeight="480">
           <DockPanel>
               <TextBlock DockPanel.Dock="Top" Text="{Binding SelectedLegalDocumentTitle}" FontWeight="Bold" FontSize="15" Margin="14,12" />
               <ScrollViewer>
                   <ItemsControl ItemsSource="{Binding SelectedLegalDocumentBlocks}" Margin="14,0,14,14">
                       <ItemsControl.ItemTemplate>
                           <DataTemplate x:DataType="services:LegalDocumentBlock">
                               <ItemsControl ItemsSource="{Binding Runs}" Margin="0,4">
                                   <ItemsControl.ItemsPanel>
                                       <ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate>
                                   </ItemsControl.ItemsPanel>
                                   <ItemsControl.ItemTemplate>
                                       <DataTemplate x:DataType="services:LegalInlineRun">
                                           <TextBlock Text="{Binding Text}" Classes.runBold="{Binding Bold}" Classes.runCode="{Binding Code}"
                                                      TextWrapping="Wrap" FontSize="13" Foreground="{DynamicResource PbTextFaintBrush}" />
                                       </DataTemplate>
                                   </ItemsControl.ItemTemplate>
                               </ItemsControl>
                           </DataTemplate>
                       </ItemsControl.ItemTemplate>
                   </ItemsControl>
               </ScrollViewer>
           </DockPanel>
       </Border>
   </controls:OverlayShell>
   ```
   `Heading1`/`Heading2`/`Quote`/`Bullet` kinds get their own visual treatment (larger/bold text for
   headings, indented italic for quote, a leading "•" for bullet) — simplest as a `Classes` binding
   on the outer per-block `ItemsControl`/its wrapping `Border` keyed off `Kind`
   (`Classes.legalHeading1="{Binding Kind, ...}"` etc., same `Classes.x="{Binding}"` boolean-class
   pattern already used elsewhere in this codebase, e.g. `Classes.ksConflict` in the Keyboard
   Shortcuts plan) rather than a full `DataTemplate.DataType`-per-kind switch — one template, styled
   by class, is less XAML than four near-duplicate templates for four block kinds this simple.
   Two new local styles (in `AboutSection.axaml`'s own `<UserControl.Styles>`, or the file's
   existing style block if it has one) back the `runBold`/`runCode` classes:
   ```xml
   <Style Selector="TextBlock.runBold"><Setter Property="FontWeight" Value="Bold" /></Style>
   <Style Selector="TextBlock.runCode"><Setter Property="FontFamily" Value="Consolas,monospace" /></Style>
   ```

4. **Changelog → accordion + category tags**, replacing the `ItemsControl.ItemTemplate` currently
   rendering `Version`/`Date`/`Body` directly:
   ```xml
   <DataTemplate x:DataType="services:ChangelogEntry">
       <StackPanel>
           <ToggleButton Name="ExpandToggle" Classes="changelogExpandHeader">
               <ToggleButton.IsChecked>
                   <MultiBinding Converter="{x:Static views:VersionEqualsCurrentConverter.Instance}" Mode="OneTime">
                       <Binding Path="Version" />
                       <Binding Path="#Root.((vm:PreferencesScreenViewModel)DataContext).CurrentVersion" />
                   </MultiBinding>
               </ToggleButton.IsChecked>
               <!-- header row: chevron (rotates via a RenderTransform bound to IsChecked),
                    Version, Date, and a "Current" badge reusing the same MultiBinding for IsVisible -->
           </ToggleButton>
           <StackPanel IsVisible="{Binding #ExpandToggle.IsChecked}" Margin="0,0,0,10">
               <!-- category-tag groups, see below -->
           </StackPanel>
       </StackPanel>
   </DataTemplate>
   ```
   `Mode="OneTime"` on the `MultiBinding` matters: it seeds the toggle's starting state from "does
   this entry's `Version` match `CurrentVersion`" without fighting the user's own click-driven
   `IsChecked` changes afterward (a live `OneWay` re-evaluation would fight the user's toggle on
   every property-changed pass, and there's no meaningful reverse conversion for `TwoWay` anyway).
   `#Root` is the existing named root element `ConnectionsSection.axaml` already binds through for
   this exact "reach the section's own DataContext from inside a nested `ItemsControl`" case —
   confirm `AboutSection.axaml`'s root element already carries `Name="Root"` (or add it) before
   relying on this.
   - **Category tags**: new `ChangelogBodyFormatter` static helper (view-layer, in
     `Paperbunkr.App/Services/` alongside `ChangelogParser` but *not* touching
     `ChangelogParser`/`ChangelogEntry` per the design's explicit constraint) —
     `Format(string body) -> IReadOnlyList<(string? Category, IReadOnlyList<string> Lines)>`:
     splits on `### ` sub-headings; a body with none produces one group with `Category = null`
     (rendered as today's plain paragraph, satisfying the design's stated fallback). Bound via a
     converter (`ChangelogBodyToGroupsConverter`) on the entry's `Body`, feeding a nested
     `ItemsControl` of tag+line rows (tag `Border` hidden — `IsVisible="{Binding Category,
     Converter={x:Static ObjectConverters.IsNotNull}}"` — when `Category` is null).
   - "Current" badge and initial-expand state both key off `Version == CurrentVersion` — same
     compare, reused rather than duplicated (one converter, two bindings).

**Depends on:** Steps 1-2 (needs `LegalDocumentBlock`/`LegalInlineRun` types and the new VM
properties/commands to exist for the bindings above to resolve)
**Verify:** `dotnet build` — this edits an existing `.axaml`/`x:Class`, so the AVLN2000
fresh-view gotcha doesn't apply; if an interim build fails mid-edit and a later one suspiciously
reports 0 errors, force `CoreCompile` stale per `CLAUDE.md` (delete
`src/Paperbunkr.App/obj/Debug/net10.0/Paperbunkr.App.dll`/`.pdb`, rebuild) before trusting it.
Manual pass per Step 6.

## Step 4: New converters

**Files:** `src/Paperbunkr.App/Views/AboutSectionConverters.cs` (new — same location/shape as the
confirmed precedent, `Views/DetailsCellConverters.cs`)

**What:** Two `sealed class`es, each with a `public static readonly X Instance = new();`, no shared
base:
- `VersionEqualsCurrentConverter : IMultiValueConverter` — `Convert(IList<object?> values, ...)`:
  `values.Count >= 2 && values[0] is string version && values[1] is string current && version ==
  current`. Used both for the accordion's initial `IsChecked` (Step 3) and the "Current" badge's
  `IsVisible`.
- `ChangelogBodyToGroupsConverter : IValueConverter` — `Convert(object? value, ...)`: `value is
  string body ? ChangelogBodyFormatter.Format(body) : Array.Empty<...>()`, delegating entirely to
  the helper Step 3 introduces (`ChangelogBodyFormatter`, in `Services/`) — the converter is a thin
  binding adapter, not where the parsing logic lives.

No converter needed for Bold/Code (Step 3 uses `Classes.x="{Binding}"` + `Style` instead, matching
this codebase's more common idiom for a bool-driven visual switch).

**Depends on:** Step 3's `ChangelogBodyFormatter` (for `ChangelogBodyToGroupsConverter` to compile)
**Verify:** exercised transitively by Step 3's manual pass; no dedicated unit test needed — both are
thin adapters over logic that's either trivial (`VersionEqualsCurrentConverter`) or already tested
elsewhere (`ChangelogBodyFormatter`, covered by Step 5).

## Step 5: Tests

**Files:** `src/Paperbunkr.App.Tests/ChangelogBodyFormatterTests.cs` (new),
`src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit — add near the existing About
coverage)

**What:**
- `ChangelogBodyFormatterTests`: body with 2 `### ` sub-headings → 2 groups with correct
  category/lines; body with no sub-headings → 1 group with `Category = null` and the raw body as
  its single "line"; empty body → empty result (no throw).
- `PreferencesScreenViewModelTests` — new cases for `OpenLegalDocument` (currently untested):
  `OpenLegalDocument_ExistingFile_PopulatesBlocksAndOpensViewer` (call with `"LICENSE"` against a
  temp file or the actual bundled copy — check how other file-reading tests in this file set up
  their working directory before choosing), `OpenLegalDocument_MissingFile_LeavesViewerClosed`
  (nonexistent filename → `IsLegalDocumentViewerOpen` stays `false`, no exception).

**Depends on:** Steps 1-2
**Verify:** `dotnet test --filter
"FullyQualifiedName~ChangelogBodyFormatterTests|FullyQualifiedName~PreferencesScreenViewModelTests"`
— all pass (targeted filter per this project's full-suite-flake note, not the whole suite).

## Step 6: Build, review pass, manual on-screen verification

**Files:** none (verification only); also update `docs/preferences-descriptions-todo.md`'s `## About`
section to add the four new Legal rows (mirroring the two Updates rows already listed there) if
their `Description` text in Step 3 isn't already considered final copy.

**What:**
1. `dotnet build` — 0 errors (force-stale-`CoreCompile` workaround if needed, per `CLAUDE.md`).
2. Review-checklist subset: no hardcoded hex colors introduced (everything through
   `DynamicResource`/existing style classes); the new `Image` has appropriate
   `AutomationProperties`/is purely decorative (mark `AutomationProperties.IsOffscreenBehavior` or
   equivalent if this codebase has a convention for decorative images — check
   `avalonia-pro-max/review-checklist` for the exact accessibility ask); the Legal rows' "Open"
   buttons share the existing `headerAction ghost` class already used throughout Preferences.
3. Manual on-screen pass (standing no-computer-use limitation — hand off to the user): identity
   header shows the icon/version/tagline; each Legal row opens the in-app viewer with headers/bold/
   bullets visually distinct from plain paragraphs, and the close button/Escape both dismiss it
   (Escape via `MainViewModel.Escape()`'s existing central handling — confirm it reaches this new
   `IsLegalDocumentViewerOpen` flag the same way it reaches every other overlay, since `OverlayShell`
   itself deliberately doesn't handle Escape); Changelog's current version starts expanded with
   category tags, the older entry starts collapsed, and both can be open at once; the
   update-available overlay's own changelog rendering (elsewhere in the app, sharing
   `ChangelogParser`/`ChangelogEntry`) is unaffected since neither was touched.

**Depends on:** Steps 1-5
**Verify:** build + targeted test filters green; manual pass confirms all 5 design goals.
