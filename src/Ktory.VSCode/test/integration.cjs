const vscode = require('vscode');
const assert = require('node:assert/strict');
const { publisher, name } = require('../package.json');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

exports.run = async () => {
  const extension = vscode.extensions.getExtension(`${publisher}.${name}`);
  assert.ok(extension, 'Extension discovered');
  const api = await extension.activate();
  const source = `@defaultLang: zh
主角:
  @zh: 开场。
  @en: Opening.
  .sfx("bell")
#choice
  * [进入]
    主角:
      @zh: 分支第一拍。
      @en: First branch.
    主角: 分支第二拍。
-> end
=== Aside ===
  主角: 独立入口。
`;
  const document = await vscode.workspace.openTextDocument({ language: 'ktory', content: source });
  await vscode.window.showTextDocument(document);
  await vscode.commands.executeCommand('ktory.openPreview', document.uri);

  const waitFor = async (predicate, label, allowError = false) => {
    const end = Date.now() + 65000;
    while (Date.now() < end) {
      if (predicate(api.lastEvent)) return api.lastEvent;
      if (!allowError && api.lastEvent?.type === 'error') throw new Error(`${label}: ${api.lastEvent.message}`);
      await sleep(50);
    }
    throw new Error(`${label}: timed out; last event = ${JSON.stringify(api.lastEvent)}`);
  };
  const action = data => api.panel.webview.postMessage({ type: 'action', revision: api.lastEvent.revision,
    sessionId: api.lastEvent.snapshot?.sessionId, presentationId: api.lastEvent.snapshot?.presentationId, ...data });
  await waitFor(e => e?.snapshot?.payload?.content, 'offline WASM boot');
  await action({ action: 'language', locale: 'zh' });
  const first = await waitFor(e => e?.snapshot?.payload?.content === '开场。', 'first beat in explicit primary language');
  assert.equal(first.version, document.version);
  assert.equal(first.snapshot.recentTags.length, 1);
  await action({ action: 'language', locale: 'en' });
  const translated = await waitFor(e => e?.snapshot?.payload?.content === 'Opening.', 'hot language switch');
  assert.equal(translated.snapshot.presentationId, first.snapshot.presentationId);
  assert.equal(translated.snapshot.recentTags.length, 1, 'language refresh does not replay tags');

  await action({ action: 'advance' });
  const choice = await waitFor(e => e?.snapshot?.status === 'AwaitingChoice', 'choice boundary');
  const choiceAction = { action: 'choice', id: choice.snapshot.choice.options[0].id,
    sessionId: choice.snapshot.sessionId, presentationId: choice.snapshot.presentationId };
  await action(choiceAction);
  await action(choiceAction);
  const branch = await waitFor(e => e?.snapshot?.payload?.content === 'First branch.', 'choice first beat');
  await sleep(450);
  assert.equal(api.lastEvent.snapshot.presentationId, branch.snapshot.presentationId, 'duplicate choice does not skip first beat');
  await action({ action: 'advance' });
  const fallback = await waitFor(e => e?.snapshot?.payload?.content === '分支第二拍。', 'next branch beat');
  assert.equal(fallback.snapshot.payload.actualLanguage, 'zh');
  assert.equal(fallback.snapshot.payload.requestedLanguage, 'en');

  const edit = async text => {
    const change = new vscode.WorkspaceEdit();
    change.replace(document.uri, new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length)), text);
    assert.ok(await vscode.workspace.applyEdit(change));
  };
  await edit('主角: 未保存的新内容。');
  await sleep(100);
  assert.equal(api.lastEvent, fallback, 'editing does not silently restart the preview');
  await vscode.commands.executeCommand('ktory.reloadPreview');
  const reloaded = await waitFor(e => e?.snapshot?.payload?.content === '未保存的新内容。', 'reload unsaved buffer');
  assert.equal(reloaded.version, document.version);
  await api.panel.webview.postMessage({ type: 'action', revision: choice.revision, ...choiceAction });
  await sleep(100);
  assert.equal(api.lastEvent, reloaded, 'old-session input is ignored');

  const beforeRestart = api.lastEvent;
  await action({ action: 'restart' });
  const restarted = await waitFor(e => e?.snapshot?.sessionId && e.snapshot.sessionId !== beforeRestart.snapshot.sessionId, 'new session identity');
  await action({ action: 'advance', sessionId: beforeRestart.snapshot.sessionId,
    presentationId: beforeRestart.snapshot.presentationId });
  await sleep(100);
  assert.equal(api.lastEvent, restarted, 'old-session input is ignored even within the same document revision');

  await edit('@speaker alice: ja="アリス"\nalice:\n  @fr: Bonjour');
  await vscode.commands.executeCommand('ktory.reloadPreview');
  const missing = await waitFor(e => e?.snapshot?.payload?.content === 'Bonjour', 'third-language fallback');
  assert.equal(missing.snapshot.payload.actualLanguage, 'fr');
  assert.equal(missing.snapshot.payload.speakerActualLanguage, 'ja');
  assert.equal(missing.snapshot.diagnostics.filter(item => item.kind === 256).length, 2);

  await edit('@speaker alice: zh="Alice"\n  .emotion(normal).voice(soft)\nalice: first .emotion(happy)\nalice: second');
  await vscode.commands.executeCommand('ktory.reloadPreview');
  const defaultFirst = await waitFor(e => e?.snapshot?.payload?.content === 'first', 'speaker local override');
  assert.equal(defaultFirst.snapshot.payload.tags.find(tag => tag.name === 'emotion').positionalArgs[0], 'happy');
  // State arrives before the five-character typewriter finishes; a click during typing only fast-forwards.
  await sleep(450);
  await action({ action: 'advance', sessionId: defaultFirst.snapshot.sessionId, presentationId: defaultFirst.snapshot.presentationId });
  const defaultSecond = await waitFor(e => e?.snapshot?.payload?.content === 'second', 'speaker default restored');
  assert.equal(defaultSecond.snapshot.payload.tags.find(tag => tag.name === 'emotion').positionalArgs[0], 'normal');

  await edit(`@defaultLang: zh
#.clock(name="song")
#.track(source="Lyrics", name="lyric")
:
  @zh: 同时对白。
  @en: Parallel dialogue.
#choice
  + [继续]
    : 分支首拍。
#.awaitTrack(name="lyric")
: joined
=== Lyrics ===
  :
    @zh: 同时歌词。
    @en: Parallel lyric.
    .at(value=.25, clock="song").duration(seconds=20)
`);
  await vscode.commands.executeCommand('ktory.reloadPreview');
  const parallel = await waitFor(e => e?.snapshot?.retainedPresentations?.some(p => !p.isMain && p.payload),
    'auxiliary output and built-in clock gate');
  assert.equal(parallel.snapshot.status, 'SuspendedAtBeat');
  assert.equal(parallel.snapshot.activePresentations.length, 1, 'only Main owns active input');
  assert.equal(parallel.snapshot.pendingClockGates.length, 0);
  const dialogueLane = parallel.snapshot.activePresentations[0];
  const lyricLane = parallel.snapshot.retainedPresentations.find(lane => !lane.isMain);
  await action({ action: 'secondary-language', locale: 'zh' });
  const bilingual = await waitFor(e => e?.snapshot?.secondaryLanguage === 'zh', 'retained auxiliary secondary translation');
  assert.deepEqual(bilingual.snapshot.activePresentations.map(lane => lane.token), parallel.snapshot.activePresentations.map(lane => lane.token));
  assert.equal(bilingual.snapshot.retainedPresentations.find(lane => !lane.isMain).payload.text.secondary.content, '同时歌词。');
  assert.equal(bilingual.snapshot.recentLaneTags.length, parallel.snapshot.recentLaneTags.length, 'secondary does not replay effects');
  await sleep(1100);
  await action({ action: 'advance', ...dialogueLane.token });
  const parallelMenu = await waitFor(e => e?.snapshot?.activePresentations?.some(lane => lane.choice), 'targeted Main input');
  const menuLane = parallelMenu.snapshot.activePresentations.find(lane => lane.choice);
  await action({ action: 'choice', ...menuLane.token, id: menuLane.choice.options[0].id });
  const parallelBranch = await waitFor(e => e?.snapshot?.activePresentations?.some(lane => lane.payload?.content === '分支首拍。'), 'Main choice first beat');
  assert.deepEqual(parallelBranch.snapshot.retainedPresentations.find(lane => !lane.isMain).token, lyricLane.token, 'Main input cannot advance auxiliary display');
  await action({ action: 'secondary-language', locale: null });
  await waitFor(e => e?.snapshot?.secondaryLanguage === null, 'disable secondary translation');

  await edit(`#.track(source="Init").await
#.defaultClock(name="initialized")
: initialized after await
=== Init ===
  #.clock(name="initialized")
`);
  await vscode.commands.executeCommand('ktory.reloadPreview');
  const initialized = await waitFor(e => e?.snapshot?.payload?.content === 'initialized after await', 'anonymous track await initializes before Main continues');
  assert.equal(initialized.snapshot.clocks.some(clock => clock.name === 'initialized'), true);
  assert.equal(initialized.snapshot.recentTags.some(tag => tag.name === 'await'), false, 'await sugar stays inside Core');

  await edit(`: clock intro
#.clock(name="scene")
: one .at(value=.5, clock="scene")
#.resetClock(name="scene")
: two .at(value=.5, clock="scene")
#.resetClock(name="scene")
: three .at(value=.5, clock="scene")
`);
  await vscode.commands.executeCommand('ktory.reloadPreview');
  await waitFor(e => e?.snapshot?.payload?.content === 'clock intro', 'clock declaration prelude');
  await sleep(900); // A later declaration must not inherit time spent reading the prelude.
  await action({ action: 'advance' });
  const declared = await waitFor(e => e?.snapshot?.pendingClockGates?.some(gate => gate.clockName === 'scene'), 'late explicit clock declaration');
  const sceneClock = declared.snapshot.clocks.find(clock => clock.name === 'scene');
  assert.equal(sceneClock.position, 0);
  assert.equal(declared.snapshot.recentTags.some(tag => tag.name === 'clock'), false, 'clock operations stay inside Core');
  await waitFor(e => e?.snapshot?.payload?.content === 'one', 'declared clock releases its gate');
  await sleep(300);
  await action({ action: 'advance' });
  const redeclared = await waitFor(e => e?.snapshot?.pendingClockGates?.some(gate => gate.clockName === 'scene'), 'explicit reset restarts clock time');
  const nextClock = redeclared.snapshot.clocks.find(clock => clock.name === 'scene');
  assert.equal(nextClock.clockId, sceneClock.clockId);
  assert.equal(nextClock.position, 0);
  await waitFor(e => e?.snapshot?.payload?.content === 'two', 'redeclared clock releases its gate');
  await sleep(300);
  await action({ action: 'advance' });
  const reset = await waitFor(e => e?.snapshot?.pendingClockGates?.some(gate => gate.clockName === 'scene'), 'explicit clock reset');
  assert.equal(reset.snapshot.clocks.find(clock => clock.name === 'scene').clockId, nextClock.clockId);
  assert.equal(reset.snapshot.clocks.find(clock => clock.name === 'scene').position, 0);
  await waitFor(e => e?.snapshot?.payload?.content === 'three', 'reset clock releases its gate');

  await edit(`@defaultLang: zh
Alice:
  @zh: '''
    <b>.wait & **literal**</b>
    // @fr: literal too
  '''
  @en: **Normal** with '''<i>.next</i>'''.
  .effect
`);
  await vscode.commands.executeCommand('ktory.reloadPreview');
  await waitFor(e => e?.snapshot?.payload?.text?.primary?.segments?.some(piece => piece.isLiteral), 'raw text in real WASM');
  await action({ action: 'language', locale: 'zh' });
  const raw = await waitFor(e => e?.snapshot?.payload?.actualLanguage === 'zh' &&
    e.snapshot.payload.text.primary.segments?.some(piece => piece.isLiteral), 'multiline raw text in explicit primary language');
  assert.equal(raw.snapshot.payload.content, '<b>.wait & **literal**</b>\n// @fr: literal too');
  assert.deepEqual(raw.snapshot.recentTags.map(tag => tag.name), ['effect']);
  await action({ action: 'secondary-language', locale: 'en' });
  const rawBilingual = await waitFor(e => e?.snapshot?.payload?.text?.secondary?.segments?.some(piece => piece.isLiteral), 'raw secondary fragments');
  assert.equal(rawBilingual.snapshot.payload.text.secondary.segments[0].content, '<b>Normal</b> with ');
  assert.equal(rawBilingual.snapshot.payload.text.secondary.segments[1].content, '<i>.next</i>');
  await action({ action: 'language', locale: 'en' });
  const rawEnglish = await waitFor(e => e?.snapshot?.payload?.actualLanguage === 'en', 'raw primary language refresh');
  assert.equal(rawEnglish.snapshot.presentationId, raw.snapshot.presentationId);
  assert.equal(rawEnglish.snapshot.recentTags.length, 1, 'raw language refresh does not replay tags');
  await edit(": before\nAlice: '''unclosed");
  await vscode.commands.executeCommand('ktory.reloadPreview');
  await waitFor(e => e?.type === 'error' && /Unterminated raw text fence/.test(e.message), 'raw fence diagnostic', true);
  await waitFor(() => api.diagnostics.get(document.uri)?.length === 1, 'raw editor diagnostic', true);
  assert.equal(api.diagnostics.get(document.uri)[0].range.start.line, 1);

  await edit('-> Missing');
  await vscode.commands.executeCommand('ktory.reloadPreview');
  await waitFor(e => e?.type === 'error' && e.version === document.version, 'parse error', true);
  await waitFor(() => api.diagnostics.get(document.uri)?.length === 1, 'editor diagnostic', true);
  assert.equal(api.diagnostics.get(document.uri)[0].range.start.line, 0);
  await action({ action: 'advance' });
  await sleep(100);
  assert.equal(api.lastEvent.type, 'error', 'failed reload cannot resume the old story');
  await edit('主角: 修复完成。');
  await vscode.commands.executeCommand('ktory.reloadPreview');
  await waitFor(e => e?.snapshot?.payload?.content === '修复完成。', 'recover after error', true);
  assert.equal(api.diagnostics.get(document.uri)?.length || 0, 0);

  api.panel.dispose();
  await vscode.commands.executeCommand('ktory.openPreview', document.uri);
  await waitFor(e => e?.snapshot?.payload?.content === '修复完成。', 'close and reopen');
  if (process.env.KTORY_VISUAL_CHECK === '1') {
    await edit(source);
    await vscode.commands.executeCommand('ktory.reloadPreview');
    await waitFor(e => e?.snapshot?.payload?.content === '开场。', 'visual preview');
    await vscode.commands.executeCommand('workbench.action.focusSecondEditorGroup');
    console.log('Visual check: the tested VSIX is open for 45 seconds.');
    await sleep(45000);
  }
  api.panel.dispose();
  console.log('PASS: VS Code webview boots bundled C# WASM, reads dirty buffers, preserves Main input and retained auxiliary output, refreshes languages without effects, resets clocks without replacing identity, preserves raw text, rejects stale input, recovers from errors, and reopens cleanly.');
};
