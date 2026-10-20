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
  vm.runInContext(fs.readFileSync(path.resolve(__dirname, '../../src/Ktory.WebReader/wwwroot/app.js'), 'utf8'), context);
  vm.runInContext(`
    globalThis.advances = [];
    stepSession = id => { advances.push(id); clearTimers(); state.status = 'Completed'; };
    globalThis.api = { state, el, setupHoldTimer, handleAdvanceAction, startTypewriter,
      finishTypewriter, renderBeat, clearTimers, toggleAutoPlay, renderTextValue, prepareReaderPayload, tokenizeHtml,
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

test('raw fragments preserve literal markup, entities, whitespace and formatting outside the fence', () => {
  const r = reader();
  const raw = '<i>.wait &amp; 😀</i>\n  **raw**';
  const payload = { content: `before ${raw} after`, segments: [
    { content: '<b>before ', isLiteral: false }, { content: raw, isLiteral: true },
    { content: ' after</b>', isLiteral: false }
  ] };
  const prepared = r.prepareReaderPayload(payload);
  const html = '<b>before <span class="ktory-literal">&lt;i&gt;.wait &amp;amp; 😀&lt;/i&gt;\n  **raw**</span> after</b>';
  assert.equal(prepared.content, html);
  assert.equal(prepared.readingContent, payload.content);
  const target = { innerHTML: '' }, cursor = { style: {} };
  r.startTypewriter(target, cursor, html, []);
  r.tick(3);
  assert.equal(target.innerHTML, html);
  assert.equal(r.state.isTyping, false);
});

test('typewriter keeps tags, entities and Unicode characters as whole tokens', () => {
  const r = reader();
  assert.deepEqual([...r.tokenizeHtml('<b>&lt;😀&amp;&#39;</b>')], ['<b>', '&lt;', '😀', '&amp;', '&#39;', '</b>']);
});
