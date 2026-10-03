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
| Focus the search box | `Ctrl+F` (or `/` in the Library); Home, Books, Library and Preferences have one |
| Refresh the screen | `F5` (Library, Books, Home, Insights, Wanted, Smart Lists, Reading Lists, Continuity) |
| New list / collection | `Ctrl+N` (Smart Lists, Reading Lists) |
| Save | `Ctrl+S` (the editors and Smart Lists, even from inside a text field) |
| Previous / next tab | `Ctrl+PageUp` / `Ctrl+PageDown`, or the controller's bumpers. On a screen with tabs (detail screens, Insights, Wanted, Continuity, the editors, Preferences' sections) they switch tab; Home steps the spotlight; elsewhere they switch screen |
| Pin / unpin the sidebar | `Shift+F6` |
| Quit | `Ctrl+Q` |
| Move around the left-hand rail | `↑` `↓`, `Home`, `End`; `→` goes into the screen; `Enter` / `Space` opens the screen. From inside a screen, `←` at its left edge returns to the rail |
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

## Books grid

| Action | Default |
|---|---|
| Select all | `Ctrl+A` |
| Edit properties of the selection | `Ctrl+I` |
| Remove the selection | `Delete` |

## Detail screens

| Action | Default |
|---|---|
| Continue reading | `Ctrl+Enter` |
| Edit properties of the selected issues | `Ctrl+I` |
| Back to the previous screen | `Backspace`, or `Esc` |

## Smart Lists

| Action | Default |
|---|---|
| New smart list | `Ctrl+N` |
| Duplicate the open list | `Ctrl+D` |
| Save the open list | `Ctrl+S` |

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

The controller works in the **whole app**, not just the reader, while **Preferences → Reader → Use a game controller** is on and the PaperBunkr window is the active one.

Everywhere:

| Action | Default |
|---|---|
| Move | D-pad or left stick |
| Open / press the focused item | `A` |
| Back / close | `B` |
| Context menu | `Y` |
| Previous / next tab (or screen) | left / right bumper |
| Scroll | right stick |
| Quick open | `Start` |

In the reader these are replaced by the reading controls:

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

Every controller button can be changed in **Preferences → Keyboard Shortcuts**: click **Add shortcut…** on a row and press the controller button (the controller must be switched on in Preferences → Reader). The "All screens" group lists the shared ones, including the arrows, Enter, Space, F2, Delete, Home, End, Page Up and Page Down that act on whatever has focus; moving one of those to another key makes that key do it everywhere. Rename, remove and toggle-the-focused-item have no controller button by default, so you can give them one if you want them.

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

## Plugin commands

Every enabled **Library** plugin command gets a shortcut that runs it on the selected issues: `Ctrl+Shift+F1` to `F12` for the first twelve, or the key the plugin asks for in its manifest (`shortcut="Ctrl+Alt+K"`). They appear under **Plugins** in Preferences → Keyboard Shortcuts, where you can change or remove them, and next to the command in the right-click **Plugins** menu. Your choice is kept if you switch the plugin off and on again.

Dropdown boxes open when you click them, or press `Enter`, `Space` or `Alt+↓` while they have focus; the arrow keys alone just move on to the next control.

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
