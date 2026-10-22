const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function element(tagName = 'div') {
  const classes = new Set();
  const children = [];
  const parts = new Map();
  return {
    style: {}, dataset: {}, children, events: {}, textContent: '', hidden: false, tagName,
    classList: {
      add: (...names) => names.forEach(name => classes.add(name)),
      remove: (...names) => names.forEach(name => classes.delete(name)),
      contains: name => classes.has(name),
      toggle() {}
    },
    setAttribute(name, value) { this[name] = value; },
    get options() { return children.filter(child => child.tagName === 'option'); },
    get value() { return this.storedValue ?? this.options[0]?.value ?? ''; },
    set value(value) { this.storedValue = value; },
    set className(value) { value.split(' ').forEach(name => classes.add(name)); },
    set innerHTML(value) {
      this.html = value;
      parts.clear();
      for (const match of value.matchAll(/class="([^"]+)"/g)) {
        const child = element();
        child.className = match[1];
        for (const name of match[1].split(' ')) parts.set(`.${name}`, child);
      }
    },
    get innerHTML() { return this.html || ''; },
    appendChild(child) { child.parentNode = this; children.push(child); },
    addEventListener(type, listener) { this.events[type] = listener; },
    remove() {
      this.removed = true;
      if (this.parentNode && Array.isArray(this.parentNode.children)) {
        const idx = this.parentNode.children.indexOf(this);
        if (idx !== -1) this.parentNode.children.splice(idx, 1);
      }
    },
    querySelector(selector) {
      if (parts.has(selector)) return parts.get(selector);
      const classes = selector.startsWith('.') ? selector.split('.').filter(Boolean) : null;
      if (classes) {
        return children.find(child => !child.removed && classes.every(c => child.classList.contains(c))) || null;
      }
      return null;
    },
    querySelectorAll(selector) {
      if (selector === '.choice-card-btn') return parts.get('.choice-options-list')?.children || [];
      const classes = selector.startsWith('.') ? selector.split('.').filter(Boolean) : null;
      if (classes) {
        return children.filter(child => !child.removed && classes.every(c => child.classList.contains(c)));
      }
      return [];
    }
  };
}

function reader(fetch, host) {
  let now = 0, nextTimer = 0;
  const timers = new Map();
  const elements = new Map();
  const getElement = id => {
    if (!elements.has(id)) elements.set(id, element());
    return elements.get(id);
  };
  const schedule = (fn, delay, repeat) => {
    const id = ++nextTimer;
    timers.set(id, { fn, delay, repeat, at: now + delay });
    return id;
  };
  const context = vm.createContext({
    console, fetch,
    performance: { now: () => now },
    window: { __ktoryWasmFailed: true, ktoryReaderHost: host },
    document: { body: element(), getElementById: getElement, createElement: element, addEventListener() {} },
    setTimeout: (fn, delay) => schedule(fn, delay, false),
    setInterval: (fn, delay) => schedule(fn, delay, true),
    clearTimeout: id => timers.delete(id), clearInterval: id => timers.delete(id),
    requestAnimationFrame: fn => fn()
  });
  vm.runInContext(fs.readFileSync(path.resolve(__dirname, '../../src/Ktory.WebReader/wwwroot/app.js'), 'utf8'), context);
  vm.runInContext(`
    globalThis.advances = []; globalThis.effects = []; globalThis.choices = [];
    const realStepSession = stepSession, realSubmitChoice = submitChoice;
    stepSession = id => { advances.push(id); clearTimers(); state.status = 'Completed'; };
    submitChoice = (...args) => choices.push(args);
    updatePortraits = () => effects.push('portrait');
    processAudioTags = () => effects.push('audio');
    globalThis.api = { state, el, updateState, changeSecondaryLanguage, startSession,
      renderChoices, newEpoch: () => currentSessionEpoch++, window, multiTrackReader, readerClocks,
      handleAdvanceAction, fastForwardTypewriter, enqueueSessionOp, renderTextValue, tokenizeHtml,
      setMusicPlayer: audio => { currentBgmAudio = audio; },
      realRouting: () => { stepSession = realStepSession; submitChoice = realSubmitChoice; },
      flush: async () => { for (let i = 0; i < 10; i++) await sessionOpChain; } };
  `, context);
  return {
    ...context.api, advances: context.advances, effects: context.effects, choices: context.choices,
    tick(seconds) {
      const end = now + seconds * 1000;
      while (true) {
        const due = [...timers.entries()].filter(([, timer]) => timer.at <= end)
          .sort((a, b) => a[1].at - b[1].at)[0];
        if (!due) break;
        const [id, timer] = due;
        now = timer.at;
        if (timer.repeat) timer.at += timer.delay;
        else timers.delete(id);
        timer.fn();
      }
      now = end;
    }
  };
}

const value = (content, actualLanguage) => ({ content, actualLanguage, requestedLanguage: actualLanguage });
const output = (primary, secondary = null) => ({ primary, secondary });
const tag = (name, ...args) => ({ name, positionalArgs: [], namedArgs: name === 'skippable'
  ? { ...(args.length ? { enabled: args[0] } : {}), ...(args.length > 1 ? { seconds: args[1] } : {}) }
  : args.length ? { seconds: args[0] } : {} });

