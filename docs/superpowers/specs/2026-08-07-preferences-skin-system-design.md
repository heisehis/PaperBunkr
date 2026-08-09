# Preferences Screen — Settings Infrastructure + Skin/Theme System

*Date: 2026-08-07. First sub-project out of the CE feature-parity audit
(docs/ce-feature-inventory.md), which found Preferences at 0% implementation despite being one of
the five wireframed screens (onboarding.md §12). "Preferences" as originally wireframed is
narrower than what the audit's full CE `PreferencesDialog` itemization implies (Reader/Behavior/
Libraries/Scripts/Advanced tabs) — this spec deliberately covers only the settings-infrastructure
foundation plus the skin/theme system already named in §12/§13, since the full CE-parity surface
is multiple independent sub-projects, not one. Reader/Behavior/Libraries/Scripts/Advanced tabs are
follow-up specs that build on the tab-strip shell this spec establishes.*

## 1. Data model

New `AppSettings` entity (`Paperbunkr.Data.Entities`) — a singleton row (`Id` fixed at 1, the "one
row of app-wide config" convention) with typed columns, per the decision to migrate-as-we-go
rather than a generic key-value store (matches every other entity in this codebase):

- `ActiveSkinKey` (`string`, defaults to `"default"` — the one built-in theme)
- `SelectedFontFamily` (`string?`, `null` = app default, not a system-font override)

`PaperbunkrDbContext` gains `DbSet<AppSettings>`. `PaperbunkrDb.GetOrCreateAppSettings()` creates
the singleton row on first access if missing, mirroring how `EnsureCreated()` already seeds system
smart lists idempotently.

## 2. `theme.json` schema + `.crpck` format

`.crpck` is a plain ZIP (`System.IO.Compression.ZipFile`) containing `theme.json` plus an `icons/`
folder — matches onboarding.md §13's already-resolved format. `theme.json` maps directly onto
Paperbunkr's existing live token set in `App.axaml` (no renaming/translation):

```json
{
  "name": "Windows 11",
  "colors": {
    "bg": "#14161B", "chrome": "#1C1F26", "border": "#2A2E37",
    "text": "#ECE7DB", "textMuted": "#B3ADA0", "textFaint": "#77726A",
    "accent": "#C9803F", "accentText": "#E0995A", "accentSoft": "#29C9803F",
    "badge": "#D7AC4C", "badgeText": "#241505", "success": "#5FA889"
  },
  "spacingUnit": 4,
  "radius": 7,
  "icons": { "library": "icons/library.png", "reader": "icons/reader.png" }
}
```

`icons` is part of the schema (forward-compatible) and `SkinService.GetIcon` (§3) is real, tested
capability — but nothing in the current UI consumes it yet (the rail nav uses plain text labels,
not swappable images), so no icon manifest content ships with the built-in skin. The built-in
`"default"` skin (today's hardcoded dark theme) is an embedded resource following this exact
schema, not a special-cased code path — switching skins and the app's own default look go through
identical code.

## 3. `SkinService`

New `SkinService` (`Paperbunkr.App.Services`) plus a `SkinPaths` helper mirroring the
`CoverThumbnailPaths` convention from tonight's cover-thumbnails work: installed `.crpck` files at
`%AppData%\Paperbunkr\skins\`, extracted once to `%AppData%\Paperbunkr\skins-extracted\{key}\` (so
every consumer does plain file-path lookups, no `ZipArchive` API on any hot path — same rationale
the original WinForms ThemeFramework plugin design used).

- `GetAvailableSkins()` — built-in `"default"` + every extracted `.crpck`, reading each
  `theme.json`'s `name` for display.
- `LoadSkin(key)` — parses `theme.json` into a typed `SkinTheme` record.
- `ApplySkin(key)` — sets `Application.Current.Resources[...]` for every `Pb*Brush`/`Pb*Color`/
  `PbSpacingUnit`/`PbRadius` directly (§13's resolved mechanism - no runtime XAML loading
  anywhere), persists `ActiveSkinKey` via `AppSettings`.
- `TryInstallSkin(crpckPath, out error)` — copies + extracts + validates `theme.json` parses
  before accepting it; rescans available skins.
- `GetIcon(skinKey, iconKey)` — resolves + caches a `Bitmap?` from the active skin's icon
  manifest (same cache-hit/uncached-miss shape as `CoverImageCache`), `null` if undefined.
- `GetInstalledFontFamilies()` — wraps `SkiaSharp.SKFontManager.Default.FontFamilies` (§13's
  resolved cross-platform replacement for GDI+ `InstalledFontCollection`), prepends
  `"System Default"`.

**Font application:** `Application.Current.Resources["PbFontFamily"]` (a `FontFamily`, consumed
via `{DynamicResource}`) plus a base `Style` targeting text-bearing controls broadly - a genuine
global font override, not CE's original "just the title bar" proof-of-concept scope. This is more
thorough and is simpler in Avalonia's cascading style system than CE's piecemeal per-control
WinForms approach was.

## 4. DynamicResource conversion

221 occurrences of `{StaticResource PbXxx...}` across 11 view files become `{DynamicResource
PbXxx...}` - a pure, mechanical find-replace scoped only to the `Pb*` token keys (not touching
unrelated local resources like `ReaderScreen.axaml`'s own `ReaderBgBrush` family, which isn't part
of the skin system). `DynamicResource` is a strict superset of `StaticResource`'s behavior -
anywhere the binding worked before, it still works, just with live-swap capability added. This is
what lets a skin switch actually reach every screen instead of only ones written after this change.

## 5. Preferences screen

New rail-nav entry (`Pf`, alongside the existing `Li`/`Sm`/`Rd`/`Pl`/`Mg`/`Rx` abbreviation
convention), wired into `MainViewModel` the same way Smart/Reading are today.
`PreferencesScreenViewModel` reuses the exact tab-strip pattern `DetailTabsViewModel` already
established (mode enum + computed `Is*Tab` flags) - but only **one real tab exists yet:
"Appearance."** The tab-strip shell is real infrastructure, ready for Reader/Behavior/Libraries/
Scripts/Advanced to land as additional tabs in their own future specs, not scaffolding built for
nothing.

**Appearance tab**, matching the "Skins / Install Skin / Font / Future" layout onboarding.md §12
already named:

- **Skins** - list of installed skins (`SkinService.GetAvailableSkins()`), click to apply
  (single-action, no separate confirm step).
- **Install Skin** - "Browse…" button (`IFilePickerService.PickOpenFileAsync`, already exists,
  filtered to `*.crpck`) → `SkinService.TryInstallSkin`, rescans the list; "Open Skins Folder"
  button.
- **Font** - dropdown of `SkinService.GetInstalledFontFamilies()` + "System Default," applies
  immediately via `SkinService`, persists to `AppSettings`.
- **Future** - a visibly-disabled placeholder group ("Cover Shape - coming soon"), matching the
  original design's explicit room-to-grow stub rather than implying it's functional.

**Deliberately out of scope for this spec** (confirmed in triage, follow-up work): a real
`windows_11.crpck` reference skin with actual alternate color values - this spec ships the
mechanism with only the built-in default skin; designing a second real palette is its own small
follow-up once the mechanism is proven working.

## Testing

- `AppSettingsTests` (`Paperbunkr.Data.Tests`): singleton-row creation is idempotent
  (`GetOrCreateAppSettings` called twice returns the same row, doesn't duplicate).
- `SkinServiceTests` (`Paperbunkr.App.Tests`, joins `AvaloniaTestCollection` since `ApplySkin`
  touches `Application.Current.Resources`): a hand-built test `.crpck` (small `ZipFile`-based
  fixture, same "generate via the real code path" precedent as `CbzFixture`) round-trips through
  install → extract → parse → apply correctly; a malformed `theme.json` is rejected with a clear
  error, not a crash; `GetAvailableSkins` lists built-in + installed; `GetIcon` cache-hit/miss
  mirrors `CoverImageCache`'s existing test shape.
- `PreferencesScreenViewModelTests`: tab-switch flags, skin selection triggers `ApplySkin`, font
  selection persists.
- **Manual verification** (same no-GUI-automation approach used throughout tonight): build + run
  the real tests, then ask the user to actually switch skins in the running app and confirm colors
  update live across multiple screens - that's the one thing no unit test can prove.
