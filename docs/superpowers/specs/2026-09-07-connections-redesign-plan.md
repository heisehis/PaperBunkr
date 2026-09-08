# Connections Redesign — Implementation Plan
*Implements: [docs/superpowers/specs/2026-09-07-connections-redesign-design.md](2026-09-07-connections-redesign-design.md)*

Real current shapes confirmed by direct read before planning: `ConnectionProviderRow.cs` (36
lines, factory methods `CreateSourceProviders`/`CreateTrackerProviders`), `PreferencesScreenViewModel.cs`
lines 2107-2519 (Sources + Trackers regions, 22 `TrackersStatus`/`SourcesStatus` assignments),
`ConnectionsSection.axaml` (188 lines, the 9 `DisplayName`-gated blocks), and
`PreferencesScreenViewModelTests.cs` lines 174-357 (existing Connections test coverage). Styling
precedent confirmed against `LibrarySection.axaml`'s Virtual Tags block (selected-row
`Border.skinRow.vtSelected`, status-dot pair, `PbDangerBrush`/`PbDangerSoftBrush` ghost-button
recipe) and `PreferencesScreen.axaml`'s shared `Button.headerAction`/`Button.sideItemButton`/
`Border.skinRow` styles.

**Note on the design doc's Disconnect-button wording:** the design doc describes the target as
"border + background tint" reusing Virtual Tags' Delete Tag recipe. The actual Delete Tag button
(`LibrarySection.axaml:572`) only sets `BorderBrush="{DynamicResource PbDangerSoftBrush}"` and
`Foreground="{DynamicResource PbDangerBrush}"` — no `Background` override. This plan implements the
real recipe (border + foreground tint, no background), which is what "reusing the same recipe"
means in practice — not a design change, just using the actual existing pattern instead of the
design doc's slightly loose paraphrase of it.

## Step 1: `ConnectionProviderRow` — generic per-Kind bindable fields and command references

**Files:** `src/Paperbunkr.App/Models/ConnectionProviderRow.cs` (edit)

**What:** Add `[ObservableProperty]` backing fields:
- OAuth: `ClientId`, `ClientSecret`, `PastedValue` (all `string`, default `""`), `PasteWatermark`
  (`string`, `required init` — "Paste token here" for AniList, "Paste code here" for
  MyAnimeList/Shikimori, set in `CreateTrackerProviders()`).
- Token: `SecretValue` (`string`, default `""`).
- Credential: `Username`, `Password` (`string`, default `""`).
- Command references (all `ICommand?`, plain mutable properties — not `[RelayCommand]`, these are
  assigned externally by the ViewModel, not generated): `ConnectCommand`, `CompleteCommand` (OAuth),
  `SaveCommand` (Token), `PrimaryCommand` (Credential), `DisconnectCommand` (all three shapes).

`RequiresClientSecret`/`Kind`/`HelpText`/`PrimaryActionLabel`/`Id`/`DisplayName`/`IsConnected`
unchanged. `PasteWatermark` only set for the 3 OAuth rows in `CreateTrackerProviders()`; leave
default (`""`) for the other 6 (never read — the OAuth dialog panel is the only one that binds it).

**Depends on:** none
**Verify:** builds; no behavior yet (nothing reads these fields until Step 2).

## Step 2: `PreferencesScreenViewModel` — wire rows, migrate 9 providers' state off flat properties

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit)

**What:**

1. Add 9 private computed row-lookup properties (one line each, mirrors the pattern
   `SyncProviderRowConnectedState` already uses to find a row by `Id`), e.g.:
   ```csharp
   private ConnectionProviderRow AniListRow => TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.AniList));
   ```
   One each for AniList, MyAnimeList, Shikimori, Bangumi, MangaBaka, MangaUpdates, Kitsu (from
   `TrackerProviderRows`) and ComicVine, Metron (from `SourceProviderRows`).