test('raw fragments render as literal text in both languages throughout typing, reveal and refresh', () => {
  const r = reader();
  const literal = '<b>.wait &amp; 😀</b>\n  **raw**';
  const primary = { ...value(`before ${literal} after`, 'en'), segments: [
    { content: '<b>before ', isLiteral: false }, { content: literal, isLiteral: true },
    { content: ' after</b>', isLiteral: false }
  ] };
  const secondary = { ...value('<i>副语言</i>', 'zh'), segments: [{ content: '<i>副语言</i>', isLiteral: true }] };
  const snapshot = beat(secondary);
  snapshot.payload.text.primary = primary;
  r.updateState(snapshot);
  const html = '<b>before <span class="ktory-literal">&lt;b&gt;.wait &amp;amp; 😀&lt;/b&gt;\n  **raw**</span> after</b>';
  assert.equal(r.state.fullTextHtml, html);
  r.tick(0.4);
  assert.ok(!r.state.currentActiveTypingEl.innerHTML.includes('<b>.wait'));
  r.fastForwardTypewriter();
  assert.equal(r.state.currentActiveTypingEl.innerHTML, html);
  const translated = r.state.currentActivePassageEl.querySelector('.secondary-translation').querySelector('.secondary-content');
  assert.equal(translated.innerHTML, '<span class="ktory-literal">&lt;i&gt;副语言&lt;/i&gt;</span>');
  const swapped = { ...snapshot, requestedLanguage: 'zh', payload: { ...snapshot.payload,
    text: output(secondary, primary) } };
  r.updateState(swapped, true);
  assert.equal(r.state.currentActiveTypingEl.innerHTML, '<span class="ktory-literal">&lt;i&gt;副语言&lt;/i&gt;</span>');
  assert.equal(translated.innerHTML, html);
  assert.deepEqual([...r.effects], ['portrait', 'audio']);
});

test('typewriter treats markup, entities and Unicode characters as whole tokens', () => {
  const r = reader();
  const html = '<span class="ktory-literal">&lt;😀&amp;&#39;</span>';
  assert.deepEqual([...r.tokenizeHtml(html)], [
    '<span class="ktory-literal">', '&lt;', '😀', '&amp;', '&#39;', '</span>'
  ]);
  const snapshot = beat();
  snapshot.payload.text.primary = { ...value('<😀&', 'en'), segments: [{ content: '<😀&', isLiteral: true }] };
  r.updateState(snapshot);
  r.tick(2);
  assert.equal(r.state.currentActiveTypingEl.innerHTML, '<span class="ktory-literal">&lt;😀&amp;</span>');
  assert.equal(r.state.isTyping, false);
});
function beat(secondary = null, tags = [], secondaryName = null) {
  return {
    status: 'SuspendedAtBeat', sessionId: 'session-a', presentationId: 1,
    requestedLanguage: 'en', secondaryLanguage: secondary || secondaryName ? 'zh' : null,
    payload: { stepType: 0, tags, text: output(value('A'.repeat(100), 'en'), secondary),
      speakerName: output(value('Alice', 'en'), secondaryName) }
  };
}

test('secondary refresh leaves primary printing and fast-forward lock running without replaying effects', () => {
  const r = reader();
  const tags = [tag('skippable', false, 1), tag('next', 2)];
  r.updateState(beat(null, tags));
  r.tick(0.3);
  const target = r.state.currentActiveTypingEl;
  const partial = target.innerHTML;
  const timer = r.state.typewriterTimer;
  const lock = r.state.fastForwardLockTimer;
  const passage = r.state.currentActivePassageEl;
  r.updateState(beat(value('副语言', 'zh'), tags), 'secondary');
  assert.equal(r.state.isTyping, true);
  assert.equal(r.state.canFastForward, false);
  assert.equal(r.state.typewriterTimer, timer);
  assert.equal(r.state.fastForwardLockTimer, lock);
  assert.equal(target.innerHTML, partial);
  assert.equal(r.state.currentActivePassageEl, passage);
  assert.equal(passage.querySelector('.secondary-translation').querySelector('.secondary-content').innerHTML, '副语言');
  r.tick(0.7);
  assert.equal(r.state.canFastForward, true);
  assert.equal(r.state.isTyping, true);
  assert.ok(target.innerHTML.length > partial.length);
  assert.deepEqual([...r.effects], ['portrait', 'audio']);
  assert.equal(r.advances.length, 0);
});

for (const tags of [[tag('wait'), tag('next', 3)], [tag('wait', 2), tag('next', 3)]]) {
  test(`secondary refresh preserves elapsed clocks for ${JSON.stringify(tags)}`, () => {
    const r = reader();
    r.updateState(beat(null, tags));
    r.tick(2.2);
    const before = [r.state.holdTimer, r.state.minimumHoldDuration, r.state.holdElapsed,
      r.state.autoAdvanceDuration, r.state.autoAdvanceElapsed];
    r.updateState(beat(value('译文'.repeat(1000), 'zh'), tags), 'secondary');
    assert.deepEqual([r.state.holdTimer, r.state.minimumHoldDuration, r.state.holdElapsed,
      r.state.autoAdvanceDuration, r.state.autoAdvanceElapsed], before);
    r.tick(2.6);
    assert.equal(r.advances.length, 0);
    r.tick(0.1);
    assert.equal(r.advances.length, 1);
  });
}

test('independently absent fields stay hidden and VS Code sanitizes secondary rich text', () => {
  const sanitized = [];
  const r = reader(null, { onState() {}, sanitizeHtml(html) { sanitized.push(html); return html.replace(/<img[^>]*>/g, ''); } });
  r.updateState(beat(value('<b>译文</b><img src=x onerror=evil()>', 'zh')));
  const block = r.state.currentActivePassageEl.querySelector('.secondary-translation');
  assert.equal(block.querySelector('.secondary-content').innerHTML, '<b>译文</b>');
  assert.equal(block.querySelector('.secondary-speaker').hidden, true);
  r.updateState(beat(null, [], value('<script>名字</script>', 'ja')), 'secondary');
  assert.equal(block.querySelector('.secondary-content').hidden, true);
  assert.equal(block.querySelector('.secondary-speaker').textContent, '<script>名字</script>');
  assert.equal(block.querySelector('.secondary-speaker').innerHTML, '');
  assert.equal(block.querySelector('.secondary-speaker').lang, 'ja');
  r.updateState(beat(), 'secondary');
  assert.equal(block.hidden, true);
  assert.ok(sanitized.some(html => html.includes('onerror')));
});

