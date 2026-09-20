# Plugin API 4.1 — Slice 4 (settings schema) — Implementation Plan

> **Status: implemented 2026-09-20.** 68 schema tests, 41 storage/view-model tests, 9 headless view tests, DPAPI and
> `CredentialStore` tests pass; App builds with 0 errors after a forced recompile. **Not verified on screen.** One deviation:
> the headless test app has no theme and can't template an `ItemsControl`, so the view tests build each row from the view's own
> row `DataTemplate` directly (documented in the test).
*Implements: docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §6.*

Surveyed: `PluginManifest`, `PluginEngine.Discover`, `IPluginConfig` and its only real implementation
(`PaperbunkrPluginEnvironment.GetSetting/SetSetting` over the `PluginSettingState` table),
`PluginHostService.OpenPluginSettingsAsync`, `INativePluginSettingsUi`, `NativePluginModalHostViewModel`,
`CredentialStore`, the shared `SettingsRow` control, `SuggestBox`, `TextSpinner`.

## Facts that shape the design
- **`CredentialStore` already does DPAPI** (`dpapi1:` + base64, `CurrentUser`, an entropy string, decrypt
  failure → null). It is keyed to `ProviderCredential` rows, so it can't store plugin settings directly, but
  its crypto is exactly what §6.2 asks for. The crypto is factored into a shared `DpapiSecrets` and
  `CredentialStore` delegates to it (behaviour identical, existing tests are the regression check); plugin
  settings use it with **their own entropy**, so ciphertext isn't interchangeable between the two stores.
- Settings are one string per key in `PluginSettingState`; the schema layers typing on top, storage is unchanged.
- The settings overlay reuses `OpenPluginSettingsAsync` + `NativePluginModalHostViewModel`; the row layout
  reuses `SettingsRow`; number fields reuse the `TextSpinner` behaviour (`ClampTyped`); choice fields use
  `SuggestBox` (never `ComboBox`).
- `Paperbunkr.Plugins` cannot see `INativePluginSettingsUi` (it lives in the Avalonia-dependent `.Ui`
  project), so the native "schema **and** custom settings UI" conflict is detected in the App layer after
  discovery and the package is rejected through a new `PluginEngine.RejectPackage`.

## Steps
1. **Data:** `Credentials/DpapiSecrets.cs` (shared crypto); `CredentialStore` delegates to it.
2. **Plugins — schema model:** `<Settings>` in `PluginManifest`; `PluginSettingsSchema` (parse + validate the
   definition, validate a value, resolve an effective value), `ISecretProtector`.
3. **Plugins — engine:** parse/validate at discovery (a bad schema, or a schema plus a `ConfigScript`, blocks
   the plugin with a reason exactly like a `requiresApi` block); `PluginEngine.SettingsSchemas`; `RejectPackage`.
4. **App — storage:** `DpapiSecretProtector`; `PluginSettingsAccess` (schema-aware get/set/raw over
   `PluginSettingState`); `PaperbunkrPluginEnvironment` delegates its `GetSetting`/`SetSetting` to it.
5. **App — host:** wire the resolver, reject native schema+custom-UI plugins, make Configure available for
   schema plugins, open the schema overlay from `OpenPluginSettingsAsync`.
6. **App — UI:** `PluginSettingsSchemaViewModel` + row VMs, `PluginSettingsSchemaView.axaml` **with its
   code-behind in the same step** (the new-`x:Class` build gotcha in `CLAUDE.md`). Load the `avalonia`
   skill and read `review-checklist` before calling it done; theme resources only, no hex; a forced
   recompile to prove the XAML weave ran.
7. **Tests:** schema parsing/validation; engine integration; access (defaults, invalid→default with the raw
   string preserved, secrets encrypted at rest, undecryptable→default, undeclared keys untouched);
   view-model (saves valid edits, refuses invalid ones with an inline error, flags an invalid stored value);
   DPAPI round-trip (Windows only) + `CredentialStore` regression; headless view smoke test.
8. **Docs:** spec §6, wiki, roadmap.
