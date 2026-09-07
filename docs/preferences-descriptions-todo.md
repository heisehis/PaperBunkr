# Preferences — Description Copy Checklist

Generated as part of `docs/superpowers/specs/2026-09-07-preferences-tile-hub-redesign-{design,plan}.md`
Step 10. Every `SettingsRow` below currently ships with no `Description` — the row still renders
correctly (title + control only, no dead space), but adding a short one-line description here
closes the last piece of that redesign. Rows whose description is a *live/bound* status text (e.g.
"Write all library metadata…", "Check for updates") aren't listed — nothing to write, they already
show real-time state.

To fill one in: open the named `.axaml` file, find the `<pref:SettingsRow Title="...">` block, add
`Description="..."` next to `Title`.

## General (`src/Paperbunkr.App/Views/Preferences/GeneralSection.axaml`)

- [ ] Reading → "Resume issues where you left off"
- [ ] Reading → "Reading past the last page opens the next issue"
- [ ] Reading → "Ask me to rate a comic when I finish it"
- [ ] Library → "Allow importing files by dragging them into the window"

## Appearance (`src/Paperbunkr.App/Views/Preferences/AppearanceSection.axaml`)

- [ ] Install Skin → "Install a skin from a file"
- [ ] Install Skin → "Open the folder where installed skins live"
- [ ] Font → "Font family"
- [ ] Developer → "Open the internal component/style showcase"

## Reader (`src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml`)

- [ ] Right to Left → "Reverse left/right page-turn direction for right-to-left books"
- [ ] Display → "Auto-rotate landscape pages by default"
- [ ] Display → "Page transition speed" (Slider)
- [ ] Zoom & Navigation → "Reset zoom when turning the page"
- [ ] Zoom & Navigation → "Mouse wheel scroll speed" (Slider)
- [ ] Image Adjustment → "Brightness" (Slider)
- [ ] Image Adjustment → "Contrast" (Slider)
- [ ] Image Adjustment → "Saturation" (Slider)
- [ ] Image Adjustment → "Gamma" (Slider)
- [ ] Background & Margin → "Background color" (preset + custom hex)
- [ ] Background & Margin → "Add a margin around the page"
- [ ] Background & Margin → "Margin width" (Slider)

## About (`src/Paperbunkr.App/Views/Preferences/AboutSection.axaml`)

- [ ] Updates → "Version"
- [ ] Updates → "Check for updates on startup"

## Not on this list (already have real copy)

Every row in **Advanced** already reuses its original inline description text (Rendering's two
rows, all three Comic File Metadata toggles) — nothing left to write there. Same for several Reader
rows (High Quality Page Display, Default Fit Mode, Double-Page Spread, Page Transition, Canvas
Background) and Appearance's Motion/Navigation rows, which carried over their existing explanatory
text word-for-word.