test('secondary option labels update in place, remain text, and retain the original choice token', () => {
  const r = reader();
  const menu = {
    status: 'AwaitingChoice', sessionId: 'session-a', presentationId: 1, requestedLanguage: 'en',
    choice: { presentationId: 1, options: [{ id: 'one', canSelect: true, text: output(value('Enter', 'en')) }] }
  };
  r.updateState(menu);
  const group = r.state.currentActiveChoiceEl;
  const button = group.querySelectorAll('.choice-card-btn')[0];
  menu.secondaryLanguage = 'zh';
  menu.choice.options[0].text.secondary = value('<b>进入</b>', 'zh');
  r.updateState(menu, 'secondary');
  assert.equal(r.state.currentActiveChoiceEl, group);
  assert.equal(group.querySelectorAll('.choice-card-btn')[0], button);
  assert.equal(button.disabled, false);
  assert.equal(button.querySelector('.choice-secondary').textContent, '<b>进入</b>');
  assert.equal(button.querySelector('.choice-secondary').innerHTML, '');
  button.events.click({ stopPropagation() {} });
  assert.deepEqual([...r.choices[0]], ['one', 1, 'session-a']);
});

test('secondary HTTP requests use the serialized current token and discarded epochs cannot render', async () => {
  let release, request;
  const r = reader(async (url, init) => {
    request = { url, body: JSON.parse(init.body) };
    return new Promise(resolve => { release = () => resolve({ ok: true, json: async () => beat(value('旧译文', 'zh')) }); });
  });
  r.updateState(beat());
  const refresh = r.changeSecondaryLanguage('zh');
  await Promise.resolve();
  assert.equal(request.url, '/api/session/secondary-language');
  assert.deepEqual(request.body, { locale: 'zh', sessionId: 'session-a', presentationId: 1 });
  r.newEpoch();
  release();
  await refresh;
  assert.equal(r.state.payload.text.secondary, null);
});

function presentation(event) {
  return { ...event, laneId: event.trackId, laneName: event.trackName, status: event.choice ? 2 : 1,
    requiresInput: event.isMain };
}
function clockGate(laneId, token, options = {}) {
  return { laneId, laneName: laneId, isMain: laneId === 'root',
    token: { sessionId: 'session-a', presentationId: token },
    clockName: 'song', clockId: 1, sourceName: null, position: 2, ...options };
}

test('auxiliary next never submits advance while Main owns player input', async () => {
  const requests = [];
  const dialogue = storyOutput(1, 'root', 'A'.repeat(200));
  const lyric = storyOutput(2, 'lyric', 'Sing', { payload: {
    stepType: 0, content: 'Sing', tags: [tag('next', 0.2)]
  } });
  const following = storyOutput(3, 'root', 'Next');
  const r = reader(async (url, init) => {
    requests.push({ url, ...JSON.parse(init.body) });
    return { ok: true, json: async () => storySnapshot([following], {
      activePresentations: [presentation(following)]
    }) };
  });
  r.realRouting();
  r.updateState(storySnapshot([dialogue, lyric], { activePresentations: [presentation(dialogue)] }));
  r.tick(0.3);
  await r.flush();
  assert.equal(requests.length, 0, 'auxiliary next is display metadata, never a Core advance request');
  const main = r.multiTrackReader.lanes.get('root');
  const auxiliary = r.multiTrackReader.lanes.get('lyric');
  assert.equal(main.state.isTyping, true);
  assert.equal(auxiliary.state.isHolding, false);
  r.handleAdvanceAction();
  assert.equal(main.state.isTyping, false, 'first Main trial fast-forwards its own text');
  r.handleAdvanceAction();
  await r.flush();
  assert.deepEqual(requests, [{ url: '/api/session/step', sessionId: 'session-a', presentationId: 1 }]);
  assert.equal(main.state.currentPresentationId, 3);
  assert.equal(auxiliary.state.currentPresentationId, 2);
  assert.equal(auxiliary.state.currentActiveTypingEl.innerHTML, 'Sing');
  assert.deepEqual([...r.effects], ['portrait', 'audio', 'portrait', 'audio', 'portrait', 'audio']);
});

test('secondary refresh preserves Main printing and auxiliary display timers without replaying effects', () => {
  const r = reader();
  const main = storyOutput(1, 'root', 'A'.repeat(200));
  const auxiliary = storyOutput(2, 'aside', 'B'.repeat(200), { payload: {
    stepType: 0, content: 'B'.repeat(200), tags: [tag('duration', 10)]
  } });
  r.updateState(storySnapshot([main, auxiliary], { activePresentations: [presentation(main)] }));
  r.tick(0.2);
  const lanes = [...r.multiTrackReader.lanes.values()];
  const timers = lanes.map(lane => [lane.state.typewriterTimer, lane.state.displayTimer]);
  const text = lanes.map(lane => lane.state.currentActiveTypingEl.innerHTML);
  const refresh = (event, sequence, translation) => ({ ...event, sequence, kind: 'LanguageRefresh',
    payload: { ...event.payload, text: output(value(event.payload.content, 'en'), value(translation, 'zh')) } });
  r.updateState(storySnapshot([refresh(main, 3, '对白'), refresh(auxiliary, 4, '旁白')], {
    secondaryLanguage: 'zh', activePresentations: [presentation(main)]
  }), 'secondary');
  assert.deepEqual(lanes.map(lane => [lane.state.typewriterTimer, lane.state.displayTimer]), timers);
  assert.deepEqual(lanes.map(lane => lane.state.currentActiveTypingEl.innerHTML), text);
  assert.equal(lanes[0].state.isTyping, true);
  assert.equal(lanes[1].state.isHolding, false);
  assert.equal(lanes[0].state.currentActivePassageEl.querySelector('.secondary-translation').querySelector('.secondary-content').innerHTML, '对白');
  assert.equal(lanes[1].state.currentActivePassageEl.querySelector('.secondary-translation').querySelector('.secondary-content').innerHTML, '旁白');
  assert.equal(r.effects.length, 4);
});

