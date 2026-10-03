# Keyboard, Mouse and Controller Shortcuts

PaperBunkr is fully keyboard-navigable — arrow keys move through every grid and sidebar, `Tab`
reaches every control in order, and context menus open with the `Menu` key or `Shift+F10`.

Every shortcut on this page is an **action** with one or more inputs bound to it: a key, a mouse
button, a wheel direction, or a controller button. All of them are **remappable** in **Preferences →
Keyboard Shortcuts** (see [Remapping](#remapping)). The tables below list the defaults. Shortcuts
never fire while you are typing in a text box, except `Esc`, the browser-back key and `Ctrl+P`.

## App-wide

| Action | Default |
|---|---|
| Close the current overlay / clear the selection | `Esc` |
| Back / forward | the browser Back / Forward keys, mouse side buttons 4 / 5, a two-finger swipe left / right |
| Quick open | `Ctrl+P` |
| Open Preferences | `Ctrl+,` |
| Cycle screens forward / back | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| Undo / redo a metadata edit | `Ctrl+Z` / `Ctrl+Y` |
| Focus the search box | `Ctrl+F` (or `/` in the Library) |
| Pin / unpin the sidebar | `Shift+F6` |
| Quit | `Ctrl+Q` |
| Move up/down in the sidebar | `↑` `↓`, `Home`, `End` (fixed) |

## Library grid

| Action | Default |
|---|---|
| Select all visible | `Ctrl+A` |
| Remove selected item(s) | `Delete` |
| Refresh | `F5` |
| Show / hide the preview panel | `Ctrl+B` |
| Edit properties (bulk edit for several) | `Ctrl+I` |
| Rating: none / 1–5 stars | `Alt+Shift+0` … `Alt+Shift+5` |
| Mark as read / unread | `Alt+Shift+R` / `Alt+Shift+U` |
| Show in Explorer | `Ctrl+G` |
| Copy data / paste data | `Ctrl+C` / `Ctrl+V` |
| Copy file paths | `Ctrl+Shift+C` |
| Clear search and return to grid | `Esc` (while the search box has text) |

Menu entries and the selection bar show whatever you have bound, so they stay correct after a remap.

## Reader

Defaults mirror ComicRack CE's keymap (with two deliberate exceptions — page-turn is Left/Right
instead of CE's PageUp/Alt+Left).

### Navigation

| Action | Default | Where it applies |
|---|---|---|
| Page back (spatial left) | `←` | paged, not zoomed |
| Page forward (spatial right) | `→` | paged, not zoomed |
| Page back / forward (vertical paging) | `↑` / `↓` | top-to-bottom paged books |
| Next page (reading order) | `PageDown`, `Space`, media next-track, mouse side button 5, controller `A` / right bumper | paged modes |
| Previous page (reading order) | `PageUp`, `Shift+Space`, media previous-track, mouse side button 4, controller `B` / left bumper | paged modes |
| First / last page | `Home` / `End` | paged modes |
| Pan left / right / up / down | `←` `→` `↑` `↓` | paged, zoomed in |
| Scroll left / right / up / down | `←` `→` `↑` `↓` | continuous modes |
| Scroll up / down a page | `PageUp` / `PageDown`, mouse side buttons 4 / 5 | continuous modes |
| Scroll to start / end | `Home` / `End` | continuous modes |
| Toggle auto-scroll | `S` | continuous modes |
| Previous / next bookmark | `Ctrl+PageUp` / `Ctrl+PageDown` | always |
| Back to previous position (after a jump) | `Alt+←` | always |
| Report a bad page | `X` | always |
| Reader command palette | `Ctrl+K` | always |
| Go to page… | `Ctrl+G` | always |

The mouse side buttons turn pages in the reader while **Preferences → Reader → Mouse side buttons turn
pages** is on (the default); with it off they go back / forward like everywhere else.

### Zoom & Fit

| Action | Default |
|---|---|
| Zoom in / out | `Z` / `Shift+Z`, or `Ctrl` + wheel up / down (zooms around the cursor) |
| Fit: Original size | `1` |
| Fit: Fit all | `2` |
| Fit: Fit width | `3` |
| Fit: Fit height | `4` |
| Fit: Best fit | `5` |

### Display and tools

| Action | Default |
|---|---|
| Toggle fullscreen | `F`, `F11` |
| Rotate clockwise / counter-clockwise | `R` / `Shift+R` |
| Next reader profile | `P` |
| Toggle reading stats | `H` |
| Toggle warm tint | `W` |
| Toggle guided panel view | `G` |
| Toggle the info panel | `I` |
| Pin this page as a reference | `Shift+P` |
| Clip a region of this page | `Ctrl+Shift+C` |
| Copy page (or spread) | `Ctrl+C` |
| Toggle the performance overlay | `Ctrl+Shift+P` |

### Controller (Xbox / XInput)

Active while a reader is open and **Preferences → Reader → Use a game controller** is on.

| Action | Default |
|---|---|
| Next / previous page | `A` or right bumper / `B` or left bumper |
| Turn, pan or scroll | D-pad or left stick (what happens depends on whether the page is zoomed, as with the arrow keys) |
| Pan / scroll continuously | right stick |
| Zoom | right trigger in, left trigger out |
| Show / hide the toolbar | `Y` |
| Fullscreen | `X` |
| Command palette | `Start` |
| Close the reader | `Back` |

## Books (EPUB, FB2, MOBI)

| Action | Default |
|---|---|
| Next page | `→`, `PageDown`, `Space` |
| Previous page | `←`, `PageUp` |
| Announce the reading position (for screen readers) | `Ctrl+Shift+W` |

These work whether the focus is on the reader's toolbar or inside the page itself.

## Compare

| Action | Default |
|---|---|
| Next / previous page | `→` `PageDown` / `←` `PageUp` |
| First page | `Home` |
| Flip between the two | `Space` or `F` |
| Switch comparison mode | `M` |
| Reset zoom and pan | `0` |
| Keep A / keep B | `1` / `2` |

## Remapping

1. **Preferences → Keyboard Shortcuts**. Every action is listed under its group, with one chip for
   each input bound to it.
2. Click **Add shortcut…**, then press the key (with `Ctrl`, `Shift` or `Alt` held for a combination),
   click the **middle** or a **side** mouse button, or turn the **wheel** (also with modifiers). `Esc`
   cancels. Left and right clicks are ignored — a click is how the box got focus.
3. Click the **✕** on a chip to remove it, or **Reset** on a row to put that action back to its
   defaults. An action with no chips is simply unbound.
4. Conflicts are flagged inline — both rows turn red and a banner names the first pair. Two actions
   conflict only when one input would reach both at once: a key may be page-turn, pan *and* scroll
   (only one is ever active), but an always-available action shares nothing with them.
5. **Import Layout…** / **Export Layout…** save and load your whole layout as a file — handy for
   moving between machines or sharing. Layouts exported by earlier versions still import.
   **Reset to Defaults** reverts everything.

Your changes are stored in `keymap.json` next to the PaperBunkr database (only what you changed, so
new defaults in later versions still reach you). Controller bindings can be viewed and removed in the
editor today; adding a new controller binding there is not supported yet.