2. In the constructor, immediately after `SourceProviderRows`/`TrackerProviderRows` are populated
   (after line 142), wire every row's command references to the existing `[RelayCommand]`-generated
   properties, e.g.:
   ```csharp
   AniListRow.ConnectCommand = ConnectAniListCommand;
   AniListRow.CompleteCommand = CompleteAniListConnectCommand;
   AniListRow.DisconnectCommand = DisconnectAniListCommand;
   ```
   Repeated for all 9 providers with their actual shape (`SaveCommand` for the 3 Token providers,
   `PrimaryCommand` for the 3 Credential providers). Command *bodies* are untouched by this step —
   this only tells each row which existing command to invoke.

3. Remove these `[ObservableProperty]` fields entirely (their storage moves onto the rows):
   `_comicVineApiKey`, `_metronUsername`, `_metronPassword`, `_aniListClientId`,
   `_aniListPastedToken`, `_myAnimeListClientId`, `_myAnimeListPastedCode`, `_shikimoriClientId`,
   `_shikimoriClientSecret`, `_shikimoriPastedCode`, `_bangumiPersonalAccessToken`,
   `_mangaBakaPersonalAccessToken`, `_mangaUpdatesUsername`, `_mangaUpdatesPassword`,
   `_kitsuUsername`, `_kitsuPassword`. Keep `_isXConnected` fields as-is (unrelated to this
   consolidation — `IsConnected` already lives on the row too, but the flat `IsXConnected` bools
   are also read elsewhere, e.g. tests at lines 266/271/332/333/349 — leave them exactly as they
   are, only their *credential-value* siblings are being removed).

4. Replace `TrackersStatus`/`SourcesStatus` (`_trackersStatus`, `_sourcesStatus`) with one
   `[ObservableProperty] private string? _connectionDialogStatus;`.

5. Update every one of the 9 providers' command bodies (`SaveComicVineCredentials`,
   `SaveMetronCredentials`, `DisconnectComicVine`, `DisconnectMetron`, `ConnectAniList`,
   `CompleteAniListConnect`, `ConnectMyAnimeList`, `CompleteMyAnimeListConnectAsync`,
   `ConnectShikimori`, `CompleteShikimoriConnectAsync`, `SaveBangumiToken`, `SaveMangaBakaToken`,
   `ConnectMangaUpdatesAsync`, `ConnectKitsuAsync`, and all 9 `DisconnectX` methods) to read/write
   the corresponding row field (e.g. `ComicVineApiKey` → `ComicVineRow.SecretValue`,
   `AniListPastedToken` → `AniListRow.PastedValue`, `MetronUsername`/`MetronPassword` →
   `MetronRow.Username`/`.Password`) instead of the removed flat property, and set
   `ConnectionDialogStatus` instead of `TrackersStatus`/`SourcesStatus`. Adapter/`CredentialStore`
   call arguments are otherwise unchanged — this is a rename of where the value is read from, not a
   logic change.

6. Update `RefreshSourceCredentials`/`RefreshTrackerConnectionState`'s persisted-value population
   (lines 2149-2151, 2294-2297) to set the row field directly instead of the removed flat property
   (e.g. `ComicVineRow.SecretValue = CredentialStore.Get(...)`).

7. `OpenConnectionDialog`: replace the 3-line paste-field clear (`AniListPastedToken =
   string.Empty;` etc.) with clearing all 3 OAuth rows' `PastedValue` unconditionally (simpler than
   branching on `row.Kind` for 3 fields) plus `ConnectionDialogStatus = string.Empty;`.

**Depends on:** Step 1
**Verify:** `dotnet build` compiles (this step alone will not — Step 4's test updates are required
for the test project to compile, since tests reference the removed flat properties directly).

## Step 3: `ConnectionsSection.axaml` — row treatment, 3 shared dialog templates, status dot

**Files:** `src/Paperbunkr.App/Views/Preferences/ConnectionsSection.axaml` (edit)

**What:**

1. **Row treatment (design doc §1):** add local styles in `UserControl.Styles` (new — this file has
   none today):
   ```xml
   <Style Selector="Button.sideItemButton.connConnected">
       <Setter Property="Background" Value="{DynamicResource PbAccentSoftBrush}" />
   </Style>
   ```
   Wrap `ProviderRowTemplate`'s `Grid` content unchanged; add `Classes.connConnected="{Binding
   IsConnected}"` to the template's root `Button`. (No separate `Border` wrapper needed — unlike
   Virtual Tags' `Border.skinRow`, this row is already a bare `Button`, so the class binds directly
   to it; matches this file's existing structure rather than importing an unrelated wrapper.)

