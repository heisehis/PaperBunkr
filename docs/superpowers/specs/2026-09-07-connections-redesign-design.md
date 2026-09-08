# Connections Redesign — Design (Phase 5 of Preferences)

Builds on the row-list + dialog restructuring already shipped in
[2026-09-06-connections-tracker-dialog-redesign-design.md](2026-09-06-connections-tracker-dialog-redesign-design.md).
This phase is the Preferences Tile-Hub Redesign's §4 deferred item 4 — the visual-language pass
that Library Health, Library Folder Management, and Virtual Tags each got as their own phase — plus
one correctness bug this exact work surfaces.

Reached via `/grilling` + one round in the visual companion (row treatment, group header style,
dialog status indicator).

## Background

`ConnectionsSection.axaml` today: two `Border.groupBox` lists (Reading List Sources, Trackers),
each row a `BrandMark` + name + conditional checkmark opening a shared overlay dialog. The dialog
has three `IsVisible`-gated panels keyed on `SelectedConnectionProvider.Kind` (OAuth/Token/
Credential) — but **within each Kind panel, the three providers of that shape are three separate
`StackPanel`s gated on `DisplayName` string equality**, each hardcoding that provider's own named
`TextBox` bindings (`AniListClientId`, `MyAnimeListClientId`, ...) and command references
(`ConnectAniListCommand`, `ConnectMyAnimeListCommand`, ...). `ConnectionProviderRow` already
carries `Kind`, `HelpText`, `PrimaryActionLabel`, and `RequiresClientSecret` — enough to drive one
real template per Kind instead of three near-identical copies; the duplication is pure leftover
from the 09-06 spec's own explicit choice (three dialog *shapes*, not three-per-shape *copies* —
that per-shape genericization just wasn't done yet).

**Confirmed bug during this survey:** `TrackersStatus`/`SourcesStatus` are single shared strings,
shown unconditionally at the bottom of every dialog regardless of which provider is open. Connect
AniList (`TrackersStatus = "AniList connected."`), close the dialog, open ComicVine — the stale
AniList message is still there until you touch a Source action. `OpenConnectionDialog` already
clears the ephemeral paste-back fields for exactly this class of bleed-through; the status strings
were missed.

## Goals

1. **Row treatment:** a connected row (`IsConnected == true`) in either list gets a soft
   accent-tinted border + background (`PbAccentSoftBrush`/accent border — same recipe used for a
   selected row elsewhere in the app). Unconnected rows stay neutral — "not connected yet" isn't a
   negative state worth dimming, unlike Virtual Tags' disabled-row treatment.
2. **Group headers stay as-is.** "Reading List Sources"/"Trackers" keep the current
   `Border.groupBox`/`groupHeader` card treatment. Confirmed via the visual companion — the user
   picked the card over the caption-style label the rest of Preferences is moving toward. This is a
   deliberate exception, not an oversight: revisit only if a future phase decides consistency here
   matters more than the current look.
3. **Dialog status dot:** the dialog header (provider `BrandMark` + name) gains a small status dot
   + label (`PbSuccessBrush` dot + "Connected" when `IsConnected`, nothing when not) — same shape as
   Virtual Tags' Enabled-row status dot. This is a live, always-current signal, independent of
   whatever the last status message said.
