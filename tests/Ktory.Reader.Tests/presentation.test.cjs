const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

// Execute the shared Reader source with a controlled clock and minimal DOM.
// Startup is suppressed; only the final bridge boundary is replaced by a trace.
function reader(tags = [], options = {}) {
  let now = 0, nextId = 0;
  const timers = new Map();
  const element = () => ({ style: {}, classList: { toggle() {} }, scrollTo() {}, innerHTML: '' });
  const context = vm.createContext({
    console,
    document: { body: element(), getElementById: element, addEventListener() {} },
    window: {},
    requestAnimationFrame: fn => fn(),
    setInterval: (fn, delay) => schedule(fn, delay, true),
    setTimeout: (fn, delay) => schedule(fn, delay, false),
    clearInterval: id => timers.delete(id),
    clearTimeout: id => timers.delete(id)
  });
  function schedule(fn, delay, repeat) {
    const id = ++nextId;
    timers.set(id, { fn, delay, repeat, at: now + delay });
    return id;
  }
  vm.runInContext(fs.readFileSync(path.resolve(__dirname, '../../src/Ktory.Runner/wwwroot/app.js'), 'utf8'), context);
  vm.runInContext(`
    globalThis.advances = [];
    stepSession = id => { advances.push(id); clearTimers(); state.status = 'Completed'; };
    globalThis.api = { state, el, setupHoldTimer, handleAdvanceAction, startTypewriter,
      finishTypewriter, renderBeat, clearTimers, toggleAutoPlay,
      newEpoch: () => currentSessionEpoch++ };
  `, context);
  const api = context.api;
  Object.assign(api.state, {
    status: 'SuspendedAtBeat', currentPresentationId: 1, activeTags: tags,
    payload: { content: 'First', actualLanguage: 'en', stepType: 0, tags }, ...options
  });
  return {
    ...api, advances: context.advances,
    tick(seconds) {
      const end = now + seconds * 1000;
      while (true) {
        const due = [...timers.entries()].filter(([, t]) => t.at <= end).sort((a, b) => a[1].at - b[1].at)[0];
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

const tag = (name, ...positionalArgs) => ({ name, positionalArgs });

for (const [wait, seconds] of [[tag('wait', 2), 2], [tag('wait'), 1], [tag('wait', 0), 0]]) {
  test(`manual ${JSON.stringify(wait)} discards early clicks and needs a fresh click`, () => {
    const r = reader([wait]);
    r.setupHoldTimer([wait]);
    if (seconds > 0) {
      r.handleAdvanceAction();
      r.handleAdvanceAction();
    }
    r.tick(seconds + 10);
    assert.equal(r.advances.length, 0);
    assert.equal(r.state.isHolding, false);
    r.handleAdvanceAction();
    assert.equal(r.advances.length, 1);
  });
}

for (const [wait, next] of [[2, 2], [2, 5], [5, 2], [2, undefined], [undefined, undefined]]) {
  for (const reversed of [false, true]) {
    test(`wait(${wait}) + next(${next}), reversed=${reversed}, uses the longer concurrent timer`, () => {
      const tags = [tag('wait', ...(wait === undefined ? [] : [wait])), tag('next', ...(next === undefined ? [] : [next]))];
      if (reversed) tags.reverse();
      const r = reader(tags);
      r.setupHoldTimer(tags);
      r.handleAdvanceAction();
      const duration = Math.max(wait ?? 1, next ?? 0);
      r.tick(duration - 0.025);
      assert.equal(r.advances.length, 0);
      r.tick(0.025);
      assert.equal(r.advances.length, 1);
      r.tick(10);
      assert.equal(r.advances.length, 1);
    });
  }
}

test('fresh click after wait can interrupt next, cancelling its timer', () => {
  const tags = [tag('wait', 2), tag('next', 5)];
  const r = reader(tags);
  r.setupHoldTimer(tags);
  r.tick(1);
  r.handleAdvanceAction();
  assert.equal(r.advances.length, 0);
  r.tick(1);
  r.handleAdvanceAction();
  assert.equal(r.advances.length, 1);
  r.tick(10);
  assert.equal(r.advances.length, 1);
});

for (const timed of [false, true]) {
  test(`skippable lock timed=${timed} never replays ignored clicks`, () => {
    const tags = [tag('skippable', false, ...(timed ? [2] : []))];
    const r = reader(tags);
    const target = { innerHTML: '' }, cursor = { style: {} };
    r.startTypewriter(target, cursor, 'A'.repeat(200), tags);
    r.handleAdvanceAction();
    r.tick(2);
    assert.equal(r.state.isTyping, true);
    assert.equal(r.advances.length, 0);
    r.tick(5);
    assert.equal(r.state.isTyping, false);
    assert.equal(r.advances.length, 0);
    r.handleAdvanceAction();
    assert.equal(r.advances.length, 1);
  });
}

test('estimated wait refresh keeps next clock and does not relock an elapsed wait', () => {
  const tags = [tag('wait'), tag('next', 3)];
  const r = reader(tags);
  r.state.currentActiveTypingEl = { innerHTML: '' };
  r.setupHoldTimer(tags);
  r.tick(0.5);
  r.renderBeat({ content: 'one two three four five six seven', actualLanguage: 'en', tags }, true);
  assert.equal(r.state.holdElapsed, 1);
  r.tick(1);
  assert.equal(r.state.allowClickInterrupt, true);
  r.renderBeat({ content: '一二三四五六七', actualLanguage: 'zh', tags }, true);
  assert.equal(r.state.allowClickInterrupt, true);
  r.tick(1.475);
  assert.equal(r.advances.length, 0);
  r.tick(0.025);
  assert.equal(r.advances.length, 1);
});

for (const options of [{ autoPolicy: { enabled: true } }, { autoPlay: true }]) {
  test(`explicit automatic mode still respects local wait: ${JSON.stringify(options)}`, () => {
    const tags = [tag('wait', 2)];
    const r = reader(tags, options);
    r.setupHoldTimer(tags);
    r.handleAdvanceAction();
    r.tick(1.975);
    assert.equal(r.advances.length, 0);
    r.tick(0.025);
    assert.equal(r.advances.length, 1);
  });
}

for (const next of [false, true]) {
  test(`directive wait with next=${next}`, () => {
    const tags = [tag('wait', 2), ...(next ? [tag('next')] : [])];
    const r = reader(tags, { payload: { content: 'pause', stepType: 1, tags } });
    r.setupHoldTimer(tags);
    r.handleAdvanceAction();
    r.tick(2);
    assert.equal(r.advances.length, next ? 1 : 0);
    if (!next) {
      r.handleAdvanceAction();
      assert.equal(r.advances.length, 1);
    }
  });
}

for (const restart of [false, true]) {
  test(`old timer cannot advance after ${restart ? 'session restart' : 'presentation change'}`, () => {
    const tags = [tag('wait', 2), tag('next')];
    const r = reader(tags);
    r.setupHoldTimer(tags);
    if (restart) r.newEpoch();
    else r.state.currentPresentationId++;
    r.tick(5);
    assert.equal(r.advances.length, 0);
  });
}

for (const initiallyAuto of [false, true]) {
  test(`toggling autoplay during wait (initially ${initiallyAuto}) preserves minimum wait`, () => {
    const tags = [tag('wait', 2)];
    const r = reader(tags, { autoPlay: initiallyAuto });
    r.setupHoldTimer(tags);
    r.tick(1);
    r.toggleAutoPlay();
    r.tick(0.975);
    assert.equal(r.advances.length, 0);
    r.tick(0.025);
    assert.equal(r.advances.length, initiallyAuto ? 0 : 1);
  });
}
