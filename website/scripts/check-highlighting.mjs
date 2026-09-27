import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { roles } from '../../src/Ktory.Highlighting/theme.mjs';

const dist = fileURLToPath(new URL('../dist/', import.meta.url));
let pages = 0;
async function checkAssets(directory) {
  for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) await checkAssets(file);
    else if (entry.name.endsWith('.html')) {
      const html = await fs.readFile(file, 'utf8');
      for (const [, asset] of html.matchAll(/(?:href|src)="(\/_astro\/ec\.[^"]+)"/g)) {
        await assert.doesNotReject(fs.access(path.join(dist, asset.slice(1))), `Missing code-block asset ${asset} in ${file}`);
      }
      pages++;
    }
  }
}
await checkAssets(dist);

// A cached page can still reference existing assets while retaining an old grammar
// or theme. Verify real rendered tokens against the shared dark palette as well.
for (const locale of ['', 'en/', 'ja/']) {
  const file = path.join(dist, locale, '02-syntax/05-speaker-defaults/index.html');
  const html = await fs.readFile(file, 'utf8');
  const tokens = [...html.matchAll(/<span style="([^"]+)">([^<]+)<\/span>/g)];
  for (const [text, roleName] of [['@speaker', 'declaration'], ['alice', 'speaker'], ['zh', 'locale'], ['.portrait', 'decorator']]) {
    const color = roles.find(role => role.name === roleName).dark.toUpperCase();
    assert.ok(tokens.some(([, style, value]) => value === text && style.toUpperCase().includes(color)), `Stale or missing ${roleName} highlighting in ${file}`);
  }
}
console.log(`Highlighting verified: code assets in ${pages} pages and current token colors in all three documentation languages.`);
