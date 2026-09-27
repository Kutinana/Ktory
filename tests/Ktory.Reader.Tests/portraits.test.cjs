const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function makeElement(tag = 'div') {
  const listeners = {};
  const classes = new Set();
  const children = [];
  const el = {
    tagName: tag.toUpperCase(),
    innerHTML: '',
    textContent: '',
    style: {},
    dataset: {},
    classList: {
      add: (...names) => names.forEach(n => classes.add(n)),
      remove: (...names) => names.forEach(n => classes.delete(n)),
      contains: name => classes.has(name),
      toggle: (name, force) => {
        if (force === undefined) {
          if (classes.has(name)) { classes.delete(name); return false; }
          classes.add(name); return true;
        }
        if (force) classes.add(name); else classes.delete(name);
        return force;
      }
    },
    addEventListener: (event, fn) => {
      listeners[event] = listeners[event] || [];
      listeners[event].push(fn);
    },
    removeEventListener: (event, fn) => {
      if (!listeners[event]) return;
      listeners[event] = listeners[event].filter(f => f !== fn);
    },
    dispatchEvent: event => {
      for (const fn of listeners[event.type] || []) fn(event);
    },
    appendChild: child => { children.push(child); return child; },
    querySelector: () => makeElement('div'),
    querySelectorAll: () => [],
    offsetWidth: 100
  };
  return el;
}

function loadReaderEnv() {
  const elements = {};
  const timers = new Map();
  let nextId = 0;
  const element = id => elements[id] || (elements[id] = makeElement('div'));

  const context = vm.createContext({
    console,
    Event: class { constructor(type) { this.type = type; } },
    document: {
      body: element('body'),
      getElementById: element,
      createElement: tag => makeElement(tag),
      addEventListener() {},
      dispatchEvent() {}
    },
    window: {},
    setTimeout: (fn, delay) => {
      const id = ++nextId;
      timers.set(id, { fn, delay });
      return id;
    },
    clearTimeout: id => timers.delete(id),
    setInterval: () => ++nextId,
    clearInterval: () => {},
    requestAnimationFrame: fn => fn()
  });

  vm.runInContext(fs.readFileSync(path.resolve(__dirname, '../../src/Ktory.WebReader/wwwroot/app.js'), 'utf8'), context);
  vm.runInContext(`
    globalThis.api = {
      state, el, portraitState, PORTRAIT_REGISTRY,
      extractPortraitsFromTags, updatePortraits, updateSlot, clearAllPortraits,
      renderBeat
    };
  `, context);

  return { context, api: context.api, timers };
}

test('extractPortraitsFromTags extracts positional and named portraits', () => {
  const { api } = loadReaderEnv();

  // Positional 2-args
  const res1 = api.extractPortraitsFromTags([
    { name: 'portrait', positionalArgs: ['kutori', 'left'], namedArgs: {} },
    { name: 'portrait', positionalArgs: ['william', 'right'], namedArgs: {} }
  ]);
  assert.equal(res1.left?.id, 'kutori');
  assert.equal(res1.left?.src, 'portraits/kutori.webp');
  assert.equal(res1.right?.id, 'william');
  assert.equal(res1.right?.src, 'portraits/william.webp');

  // Named arguments
  const res2 = api.extractPortraitsFromTags([
    { name: 'portrait', positionalArgs: [], namedArgs: { id: 'kutori', side: 'left' } }
  ]);
  assert.equal(res2.left?.id, 'kutori');
  assert.equal(res2.right, null);

  // Auto-slot inference for kutori and william
  const res3 = api.extractPortraitsFromTags([
    { name: 'char', positionalArgs: ['kutori'], namedArgs: {} },
    { name: 'char', positionalArgs: ['william'], namedArgs: {} }
  ]);
  assert.equal(res3.left?.id, 'kutori');
  assert.equal(res3.right?.id, 'william');
});

test('updateSlot applies entering then active classes on appearance, exiting on clear', () => {
  const { api, timers } = loadReaderEnv();
  const slotEl = api.el.portraitSlotLeft;

  // 1. Initial display triggers entering then active
  api.updateSlot('left', slotEl, { id: 'kutori', src: 'portraits/kutori.webp' });
  assert.ok(slotEl.classList.contains('active'));
  assert.ok(slotEl.innerHTML.includes('portraits/kutori.webp'));

  // 2. Same portrait does not re-trigger enter animation
  api.updateSlot('left', slotEl, { id: 'kutori', src: 'portraits/kutori.webp' });
  assert.ok(slotEl.classList.contains('active'));

  // 3. Clear/absence triggers exiting (in-place fade-out)
  api.updateSlot('left', slotEl, null);
  assert.ok(!slotEl.classList.contains('active'));
  assert.ok(slotEl.classList.contains('exiting'));

  // 4. Timer expiry clears exiting class
  for (const timer of Array.from(timers.values())) {
    timer.fn();
  }
  assert.ok(!slotEl.classList.contains('exiting'));
  assert.equal(slotEl.innerHTML, '');
});

test('renderBeat updates portraits per beat and fades out omitted slots', () => {
  const { api } = loadReaderEnv();
  const leftSlot = api.el.portraitSlotLeft;
  const rightSlot = api.el.portraitSlotRight;

  // Beat 1: kutori appears on left
  api.renderBeat({
    stepType: 0,
    speaker: '珂朵莉',
    content: '你好，威廉。',
    tags: [{ name: 'portrait', positionalArgs: ['kutori', 'left'], namedArgs: {} }]
  });
  assert.ok(leftSlot.classList.contains('active'));
  assert.ok(!rightSlot.classList.contains('active'));

  // Beat 2: william appears on right; kutori not specified so fades out
  api.renderBeat({
    stepType: 0,
    speaker: '威廉',
    content: '你好，珂朵莉。',
    tags: [{ name: 'portrait', positionalArgs: ['william', 'right'], namedArgs: {} }]
  });
  assert.ok(leftSlot.classList.contains('exiting'));
  assert.ok(rightSlot.classList.contains('active'));
});

test('portraits are skipped in VS Code extension environment (window.ktoryReaderHost present)', () => {
  const { context, api } = loadReaderEnv();
  context.window.ktoryReaderHost = {};
  const leftSlot = api.el.portraitSlotLeft;
  const rightSlot = api.el.portraitSlotRight;

  api.renderBeat({
    stepType: 0,
    speaker: '珂朵莉',
    content: '你好，威廉。',
    tags: [{ name: 'portrait', positionalArgs: ['kutori', 'left'], namedArgs: {} }]
  });

  assert.equal(leftSlot.classList.contains('active'), false);
  assert.equal(leftSlot.innerHTML, '');
  assert.equal(rightSlot.classList.contains('active'), false);
});

