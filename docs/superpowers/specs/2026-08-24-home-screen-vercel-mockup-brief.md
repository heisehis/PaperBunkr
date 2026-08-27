# Home Screen — Vercel/v0 Mockup Brief

**Purpose:** direction for generating a web mockup (v0.dev / Vercel) of Paperbunkr's Home screen
redesign, to explore the visual direction — especially the hero — before it gets built for real in
Avalonia. Not implementation-bound to any framework; the goal is a picture to react to.

**Why this exists:** the first pass at the hero (built directly in Avalonia, per
[2026-08-24-home-screen-design.md](2026-08-24-home-screen-design.md)) rendered as a full-bleed
stretched/cropped comic cover behind the title — the exact failure mode flagged and rejected during
this feature's own brainstorming (a portrait comic cover isn't composed for a wide backdrop crop,
so stretching it cuts off the art's actual subject and looks broken, not premium). This brief
corrects that direction for the mockup: **backdrop art is blurred/darkened mood only; the real,
undistorted poster sits on top of it at its own aspect ratio.** See the Hero section below.

---

## Quick-paste prompt (v0.dev)

If you want a single block to paste into v0 rather than working through the sections below:

> Build a dark-themed "Home" screen for a comic/manga library reader app called Paperbunkr, styled
> like a hybrid of a comic-book cover and a streaming app (Plex/Netflix/Apple TV browsing patterns,
> not their literal look). Near-black background (#0A0B0D), warm amber accent (#C9803F), cream body
> text (#ECE7DB). Display headings in Bebas Neue (all-caps, condensed, bold); body/UI text in Source
> Serif 4. Layout top to bottom: (1) a quiet header with a page title and a search box, no hero
> banner competing with it; (2) a hero card showing a comic issue "spotlight" — a BLURRED, DARKENED
> version of the cover art fills the card as atmosphere, and the actual sharp, undistorted cover
> (correct portrait aspect ratio, ~180px tall) sits on top of it on the left with drop shadow, next
> to the series name, issue title in large Bebas Neue, and an amber "Read Now" button; small dot
> indicators below for a rotating set of issues; (3) a horizontal-scrolling row of poster cards
> titled "Continue Reading" — each card is a portrait comic cover with a thin amber progress bar
> along the bottom edge and a small pill badge in the top-right corner; (4) another horizontal row
> "Recently Added" with the same poster-card style, no progress bar; (5) a "Because You Read X" row,
> same card style. Cards get a soft amber glow ring on hover. Rounded corners throughout (14px on
> big cards, 5px on poster cards), no sharp edges. Keep it dense and content-forward, not spacious —
> this is a library browsing screen, not a marketing landing page.

The sections below give the exact tokens and per-section detail behind that prompt, for anyone
iterating further.

---

## Design tokens (real values — reuse exactly, don't reinvent)

These are Paperbunkr's actual shipped design tokens (`src/Paperbunkr.App/App.axaml`,
"Evolved Amber" palette). Using the real values means the mockup can be compared apples-to-apples
against the real app, and colors that get approved can be copy-pasted straight back.

| Token | Hex | Role |
|---|---|---|
| `surface0` | `#000000` | Deepest background (behind everything) |
| `surface1` | `#0A0B0D` | Page background |
| `surface2` | `#131519` | Card/chrome background |
| `surface3` | `#1B1E24` | Elevated panel background (modals, floating panels) |
| `border` | `#2A2E37` | Hairline borders |
| `text` | `#ECE7DB` | Primary text (cream, not pure white) |
| `text-muted` | `#B3ADA0` | Secondary text |
| `text-faint` | `#77726A` | Tertiary/disabled text |
| `accent` | `#C9803F` | Primary amber (buttons, active states) |
| `accent-text` | `#E0995A` | Brighter amber for text-on-dark (links, "Read Now") |
| `accent-soft` | `#29C9803F` | Amber at ~16% opacity, for subtle fills (tag chips) |
| `badge` | `#D7AC4C` | Badge/pill background (gold, distinct from accent) |
| `badge-text` | `#241505` | Text on top of a badge |
| `glow` | `#66E0995A` | Hover/focus glow ring color (amber, ~40% opacity) |

Hero vignette gradient: transparent at the art → `surface0` at the edges (never a tinted color,
always neutral-to-black).

**Radii:** 5px (small — poster cards, chips), 7px (default — buttons), 14px (large — hero card,
floating panels). **Never sharp corners, never fully round except pills/dots.**

**Typography:**
- Display/headline font: **Bebas Neue** (Google Fonts: `Bebas Neue`) — all-caps or title-case,
  condensed, used ONLY for hero titles and section-adjacent big moments. Never for body/UI text.
- Body/UI font: **Source Serif 4** (Google Fonts: `Source Serif 4`) — used for everything else:
  labels, buttons, card titles, paragraph text. This is a deliberate "Comic Ink" choice, not a
  neutral sans system — lean into it rather than substituting a generic sans-serif.

**Motion:** interactions are snappy, ~150ms, ease-out. Hover states use a soft amber glow ring
(`box-shadow` using the `glow` token color), not a color/background swap.

---

## Section-by-section direction

### 1. Header
Quiet, not a hero itself. Page title + a search box + a small refresh affordance, left-to-right or
centered — doesn't need to compete visually with the hero card below it. This is intentional
restraint: the hero card is the one big visual moment on this screen, not the header.

### 2. Hero card — the part that needs fixing

**What went wrong the first time:** a portrait comic cover (roughly 2:3 aspect ratio) was stretched
and cropped to fill a wide horizontal band. This cuts the actual subject/composition of the cover
off — you end up seeing an arbitrary sliver of the middle of the image (a torso, a hand) instead of
anything that reads as "this is the cover of X." It looked like a rendering bug, not a design
choice.

**Corrected direction:**
- The card is a large, rounded (14px) container, roughly 3-3.5x wider than tall (e.g. a full-width
  band ~220-280px tall).
- **Backdrop layer:** the same cover art, but *blurred and darkened* (think: `filter: blur(20px)
  brightness(0.4)`, scaled up slightly to avoid visible edges from the blur) — this fills the whole
  card as ambient color/mood. It should never be sharp, and it's never the thing a viewer is meant
  to actually look at or read.
- **Foreground layer:** the real, unmodified cover art at its correct portrait aspect ratio, sized
  to roughly the card's full height minus padding (e.g. ~180-200px tall), positioned left-aligned
  with a drop shadow so it visibly sits "in front of" the blurred backdrop. This is the actual
  artwork a user recognizes — it is never stretched, cropped, or distorted.
- **Vignette:** a gradient overlay on the backdrop layer only, transparent near the foreground
  poster fading to `surface0` at all edges (not just the bottom) — this is what makes the blurred
  backdrop read as "atmosphere" rather than "a bad blur filter applied to a photo," and keeps any
  text content that sits over it legible.
- **Content:** next to the foreground poster — a small kicker line (series name, muted color), the
  issue title in large Bebas Neue (this can wrap to two lines), and an amber "Read Now" text/button
  underneath.
- **Rotation indicator:** small dots below the card, one lit amber for the currently-shown item —
  this card auto-rotates through a handful of picks.

This is closer to how Apple TV+ or a well-made streaming app handles a still-frame hero (real image,
heavy treatment around it) than Netflix's full-bleed wide key-art hero — because unlike a TV show,
there's no wide-format key art to draw on here, only the portrait cover itself.

### 3. Poster card rows (Continue Reading / Recently Added / Because You Read)
- Each row: a section header (Bebas Neue or bold Source Serif 4, either reads fine — pick one and
  stay consistent) plus a horizontally-scrolling strip of cards.
- Each card: portrait cover art (correct aspect ratio, never cropped/stretched), rounded 5px
  corners, a small pill badge top-right (e.g. issue count), title text below the cover in Source
  Serif 4.
- **Continue Reading** cards additionally show a thin (~3px) amber progress bar along the bottom
  edge of the cover, indicating how far into that issue the reader is.
- On hover: a soft amber glow ring around the card (the `glow` token), not a background/scale
  change — this should feel like a subtle highlight, not a jump.
- "Because You Read X" can repeat as multiple stacked rows, one per seed series, each with its own
  header.

### 4. "Try This Reading List" card
A different shape from the poster rows — wider, showing a cover thumbnail on the left plus a
synopsis paragraph and a row of genre tag chips (small pill shapes, `accent-soft` background,
`accent-text` text) on the right. Not part of the hero rotation — its own distinct single card
below the rows.

---

## Explicit non-goals / constraints

- **Don't invent a new color scheme.** Use the tokens above exactly — this needs to map back to a
  real dark-amber app, not become a generic "dark mode SaaS dashboard."
- **Don't make it spacious/marketing-site-like.** This is a dense library-browsing screen the user
  returns to constantly, not a landing page — err toward more content visible, less whitespace.
- **Don't substitute generic sans-serif fonts for Bebas Neue/Source Serif 4** — the serif body text
  is a deliberate "comic ink" identity choice, not a placeholder waiting to be swapped for something
  more "modern."
- **Don't add navigation chrome (sidebar/rail) as part of this mockup** unless useful for context —
  the nav rail is a separate, already-built piece; this mockup is about the Home screen's own
  content, not the app shell around it.
- **Cover art:** use real portrait comic/manga cover images (any copyright-safe placeholder set is
  fine — the point is testing the hero treatment against genuinely portrait-shaped, illustrated
  cover art, not photography or square album-art-style placeholders, since those don't expose the
  same cropping problem this brief exists to solve.
