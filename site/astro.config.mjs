// @ts-check
import { defineConfig } from 'astro/config';
import sitemap from '@astrojs/sitemap';

// Served from GitHub Pages as a project site, so every URL lives under /PaperBunkr.
export default defineConfig({
  site: 'https://heisehis.github.io',
  base: '/PaperBunkr',
  trailingSlash: 'always',
  integrations: [sitemap()],
  devToolbar: { enabled: false },
});