test('restart retires auxiliary display timers and retained Main choice listeners', async () => {
  let calls = 0;
  const r = reader(async () => { calls++; return { ok: true, json: async () => storySnapshot([]) }; });
  r.realRouting();
  const menu = storyOutput(1, 'root', '', { payload: null,
    choice: { presentationId: 1, options: [{ id: 'one', label: 'Enter', canSelect: true }] } });
  const lyric = storyOutput(2, 'lyric', 'Lyric', { payload: {
    stepType: 0, content: 'Lyric', tags: [tag('duration', 1)]
  } });
  r.updateState(storySnapshot([menu, lyric], { activePresentations: [presentation(menu)] }));
  const button = r.multiTrackReader.lanes.get('root').state.currentActiveChoiceEl.querySelectorAll('.choice-card-btn')[0];
  r.newEpoch();
  r.multiTrackReader.reset();
  r.tick(5);
  button.events.click({ stopPropagation() {} });
  await r.flush();
  assert.equal(calls, 0);
});

test('a declared built-in clock waits for its deadline and reports the original session', async () => {
  const requests = [];
  const next = storyOutput(2, 'root', 'Now');
  const r = reader(async (url, init) => {
    requests.push({ url, ...JSON.parse(init.body) });
    return { ok: true, json: async () => storySnapshot([next], { activePresentations: [presentation(next)] }) };
  });
  r.realRouting();
  r.updateState(storySnapshot([], { status: 'WaitingForClock',
    clocks: [{ clockId: 1, name: 'song', sourceName: null, position: 0 }],
    pendingClockGates: [clockGate('root', 1)] }));
  r.tick(1.95);
  await r.flush();
  assert.equal(requests.length, 0);
  r.tick(0.05);
  await r.flush();
  assert.deepEqual(requests, [{ url: '/api/session/clocks/advance', deltaTime: 2, sessionId: 'session-a', sourcePositions: {} }]);
  assert.equal(r.multiTrackReader.lanes.get('root').state.payload.content, 'Now');
});

test('unresolvable external sources remain waiting and clicks cannot invent progress', async () => {
  const requests = [];
  const r = reader(async (url, init) => requests.push({ url, ...JSON.parse(init.body) }));
  r.realRouting();
  r.updateState(storySnapshot([], { status: 'WaitingForClock',
    clocks: [{ clockId: 1, name: 'animation', sourceName: 'progress', position: null }],
    pendingClockGates: [clockGate('root', 1, { clockName: 'animation', sourceName: 'progress', position: -2 })] }));
  r.tick(20);
  r.handleAdvanceAction();
  await r.flush();
  assert.equal(requests.length, 0);
  assert.equal(r.multiTrackReader.lanes.get('root').requiresInput, false);
  assert.match(r.multiTrackReader.lanes.get('root').elements.dockStepHint.textContent, /cannot resolve source progress/);
});

test('waiting for an auxiliary with an unknown source explains the unresolved dependency', () => {
  const r = reader();
  r.updateState(storySnapshot([], { status: 'WaitingForTrack',
    clocks: [{ clockId: 1, name: 'song', sourceName: 'external.player', position: null }],
    pendingClockGates: [clockGate('lyric', 1, { sourceName: 'external.player' })] }));
  assert.match(r.el.dockStepHint.textContent, /cannot resolve source external.player/);
  assert.equal(r.multiTrackReader.lanes.get('lyric').requiresInput, false);
});

test('a burst of immediately resolved session operations yields before monopolizing the browser task', async () => {
  const r = reader();
  let count = 0;
  for (let i = 0; i < 40; i++) r.enqueueSessionOp(() => { count++; });
  for (let i = 0; i < 150; i++) await Promise.resolve();
  assert.equal(count, 32);
  r.tick(0);
  await r.flush();
  assert.equal(count, 40);
});

test('clock-only snapshots preserve a single presentation and cannot replay its audio or finish typing', () => {
  const r = reader();
  const snapshot = { ...beat(), clocks: [{ clockId: 1, name: 'intro', sourceName: null, position: 0 }] };
  r.updateState(snapshot);
  r.tick(0.2);
  const timer = r.state.typewriterTimer;
  const text = r.state.currentActiveTypingEl.innerHTML;
  r.updateState({ ...snapshot, clocks: [{ ...snapshot.clocks[0], position: 0.2 }] }, 'clock');
  assert.equal(r.state.typewriterTimer, timer);
  assert.equal(r.state.currentActiveTypingEl.innerHTML, text);
  assert.equal(r.state.isTyping, true);
  assert.deepEqual([...r.effects], ['portrait', 'audio']);
});

