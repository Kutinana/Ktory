const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const tm = require('vscode-textmate');
const onig = require('vscode-oniguruma');
const root = path.resolve(__dirname, '..');
const { extensionRoot, readerRoot, buildRoot } = require('../scripts/paths.cjs');

test('the contributed TextMate grammar highlights a representative .ktr document', async () => {
  const wasm = fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm'));
  await onig.loadWASM(wasm.buffer.slice(wasm.byteOffset, wasm.byteOffset + wasm.byteLength));
  const registry = new tm.Registry({
    onigLib: Promise.resolve({ createOnigScanner: patterns => new onig.OnigScanner(patterns), createOnigString: value => new onig.OnigString(value) }),
    loadGrammar: async scope => scope === 'source.ktory' ? JSON.parse(fs.readFileSync(path.join(root, 'syntaxes/ktory.tmLanguage.json'), 'utf8')) : null
  });
  const grammar = await registry.loadGrammar('source.ktory');
  for (const [line, expected] of [
    ['@defaultLang: zh', 'variable.language.metadata.ktory'],
    ['// author note', 'comment.line.double-slash.ktory'],
    ['=== Start ===', 'keyword.control.section.ktory'],
    ['  #choice.loop', 'keyword.control.anchor.ktory'],
    ['#.sfx("wind.ogg").wait(1).next()', 'keyword.control.anchor.ktory'],
    ['  * [继续] => Next', 'string.quoted.choice.ktory'],
    ['    .sfx("bell")', 'entity.name.function.decorator.ktory'],
    ['    @en: Hello.', 'constant.other.locale.ktory'],
    ['@speaker alice: zh="爱丽丝" | en="Alice"', 'keyword.declaration.speaker.ktory'],
    ['  @speaker alice_2-id ： zh="爱丽丝"', 'entity.name.type.speaker.ktory'],
    ['@speaker: zh="爱丽丝"', 'keyword.declaration.speaker.ktory'],
    ['Alice: Hello.', 'entity.name.type.speaker.ktory'],
    ['-> end', 'keyword.control.flow.ktory']
  ]) {
    assert.ok(grammar.tokenizeLine(line, tm.INITIAL).tokens.some(token => token.scopes.includes(expected)), `${line}: ${expected}`);
  }
  const declaration = '@speaker alice: zh="爱丽丝"';
  const tokens = grammar.tokenizeLine(declaration, tm.INITIAL).tokens;
  const scopeAt = index => tokens.find(token => token.startIndex <= index && token.endIndex > index).scopes;
  assert.ok(scopeAt(declaration.indexOf('alice')).includes('entity.name.type.speaker.ktory'));
  assert.ok(!scopeAt(declaration.indexOf('zh=')).includes('entity.name.type.speaker.ktory'), 'declaration header ends at the colon');
  for (const line of ['  @en: Hello.', '@speakerName: Hello.']) {
    const scopes = grammar.tokenizeLine(line, tm.INITIAL).tokens.flatMap(token => token.scopes);
    assert.ok(scopes.includes('constant.other.locale.ktory'));
    assert.ok(!scopes.includes('keyword.declaration.speaker.ktory'));
    assert.ok(!scopes.includes('entity.name.type.speaker.ktory'));
  }
  for (const line of ['  * [@zh: "继续"]', '    [@en: "Continue"]', '    [@ja: 続ける]']) {
    const scopes = grammar.tokenizeLine(line, tm.INITIAL).tokens.flatMap(token => token.scopes);
    assert.ok(scopes.includes('string.quoted.choice.ktory'), `${line} must be scoped as choice`);
    assert.ok(scopes.includes('constant.other.locale.ktory'), `${line} must recognize locale tag`);
    assert.ok(!scopes.includes('entity.name.type.speaker.ktory'), `${line} must not be misidentified as speaker`);
  }
  assert.ok(scopeAt(declaration.indexOf('zh=')).includes('constant.other.locale.ktory'));
  const anonymous = '#.sfx("wind.ogg").wait(1).next()';
  const anonymousTokens = grammar.tokenizeLine(anonymous, tm.INITIAL).tokens;
  const anonymousScope = index => anonymousTokens.find(t => t.startIndex <= index && t.endIndex > index).scopes;
  assert.ok(anonymousScope(0).includes('keyword.control.anchor.ktory'));
  assert.ok(anonymousScope(anonymous.indexOf('.sfx')).includes('entity.name.function.decorator.ktory'));
  assert.ok(anonymousScope(anonymous.indexOf('.ogg')).includes('string.quoted.double.ktory'));
  assert.ok(!anonymousScope(anonymous.indexOf('.ogg')).includes('entity.name.function.decorator.ktory'));
  registry.dispose();
});

