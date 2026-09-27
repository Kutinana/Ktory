const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { nextPatch, packagePatch } = require('../scripts/package-patch.cjs');

test('patch increments within the chosen release line and avoids existing local packages', () => {
  assert.equal(nextPatch('0.1.9', []), '0.1.10');
  assert.equal(nextPatch('0.1.0', ['ktory-vscode-0.1.3.vsix', 'ktory-vscode-0.2.8.vsix']), '0.1.4');
  assert.throws(() => nextPatch('0.1.0-beta', []));
});
test('failed packaging preserves the version; successful builds advance it once', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ktory-version-'));
  const manifestPath = path.join(root, 'package.json');
  const packagesRoot = path.join(root, 'packages');
  fs.writeFileSync(manifestPath, '{"name":"ktory","version":"0.1.0"}\n');
  try {
    await assert.rejects(packagePatch({ manifestPath, packagesRoot, packageExtension: async () => { throw new Error('compile failed'); } }));
    assert.equal(JSON.parse(fs.readFileSync(manifestPath)).version, '0.1.0');
    const build = async ({ version }) => { const file = path.join(packagesRoot, `ktory-vscode-${version}.vsix`); fs.writeFileSync(file, 'test package'); return file; };
    assert.equal((await packagePatch({ manifestPath, packagesRoot, packageExtension: build })).version, '0.1.1');
    assert.equal((await packagePatch({ manifestPath, packagesRoot, packageExtension: build })).version, '0.1.2');
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});
test('concurrent patch builds cannot claim the same version', async () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ktory-version-'));
  const manifestPath = path.join(root, 'package.json');
  const packagesRoot = path.join(root, 'packages');
  fs.writeFileSync(manifestPath, '{"version":"0.1.0"}');
  let finish;
  const pending = new Promise(resolve => { finish = resolve; });
  const build = packagePatch({ manifestPath, packagesRoot, packageExtension: async ({ version }) => {
    await pending; const file = path.join(packagesRoot, `ktory-vscode-${version}.vsix`); fs.writeFileSync(file, 'test'); return file;
  } });
  try {
    await assert.rejects(packagePatch({ manifestPath, packagesRoot, packageExtension: async () => '' }), /Another patch build/);
  } finally { finish(); await build; fs.rmSync(root, { recursive: true, force: true }); }
});
