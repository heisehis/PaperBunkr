# ComicVine API Usage Notice

This notice covers best practices and compliance requirements for using **your own ComicVine API
key** with Paperbunkr's ComicVine metadata integration. It's aimed at users and self-hosters, not
at end consumers of a hosted service — Paperbunkr doesn't operate one.

## 1. Bring your own key

Paperbunkr does not ship with, embed, or bundle a ComicVine API key. You must register at
[comicvine.gamespot.com/api](https://comicvine.gamespot.com/api/) and generate your own personal
key, then enter it in Paperbunkr's preferences.

Do not share a single API key across multiple independent Paperbunkr installations or users.
ComicVine's terms treat a key as tied to one account, and sharing it risks the key being rate-
limited or revoked for everyone using it.

## 2. Rate limits are real — don't try to bypass them

ComicVine enforces request throttling on its API (commonly cited limits are on the order of
1 request/second and a few hundred requests/hour; check ComicVine's current published limits, as
they can change). Paperbunkr applies its own request throttling when talking to ComicVine to stay
within these bounds.

If you're bulk-importing or re-scraping a large library:

- Let Paperbunkr's built-in throttling run rather than working around it.
- Expect large imports to take real wall-clock time — that's the API being respectful of
  ComicVine's infrastructure, not a bug.
- Repeated aggressive requests risk a temporary or permanent ban of your key or IP by ComicVine,
  which Paperbunkr cannot undo for you.

## 3. Keep your key secure

Your ComicVine API key is a credential, not a public setting:

- Paperbunkr stores it locally in your instance's configuration/database. It is never sent to the
  Paperbunkr project or to any service other than ComicVine's own API endpoint.
- If you run Paperbunkr in a container or CI environment, pass the key via an environment variable
  or secret manager rather than baking it into a config file.
- Never commit a config file, `.env`, or database dump containing your raw API key to a public
  Git repository. If you accidentally do, treat the key as compromised and regenerate it from your
  ComicVine account.

## 4. Attribution and data ownership

Metadata, cover images, character and creator information, and other data retrieved via the
ComicVine API remain the property of ComicVine / its operator. Paperbunkr is a local client that
organizes this metadata for your own reading library — fetching it through the API does not grant
you, or Paperbunkr, any redistribution or commercial rights over it. Review
[ComicVine's API terms of use](https://comicvine.gamespot.com/api/) for the current, authoritative
rules.