test('the portal consumes the extension grammar instead of maintaining another copy', () => {
  const config = fs.readFileSync(path.resolve(root, '../../website/astro.config.mjs'), 'utf8');
  assert.ok(config.includes('../src/Ktory.VSCode/syntaxes/ktory.tmLanguage.json'));
  assert.equal(fs.existsSync(path.resolve(root, '../../website/src/grammars/ktory.tmLanguage.json')), false);
  const homepage = fs.readFileSync(path.resolve(root, '../../website/src/lib/highlighter.ts'), 'utf8');
  assert.ok(homepage.includes('../../../src/Ktory.VSCode/syntaxes/ktory.tmLanguage.json'));
  assert.ok(homepage.includes('Ktory.Highlighting/renderer.mjs'));
  const readerGrammar = fs.readFileSync(path.join(readerRoot, 'highlighting/ktory.tmLanguage.json'), 'utf8');
  assert.equal(readerGrammar, fs.readFileSync(path.join(root, 'syntaxes/ktory.tmLanguage.json'), 'utf8'));
});

test('the offline bundle contains Core and the real WASM host', () => {
  const framework = fs.readdirSync(path.join(readerRoot, '_framework'));
  assert.ok(framework.some(name => /^Ktory.Core.*\.wasm$/.test(name)));
  assert.ok(framework.some(name => /^Ktory.Wasm.*\.wasm$/.test(name)));
  assert.ok(framework.includes('blazor.webassembly.js'));
  assert.ok(fs.existsSync(path.join(readerRoot, 'notices/microsoft.netcore.app.runtime.mono.browser-wasm')));
  assert.equal(fs.readFileSync(path.join(readerRoot, 'app.js'), 'utf8'),
    fs.readFileSync(path.resolve(root, '../Ktory.WebReader/wwwroot/app.js'), 'utf8'));
  assert.match(JSON.parse(fs.readFileSync(path.join(readerRoot, 'build-info.json'), 'utf8')).sourceCommit, /^[a-f0-9]{40}$/);
});

test('the offline extension reader excludes sample scripts and character portraits', () => {
  assert.equal(fs.existsSync(path.join(readerRoot, 'portraits')), false, 'portraits directory must not be bundled');
  function scan(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        scan(full);
      } else {
        assert.ok(!/\.(ktr|ktory)$/i.test(entry.name), `sample script should not be bundled: ${entry.name}`);
        assert.ok(!/kutori|william/i.test(entry.name), `portrait image should not be bundled: ${entry.name}`);
      }
    }
  }
  scan(readerRoot);

  const webviewCss = fs.readFileSync(path.join(root, 'webview.css'), 'utf8');
  assert.ok(webviewCss.includes('.portrait-stage') || webviewCss.includes('#portraitStage'),
    'webview.css must hide portrait stage');
});

test('the staged extension includes current runtime sources and no build tooling', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(extensionRoot, 'package.json'), 'utf8'));
  assert.equal(manifest.scripts, undefined);
  assert.equal(manifest.devDependencies, undefined);
  for (const file of [
    'extension.js', 'webview.js', 'linter.js', 'webview.css', 'LICENSE.txt', 'syntaxes/ktory.tmLanguage.json',
    'README.md', 'README.zh-CN.md', 'README.ja.md', 'CHANGELOG.md',
    'package.nls.json', 'package.nls.zh-cn.json', 'package.nls.ja.json',
    'l10n/bundle.l10n.json', 'l10n/bundle.l10n.zh-cn.json', 'l10n/bundle.l10n.ja.json'
  ]) {
    assert.equal(fs.readFileSync(path.join(extensionRoot, file), 'utf8'), fs.readFileSync(path.join(root, file), 'utf8'));
  }
  for (const directory of ['node_modules', 'scripts', 'test']) {
    assert.equal(fs.existsSync(path.join(extensionRoot, directory)), false);
  }
  assert.equal(fs.existsSync(buildRoot), false, 'temporary publish output is cleaned after building');
  assert.equal(fs.existsSync(path.join(root, 'reader')), false, 'generated Reader stays outside the source tree');
});

