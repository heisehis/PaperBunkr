# Connections: Trackers & Sources as List + Dialog — Design

Redesigns Preferences → Connections from nine providers' worth of always-visible inline credential
fields into a Komikku-style row list (icon, name, connected checkmark) where tapping a row opens a
dialog shaped to that provider's actual auth mechanism. Inspired by Komikku's Tracking settings
screen (screenshots shared in chat). Sub-project A of the Komikku-inspired settings work — Design B
(animated toggles) is a separate, independent spec.

Date: 2026-09-06. Status: design, approved via grilling round in chat (A1/A2 both answered). Not
yet approved for `writing-plans`.

---

## Why now (context)

Direct code check of `PreferencesScreenViewModel.cs` (`Reading List Sources` region, lines
~2032-2069, and `Trackers` region, lines ~2071-2240) confirms today's actual shape: all nine
providers' fields sit permanently on screen in `ConnectionsSection.axaml`'s two `groupBox`
sections — nothing is click-to-reveal. This works, but doesn't scale visually past a handful of
providers and buries the "which ones am I actually connected to" signal in prose text scattered
across nine always-expanded blocks.

**Confirmed current field/command names** (real, not paraphrased):

| Provider | Auth shape | Fields | Commands |
|---|---|---|---|
| AniList | OAuth (Client ID only) | `AniListClientId`, `AniListPastedToken` | `ConnectAniListCommand`, `CompleteAniListConnectCommand` |
| MyAnimeList | OAuth (PKCE, Client ID) | `MyAnimeListClientId`, `MyAnimeListPastedCode` | `ConnectMyAnimeListCommand`, `CompleteMyAnimeListConnectAsyncCommand` |
| Shikimori | OAuth (Client ID + Secret) | `ShikimoriClientId`, `ShikimoriClientSecret`, `ShikimoriPastedCode` | `ConnectShikimoriCommand`, `CompleteShikimoriConnectAsyncCommand` |
| Bangumi | PAT | `BangumiPersonalAccessToken` | `SaveBangumiTokenCommand` |
| MangaBaka | PAT | `MangaBakaPersonalAccessToken` | `SaveMangaBakaTokenCommand` |
| ComicVine | PAT (no verify handshake) | `ComicVineApiKey` | `SaveComicVineCredentialsCommand` |
| Metron | Username+password (no verify handshake) | `MetronUsername`, `MetronPassword` | `SaveMetronCredentialsCommand` |
| MangaUpdates | Username+password (real auth handshake) | `MangaUpdatesUsername`, `MangaUpdatesPassword` | `ConnectMangaUpdatesAsyncCommand` |
| Kitsu | Username+password (real auth handshake) | `KitsuUsername`, `KitsuPassword` | `ConnectKitsuAsyncCommand` |

`Is*Connected` bools already exist for the seven Trackers (via `CredentialStore.HasCredentials`)
but **not** for ComicVine/Metron — those two currently have no "connected" concept at all, just a
one-line status message after Save. Folding them into the same checkmark-list UI means adding
`IsComicVineConnected`/`IsMetronConnected` (same `CredentialStore.HasCredentials` check the
Trackers already use) — a small, explicitly-called-out new behavior, not a hidden side effect.

**Grilling decisions (A1/A2):**
- **A1 — both sections redesigned**, not just Trackers. ComicVine/Metron have the identical
  always-visible-inline problem and map cleanly onto two of the three dialog shapes below.
- **A2 — three separate dialog views**, not one generic data-driven dialog. Chosen over the
  data-driven recommendation; the three shapes below are genuinely different enough (OAuth needs a
  "open browser" step and optional Client Secret; PAT is one field; credential is two fields plus a
  Save-vs-Connect distinction) that three small, concrete views are clearer than one view with
  conditional visibility switches for every field.

## Goals

- One list per section (Reading List Sources, Trackers), each row: `BrandMark` icon, provider name,
  a checkmark when connected (nothing when not) — matching Komikku's own visual language from the
  reference screenshots.
- Tapping a row opens the dialog shaped to that provider's real auth mechanism (table above) with
  today's already-working fields/commands wired in, not reimplemented.
- Preserve every existing behavior exactly: OAuth's "open browser, paste code back" flow, PAT's
  single-field save, credential's Save-only (ComicVine/Metron) vs. real-handshake-Connect
  (MangaUpdates/Kitsu) distinction, and all existing status/error messages.
