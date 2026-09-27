# ComicVine & Metron Notice

**Last updated:** 2026-09-26

Paperbunkr can fetch comic metadata and covers from [ComicVine](https://comicvine.gamespot.com/api/)
and [Metron](https://metron.cloud). Both are free services run by other people. This notice explains
what they expect from you and what Paperbunkr does to respect them.

## 1. Use your own key or login

Paperbunkr does not ship with a ComicVine key or a Metron account. Create your own and enter it under
Preferences → **Connections**:

- **ComicVine:** register at [comicvine.gamespot.com/api](https://comicvine.gamespot.com/api/) to get
  a personal API key.
- **Metron:** sign up at [metron.cloud](https://metron.cloud) and use that username and password.

Keep a key or login to yourself. Sharing one across several people or installations makes them hit
the limits together and can get the key or account blocked for everyone using it.

## 2. Request limits

Both services limit how many requests one user may make. Paperbunkr paces every request it sends,
from searches you start and from background tasks alike, and always keeps part of the allowance for
what you are doing in the app:

- **ComicVine:** at most one request every 1.1 seconds and 200 an hour. Background work (scheduled
  scrapes, Acquisition, followed arcs) may only use 150 of those.
- **Metron:** at most 18 requests a minute, 14 of them for background work. Metron also has a daily
  allowance, which Paperbunkr reads from Metron's own replies; background work pauses when little of
  it is left. Metron documents its limits as 20 requests a minute and 5,000 a day.

If a service says too many requests were made, Paperbunkr pauses all requests to it for 1 minute,
then 5, then 15, then an hour if it keeps happening, and starts again normally after the next
success. Large imports and scrapes therefore take real time. That is expected, not a fault, and
working around it risks getting your key or account blocked, which Paperbunkr cannot undo.

## 3. How your key and login are kept

- They are stored in Paperbunkr's database, encrypted with Windows (DPAPI) so that only your Windows
  account on this computer can read them. A database copied to another account or computer will ask
  for them again.
- They are only ever sent to the service they belong to, over an encrypted connection. ComicVine's
  API takes the key as part of each request's address, as ComicVine requires; Metron uses a standard
  sign-in header.
- If you think a key has leaked, create a new one on the service's site and replace it in
  Preferences → Connections.

## 4. Whose data it is

Metadata, cover images, creator and character details and everything else fetched from ComicVine or
Metron belong to those services and the people who contributed them. Paperbunkr only organises them
for your own library. Fetching them does not give you, or Paperbunkr, the right to republish or sell
them. Each service's own terms, published on its site, are the ones that count.