test('built-in clocks start at declaration and explicit reset keeps identity with a fresh deadline', async () => {
  const requests = [];
  const clock = position => ({ clockId: 1, name: 'intro', sourceName: null, position });
  const waiting = token => storySnapshot([], { status: 'WaitingForClock', clocks: [clock(0)],
    pendingClockGates: [clockGate('root', token, { clockName: 'intro' })] });
  const showing = token => {
    const event = storyOutput(token, 'root', 'Entered');
    return storySnapshot([event], { clocks: [clock(2)], activePresentations: [presentation(event)] });
  };
  let gate = 0;
  const r = reader(async (url, init) => {
    requests.push({ url, ...JSON.parse(init.body) });
    const data = url.endsWith('/step') ? waiting(++gate * 2) : showing(gate * 2 + 1);
    return { ok: true, json: async () => data };
  });
  r.realRouting();
  const intro = storyOutput(1, 'root', 'Clock not declared yet');
  r.updateState(storySnapshot([intro], { clocks: [], activePresentations: [presentation(intro)] }));
  r.tick(10);
  r.handleAdvanceAction();
  await r.flush();
  assert.equal(requests[0].deltaTime, 10);
  r.tick(1.95);
  await r.flush();
  assert.equal(requests.length, 1);
  r.tick(0.05);
  await r.flush();
  assert.equal(requests[1].deltaTime, 2);
  r.tick(3);
  r.handleAdvanceAction();
  await r.flush();
  assert.equal(requests[2].deltaTime, 3);
  assert.equal(r.readerClocks.gates[0].clockId, 1, 'reset preserves the existing clock identity');
  r.tick(1.95);
  await r.flush();
  assert.equal(requests.length, 3);
  r.tick(0.05);
  await r.flush();
  assert.equal(requests[3].deltaTime, 2);
});

test('secondary language RPC neither publishes sources nor consumes elapsed clock time', async () => {
  const requests = [];
  const snapshot = { ...beat(), clocks: [{ clockId: 1, name: 'animation', sourceName: 'progress', position: null }] };
  const r = reader(async (url, init) => {
    requests.push({ url, ...JSON.parse(init.body) });
    return { ok: true, json: async () => ({ ...snapshot, secondaryLanguage: 'zh' }) };
  });
  r.updateState(snapshot);
  r.tick(2);
  await r.changeSecondaryLanguage('zh');
  assert.equal(requests.length, 1);
  assert.equal(requests[0].url, '/api/session/secondary-language');
  assert.equal(requests[0].deltaTime, undefined);
  assert.equal(requests[0].sourcePositions, undefined);
  const input = r.readerClocks.captureInput('session-a');
  assert.equal(input.deltaTime, 2);
  assert.deepEqual({ ...input.sourcePositions }, {});
});

test('VS Code previews Main and all auxiliary outputs independently of track names', () => {
  const r = reader(undefined, { onState() {} });
  const main = storyOutput(1, 'root', 'Hello');
  const auxiliary = storyOutput(2, 'ambient', 'Song');
  r.updateState(storySnapshot([main, auxiliary], { activePresentations: [presentation(main)] }));
  assert.equal(r.multiTrackReader.lanes.size, 2);
  assert.equal(r.multiTrackReader.lanes.get('root').requiresInput, true);
  assert.equal(r.multiTrackReader.lanes.get('ambient').requiresInput, false);
  assert.equal(r.multiTrackReader.lanes.get('ambient').panel.hidden, false);
  r.updateState(storySnapshot([], { status: 'Completed' }));
  assert.equal(r.multiTrackReader.lanes.size, 2, 'end of execution does not erase either display');
  assert.equal(r.multiTrackReader.lanes.get('root').requiresInput, false);
  assert.equal(r.multiTrackReader.lanes.get('ambient').state.currentActiveTypingEl.innerHTML, 'Song');
});

test('WASM clock batches use the explicit bridge exports and preserve old input arities', async () => {
  const r = reader();
  const calls = [];
  r.window.registerKtoryWasmBridge({ invokeMethodAsync: async (...args) => { calls.push(args); return '{}'; } });
  calls.length = 0; // Registration also requests the bundled sample catalog.
  await r.window.KtoryWasm.step(1, 'session-a');
  await r.window.KtoryWasm.step(1, 'session-a', 2, { progress: 3 });
  await r.window.KtoryWasm.choice('option', 1, 'session-a', 2, { progress: 3 });
  await r.window.KtoryWasm.advanceClocks(2, 'session-a', { progress: 3 });
  await r.window.KtoryWasm.setLanguage('zh', 1, 'session-a');
  await r.window.KtoryWasm.setSecondaryLanguage('en', 1, 'session-a');
  assert.deepEqual(calls, [
    ['Step', 1, 'session-a'], ['StepWithClock', 1, 'session-a', 2, { progress: 3 }],
    ['ChoiceWithClock', 'option', 1, 'session-a', 2, { progress: 3 }],
    ['AdvanceClocksWithSources', 2, 'session-a', { progress: 3 }],
    ['SetLanguage', 'zh', 1, 'session-a'], ['SetSecondaryLanguage', 'en', 1, 'session-a']
  ]);
});

