// @ts-check
import { defineConfig } from 'astro/config';
import svelte from '@astrojs/svelte';
import mdx from '@astrojs/mdx';
import sitemap from '@astrojs/sitemap';
import tailwindcss from '@tailwindcss/vite';

// IMPORTANT: set `site` to the real production origin before deploy.
// It drives canonical URLs, the sitemap, and the RSS feed.
export default defineConfig({
  site: 'https://radiopadstudio.com',
  integrations: [svelte(), mdx(), sitemap()],
  // Brand-consistent code blocks: use our own .prose pre styling, not Shiki's theme.
  markdown: {
    syntaxHighlight: false,
  },
  vite: {
    plugins: [tailwindcss()],
  },
});