4. **Disconnect button gets the red-soft treatment** (border + background tint in
   `PbDangerBrush` at reduced opacity, reusing the same recipe Virtual Tags' "Delete Tag" and
   Library Health's "Remove All Confirmed Missing" already use) instead of today's plain
   `ghost destructive` (red text only, no fill/border color).
5. **Consolidate the 9 per-provider dialog blocks into 3 shared templates**, one per
   `ConnectionDialogKind`, each bound generically to `SelectedConnectionProvider`'s fields and
   command references (see Architecture §2) instead of a `DisplayName` string switch.
6. **Fix the status-bleed bug:** collapse `TrackersStatus`/`SourcesStatus` into one
   `ConnectionDialogStatus` string, cleared in `OpenConnectionDialog` alongside the existing
   paste-field clearing, set by whichever command runs. One `TextBlock` in the dialog instead of
   two always-rendered ones.

## Non-goals

- No change to any adapter, `CredentialStore`, or auth/verification logic for any of the 9
  providers — every `[RelayCommand]` method keeps its exact current behavior; only where its input
  values live (a named ViewModel property → a field on the row it was already conceptually "for")
  and how the view finds them changes.
- No new providers, no change to `ExternalMetadataProvider`'s scaffolding-only entries.
- No change to `TrackingLink` (per-series tracker association).
- Group header visual treatment is explicitly *not* being changed this phase (see Goal 2) — not an
  omission to flag later, a confirmed choice.
- No delete-confirmation added to Disconnect — it stays an immediate, unconfirmed action, consistent
  with how Virtual Tags' "Delete Tag" and the folder rows' "Remove" both stayed unconfirmed in their
  own redesigns. A cross-cutting confirm-on-destructive-action policy is a separate decision, not
  something to slip in per-component.

## Architecture

### 1. Row treatment

`ProviderRowTemplate`'s `Button.sideItemButton` wraps its `Grid` in a `Border` (currently there
isn't one — the `Button`'s own chrome is the row surface). Add
`Classes.connected="{Binding IsConnected}"` (or an equivalent style-class binding) so a connected
row's `Button` gets an accent border + `PbAccentSoftBrush` background via a style selector,
matching the selected-row recipe used elsewhere (e.g. Virtual Tags' selected tag row). The
checkmark stays as today — the tint is additional weight, not a replacement signal.

### 2. Dialog: shared templates + status dot + button color

`ConnectionProviderRow` gains the bindable fields and command references each Kind's generic
template needs, replacing the corresponding named `PreferencesScreenViewModel` properties/commands
as the thing the view binds to (the underlying `[RelayCommand]` method bodies are unchanged, just
re-pointed at `row.X` instead of a flat `XProperty`):

- **OAuth** (AniList, MyAnimeList, Shikimori): `ClientId`, `ClientSecret` (bound only when
  `RequiresClientSecret`), `PastedValue`, `PasteWatermark` (string — "Paste token here" for
  AniList, "Paste code here" for MyAnimeList/Shikimori, since the two flows return different
  things). Commands: `ConnectCommand` ("Open Authorization Page"), `CompleteCommand` ("Complete
  Connection"), `DisconnectCommand`.
- **Token** (Bangumi, MangaBaka, ComicVine): `SecretValue` (single masked field). Commands:
  `SaveCommand` (label from existing `PrimaryActionLabel`), `DisconnectCommand`.
- **Credential** (Metron, MangaUpdates, Kitsu): `Username`, `Password`. Commands: `PrimaryCommand`
  (label from `PrimaryActionLabel` — "Save" for Metron, "Connect" for MangaUpdates/Kitsu, already
  modeled), `DisconnectCommand`.

Each row's command references are assigned once, after the `[RelayCommand]`-generated command
properties exist (constructor, after `CreateSourceProviders()`/`CreateTrackerProviders()` populate
the observable collections) — e.g. `aniListRow.ConnectCommand = ConnectAniListCommand;
aniListRow.CompleteCommand = CompleteAniListConnectCommand; ...`. `HelpText`,
`PrimaryActionLabel`, `RequiresClientSecret`, `Kind` are already row-level and need no change.

Persisted-value population (today: `AniListClientId = CredentialStore.Get(...)` etc. in
`RefreshTrackerConnectionState`/`RefreshSourceCredentials`) moves to setting the matching row's
field directly (`aniListRow.ClientId = CredentialStore.Get(...)`).

The three `IsVisible`-per-`Kind` panels in `ConnectionsSection.axaml` stay (that structural split
was A2's deliberate choice and is unchanged) — only the *contents* of each becomes one generic
block bound to `SelectedConnectionProvider.ClientId`/`.SecretValue`/`.Username` etc. instead of
three `DisplayName`-gated copies.

**Status dot:** in the dialog header row (next to the `BrandMark`), add an 8px dot
(`PbSuccessBrush` when `SelectedConnectionProvider.IsConnected`, else not rendered) + a small
"Connected" `TextBlock` (also gated on `IsConnected`) — same visual recipe as Virtual Tags' Enabled
dot.

**Disconnect button:** classes change from `headerAction ghost destructive` to a red-soft-tinted
treatment — reuses the same brushes Virtual Tags' "Delete Tag" button already introduced
(`PbDangerBrush`-tinted border/background at reduced opacity), no new brush needed.

### 3. Status-bleed fix

Replace `TrackersStatus` and `SourcesStatus` with one `ConnectionDialogStatus` string property.
Every command body that today sets one of the two (22 assignments — connect/complete/save/disconnect
across all 9 providers, confirmed by direct grep of `PreferencesScreenViewModel.cs`) sets
`ConnectionDialogStatus` instead. `OpenConnectionDialog` clears it
(`ConnectionDialogStatus = string.Empty;`) alongside its existing `AniListPastedToken =
string.Empty;` etc. block. The dialog XAML drops the two always-rendered `TextBlock`s for one bound
to `ConnectionDialogStatus`.

## ViewModel changes (summary)

- `ConnectionProviderRow`: new `[ObservableProperty]` fields — `ClientId`, `ClientSecret`,
  `PastedValue`, `PasteWatermark` (OAuth); `SecretValue` (Token); `Username`, `Password`
  (Credential) — populated only for rows of the relevant `Kind` (harmless-unused elsewhere, not
  worth a discriminated-union split for 3 optional-field groups on 9 total rows). New properties:
  `ConnectCommand`, `CompleteCommand`, `SaveCommand`/`PrimaryCommand`, `DisconnectCommand`
  (`ICommand?`), assigned once per row after construction.
- `PreferencesScreenViewModel`: the 9 provider-named properties this replaces (`AniListClientId`,
  `MyAnimeListClientId`, `ShikimoriClientId`/`ShikimoriClientSecret`, `BangumiPersonalAccessToken`,
  `MangaBakaPersonalAccessToken`, `ComicVineApiKey`, `MetronUsername`/`MetronPassword`,
  `MangaUpdatesUsername`/`MangaUpdatesPassword`, `KitsuUsername`/`KitsuPassword`, plus the 3
  paste-back fields `AniListPastedToken`/`MyAnimeListPastedCode`/`ShikimoriPastedCode`) are removed;
  every `[RelayCommand]` method body that read/wrote one now reads/writes the corresponding field on
  its row instead. `TrackersStatus`/`SourcesStatus` removed, replaced by `ConnectionDialogStatus`.
- `OpenConnectionDialog`: paste-field-clearing block becomes a `ConnectionDialogStatus =
  string.Empty;` clear (the three paste-back fields it clears today live on the rows now, so this
  method's own field-clearing responsibility shrinks correspondingly — it clears whichever OAuth
  row's `PastedValue` if `row.Kind == OAuth`, or simply always clears all three OAuth rows'
  `PastedValue` unconditionally, whichever reads simpler at implementation time).
- No changes to `CredentialStore`, `ProviderCredential`, `CredentialKind`, or any adapter.

## Testing

- `PreferencesScreenViewModelTests`: existing connect/disconnect/save-credential tests updated to
  read/write through the row (`sourceRows.First(r => r.Id == "ComicVine").SecretValue = "..."`
  instead of `ComicVineApiKey = "..."`) — behavior assertions (connected state flips, credential
  persisted) unchanged.
- New: `ConnectionDialogStatus` is cleared on `OpenConnectionDialogCommand` and set correctly by
  each of the 9 connect/save/disconnect paths (spot-check a representative one per Kind — AniList,
  Bangumi, Metron — plus the specific bleed scenario: connect AniList, open ComicVine's dialog,
  assert `ConnectionDialogStatus` is empty, not "AniList connected.").
- New: each row's `ConnectCommand`/`CompleteCommand`/`SaveCommand`/`PrimaryCommand`/
  `DisconnectCommand` reference is non-null and invoking it produces the same effect the old
  named command did (one test per provider, reusing existing setup).
- Manual on-screen pass (standing no-unattended-GUI caveat): connected rows show the accent tint;
  open each of the 9 dialogs and confirm the right fields/labels/watermarks show, the status dot
  reflects connection state, Disconnect shows the red-soft styling only when connected; confirm the
  status-bleed scenario is fixed by hand (connect one provider, open a different one, no stale
  text).

## Deliverable

No new `SettingsRow`s — this stays a dynamic list + modal-dialog shape, same reasoning as the other
three Library-section phases. Nothing to add to `docs/preferences-descriptions-todo.md`.