test('the extension contributes localization files and bundles for zh-cn, en, and ja', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
  assert.equal(manifest.l10n, './l10n');
  assert.ok(manifest.contributes.configuration);
  assert.ok(manifest.contributes.configuration.properties['ktory.preview.uiLanguage']);

  const nlsEn = JSON.parse(fs.readFileSync(path.join(root, 'package.nls.json'), 'utf8'));
  const nlsZh = JSON.parse(fs.readFileSync(path.join(root, 'package.nls.zh-cn.json'), 'utf8'));
  const nlsJa = JSON.parse(fs.readFileSync(path.join(root, 'package.nls.ja.json'), 'utf8'));
  for (const key of Object.keys(nlsEn)) {
    assert.ok(nlsZh[key], `missing zh-cn nls key: ${key}`);
    assert.ok(nlsJa[key], `missing ja nls key: ${key}`);
  }

  const l10nEn = JSON.parse(fs.readFileSync(path.join(root, 'l10n/bundle.l10n.json'), 'utf8'));
  const l10nZh = JSON.parse(fs.readFileSync(path.join(root, 'l10n/bundle.l10n.zh-cn.json'), 'utf8'));
  const l10nJa = JSON.parse(fs.readFileSync(path.join(root, 'l10n/bundle.l10n.ja.json'), 'utf8'));
  for (const key of Object.keys(l10nEn)) {
    assert.ok(l10nZh[key], `missing zh-cn l10n key: ${key}`);
    assert.ok(l10nJa[key], `missing ja l10n key: ${key}`);
  }
});

test('the package manifest declares an icon file that exists on disk', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
  assert.ok(manifest.icon, 'package.json must declare an icon');
  assert.ok(fs.existsSync(path.join(root, manifest.icon)), 'the icon file must exist');
});

test('shared HTML renderer preserves anonymous anchors, quoted filenames and escaped source', async () => {
  const { createKtoryHighlighter } = await import('../../Ktory.Highlighting/renderer.mjs');
  const grammar = JSON.parse(fs.readFileSync(path.join(root, 'syntaxes/ktory.tmLanguage.json'), 'utf8'));
  const highlighter = await createKtoryHighlighter(tm, onig, grammar);
  try {
    const [line, escaped] = highlighter.highlightLines('#.sfx("wind.ogg").wait(1).next()\n: <img src=x onerror=alert(1)>');
    assert.match(line, /^<span class="ktr-token ktr-keyword"[^>]*>#<\/span>/);
    assert.match(line, /<span class="ktr-token ktr-string"[^>]*>&quot;wind\.ogg&quot;<\/span>/);
    assert.ok(!escaped.includes('<img'));
    assert.ok(escaped.includes('&lt;img'));
  } finally { highlighter.dispose(); }
});

test('linter service parses .ktr scripts and produces diagnostics via bundled WASM', async () => {
  const { lintScript } = require('../linter.js');
  const frameworkDir = path.join(readerRoot, '_framework');

  const valid = await lintScript('@defaultLang: zh\n: 开场\n', frameworkDir);
  assert.equal(valid.length, 0);

  const error = await lintScript('=== Start ===\n  -> Missing\n', frameworkDir);
  assert.equal(error.length, 1);
  assert.equal(error[0].severity, 1);
  assert.equal(error[0].code, 'KTR_E004');

  const warn = await lintScript('@speaker alice: zh="爱丽丝"\nalice: 对白\n=== Aside ===\n  : 独立\n  -> return\n', frameworkDir);
  assert.ok(warn.some(item => item.code === 'KTR_W002'));
  assert.ok(warn.some(item => item.severity === 2));
});

test('linter initializes without proxy fetch protocol errors when global fetch rejects non-http URLs', async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async url => {
    if (!String(url).startsWith('http:') && !String(url).startsWith('https:')) {
      throw new Error(`InvalidArgumentError: Invalid URL protocol: the URL must start with http: or https:. Got: ${url}`);
    }
    return originalFetch ? originalFetch(url) : { ok: false };
  };
  try {
    delete require.cache[require.resolve('../linter.js')];
    const { lintScript } = require('../linter.js');
    const frameworkDir = path.join(readerRoot, '_framework');
    const result = await lintScript(': 开场\n', frameworkDir);
    assert.ok(Array.isArray(result));
  } finally {
    globalThis.fetch = originalFetch;
  }
});

