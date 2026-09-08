# Privacy Notice

> **Template, not legal advice.** This is a starting point for Paperbunkr's repository
> documentation, written by a contributor to describe how the software behaves. It is not a
> substitute for review by a lawyer, and it does not create any legal obligation on its own. Adapt
> it to your actual deployment before relying on it.

**Last updated:** 2026-09-07

Paperbunkr is self-hosted software. There is no Paperbunkr-operated service, account system, or
central server — this notice describes what the application itself does with data, not what any
particular installation's operator does with it (that's a matter between the operator and their
users, if they have any).

## 1. Data stays on your infrastructure

Paperbunkr stores your library metadata, reading history, preferences, and cached cover art in a
local SQLite database and local file paths that you configure. Comic, manga, and book files you
import are read from and written to folders you control. None of this is transmitted to the
Paperbunkr project or its maintainers — we don't operate any servers that Paperbunkr phones home
to, and the application has no telemetry or analytics reporting built in.

## 2. Third-party metadata and tracking services

Paperbunkr can optionally connect directly to third-party metadata and tracking providers to fetch
covers, issue/series metadata, and tracking data — for example ComicVine, AniList, MangaUpdates,
MyAnimeList, Kitsu, Metron, and MangaDex, depending on which integrations you enable. When you use
one of these:

- Requests (search terms, series/issue identifiers, and — where required — your API key) go
  **directly from your machine's IP address to that provider**, not through any Paperbunkr-operated
  intermediary.
- Each provider handles that traffic under its own privacy policy and terms of service, which
  Paperbunkr does not control and is not a party to. Review the relevant provider's policy before
  enabling an integration.
- API keys you supply for these services are stored locally in your Paperbunkr configuration/
  database and are sent only to the provider they're configured for.

## 3. Logs

Paperbunkr writes application logs to your local machine for diagnostics. These logs stay on your
host and are not collected or transmitted anywhere by the project.

## 4. Multi-user / self-hosted-for-others deployments

If you run Paperbunkr for other people (family, a community, etc.), you — the operator — are
responsible for your own privacy disclosures to those users, and for complying with any privacy
law that applies to you (e.g. GDPR, CCPA). This document describes the software's behavior; it is
not a substitute for that operator-to-user disclosure.

## 5. Changes to this notice

This is a living document maintained alongside the software. Material changes to what Paperbunkr
connects to or stores will be reflected here and noted in the changelog.
