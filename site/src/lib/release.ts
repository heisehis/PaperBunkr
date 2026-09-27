// Latest-release data, read from the GitHub API once at build time. This is the only network
// code in the site: the page itself never calls GitHub, and the site workflow is re-run by
// release.yml after every release so the numbers here can't drift the way the old hand-typed
// "0.3.0-beta" did.

const REPO = 'heisehis/PaperBunkr';
export const RELEASES_URL = `https://github.com/${REPO}/releases`;

export interface Release {
  /** Version without the leading "v", e.g. "0.7.3-beta"; "latest" in the local fallback. */
  version: string;
  tag: string | null;
  publishedAt: Date | null;
  htmlUrl: string;
  installer: { url: string; sizeBytes: number } | null;
  /** Bolded lead-ins from the release notes' "### Added" list, e.g. "Comic reader: page intelligence". */
  highlights: string[];
}

interface GitHubAsset {
  name: string;
  size: number;
  browser_download_url: string;
}

interface GitHubRelease {
  tag_name: string;
  published_at: string;
  html_url: string;
  body: string | null;
  assets: GitHubAsset[];
}

const FALLBACK: Release = {
  version: 'latest',
  tag: null,
  publishedAt: null,
  htmlUrl: `${RELEASES_URL}/latest`,
  installer: null,
  highlights: [],
};

export function extractHighlights(body: string, max = 4): string[] {
  const added = body.split(/\r?\n### /).find((section) => section.startsWith('Added'));
  if (!added) return [];
  const highlights: string[] = [];
  // Releases often lead with several "Comic reader: …" entries; take one per prefix so the
  // card shows the breadth of the release rather than four items about one screen.
  const seenPrefixes = new Set<string>();
  for (const match of added.matchAll(/^- \*\*(.+?)\*\*/gm)) {
    const text = match[1].replace(/[.:]\s*$/, '');
    const prefix = text.includes(':') ? text.slice(0, text.indexOf(':')).toLowerCase() : null;
    if (prefix !== null && seenPrefixes.has(prefix)) continue;
    if (prefix !== null) seenPrefixes.add(prefix);
    highlights.push(text);
    if (highlights.length === max) break;
  }
  return highlights;
}

let cached: Promise<Release> | undefined;

/** Memoised so every component on the page shares one request per build. */
export function getLatestRelease(): Promise<Release> {
  cached ??= fetchLatestRelease();
  return cached;
}

async function fetchLatestRelease(): Promise<Release> {
  const headers: Record<string, string> = {
    Accept: 'application/vnd.github+json',
    'User-Agent': 'paperbunkr-site-build',
  };
  if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;

  try {
    const response = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, { headers });
    if (!response.ok) throw new Error(`GitHub API answered ${response.status} ${response.statusText}`);
    const data = (await response.json()) as GitHubRelease;

    const exe = data.assets.find((a) => /^PaperbunkrSetup-.*\.exe$/i.test(a.name));
    return {
      version: data.tag_name.replace(/^v/, ''),
      tag: data.tag_name,
      publishedAt: new Date(data.published_at),
      htmlUrl: data.html_url,
      installer: exe ? { url: exe.browser_download_url, sizeBytes: exe.size } : null,
      highlights: extractHighlights(data.body ?? ''),
    };
  } catch (error) {
    // In CI a stale or empty page is worse than a red build: the last good deploy stays live.
    if (process.env.CI) throw new Error(`Could not read the latest release: ${String(error)}`);
    console.warn(`[release] falling back to /releases/latest: ${String(error)}`);
    return FALLBACK;
  }
}

export function formatSize(bytes: number): string {
  return `${Math.round(bytes / (1024 * 1024))} MB`;
}

export function formatDate(date: Date): string {
  return date.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' });
}