2. **Consolidate the 3 `IsVisible`-per-`Kind` panels** (lines 78-177) — each currently contains 3
   `DisplayName`-gated `StackPanel`s; replace each with one generic block:
   - **OAuth panel:** one `TextBox` bound to `ClientId` (watermark "Client ID"), one bound to
     `ClientSecret` (watermark "Client Secret", `IsVisible="{Binding RequiresClientSecret}"`,
     `PasswordChar="•"`), "Open Authorization Page" button (`Command="{Binding ConnectCommand}"`),
     one `TextBox` bound to `PastedValue` (`Watermark="{Binding PasteWatermark}"`), "Complete
     Connection" button (`Command="{Binding CompleteCommand}"`), Disconnect button (see item 4).
   - **Token panel:** one `TextBox` bound to `SecretValue` (`PasswordChar="•"`, watermark "Personal
     Access Token" — reasonable generic watermark; the per-provider distinction between "Personal
     Access Token" and "API Key" was cosmetic text only, `HelpText` already carries the real
     provider-specific instructions above it), primary button (`Content="{Binding
     PrimaryActionLabel}"`, `Command="{Binding SaveCommand}"`), Disconnect button.
   - **Credential panel:** `TextBox` bound to `Username`, `TextBox` bound to `Password`
     (`PasswordChar="•"`), primary button (`Content="{Binding PrimaryActionLabel}"`,
     `Command="{Binding PrimaryCommand}"`), Disconnect button.
   - All bindings are against `SelectedConnectionProvider` (the existing
     `DataContext="{Binding SelectedConnectionProvider}"` on the dialog's outer `StackPanel` already
     scopes this — no `#Root...` indirection needed for the new generic fields, only the 3
     `ConnectCommand`/`CompleteCommand`/etc. property paths resolve directly off
     `SelectedConnectionProvider` since they're now row properties, not
     `PreferencesScreenViewModel` properties like today's `#Root.((vm:...)DataContext).ConnectAniListCommand`).

3. **Status dot (design doc §2):** in the dialog header `Grid` (currently `BrandMark` +
   Close button), add between them: an 8px `Border` pair (same shape as Virtual Tags' Enabled dot —
   `PbSuccessBrush` background, `IsVisible="{Binding IsConnected}"`) + a `TextBlock Text="Connected"
   Foreground="{DynamicResource PbSuccessBrush}"` also gated on `IsConnected`. Nothing renders when
   not connected (no muted dot — Goal 3 in the design doc only asks for a positive signal, matching
   the row treatment's own "don't dim the negative state" reasoning).

4. **Disconnect button:** one shared markup block (still repeated 3× — once per Kind panel, since
   each panel is now a single generic block, not 3× per provider) —
   ```xml
   <Button Classes="headerAction ghost" Foreground="{DynamicResource PbDangerBrush}"
           BorderBrush="{DynamicResource PbDangerSoftBrush}" HorizontalAlignment="Left"
           IsVisible="{Binding IsConnected}" Command="{Binding DisconnectCommand}">
       <StackPanel Orientation="Horizontal" Spacing="6">
           <fi:SymbolIcon Symbol="PlugDisconnected" FontSize="{StaticResource PbIconSizeXs}" Foreground="{DynamicResource PbDangerBrush}" />
           <TextBlock Text="Disconnect" />
       </StackPanel>
   </Button>
   ```
   (icon choice: check `Assets/Icons/icon-mapping.md` for an existing "disconnect" mapping before
   picking `PlugDisconnected` — reuse if one already exists, add this as the mapping if not, per the
   app's "one Symbol per action" convention.)

5. **Status text:** replace the two always-rendered `TrackersStatus`/`SourcesStatus` `TextBlock`s
   (lines 179-182) with one bound to `ConnectionDialogStatus`.

6. Add `xmlns:fi="using:FluentIcons.Avalonia"` to the file's namespace list (not currently imported
   — needed for the status dot's checkmark-adjacent icon if used, and the Disconnect icon).

**Depends on:** Steps 1-2
**Verify:** `dotnet build` (this is not a new View/`x:Class`, so the AVLN2000 fresh-view gotcha in
`CLAUDE.md` doesn't apply — a normal build is sufficient here). Manual on-screen pass per the
Testing section below.

## Step 4: Update existing tests for the field/property renames

**Files:** `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)

**What:** Every existing assertion/setup that reads or writes a removed flat property is updated to
go through the row instead:
- Line 235/239 (`vm.AniListPastedToken`) → `vm.TrackerProviderRows.Single(r => r.Id ==
  nameof(TrackingService.AniList)).PastedValue`.
- Lines 343-357 (`vm.MetronUsername`/`vm.MetronPassword`) → `vm.SourceProviderRows.Single(r =>
  r.Id == "Metron").Username`/`.Password`.
- Any other direct reference to a removed property found during implementation (the grep in this
  plan's header found the full set; re-grep at implementation time to catch anything missed).

No behavioral assertion changes — same connect/disconnect/save-credential outcomes, just read
through the new location.

**Depends on:** Steps 1-2
**Verify:** `dotnet test --filter PreferencesScreenViewModelTests` passes.

## Step 5: New tests for the consolidation and the bleed-fix

**Files:** `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)

**What:**
1. `OpenConnectionDialogCommand_ClearsConnectionDialogStatus` — set `vm.ConnectionDialogStatus` to
   a non-empty value (simulating a prior action), call `OpenConnectionDialogCommand.Execute(row)`
   for a *different* row, assert `ConnectionDialogStatus` is empty. This is the concrete
   regression test for the bug the design doc calls out (connect AniList, open ComicVine, stale
   text).
2. `ConnectionProviderRow_CommandReferencesWired` — for a representative row per Kind (AniList,
   Bangumi, Metron), assert `ConnectCommand`/`CompleteCommand` (or `SaveCommand`/`PrimaryCommand`)
   and `DisconnectCommand` are non-null and reference-equal to the corresponding
   `PreferencesScreenViewModel` command property (e.g. `Assert.Same(vm.ConnectAniListCommand,
   aniListRow.ConnectCommand)`).
3. Extend the 3 `RefreshX`/`SaveX`/`DisconnectX` tests already present (lines 200-357) — no new
   scenarios needed there, Step 4 already keeps them passing through the row; this item is just
   confirming (not adding) that row-based field values still round-trip through
   `CredentialStore` correctly, which the existing tests already do once updated.

**Depends on:** Steps 1-2, 4
**Verify:** `dotnet test --filter PreferencesScreenViewModelTests` — all pass, including the 2 new
cases.

## Step 6: Build, avalonia-pro-max review pass, manual on-screen verification

**Files:** none (verification only)

**What:**
1. `dotnet build` the whole solution — confirm 0 errors, and per `CLAUDE.md`'s Avalonia build
   gotcha, treat "0 Errors" as insufficient on its own if this is following a prior *failed* XAML
   compile in the same session; this step isn't adding a new `x:Class` so the fresh-view failure
   mode doesn't apply, but a normal clean build is still the bar.
2. Run the `avalonia-pro-max/review-checklist` subskill's relevant subset against just the changed
   surface (not a full-app audit): no hardcoded hex in the new markup (all `DynamicResource`), the
   Disconnect button uses the shared `ghost`/danger classes, the status dot's `IsConnected`
   state isn't the *only* signal (paired with the "Connected" text, and the row-level checkmark
   still exists independently) — satisfies "no information by color alone."
3. Manual on-screen pass (standing no-unattended-GUI caveat — hand off to the user, or drive via
   FlaUI `UiTests` if a existing harness covers Preferences already): connected rows show the
   accent tint in both lists; open each of the 9 providers' dialogs and confirm the right
   fields/labels/watermarks/help text show, the status dot appears only when connected, Disconnect
   shows the danger-soft styling only when connected; reproduce the bleed scenario by hand (connect
   one provider, open a different one's dialog, confirm no stale status text) to close the loop on
   the bug this phase fixes.

**Depends on:** Steps 1-5
**Verify:** build succeeds; `dotnet test` full Preferences-related filter passes; manual pass
confirms the 3 visual decisions and the bug fix.
