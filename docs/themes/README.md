# Writing a Paperbunkr theme

A theme is a `.crpck` file: a zip with a `theme.json` at its root, plus any images, icons and layout files it refers to. There is no editor.
You write the JSON, zip it, and install it from **Preferences > Appearance > Theme > Install…**. Anything Paperbunkr could not use is listed
under the gallery with the place in the file it is about, and the theme still installs if it can.

- [theme.schema.json](theme.schema.json) gives an editor completion and hover help (in VS Code: `"$schema": "./theme.schema.json"` at the top
  of your file, or a `json.schemas` mapping). It is a convenience; the app does its own checking.
- [samples/harbor](samples/harbor) has three variants (dark, light, OLED), component skins, a material, a motion and a sidebar Home layout.
- [samples/gazette](samples/gazette) is one light look with a tabbed Home layout.
- **Export this theme…** in Appearance saves any theme, built-in ones included, as a `.crpck` you can unzip and start from. Ten of the
  built-ins are version 2 families (Nordic Frost, Tokyo Midnight, Reading Room, Canopy, Crimson Ink, Abyss, Sunset Boulevard, Newsprint,
  Arcade, Graphite); Newsprint and Arcade also carry a Home layout.

## The smallest theme

```json
{
  "schemaVersion": 2,
  "id": "org.example.mine",
  "name": "Mine",
  "mode": "Dark",
  "base": { "palette": { "canvas": "#101418", "ink": "#E6EAF0", "accent": "#4DA3FF" } }
}
```

Three colours are a complete theme. Every role you do not set (borders, muted text, hover and pressed states, chart colours, the reader's
chrome) is worked out from them. Set a role under `semantic` only where you want something different.

A theme file without `schemaVersion` is a version 1 theme. Those keep working exactly as before and are never rewritten.

## Variants

Put what every variant shares in `base` and only the differences in `variants`. Each variant shows as its own entry in the gallery, and
Auto switch moves between the light and dark variants of the family you are using. A variant whose id or `kind` is `oled` is your own
true-black design; it is used as written, and the app's True black option leaves it alone.

## What a theme can contain

| Section | What it does |
| --- | --- |
| `palette` | Your raw colours. Other values can point at them with `{ "ref": "palette.accent" }`. |
| `semantic` | The roles the app paints with (`surface.raised`, `text.primary`, `status.danger`, `reader.background.page`, ...). |
| `metrics` | Corner radii, the spacing unit, scrollbar width and thumb opacity. |
| `typography` | Family, size and weight for `display`, `heading`, `body`, `caption`, `monospace`. Name an installed font. |
| `components` | The look of `button.primary`, `button.secondary`, `button.ghost`, `chip`, `panel.floating`, with optional `hover`, `pressed`, `disabled` states. |
| `materials`, `surfaces` | Gradients and images, and which surface uses which. Every material has a flat `fallback`. |
| `icons` | Replace an icon by its meaning (`navigation.home`), with another symbol, SVG path data, or a file in the package. |
| `motion` | Short named animations of opacity, offset, scale and rotation. |
| `layouts` | A different arrangement of a screen. See below. |

## Layouts

A layout rearranges a screen out of parts the screen itself publishes. It is a tree of a fixed set of containers (`Stack`, `Grid`, `Dock`,
`Split`, `Tabs`, `Overlay`, `Scroll`, `Panel`) and elements (`Text`, `Image`, `Icon`, `Button`, `Spacer`, `Divider`), plus the screen's
own components. A layout cannot run code, read arbitrary data or reach the network: text can show a published value in braces
(`"{home.greeting}"`), a button can run a published command, and `visible` can follow a published true/false value or its negation.

Home (contract version 1) publishes:

- Components: `Home.Masthead`, `Home.Search`, `Home.Greeting`, `Home.Sections`, and `Home.Section` with a `section` key
  (`spotlight`, `needsAttention`, `continueReading`, `upNext`, `recentlyAdded`, `collections`, `becauseYouRead`, `readingList`).
- Values: `home.greeting`, `home.hasSections`, `home.sectionCount`, `app.name`.
- Commands: `home.refresh`, `home.search`, `home.openLibrary`, `home.openInsights`, `home.customize`.
- A Home layout must include the search (`Home.Masthead` or `Home.Search`) and the sections (`Home.Sections` or a `Home.Section`).

Give every node a stable `id`. Breakpoints and the user's own adjustments refer to nodes by id, so renaming one drops them.

Layouts are off until the user turns on **Theme layouts (experimental)** in Appearance. If a layout is refused, or fails while it is being
built, the screen shows its built-in layout and the reason appears in the theme's notes. Keyboard shortcuts belong to the screen, not the
layout, so they work the same in any arrangement.

## Motion

A motion has 2 to 16 keyframes from `at: 0` to `at: 1` and must end at rest, so a control is never left faded or shifted. A layout node
plays one when it appears (`"motion": "settle"`); `fadeIn`, `fadeUp` and `scaleIn` are built in. Reduce motion and the Performance effect
quality play none of it. A motion that repeats forever may only change opacity, and plays only at High quality on mains power.

## What a theme cannot do

- Nothing in a package is ever executed. Scripts, executables, libraries and shortcuts make the whole package be refused.
- Nothing is fetched. Images, fonts and layouts must be files inside the package; a URL is refused.
- Limits: 512 files, 64 MB in total, 16 MB per file, images up to 8192 px, `.ttf` and `.otf` fonts.
- Font files in a package are checked but not loaded yet; `typography` names fonts already installed on the computer.
- Blur behind a single panel is not available; an `acrylic` material draws as its tint.
- Comic page art is never recoloured. A book's own Light, Dark, Sepia, OLED or High contrast choice is kept; only **Match app** follows
  your theme, through `reader.background.page` and `reader.background.text`.

## Your own changes are separate

What a user changes under **Customize** is stored beside their library, never inside your package, and survives a theme update.
**Export my customizations…** and **Import customizations…** move that file between computers.

## Extensions (`.pbpck`)

A layout may name a component from an extension: `{ "id": "weather", "type": "Extension", "provider": "org.example.weather",
"component": "forecast", "children": [ <one fallback node> ] }`. When no such provider is installed, which today is always, the fallback
child is shown and nothing else happens. Paperbunkr does not load, download or run anything because a theme named a provider.

Providers are a separate project. What the app side already fixes for it: a provider is registered in process by the existing plugin
host under a stable id, lists the components it offers, and returns a control for one of them or nothing. A provider that declines or
throws is treated as absent. Packaging, signing, permissions and any sandbox for `.pbpck` are not designed here and are not part of the
theme format.