test('Web Reader keeps auxiliary output in its default dock until its display duration expires', () => {
  const r = reader();
  const main = storyOutput(1, 'root', 'Hello story');
  const auxiliary = storyOutput(2, 'ambient', 'Singing in the wind', { payload: {
    stepType: 0, content: 'Singing in the wind', tags: [tag('duration', 5)],
    text: output(value('Singing in the wind', 'en'), value('在风中歌唱', 'zh'))
  } });
  r.updateState(storySnapshot([main, auxiliary], { activePresentations: [presentation(main)] }));
  const lane = r.multiTrackReader.lanes.get('ambient');
  assert.equal(r.multiTrackReader.lanes.get('root').isPassive, false);
  assert.equal(lane.isPassive, true);
  assert.ok(lane.panel.classList.contains('passive-lyrics-lane'));
  assert.equal(r.multiTrackReader.lyricsDock.hidden, false);
  assert.equal(r.multiTrackReader.lyricsScrim.hidden, false);
  assert.ok(r.el.readerContainer.classList.contains('has-passive-lyrics'));
  const translation = lane.state.currentActivePassageEl.querySelector('.secondary-translation').querySelector('.secondary-content');
  assert.equal(translation.innerHTML, '在风中歌唱');
  r.updateState(storySnapshot([], { status: 'Completed' }));
  assert.equal(r.multiTrackReader.lyricsDock.hidden, false, 'execution completion does not clear the dock');
  r.tick(5);
  assert.equal(r.multiTrackReader.lyricsDock.hidden, true);
  assert.ok(!r.el.readerContainer.classList.contains('has-passive-lyrics'));
});

test('all auxiliary outputs are visible regardless of names while waiting on an external clock', () => {
  const r = reader();
  const main = storyOutput(1, 'root', 'Story');
  const aside = storyOutput(2, 'aside', 'Side text');
  const lyric = storyOutput(3, 'songWords', 'Current lyric');
  r.updateState(storySnapshot([main, aside, lyric], {
    activePresentations: [presentation(main)],
    clocks: [{ clockId: 1, name: 'song', sourceName: 'music.position', position: null }],
    pendingClockGates: [clockGate('songWords', 4, { sourceName: 'music.position', position: 6 })]
  }));
  for (const id of ['aside', 'songWords']) {
    const lane = r.multiTrackReader.lanes.get(id);
    assert.equal(lane.isPassive, true);
    assert.equal(lane.shouldHide, false);
    assert.equal(lane.panel.hidden, false);
    assert.equal(lane.panel.parentNode, r.multiTrackReader.lyricsDock);
    assert.equal(lane.requiresInput, false);
  }
  assert.equal(r.multiTrackReader.lanes.get('songWords').state.currentPresentationId, 3,
    'waiting execution token does not replace displayed content identity');
  assert.equal(r.multiTrackReader.lanes.get('root').panel.parentNode, r.multiTrackReader.container);
});

test('auxiliary lyrics stay visible through at intervals and retire when their replacement arrives', () => {
  const r = reader();
  const main = storyOutput(1, 'root', 'Story text');
  const first = storyOutput(2, 'lyric', '风从旧窗吹来。');
  r.updateState(storySnapshot([main, first], { activePresentations: [presentation(main)] }));
  const lane = r.multiTrackReader.lanes.get('lyric');
  const stream = lane.elements.storyStream;
  const item = stream.querySelector('.passage-item.lyrics-active');
  assert.ok(item.querySelector('.passage-prose').innerHTML.includes('风从旧窗吹来。'));
  assert.equal(item.querySelector('.typing-cursor').style.display, 'none');
  assert.equal(lane.elements.dockProgressBar.style.display, 'none');
  r.updateState(storySnapshot([], { activePresentations: [presentation(main)],
    clocks: [{ clockId: 1, name: 'song', sourceName: null, position: 1.5 }],
    pendingClockGates: [clockGate('lyric', 3)] }));
  assert.equal(stream.querySelector('.passage-item.lyrics-active'), item);
  assert.equal(item.hidden, false);
  const second = storyOutput(4, 'lyric', '灯在远岸亮起。');
  r.updateState(storySnapshot([second], { activePresentations: [presentation(main)] }));
  assert.equal(item.removed, true);
  assert.equal(item.hidden, true);
  assert.ok(stream.querySelector('.passage-item.lyrics-active').querySelector('.passage-prose').innerHTML.includes('灯在远岸亮起。'));
});

function storyOutput(sequence, trackId, content, options = {}) {
  return { sequence, kind: 'Content', sessionId: 'session-a', trackId, trackName: trackId,
    isMain: trackId === 'root', token: { sessionId: 'session-a', presentationId: sequence },
    payload: { stepType: 0, content, actualLanguage: 'en', tags: [] }, ...options };
}
function storySnapshot(outputEvents, options = {}) {
  return { status: 'SuspendedAtBeat', sessionId: 'session-a', presentationId: 1,
    requestedLanguage: 'en', activePresentations: [], retainedPresentations: [], outputEvents, ...options };
}

test('ordered outputs retain the final auxiliary lyric after track and session execution end', () => {
  const r = reader();
  const lyric = storyOutput(1, 'lyric', 'Last line', { payload: {
    stepType: 0, content: 'Last line', actualLanguage: 'en', tags: [tag('duration', 2), tag('next', 0)]
  } });
  r.updateState(storySnapshot([lyric,
    { ...lyric, sequence: 2, kind: 'TrackEnded', payload: null },
    { ...lyric, sequence: 3, kind: 'SessionEnded', payload: null }
  ], { status: 'Completed' }));
  const lane = r.multiTrackReader.lanes.get('lyric');
  const passage = lane.state.currentActivePassageEl;
  assert.equal(lane.state.currentActiveTypingEl.innerHTML, 'Last line');
  assert.equal(passage.hidden, false);
  assert.equal(lane.requiresInput, false);
  r.tick(1.99);
  assert.equal(passage.hidden, false);
  r.tick(0.01);
  assert.equal(passage.hidden, true);
  assert.equal(r.advances.length, 0);
});

