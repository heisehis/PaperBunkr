# Privacy Notice

**Last updated:** 2026-09-27

Paperbunkr is a desktop app that runs on your own computer. There is no Paperbunkr account, no
Paperbunkr server, and no telemetry: the app never reports usage, analytics or crashes to the
project. This notice explains what stays on your computer, what can leave it, and when.

## What leaves your computer

### On by default

- **Update check.** When Paperbunkr starts, it asks GitHub whether a newer version exists. GitHub
  sees your IP address and an ordinary web request; the app sends nothing about you or your
  library. It is skipped on the very first launch and on the first launch after an update, and
  nothing is downloaded without asking. Turn it off under Preferences → About → **Check for updates
  on startup**.
- **Shared-universe suggestions.** Once a day, a background task looks up up to 20 of your series on
  Wikidata to suggest shared-universe continuities. The searches contain series names and the names
  of each series' most frequent characters. No account or key is involved. Turn it off under
  Preferences → Automation → **Find shared-universe suggestions**.
- **Story event checks.** Once a week, **Check story events** looks up your story events' ComicVine and
  Metron arc IDs (with your own key or login) and asks Wikidata which events follow which. The
  lookups contain story event names and those IDs. Turn it off under Preferences → Automation →
  **Check story events**.
- **Grand Comics Database matching.** Once you have downloaded the GCD data, a weekly task matches
  your series against it on your computer. For series scraped from Metron it asks Metron (with your
  login) for their GCD ID. Turn it off under Preferences → Automation → **Match series to GCD**.

### Only when you use them

- **Metadata lookups.** When you scrape, search or link a series, the search text (series and issue
  names, cleaned up) and the provider's own IDs go straight from your computer to the provider you
  picked: ComicVine or Metron (with your own API key or login), or AniList, MangaDex and MangaBaka
  (no login needed).
- **Reading orders.** Building a reading list from a story arc can fetch pages from the reading-order
  sites the app names while you choose (ComicArc, Comic Book Reading Orders, ReadingOrders.com and
  Read Things Right), besides ComicVine and Metron.
- **Trackers.** Trackers you connect (AniList, MyAnimeList, Kitsu, MangaUpdates, MangaDex,
  MangaBaka, Bangumi, Shikimori) receive your reading progress, scores and dates as the tracking
  settings under Preferences → Connections say. They are only contacted once you connect them.
- **Prowlarr and qBittorrent.** If you turn on Acquisition, searches built from series and issue
  names go to the Prowlarr and qBittorrent addresses you enter. These are usually on your own
  network; an address that starts with `http://` is not encrypted.
- **Library sharing.** If you turn it on, other computers can browse the parts of your library you
  choose to share (see below).
- **Plugins.** A plugin you install runs inside Paperbunkr with the same access the app has,
  including the network. Only install plugins you trust; the project does not review them.
- **Grand Comics Database data.** Downloading it, or checking for newer data, under Preferences →
  Connections fetches a small description file and the data itself from GitHub. Nothing about your
  library is sent.
- **Books.** The book reader shows EPUB and FB2 pages in Microsoft's WebView2. If a book's pages
  point to images or styles on the web, WebView2 may load them.

Every request goes directly from your computer to the service. Paperbunkr has no relay in between,
and each service handles what it receives under its own privacy policy.

## Where your data lives

- Your library database, cover cache, backups, plugins and logs are in `%AppData%\Paperbunkr`.
  The book reader's WebView2 profile is in `%LocalAppData%\Paperbunkr\WebView2`. Preferences →
  About → **Logs and data** opens these folders.
- Every time you open or finish something, Paperbunkr adds a line to a reading log in that database, with the
  title as it was at the time. Insights (stats, goals, Recap and History) is built from it. **Remove from
  history** and **Clear all history** only hide entries from the History list; the log itself, and your stats,
  are kept.
- API keys, passwords and tracker sign-ins are encrypted with Windows (DPAPI) so that only your
  Windows account on this computer can read them. A database copied to another account or computer
  will ask for them again. A library-sharing password is stored only as a salted hash.
- Logs and crash reports stay on your computer. A crash report can include file and folder paths
  from your library; it only leaves your computer if you save it and send it to someone yourself.
- Paperbunkr only writes to your comic files if you turn on **Write metadata to files** under
  Preferences → Advanced (off by default), or if you turn on Acquisition, which writes a
  `ComicInfo.xml` into comics it downloads for you.

## Library sharing

Sharing is off until you turn it on under Preferences → Sharing and set a password. While
Paperbunkr is open with sharing on:

- It listens on your network (port 7614 by default) over an encrypted connection, and by default
  only accepts computers on a private network.
- It announces itself on your local network under its display name, which is your computer's name
  by default.
- Anyone with the password can browse and read the parts of your library you chose to share (all
  of it, or selected lists, collections and smart lists), including covers, pages and metadata.

If you share with other people, what they can see is up to you, and so is telling them about it.

## Changes

This notice changes with the app. Changes to what Paperbunkr connects to are listed in the
changelog.
