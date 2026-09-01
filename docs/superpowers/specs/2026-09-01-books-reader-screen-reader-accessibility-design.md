# Books Reader Screen-Reader Accessibility — Design

**Third of three deferred follow-ups** to the Thorium/eBoox research-doc application to Paperbunkr's
Books section. First (reader ergonomics + annotations/export,
`2026-09-01-books-reader-ergonomics-and-annotations-design.md`) and second (FB2/MOBI ingestion,
`2026-09-01-books-format-ingestion-fb2-mobi-design.md`) are written and committed, both on hold
pending implementation plans. Fourth (catalog federation + cloud sync) is a separate later spec.

## Background

The research doc's accessibility domain bundles two different things: screen-reader support for
blind/low-vision users, and text-to-speech as a general convenience feature. Scoped down to
**screen-reader support only** — TTS dropped entirely for this round.

This spec is directly coupled to the first spec's `ParagraphView` design. That spec replaces the
reflow reader's plain `TextBlock` (per-paragraph) with a custom `Control`-derived `ParagraphView`
built on Avalonia's `TextLayout` API, to support word spacing, drag-selection, and highlight
rendering together. `TextBlock` gets automatic screen-reader exposure for free via Avalonia's
built-in `TextBlockAutomationPeer`; a custom `Control` gets **none** unless it implements its own
`AutomationPeer`. Without addressing this, shipping `ParagraphView` as designed would make every book
read through the reflow reader invisible to Narrator/NVDA/JAWS — a real regression introduced by the
first spec, not a pre-existing gap. This spec's Section 3 (`ParagraphViewAutomationPeer`) is the
piece that closes it, and should be built alongside `ParagraphView` itself in the first spec's
implementation, not as separate follow-on work.

Today, `BookReaderScreen.axaml` has **zero** `AutomationProperties` usage, versus 139 occurrences
across the rest of the app (Library, Home, property overlays, etc. — from the existing UI Automation
work, `docs/superpowers/specs/...ui-automation...`). This is a genuine, pre-existing gap independent
of `ParagraphView`.

**Out of scope**: PDF reading. It's rasterized page images (`PdfPageReaderScreenViewModel`) with no
extractable text; real accessibility there needs OCR, which this app has no existing capability for
and which is a substantially larger separate undertaking. This is a stated, known limitation of this
spec, not a silent gap.

**CE note:** no CE equivalent — nothing to verify against.

## Decisions

| Area | Decision |
|---|---|
| **Scope** | Screen-reader support for the reflowable (EPUB/FB2/MOBI) reader only. TTS dropped. PDF reading explicitly excluded (OCR-sized problem, out of scope). |
| **Chrome labeling** | `AutomationProperties.Name` on every icon-only control in `BookReaderScreen.axaml` (TOC/Bookmarks/Highlights/Font-sheet/Search toggles, fullscreen, etc.) — currently zero coverage. `AutomationProperties.LabeledBy` linking each drawer's header to its list. |
| **Chrome auto-hide interaction** | No special-case code needed: auto-hide (spec 1) triggers off pointer idle only, so a screen-reader/keyboard-only user never triggers it — verified in testing, not defended against in code. |
| **Position announcements** | Page-turn/chapter-change events get an `AutomationProperties.LiveSetting="Polite"` status announcement, reusing the existing toast live-region pattern already used elsewhere in the app. |
| **`ParagraphView` text exposure** | New `ParagraphViewAutomationPeer : ControlAutomationPeer`, returned from `ParagraphView.OnCreateAutomationPeer()`. Reports `AutomationControlType.Text` and the paragraph's plain text (the same string backing its `TextLayout`, independent of the word-spacing rendering adjustment, which is display-only). Highlighted-range status exposure is a nice-to-have layered on top, not a blocker for the core requirement (text reachable at all). |
| **Accessibility-tree discoverability** | Explicitly verify (not just implement) that each `ParagraphView`'s peer is discoverable as a child of the reading canvas's own peer — a common Avalonia pitfall is a correctly-implemented child peer that's unreachable because the hosting `ItemsControl`/container peer doesn't expose children correctly. |
| **"Where am I?" heading trail** | `Ctrl+Shift+W` (a shortcut not already used elsewhere in this app; deliberately not Thorium's `Ctrl+F10`, which has no meaning here) announces current position as a screen-reader-only live-region string, e.g. `"Chapter 4 of 12: The Long Way Home"`, built from data (`TableOfContents`, current chapter index) `BookReaderScreenViewModel` already tracks. No visual UI — it's an announcement, not a panel. |
| **Testing posture** | This domain isn't unit-testable the way the rest of the suite is. A real, manual Narrator pass is required and should be called out explicitly as a plan step, not assumed satisfied by green automated tests. `AutomationProperties.AutomationId` coverage lets the existing FlaUI `Paperbunkr.UiTests` suite assert elements are present/named in the tree — a useful proxy check, not a substitute for the manual pass. |

## Components

### 1. `BookReaderScreen.axaml` — chrome labeling

- `AutomationProperties.Name`/`HelpText` added to every currently-unlabeled icon-only button
  (mirrors the pattern already used in `BooksScreen.axaml`/`LibraryToolbar.axaml`/etc.).
- `AutomationProperties.LabeledBy` between each drawer section header `TextBlock` and its
  `ItemsControl` (Chapters/Bookmarks/Highlights).
- A new hidden/live-region `TextBlock` (`AutomationProperties.LiveSetting="Polite"`,
  `AutomationProperties.Name="Reading position"`) bound to a `BookReaderScreenViewModel` property
  updated on page-turn/chapter-change.

### 2. `ParagraphViewAutomationPeer` (`Paperbunkr.App/Views/ParagraphViewAutomationPeer.cs`)

- Wraps a `ParagraphView` instance; `GetNameCore()`/text-provider surface returns the paragraph's
  plain text; `GetAutomationControlTypeCore() => AutomationControlType.Text`.
- `ParagraphView.OnCreateAutomationPeer()` returns this peer. Built and verified alongside
  `ParagraphView` itself as part of the first spec's implementation plan (explicit cross-spec
  dependency, not follow-on work).

### 3. `BookReaderScreenViewModel` — heading trail

- New `Ctrl+Shift+W` key binding → builds and raises the position-announcement string described in
  Decisions, written to the same live-region property chrome labeling (Component 1) already wires up.

## Risks / Open Questions

- **`ParagraphViewAutomationPeer` is the one component of this spec that can silently "half-work"** —
  it's easy to implement a peer that reports correctly in isolation but never gets discovered by AT
  because of a parent-peer children-exposure issue; this must be verified with an actual Narrator
  session against the real running reader, not assumed from code review alone.
- **No existing Narrator/NVDA testing precedent in this codebase** to build on — the manual-pass step
  in the plan is genuinely new territory for this project's verification process, not a repeat of an
  established pattern.

## Testing

- `AutomationProperties.AutomationId` added across chrome controls, exercised by new/extended
  `Paperbunkr.UiTests` (FlaUI) assertions confirming elements are present and named — a structural
  proxy check.
- Manual verification: a real Windows Narrator pass over the reader (open a book, navigate chapters,
  read paragraph text, trigger the heading-trail announcement, operate every chrome control via
  keyboard-only) — called out explicitly as a required plan step, since no automated test in this
  codebase can substitute for it.
