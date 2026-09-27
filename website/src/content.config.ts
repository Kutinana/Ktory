import { defineCollection } from 'astro:content';
import { docsLoader, i18nLoader } from '@astrojs/starlight/loaders';
import { docsSchema, i18nSchema } from '@astrojs/starlight/schema';
import ktoryGrammar from '../../src/Ktory.VSCode/syntaxes/ktory.tmLanguage.json';
import { themes } from '../../src/Ktory.Highlighting/theme.mjs';

const baseDocsLoader = docsLoader();
// Astro caches rendered Markdown by content. Starlight's integration settings are
// excluded from that cache key, so grammar/theme changes must invalidate it here.
const highlightingConfig = JSON.stringify({ ktoryGrammar, themes });
const highlightedDocsLoader = {
  ...baseDocsLoader,
  load: (context: Parameters<typeof baseDocsLoader.load>[0]) => baseDocsLoader.load({
    ...context,
    generateDigest: (content: unknown) => context.generateDigest({ content, highlightingConfig }),
  }),
};

export const collections = {
  docs: defineCollection({ loader: highlightedDocsLoader, schema: docsSchema() }),
  i18n: defineCollection({ loader: i18nLoader(), schema: i18nSchema() }),
};