test('auxiliary at waiting preserves content identity and ignores wait/next input policies', () => {
  const r = reader();
  const first = storyOutput(1, 'lyric', 'A', { payload: {
    stepType: 0, content: 'A', tags: [tag('wait', 20), tag('next', 0), tag('duration', 3)]
  } });
  r.updateState(storySnapshot([first], { pendingClockGates: [{
    laneId: 'lyric', laneName: 'lyric', isMain: false, clockId: 1, clockName: 'song', sourceName: 'progress', position: 6,
    token: { sessionId: 'session-a', presentationId: 2 }
  }] }));
  const lane = r.multiTrackReader.lanes.get('lyric');
  assert.equal(lane.state.currentPresentationId, 1, 'gate does not replace the content token');
  assert.equal(lane.state.isHolding, false);
  r.tick(1);
  r.updateState(storySnapshot([storyOutput(3, 'lyric', 'B')]));
  const replacement = lane.state.currentActivePassageEl;
  r.tick(5);
  assert.equal(replacement.hidden, false, 'A duration cannot hide B');
  assert.equal(lane.state.currentActiveTypingEl.innerHTML, 'B');
  assert.equal(r.advances.length, 0);
});

test('empty Main input retires its preceding active sentence and takes a fresh input token', () => {
  const r = reader();
  r.updateState(storySnapshot([storyOutput(1, 'root', 'Keep me')]));
  r.tick(1);
  const lane = r.multiTrackReader.lanes.get('root');
  const passage = lane.state.currentActivePassageEl;
  const empty = storyOutput(2, 'root', '');
  r.updateState(storySnapshot([empty], { activePresentations: [{ ...empty, laneId: 'root', status: 1, requiresInput: true }] }));
  assert.equal(lane.state.currentActivePassageEl, null);
  assert.equal(lane.state.currentActiveTypingEl, null);
  assert.ok(passage.classList.contains('sentence-history'));
  assert.equal(lane.state.currentPresentationId, 2);
  assert.equal(lane.state.isTyping, false);
  assert.equal(r.advances.length, 0);
  r.handleAdvanceAction();
  assert.equal(r.advances[0], 2);
});

test('language refresh of a completed auxiliary output preserves its expiry without replaying effects', () => {
  const r = reader();
  const first = storyOutput(1, 'lyric', 'Song', { payload: { stepType: 0, content: 'Song', tags: [tag('duration', 2)] } });
  r.updateState(storySnapshot([first, { ...first, sequence: 2, kind: 'TrackEnded', payload: null }]));
  const lane = r.multiTrackReader.lanes.get('lyric');
  r.tick(1);
  const effects = r.effects.length;
  r.updateState(storySnapshot([{ ...first, sequence: 3, kind: 'LanguageRefresh', payload: {
    ...first.payload, content: '歌', actualLanguage: 'zh'
  } }], { requestedLanguage: 'zh' }), true);
  assert.equal(lane.state.currentActiveTypingEl.innerHTML, '歌');
  assert.equal(lane.state.executionEnded, true);
  assert.equal(r.effects.length, effects);
  r.tick(1);
  assert.equal(lane.state.currentActivePassageEl.hidden, true);
});

test('output delivery is idempotent and VS Code renders auxiliaries alongside Main', () => {
  const r = reader(undefined, { sanitizeHtml: value => value });
  const snapshot = storySnapshot([storyOutput(1, 'root', 'Main'), storyOutput(2, 'lyric', 'Aux')]);
  r.updateState(snapshot);
  const effects = r.effects.length;
  r.updateState(snapshot);
  assert.equal(r.effects.length, effects);
  assert.equal(r.multiTrackReader.lanes.size, 2);
  assert.equal(r.multiTrackReader.lanes.get('lyric').state.currentActiveTypingEl.innerHTML, 'Aux');
});

for (const kind of ['empty', 'directive', 'choice']) {
  test(`${kind} content retires the preceding sentence and its duration and translation identity`, () => {
    const r = reader();
    const text = storyOutput(1, 'root', 'Keep', { payload: {
      stepType: 0, content: 'Keep', text: output(value('Keep', 'en'), value('保留', 'zh')),
      tags: [tag('duration', 3)]
    } });
    r.updateState(storySnapshot([text]));
    r.tick(1);
    const lane = r.multiTrackReader.lanes.get('root');
    const passage = lane.state.currentActivePassageEl;
    const replacement = storyOutput(2, 'root', '', kind === 'directive'
      ? { payload: { stepType: 1, content: '', tags: [] } }
      : kind === 'choice' ? { payload: null, choice: { presentationId: 2, options: [] } } : {});
    r.updateState(storySnapshot([replacement], { activePresentations: [presentation(replacement)] }));
    assert.equal(lane.state.currentActivePassageEl, null);
    assert.equal(lane.state.displayTimer, null);
    assert.ok(passage.classList.contains('sentence-history'));
    r.updateState(storySnapshot([{ ...text, sequence: 3, kind: 'LanguageRefresh', payload: {
      ...text.payload, text: output(value('Retired', 'en'), value('残して', 'ja'))
    } }], { activePresentations: [presentation(replacement)] }), 'secondary');
    assert.equal(passage.querySelector('.secondary-translation').querySelector('.secondary-content').innerHTML, '保留');
    r.tick(5);
    assert.equal(passage.hidden, false, 'history has no running display lifetime');
    assert.equal(lane.state.currentActivePassageEl, null);
  });
}