- A **Disconnect** action in the dialog for any already-connected provider — doesn't exist today at
  all (there's no way to clear a saved credential from the UI currently). New, but small:
  `CredentialStore.Delete(context, provider, kind)` already exists (`src/Paperbunkr.Data/Credentials/CredentialStore.cs:41`)
  - each provider's `DisconnectCommand` just calls it for that provider's relevant `CredentialKind`s
  (e.g. Shikimori clears both `OAuthClientId`/`OAuthClientSecret`/`OAuthAccessToken`) and refreshes
  connection state, no new storage-layer work needed.

## Non-goals (v1)

- No change to any adapter, `CredentialStore`, or `ProviderCredential`/`CredentialKind` — this is a
  presentation reorg only, per A2's own reasoning: three views around existing commands.
- No new auth mechanism or provider. `ExternalMetadataProvider`'s scaffolding-only entries
  (MangaDex, AnimePlanet, GrandComicsDatabase, LeagueOfComicGeeks — no adapter, no UI today) are
  untouched; this spec only reshapes the nine providers already wired into the UI.
- No change to `TrackingLink` (per-series tracker association) - this is account-level
  connect/disconnect only, not the "link this series to this tracker's entry" flow elsewhere.

## Dialog views

All three share one small host mechanism: a local overlay `Border` (dark scrim, matching the
existing `IsIssuePropertiesOverlayOpen`/`IsBulkIssuePropertiesOverlayOpen` pattern's visual weight -
`MainWindow.axaml` lines ~1057-1079) but scoped *inside* `ConnectionsSection.axaml` itself rather
than hoisted to `MainViewModel` - `PreferencesScreenViewModel` already owns every field/command
involved, so there's no reason to bubble dialog-open state up a level the way the heavier,
multi-tab Issue Properties overlay needs to. A single `SelectedConnectionProvider` (nullable enum:
`TrackingService?` for trackers, or a small parallel value for the two Sources) plus
`IsConnectionDialogOpen` drives which of the three dialog `DataTemplate`s renders.

**`OAuthConnectionDialog`** (AniList, MyAnimeList, Shikimori):
- Client ID `TextBox` (always).
- Client Secret `TextBox` (only when `SelectedConnectionProvider == Shikimori` - `IsVisible` bound
  to a small per-provider `bool RequiresClientSecret` on the row-item model).
- "Open Authorization Page" button → the existing `Connect*` command (opens browser, stores Client
  ID/Secret first exactly as today).
- Pasted code/token `TextBox` + "Complete Connection" button → the existing `Complete*Connect*`
  command.
- "Disconnect" button, visible only when `Is*Connected` is true.
- Status line bound to `TrackersStatus` (existing property, reused as-is).

**`TokenConnectionDialog`** (Bangumi, MangaBaka, ComicVine):
- Single secret `TextBox` (`PasswordChar="•"`) + "Save"/"Connect" button → the existing
  `Save*Token`/`SaveComicVineCredentials` command.
- "Disconnect" button when connected.
- Status line (`TrackersStatus` for Bangumi/MangaBaka, `SourcesStatus` for ComicVine).

**`CredentialConnectionDialog`** (Metron, MangaUpdates, Kitsu):
- Username `TextBox` + Password `TextBox` (`PasswordChar="•"`).
- Help text: "Your password is used once to connect and is never stored" for MangaUpdates/Kitsu
  (real handshake); no such note for Metron (plain storage, matches today's actual behavior -
  **not** claiming a guarantee Metron's code doesn't provide).
- Primary button label differs by provider: "Save" for Metron (→ `SaveMetronCredentials`), "Connect"
  for MangaUpdates/Kitsu (→ `ConnectMangaUpdatesAsync`/`ConnectKitsuAsync`) - same dialog shell, a
  per-provider command reference on the row-item model picks which one the button invokes.
- "Disconnect" button when connected.

## Testing

- `PreferencesScreenViewModelTests`: `OpenConnectionDialogCommand` (new) sets
  `SelectedConnectionProvider`/`IsConnectionDialogOpen` correctly per provider; selecting a second
  provider while the dialog is open resets any typed-but-unsaved field values from the first
  (no stale bleed-through between providers sharing the same dialog shell type).
- Existing connect/disconnect/save-credential command tests (already present, exercising the
  ViewModel commands directly) are unaffected - this spec doesn't change command bodies, only what
  triggers them from the View.
- New `DisconnectCommand` tests: clears the relevant `CredentialStore` row(s) and flips `Is*Connected`
  back to false, for each of the nine providers.
- Manual: open each of the nine providers' dialogs, confirm the right fields/buttons show, confirm
  Disconnect appears only when connected, confirm the checkmark in the list updates after
  connect/disconnect without needing to leave and re-enter the Connections tab.
