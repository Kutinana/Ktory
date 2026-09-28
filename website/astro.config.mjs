import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import tailwindcss from '@tailwindcss/vite';
import fs from 'node:fs';
import { themes as ktoryThemes } from '../src/Ktory.Highlighting/theme.mjs';

const ktoryGrammar = JSON.parse(
  fs.readFileSync(new URL('../src/Ktory.VSCode/syntaxes/ktory.tmLanguage.json', import.meta.url), 'utf-8')
);

export default defineConfig({
  site: 'https://ktory.ink',
  vite: {
    plugins: [tailwindcss()],
  },
  integrations: [
    starlight({
      title: 'Ktory',
      description: 'A dialog-driven, discrete step-by-step game narrative script system.',
      defaultLocale: 'root',
      locales: {
        root: {
          label: '简体中文',
          lang: 'zh-CN',
        },
        en: {
          label: 'English',
          lang: 'en',
        },
        ja: {
          label: '日本語',
          lang: 'ja',
        },
      },
      logo: {
        src: './src/assets/logo.webp',
      },
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/Kutinana/Ktory' },
      ],
      customCss: [
        './src/styles/custom.css',
      ],
      head: [
        {
          tag: 'script',
          content: `(() => {
  const path = window.location.pathname;
  if (path === '/' || path === '/index.html' || path.startsWith('/en/') || path.startsWith('/ja/') || path === '/en' || path === '/ja') return;

  const supported = ['zh', 'en', 'ja'];
  let preferred = null;
  try {
    const saved = localStorage.getItem('ktory-locale');
    if (saved && supported.includes(saved)) {
      preferred = saved;
    }
  } catch (e) {}

  if (!preferred) {
    const langs = navigator.languages || [navigator.language || ''];
    for (const lang of langs) {
      if (!lang) continue;
      const l = lang.toLowerCase().trim();
      if (l.startsWith('zh')) { preferred = 'zh'; break; }
      if (l.startsWith('ja')) { preferred = 'ja'; break; }
      if (l.startsWith('en')) { preferred = 'en'; break; }
    }
    if (!preferred) preferred = 'en';
  }

  if (preferred === 'en') {
    window.location.replace('/en' + path + window.location.search + window.location.hash);
  } else if (preferred === 'ja') {
    window.location.replace('/ja' + path + window.location.search + window.location.hash);
  }
})();`,
        },
      ],
      components: {
        LanguageSelect: './src/components/starlight/LanguageSelect.astro',
        ThemeSelect: './src/components/starlight/ThemeSelect.astro',
        TableOfContents: './src/components/starlight/TableOfContents.astro',
        PageTitle: './src/components/starlight/PageTitle.astro',
      },
      expressiveCode: {
        themes: ktoryThemes,
        shiki: {
          langs: [ktoryGrammar],
        },
      },
      sidebar: [
        {
          label: '概览与快速上手',
          translations: {
            en: 'Overview & Quickstart',
            ja: '概要とクイックスタート',
          },
          items: [{ autogenerate: { directory: '01-overview' } }],
        },
        {
          label: '剧本语法规范',
          translations: {
            en: 'Script Syntax',
            ja: 'スクリプト構文仕様',
          },
          items: [{ autogenerate: { directory: '02-syntax' } }],
        },
        {
          label: '引擎集成指南',
          translations: {
            en: 'Engine Integration',
            ja: 'エンジン統合ガイド',
          },
          items: [{ autogenerate: { directory: '03-integration' } }],
        },
        {
          label: '设计哲学与架构',
          translations: {
            en: 'Architecture & Design',
            ja: '設計哲学とアーキテクチャ',
          },
          items: [{ autogenerate: { directory: '04-architecture' } }],
        },
      ],
    }),
  ],
});
