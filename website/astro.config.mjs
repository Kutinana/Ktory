import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import tailwindcss from '@tailwindcss/vite';
import fs from 'node:fs';

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
      components: {
        LanguageSelect: './src/components/starlight/LanguageSelect.astro',
        ThemeSelect: './src/components/starlight/ThemeSelect.astro',
      },
      expressiveCode: {
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
