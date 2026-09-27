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

class MockAudio {
  static instances = [];

  constructor() {
    this.src = '';
    this.loop = false;
    this.volume = 1.0;
    this.paused = true;
    this.currentTime = 0;
    this.played = false;
    MockAudio.instances.push(this);
  }

  play() {
    this.paused = false;
    this.played = true;
    return Promise.resolve();
  }

  pause() {
    this.paused = true;
  }
}

function loadReaderEnv(hostMode = false) {
  MockAudio.instances = [];
  const elements = {};
  const timers = new Map();
  let nextId = 0;
  const element = id => elements[id] || (elements[id] = makeElement('div'));

  let mockTime = 1000;
  const rafCallbacks = new Map();

  const context = vm.createContext({
    console,
    Audio: MockAudio,
    performance: {
      now: () => mockTime
    },
    Event: class { constructor(type) { this.type = type; } },
    document: {
      body: element('body'),
      getElementById: element,
      createElement: tag => makeElement(tag),
      addEventListener() {},
      dispatchEvent() {}
    },
    window: hostMode ? { ktoryReaderHost: {} } : {},
    setTimeout: (fn, delay) => {
      const id = ++nextId;
      timers.set(id, { fn, delay });
      return id;
    },
    clearTimeout: id => timers.delete(id),
    setInterval: () => ++nextId,
    clearInterval: () => {},
    requestAnimationFrame: fn => {
      const id = ++nextId;
      rafCallbacks.set(id, fn);
      return id;
    },
    cancelAnimationFrame: id => {
      rafCallbacks.delete(id);
    }
  });

  const flushRaf = (advanceMs = 600) => {
    mockTime += advanceMs;
    const cbs = Array.from(rafCallbacks.values());
    rafCallbacks.clear();
    for (const cb of cbs) {
      cb(mockTime);
    }
  };

  vm.runInContext(fs.readFileSync(path.resolve(__dirname, '../../src/Ktory.WebReader/wwwroot/app.js'), 'utf8'), context);
  vm.runInContext(`
    globalThis.api = {
      state, el,
      resolveAudioUrl, parseVolumeArg, parseDurationArg,
      playBgm, setBgmVolume, stopBgm, processAudioTags,
      getCurrentBgmAudio: () => currentBgmAudio,
      getCurrentBgmTrack: () => currentBgmTrack,
      renderBeat
    };
  `, context);

  return { context, api: context.api, timers, flushRaf, MockAudio };
}

test('resolveAudioUrl resolves filenames, paths, and URLs', () => {
  const { api } = loadReaderEnv();

  assert.equal(api.resolveAudioUrl('Scarborough Fair.mp3'), 'audio/Scarborough Fair.mp3');
  assert.equal(api.resolveAudioUrl('Scarborough Fair'), 'audio/Scarborough Fair.mp3');
  assert.equal(api.resolveAudioUrl('audio/Scarborough Fair.mp3'), 'audio/Scarborough Fair.mp3');
  assert.equal(api.resolveAudioUrl('https://example.com/song.ogg'), 'https://example.com/song.ogg');
  assert.equal(api.resolveAudioUrl('/music/track.wav'), '/music/track.wav');
  assert.equal(api.resolveAudioUrl('"Scarborough Fair.mp3"'), 'audio/Scarborough Fair.mp3');
});

test('parseVolumeArg and parseDurationArg handle numbers and key-value formats', () => {
  const { api } = loadReaderEnv();

  assert.equal(api.parseVolumeArg(0.5), 0.5);
  assert.equal(api.parseVolumeArg('0.5'), 0.5);
  assert.equal(api.parseVolumeArg('volume: 0.5'), 0.5);
  assert.equal(api.parseVolumeArg('vol=0.8'), 0.8);
  assert.equal(api.parseVolumeArg(null), null);

  assert.equal(api.parseDurationArg(1.5), 1.5);
  assert.equal(api.parseDurationArg('0.5'), 0.5);
  assert.equal(api.parseDurationArg('duration: 0.5'), 0.5);
  assert.equal(api.parseDurationArg('time = 2.0'), 2.0);
  assert.equal(api.parseDurationArg(null), null);
});

test('playBgm plays audio with correct volume and loop', () => {
  const { api, MockAudio } = loadReaderEnv();

  api.processAudioTags([
    { name: 'playBgm', positionalArgs: ['Scarborough Fair.mp3'], namedArgs: { volume: 0.5 } }
  ]);

  const currentAudio = api.getCurrentBgmAudio();
  assert.ok(currentAudio instanceof MockAudio);
  assert.equal(currentAudio.src, 'audio/Scarborough Fair.mp3');
  assert.equal(currentAudio.loop, true);
  assert.equal(currentAudio.volume, 0.5);
  assert.equal(currentAudio.paused, false);
});

test('setBgmVolume updates volume of currently playing track with smooth transition', () => {
  const { api, flushRaf } = loadReaderEnv();

  api.processAudioTags([
    { name: 'playBgm', positionalArgs: ['Scarborough Fair.mp3'], namedArgs: { volume: 0.5 } }
  ]);

  const currentAudio = api.getCurrentBgmAudio();
  assert.equal(currentAudio.volume, 0.5);

  // Trigger smooth transition over 0.5s to volume 1
  api.processAudioTags([
    { name: 'setBgmVolume', positionalArgs: [1, 0.5], namedArgs: {} }
  ]);

  // Partial frame at 250ms
  flushRaf(250);
  assert.ok(currentAudio.volume > 0.5 && currentAudio.volume < 1.0);

  // Frame at 600ms (elapsed >= 500ms)
  flushRaf(350);
  assert.equal(currentAudio.volume, 1);
});

test('stopBgm pauses and resets audio', () => {
  const { api } = loadReaderEnv();

  api.processAudioTags([
    { name: 'playBgm', positionalArgs: ['Scarborough Fair.mp3'], namedArgs: { volume: 0.5 } }
  ]);

  assert.ok(api.getCurrentBgmAudio());

  api.processAudioTags([
    { name: 'stopBgm', positionalArgs: [0], namedArgs: {} }
  ]);

  assert.equal(api.getCurrentBgmAudio(), null);
});

test('audio tags are ignored in VS Code host mode (window.ktoryReaderHost)', () => {
  const { api, MockAudio } = loadReaderEnv(true);

  api.processAudioTags([
    { name: 'playBgm', positionalArgs: ['Scarborough Fair.mp3'], namedArgs: { volume: 0.5 } },
    { name: 'setBgmVolume', positionalArgs: [1, 0.5], namedArgs: {} }
  ]);

  assert.equal(MockAudio.instances.length, 0);
  assert.equal(api.getCurrentBgmAudio(), null);
});
