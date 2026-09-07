# Connections Dialog Redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-06-connections-tracker-dialog-redesign-design.md*

## Step 1: Provider metadata + dialog-kind model
**Files:** `src/Paperbunkr.App/Models/ConnectionProviderRow.cs` (new)
**What:** `ConnectionDialogKind` enum (`OAuth`, `Token`, `Credential`). `ConnectionProviderRow` record/class:
`Id` (string, e.g. `"AniList"`), `DisplayName`, `Kind`, `RequiresClientSecret` (bool, true only for
Shikimori), `PrimaryActionLabel` ("Save" for Metron/ComicVine, "Connect" for the rest), plus an
`IsConnected` bound property the ViewModel refreshes. A static `ConnectionProviderCatalog` list of
all 9 rows in fixed order (matches the design doc's table).
**Depends on:** none
**Verify:** compiles; no behavior yet.

## Step 2: ViewModel dialog state + Disconnect commands
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit)
**What:** Add `SourceProviderRows`/`TrackerProviderRows` (`ObservableCollection<ConnectionProviderRow>`),
populated in `RefreshTrackerConnectionState`/`RefreshSourceCredentials` (extend, don't replace - keep
existing `Is*Connected`/field properties as-is, the dialog binds to those directly). Add
`IsComicVineConnected`/`IsMetronConnected` (new, via `CredentialStore.HasCredentials`) to those
refresh methods. Add `SelectedConnectionProvider` (`ConnectionProviderRow?`), `IsConnectionDialogOpen`
(bool). `OpenConnectionDialogCommand(ConnectionProviderRow)` sets both and clears any stale paste-back
fields (`AniListPastedToken`, `MyAnimeListPastedCode`, `ShikimoriPastedCode` - not the persisted
Client ID/secret fields, only the ephemeral paste-back ones). `CloseConnectionDialogCommand` clears
both. Nine `Disconnect*Command`s, each calling `CredentialStore.Delete` for that provider's relevant
`CredentialKind`s (OAuth: `OAuthClientId`+`OAuthClientSecret`+`OAuthAccessToken`+`OAuthRefreshToken`;
Token: `ApiKey`; Credential: `Username`+`Password`, or `OAuthAccessToken` too for MangaUpdates/Kitsu
since they do a real handshake - check `CompleteConnectAsync` in each adapter for which `CredentialKind`s
it actually writes before deciding what Disconnect clears), then refreshes connection state.
**Depends on:** Step 1
**Verify:** `PreferencesScreenViewModelTests` - new tests per Testing section of the design doc.

## Step 3: Rewrite ConnectionsSection.axaml
**Files:** `src/Paperbunkr.App/Views/Preferences/ConnectionsSection.axaml` (edit)
**What:** Replace the two always-visible field blocks with two `ItemsControl`s (`SourceProviderRows`,
`TrackerProviderRows`) using a shared row `DataTemplate`: `BrandMark` + name + checkmark
(`IsVisible="{Binding IsConnected}"`), whole row wired to `OpenConnectionDialogCommand`. Add a local
overlay `Border` (dark scrim, matching `MainWindow.axaml`'s `IsIssuePropertiesOverlayOpen` visual
weight) `IsVisible="{Binding IsConnectionDialogOpen}"`, containing three `IsVisible`-gated panels keyed
on `SelectedConnectionProvider.Kind` (`OAuth`/`Token`/`Credential`) per the design doc's three dialog
specs - reuse every existing `TextBox`/Button/status-line binding verbatim, just relocated into the
overlay.
**Depends on:** Steps 1-2
**Verify:** `dotnet build` (AVLN2000 gotcha - this edits an existing compiled view, no new `x:Class`,
so no code-behind risk). Manual: open each of the 9 dialogs, confirm correct fields/Disconnect
visibility, confirm checkmarks update after connect/disconnect.

## Step 4: Tests
**Files:** `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)
**What:** `OpenConnectionDialogCommand_SetsSelectedProviderAndOpensDialog`,
`OpenConnectionDialogCommand_SwitchingProviders_ClearsStalePasteBackFields`,
`DisconnectCommand_ClearsCredentialsAndFlipsConnectedFlag` (parameterized or one-per-provider, follow
this file's existing style), `RefreshSourceCredentials_ComicVineAndMetron_ReportConnectedState` (new
behavior per the design doc).
**Depends on:** Step 2
**Verify:** `dotnet test --filter FullyQualifiedName~PreferencesScreenViewModelTests` (targeted, per
this project's full-suite-headless-flake convention).
