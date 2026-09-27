const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function reader(fetch) {
  const elements = new Map();
  const makeElement = () => {
    const classes = new Set();
    return {
      style: {}, dataset: {}, children: [], events: {}, textContent: '', innerHTML: '',
      classList: { add: name => classes.add(name), contains: name => classes.has(name), toggle() {} },
      appendChild(child) { this.children.push(child); },
      addEventListener(type, listener) { this.events[type] = listener; },
      remove() { this.removed = true; },
      querySelector(selector) {
        if (selector === '.choice-options-list') return this.list ||= makeElement();
        return this.children.find(child => !child.removed && !child.classList.contains('has-selection')) || null;
      },
      querySelectorAll() { return this.list?.children || []; }
    };
  };
  const element = id => {
    if (!elements.has(id)) elements.set(id, makeElement());
    return elements.get(id);
  };
  const context = vm.createContext({
    console, fetch, setTimeout, clearTimeout, setInterval, clearInterval,
    document: { body: element('body'), getElementById: element, createElement: makeElement, addEventListener() {} },
    window: {}, requestAnimationFrame: fn => fn()
  });
  vm.runInContext(fs.readFileSync(path.resolve(__dirname, '../../src/Ktory.WebReader/wwwroot/app.js'), 'utf8'), context);
  vm.runInContext(`
    globalThis.updates = [];
    // Test the real request queue and token capture; rendering is verified separately.
    updateState = data => { updates.push(data); state.currentSessionId = data.sessionId;
      state.currentPresentationId = data.presentationId; state.status = data.status; };
    globalThis.api = { state, el, stepSession, submitChoice, startSession, renderDiagnostics, renderChoices };
  `, context);
  Object.assign(context.api.state, { status: 'SuspendedAtBeat', currentSessionId: 'session-a', currentPresentationId: 1 });
  return { ...context.api, updates: context.updates };
}

test('HTTP advance carries the captured session and presentation together', async () => {
  let request;
  const r = reader(async (url, init) => {
    request = { url, body: JSON.parse(init.body) };
    return { ok: true, json: async () => ({ sessionId: 'session-a', presentationId: 2, status: 'SuspendedAtBeat' }) };
  });
  await r.stepSession(1);
  assert.equal(request.url, '/api/session/step');
  assert.deepEqual(request.body, { sessionId: 'session-a', presentationId: 1 });
  assert.equal(r.state.currentPresentationId, 2);
});

test('a retained old choice button cannot target a replacement session with the same presentation number', async () => {
  let calls = 0;
  const r = reader(async () => { calls++; throw new Error('must not send'); });
  Object.assign(r.state, { status: 'AwaitingChoice', currentSessionId: 'session-b', currentPresentationId: 1 });
  await r.submitChoice('old-option', 1, 'session-a');
  assert.equal(calls, 0);
  assert.equal(r.state.status, 'AwaitingChoice');
});

test('an old DOM choice listener cannot clear the new menu before its request is rejected', () => {
  let calls = 0;
  const r = reader(async () => { calls++; throw new Error('must not send'); });
  r.state.status = 'AwaitingChoice';
  const menu = { presentationId: 1, options: [{ id: 'option', label: 'Enter', canSelect: true }] };
  r.renderChoices(menu);
  const oldButton = r.state.currentActiveChoiceEl.querySelector('.choice-options-list').children[0];
  r.state.currentSessionId = 'session-b';
  r.renderChoices(menu);
  const current = r.state.currentActiveChoiceEl;
  oldButton.events.click({ stopPropagation() {} });
  assert.equal(r.state.currentActiveChoiceEl, current);
  assert.equal(current.classList.contains('has-selection'), false);
  assert.equal(current.querySelector('.choice-options-list').children[0].disabled, false);
  assert.equal(calls, 0);
});

test('an in-flight response from before restart cannot render into the new session', async () => {
  let release;
  const r = reader(async url => {
    if (url.endsWith('/step')) return new Promise(resolve => { release = () => resolve({ ok: true,
      json: async () => ({ sessionId: 'session-a', presentationId: 2, status: 'SuspendedAtBeat' }) }); });
    return { ok: true, json: async () => ({ sessionId: 'session-b', presentationId: 1, status: 'SuspendedAtBeat' }) };
  });
  const old = r.stepSession(1);
  await Promise.resolve();
  assert.ok(release);
  const fresh = r.startSession(': new');
  release();
  await Promise.all([old, fresh]);
  assert.equal(r.updates.length, 1);
  assert.equal(r.updates[0].sessionId, 'session-b');
});

test('visible diagnostics show warning text safely and keep ignored input in the background', () => {
  const r = reader();
  r.state.diagnostics = [
    { kind: 256, block: 'root', lineNumber: 2, message: '<script>literal warning</script>' },
    { kind: 512, block: 'root', lineNumber: 2, message: 'old input ignored' }
  ];
  r.renderDiagnostics();
  assert.equal(r.el.runtimeDiagnostics.hidden, false);
  assert.equal(r.el.runtimeDiagnosticSummary.textContent, '剧本警告（1）');
  assert.equal(r.el.runtimeDiagnosticMessages.textContent, 'root · L2: <script>literal warning</script>');
  assert.equal(r.el.runtimeDiagnosticMessages.innerHTML, '');
  r.state.diagnostics = [];
  r.renderDiagnostics();
  assert.equal(r.el.runtimeDiagnostics.hidden, true);
});