for (const nextState of ['clock', 'completed']) {
  test(`manual advance into ${nextState} retains duration and translation refresh cannot restart it`, () => {
    const r = reader();
    const text = storyOutput(1, 'root', 'Keep', { payload: {
      stepType: 0, content: 'Keep', tags: [tag('duration', 3)]
    } });
    r.updateState(storySnapshot([text], { activePresentations: [presentation(text)] }));
    r.handleAdvanceAction(); // Only finish printing; duration starts now.
    const lane = r.multiTrackReader.lanes.get('root');
    const passage = lane.state.currentActivePassageEl;
    const expiry = lane.state.displayTimer;
    r.tick(1);
    r.handleAdvanceAction();
    assert.equal(r.advances.length, 1);
    const options = nextState === 'clock'
        ? { status: 'WaitingForClock', pendingClockGates: [clockGate('root', 2, { sourceName: 'animation' })] }
        : { status: 'Completed' };
    r.updateState(storySnapshot([], options));
    r.updateState(storySnapshot([{ ...text, sequence: 3, kind: 'LanguageRefresh', payload: {
      ...text.payload, text: output(value('Keep', 'en'), value('保留', 'zh'))
    } }], { ...options, secondaryLanguage: 'zh' }), 'secondary');
    assert.equal(lane.state.displayTimer, expiry);
    assert.equal(passage.querySelector('.secondary-translation').querySelector('.secondary-content').innerHTML, '保留');
    r.tick(1.99);
    assert.equal(passage.hidden, false);
    r.tick(0.01);
    assert.equal(passage.hidden, true);
    assert.equal(r.advances.length, 1);
  });
}

for (const clockName of ['song', null]) {
  test(`music position releases ${clockName ?? 'anonymous'} clock using its actual source`, async () => {
    const requests = [];
    const released = storyOutput(2, 'root', 'Released');
    const waiting = position => storySnapshot([], { status: 'WaitingForClock',
      clocks: [{ clockId: 1, name: clockName, sourceName: 'music.position', position }],
      pendingClockGates: [clockGate('root', 1, { clockName, sourceName: 'music.position', position: 6 })] });
    const r = reader(async (url, init) => {
      const body = JSON.parse(init.body);
      requests.push({ url, ...body });
      const position = body.sourcePositions['music.position'];
      return { ok: true, json: async () => position >= 6
        ? storySnapshot([released], { activePresentations: [presentation(released)] }) : waiting(position) };
    });
    r.realRouting();
    r.updateState(waiting(null));
    r.tick(5);
    r.handleAdvanceAction();
    await r.flush();
    assert.equal(requests.length, 0, 'unavailable music and player clicks do not forge progress');
    const audio = { readyState: 1, currentTime: 4, paused: true, error: null };
    r.setMusicPlayer(audio);
    r.tick(0.05);
    await r.flush();
    assert.equal(requests.at(-1).sourcePositions['music.position'], 4);
    r.tick(20);
    await r.flush();
    assert.equal(requests.length, 1, 'a paused player does not advance with wall time');
    audio.currentTime = 2;
    r.tick(0.05);
    await r.flush();
    assert.equal(requests.at(-1).sourcePositions['music.position'], 2, 'backward seek is preserved');
    audio.currentTime = 6.25;
    r.tick(0.05);
    await r.flush();
    assert.equal(requests.at(-1).url, '/api/session/clocks/advance');
    assert.equal(requests.at(-1).sourcePositions['music.position'], 6.25);
    assert.equal(r.multiTrackReader.lanes.get('root').state.payload.content, 'Released');
  });
}

test('music stop, loading and media errors clear the old source value', async () => {
  const requests = [];
  const snapshot = position => storySnapshot([], { status: 'WaitingForClock',
    clocks: [{ clockId: 1, name: 'song', sourceName: 'music.position', position }],
    pendingClockGates: [clockGate('root', 1, { sourceName: 'music.position', position: 99 })] });
  const r = reader(async (url, init) => {
    const body = JSON.parse(init.body);
    requests.push(body);
    return { ok: true, json: async () => snapshot(body.sourcePositions['music.position']) };
  });
  const audio = { readyState: 1, currentTime: 12, error: null };
  r.setMusicPlayer(audio);
  r.updateState(snapshot(12));
  for (const state of [null, { readyState: 0, currentTime: 12 }, { readyState: 4, currentTime: 12, error: {} }]) {
    r.setMusicPlayer(state);
    r.updateState(snapshot(12));
    await r.flush();
    assert.equal(requests.at(-1).sourcePositions['music.position'], null);
  }
});

test('estimated reading time uses primary text rather than generated markup before and after refresh', () => {
  const r = reader();
  const text = storyOutput(1, 'root', '', { payload: {
    stepType: 0, tags: [tag('wait'), tag('next', 4)], text: output({ ...value('一二三四五六七', 'zh'),
      segments: [{ content: '一二三四五六七', isLiteral: true }] })
  } });
  r.updateState(storySnapshot([text], { requestedLanguage: 'zh', activePresentations: [presentation(text)] }));
  r.handleAdvanceAction();
  const lane = r.multiTrackReader.lanes.get('root');
  assert.equal(lane.state.minimumHoldDuration, 1);
  r.tick(0.5);
  r.updateState(storySnapshot([{ ...text, sequence: 2, kind: 'LanguageRefresh', payload: {
    ...text.payload, text: output(value('one two three four five six seven', 'en'))
  } }], { activePresentations: [presentation(text)] }), true);
  assert.equal(lane.state.minimumHoldDuration, 2);
  assert.equal(lane.state.holdElapsed, 1, 'estimated progress remains one half');
  assert.equal(lane.state.autoAdvanceElapsed, 0.5, 'fixed next keeps elapsed seconds');
  r.tick(3.5);
  assert.equal(r.advances.length, 1);
});
