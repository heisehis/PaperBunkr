# Books improvement arc — Slice 4b: dictionary look-up (design)

Date: 2026-10-27. Status: **built, uncommitted** (confirmed 2026-10-28 via source and tests; on-screen check by the user outstanding). Decisions: Slice 4 grilling rounds 1 and 2, all accepted. **Assumed** = not asked.

## 1. Goal

Select a word in the book reader and see its definition, offline by default.

## 2. Offline data

- An optional English dictionary built from WordNet, produced by a small extract tool (same shape as the GCD data tool) into one compact SQLite file `dictionary-en.db` (`lemma`, `pos`, `definition`, `example`; an index on `lemma`). **Assumed**: WordNet's licence is permissive (it ships with attribution); the attribution goes into the About legal texts.
- **Not bundled.** Downloaded on first use from the app's existing data-download mechanism (same as GCD), with a "Download dictionary (about N MB)" prompt in the popup the first time; it lives in the app's local data folder.
- Lookup: exact lowercase match, then a small suffix-stripping fallback (`ies→y`, `s`, `es`, `ed`, `ing`, `ly`, `er`, `est`) tried in order, returning up to 3 senses per part of speech. A pure `DictionaryLemmatizer` and `DictionaryLookup` (given a lookup function) so both are unit-tested without the database.
- Non-English books (`LanguageISO` not `en` / empty): "No offline dictionary for this language."

## 3. Online fallback

dictionaryapi.dev (`https://api.dictionaryapi.dev/api/v2/entries/en/{word}`). Off by default; the Preferences switch (Books section, "Look up unknown words online") turns it on. The popup shows a "Look up online" link only when the switch is on; the request sends just the word. Network or 404 failures show "No definition found."

## 4. In the reader

Selecting a single word (no whitespace, up to 40 characters) opens the existing highlight popup (already anchored next to the selection). It gains a **Look up** button; pressing it replaces the colour row with a definition card (word, part of speech, up to three senses, "Back" returns to the colours). A multi-word selection shows no Look up button. Also reachable by a remappable action `BookReader.LookUp` (default `L`) acting on the current selection (declines when there is none). **Assumed**: the popup is an Avalonia popup, which is already hosted so it shows above the WebView (the highlight popup's existing arrangement); verifying this is a plan step.

## 5. Tests and verification

- `DictionaryLemmatizerTests` (forms), `DictionaryLookupTests` (fake source: exact, stemmed, missing, sense caps), online response parsing with a fake handler, selection filtering (single word vs phrase), popup view-model state (looking up, result, back), settings default off.
- The extract tool is run once on the user's machine to produce the file; verify a handful of known words.
- On screen: the popup swap and the download prompt, light/dark/sepia.

## 6. Out of scope

Other languages, pronunciations, thesaurus, saving words, translation, a standalone dictionary screen.
