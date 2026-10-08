// Page copy that isn't tied to one component's markup. Feature claims come from CHANGELOG.md
// (released versions only); keep them in step with it rather than with unreleased branches.
import type { ImageMetadata } from 'astro';
import booksShot from '../assets/shots/books.png';
import libraryShot from '../assets/shots/library.png';
import insightsTodayShot from '../assets/shots/insights-today.png';
import insightsTrendsShot from '../assets/shots/insights-trends.png';
import automationShot from '../assets/shots/automation.png';
import detailShot from '../assets/shots/detail.png';
import detailIssuesShot from '../assets/shots/detail-issues.png';
import readingListsShot from '../assets/shots/reading-lists.png';
import readerShot from '../assets/shots/reader-comic.png';
import readingListsGalleryShot from '../assets/shots/reading-lists-gallery.png';
import continuityMapShot from '../assets/shots/continuity-map.png';
import wantedShot from '../assets/shots/wanted-releases.png';
import insightsHistoryShot from '../assets/shots/insights-history.png';

export const REPO_URL = 'https://github.com/heisehis/PaperBunkr';
export const WIKI_URL = `${REPO_URL}/wiki`;

export interface Theme {
  id: string;
  number: string;
  label: string;
  title: string;
  intro: string;
  points: string[];
  image: ImageMetadata;
  alt: string;
}

export const themes: Theme[] = [
  {
    id: 'read',
    number: '01',
    label: 'Read',
    title: 'A reader built for long sessions',
    intro: 'Real page rendering for comics and manga, and a separate reflow reader for novels.',
    points: [
      'Guided view steps through a page panel by panel',
      'One smooth zoom range from 25% to 400%, with auto levels, sharpening and auto-crop',
      'Double-page spreads, continuous and webtoon scroll, right-to-left manga',
      'Reader profiles, a warm night tint, eye-rest reminders and Xbox controller support',
      'The next issue opens in the background, so moving on is instant',
      'An Info panel with credits, your place in the reading list and Previous / Next',
      'Books: EPUB, PDF, FB2 and MOBI/AZW3 with highlights and notes',
    ],
    image: readerShot,
    alt: 'The comic reader with the Info panel on the left and the Reader Tools drawer on the right',
  },
  {
    id: 'organise',
    number: '02',
    label: 'Organise',
    title: 'A library that stays tidy',
    intro: 'Series are real records, not a text field, so everything built on them stays consistent.',
    points: [
      'Series and issue views, Collections and saved Workspaces',
      'Smart Lists with nested AND/OR rules and a template gallery, and Reading Lists that import CBL and CSV',
      'Every list remembers its own layout, and layouts can be saved by name',
      'Virtual Tags that apply themselves by rule',
      'Single and bulk metadata editors, with optional ComicInfo.xml write-back',
      'Library Organizer to rename and file comics by template',
      'Library Health finds missing files, duplicates and anything that needs a second look',
    ],
    image: libraryShot,
    alt: 'The Library grid with the Collections sidebar and a preview panel for the selected series',
  },
  {
    id: 'discover',
    number: '03',
    label: 'Discover',
    title: 'Know what to read next',
    intro: 'Your reading history and a relationship-aware engine decide what comes up, not an ad feed.',
    points: [
      'Home picks up where you left off, shows what to read next and suggests "because you read…" series',
      'Insights: reading goals, trends against the last period, and a year-in-review recap',
      'Story Events and Continuities that tie issues together across series, suggested for you',
      'Metadata and covers from ComicVine and Metron, matched by cover before they are applied',
      'Manga tracking with AniList, MangaBaka, MangaUpdates, MangaDex and Kitsu',
      'A Wanted list and a weekly pull list of new releases for the series you follow',
      'A Metron account sync keeps your pull list, collection and wish list in step with the library',
      'Series types (manga, manhwa, comic) are suggested from your trackers, with review and Undo',
    ],
    image: insightsTodayShot,
    alt: 'Insights, Today tab: a reading goal, what to continue, a "because you finished" pick and unstarted runs',
  },
  {
    id: 'automate',
    number: '04',
    label: 'Automate',
    title: 'Maintenance that runs itself',
    intro: 'The chores happen in the background, on a schedule you set, and report back quietly.',
    points: [
      'Scheduled rescans, database backups, cover checks and ad-page detection',
      'An Activity Center in the status bar for every job and alert',
      'Plugins with their own package manager, a plugin API and Python commands',
    ],
    image: automationShot,
    alt: 'Preferences, Automation: scheduled tasks with intervals and Run now buttons',
  },
];

export const facts = [
  { title: 'Free and open source', detail: 'AGPL-3.0, no paid tier' },
  { title: 'Local-first', detail: 'No cloud, no account, no telemetry' },
  { title: 'Reads it all', detail: 'CBZ · CBR · PDF · EPUB · FB2 · MOBI/AZW3' },
  { title: 'ComicRack friendly', detail: 'Imports a CE library in one pass' },
];

export const comicRackPoints = [
  {
    title: 'Non-destructive import',
    detail: 'Series, issues, read state and metadata come across from ComicDb.xml. Your CE install is never touched, and the import is safe to re-run.',
  },
  {
    title: 'Duplicates caught early',
    detail: 'Near-identical series names are flagged before anything is written, so you can merge them or keep them apart.',
  },
  {
    title: 'Guesses you can review',
    detail: 'Where CE had no answer (manga vs. manhwa, for one), the guess lands in Library Health to confirm later.',
  },
  {
    title: 'Familiar where it counts',
    detail: "CE's publisher icon pack, reader textures, auto levels and the Library Organizer's templates are all here.",
  },
];

export interface GalleryShot {
  image: ImageMetadata;
  alt: string;
  caption: string;
}

export const gallery: GalleryShot[] = [
  {
    image: detailShot,
    alt: 'A series detail screen for Absolute Batman with credits, teams, locations and characters',
    caption: 'Series detail, with credits and linked characters, teams and locations',
  },
  {
    image: detailIssuesShot,
    alt: 'The issues grid on a series detail screen, 24 covers with reading-order badges',
    caption: 'Every issue of a series at a glance',
  },
  {
    image: readingListsGalleryShot,
    alt: 'The Reading Lists gallery: a Continue Reading row and a grid of list and folder covers with progress bars',
    caption: 'Reading Lists gallery, with folders and a Continue row',
  },
  {
    image: readingListsShot,
    alt: 'A reading list for Avengers: Armageddon as a journey path with an Up next card',
    caption: 'A reading list as a path, with your pace and an estimated finish',
  },
  {
    image: continuityMapShot,
    alt: 'A continuity map: each series a lane, every issue a card in publication order',
    caption: 'Continuity map: every issue of every event in publication order',
  },
  {
    image: wantedShot,
    alt: 'Wanted, Releases tab: a week of new releases as covers with Request buttons',
    caption: "Wanted: this week's new releases from Metron",
  },
  {
    image: insightsTrendsShot,
    alt: 'Insights, Trends tab: highlights, reading streaks, pace and an activity heatmap',
    caption: 'Insights trends: streaks, pace and an activity heatmap',
  },
  {
    image: insightsHistoryShot,
    alt: 'Insights, History tab: what you read, newest first, with resume buttons',
    caption: 'History: what you read, newest first',
  },
];
