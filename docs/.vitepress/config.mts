import { defineConfig } from 'vitepress'
import { fileURLToPath, URL } from 'node:url'
import { regionSnippetPlugin } from '@radarleaf/markdown-it-region-snippets'

// Repo root, so the snippet plugin can scan the samples.
const rootDir = fileURLToPath(new URL('../..', import.meta.url))

// Deployed to GitHub Pages at https://mysticmind.github.io/mysticmind-postgresembed/
export default defineConfig({
  title: 'MysticMind.PostgresEmbed',
  description: 'A real PostgreSQL server, any version, from .NET code and tests. No Docker, no install.',
  base: '/mysticmind-postgresembed/',
  lastUpdated: true,
  cleanUrls: true,
  markdown: {
    // Expand `snippet: sample_*` markers from `#region` blocks in samples/, a test project CI runs, so the docs
    // always show code that works.
    // https://www.npmjs.com/package/@radarleaf/markdown-it-region-snippets
    config(md) {
      md.use(regionSnippetPlugin, {
        rootDir,
        dirs: ['samples'],
        include: /^sample_/,
        syntax: 'snippet-colon',
        // an anchor and a "source" link under each snippet, pointing at the sample on GitHub
        urlPrefix: 'https://github.com/mysticmind/mysticmind-postgresembed/blob/master',
      })
    },
  },
  head: [
    ['link', { rel: 'icon', href: '/mysticmind-postgresembed/logo.png' }],
  ],
  themeConfig: {
    logo: '/logo.png',
    nav: [
      { text: 'Guide', link: '/guide/getting-started' },
      { text: 'Reference', link: '/reference/options' },
      { text: 'pgembed CLI', link: 'https://mysticmind.github.io/pgembed-cli/' },
      { text: 'NuGet', link: 'https://www.nuget.org/packages/MysticMind.PostgresEmbed/' },
    ],
    sidebar: [
      {
        text: 'Guide',
        items: [
          { text: 'Introduction', link: '/guide/introduction' },
          { text: 'Getting Started', link: '/guide/getting-started' },
          { text: 'Versions', link: '/guide/versions' },
          { text: 'Named Instances', link: '/guide/named-instances' },
          { text: 'Downloads and Cache', link: '/guide/cache' },
          { text: 'Extensions', link: '/guide/extensions' },
        ],
      },
      {
        text: 'Using it',
        items: [
          { text: 'In Tests and CI', link: '/guide/testing' },
          { text: 'How It Works', link: '/guide/how-it-works' },
          { text: 'Security', link: '/guide/security' },
          { text: 'Troubleshooting', link: '/guide/troubleshooting' },
          { text: 'Upgrading to v5', link: '/guide/upgrading-to-v5' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'Options', link: '/reference/options' },
          { text: 'PgServer', link: '/reference/pgserver' },
        ],
      },
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/mysticmind/mysticmind-postgresembed' },
    ],
    search: { provider: 'local' },
    editLink: {
      pattern: 'https://github.com/mysticmind/mysticmind-postgresembed/edit/master/docs/:path',
      text: 'Edit this page on GitHub',
    },
    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2023-2026 Babu Annamalai',
    },
  },
})
