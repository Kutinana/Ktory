const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const tm = require('vscode-textmate');
const onig = require('vscode-oniguruma');
const root = path.resolve(__dirname, '..');

test('the contributed TextMate grammar highlights a representative .ktr document', async () => {
  const wasm = fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm'));
  await onig.loadWASM(wasm.buffer.slice(wasm.byteOffset, wasm.byteOffset + wasm.byteLength));
  const registry = new tm.Registry({
    onigLib: Promise.resolve({ createOnigScanner: patterns => new onig.OnigScanner(patterns), createOnigString: value => new onig.OnigString(value) }),
    loadGrammar: async scope => scope === 'source.ktory' ? JSON.parse(fs.readFileSync(path.join(root, 'syntaxes/ktory.tmLanguage.json'), 'utf8')) : null
  });
  const grammar = await registry.loadGrammar('source.ktory');
  for (const [line, expected] of [
    ['// author note', 'comment.line.double-slash.ktory'],
    ['=== Start ===', 'keyword.control.section.ktory'],
    ['  #choice.loop', 'keyword.other.container.ktory'],
    ['  * [继续] => Next', 'string.quoted.choice.ktory'],
    ['    .sfx("bell")', 'entity.name.tag.decorator.ktory'],
    ['    @en: Hello.', 'constant.language.locale.ktory'],
    ['Alice: Hello.', 'entity.name.function.speaker.ktory'],
    ['-> end', 'keyword.control.flow.ktory']
  ]) {
    assert.ok(grammar.tokenizeLine(line, tm.INITIAL).tokens.some(token => token.scopes.includes(expected)), `${line}: ${expected}`);
  }
  registry.dispose();
});

test('the portal consumes the extension grammar instead of maintaining another copy', () => {
  const config = fs.readFileSync(path.resolve(root, '../../website/astro.config.mjs'), 'utf8');
  assert.ok(config.includes('../src/Ktory.VSCode/syntaxes/ktory.tmLanguage.json'));
  assert.equal(fs.existsSync(path.resolve(root, '../../website/src/grammars/ktory.tmLanguage.json')), false);
});

test('the offline bundle contains Core and the real WASM host', () => {
  const framework = fs.readdirSync(path.join(root, 'reader/_framework'));
  assert.ok(framework.some(name => /^Ktory.Core.*\.wasm$/.test(name)));
  assert.ok(framework.some(name => /^Ktory.Web.*\.wasm$/.test(name)));
  assert.ok(framework.includes('blazor.webassembly.js'));
  assert.ok(fs.existsSync(path.join(root, 'reader/notices/microsoft.netcore.app.runtime.mono.browser-wasm')));
  assert.equal(fs.readFileSync(path.join(root, 'reader/app.js'), 'utf8'),
    fs.readFileSync(path.resolve(root, '../Ktory.Runner/wwwroot/app.js'), 'utf8'));
  assert.match(JSON.parse(fs.readFileSync(path.join(root, 'reader/build-info.json'), 'utf8')).sourceCommit, /^[a-f0-9]{40}$/);
});
