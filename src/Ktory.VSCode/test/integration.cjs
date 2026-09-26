const vscode = require('vscode');
const assert = require('node:assert/strict');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

exports.run = async () => {
  const extension = vscode.extensions.getExtension('Kutinana.ktory');
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
  const action = data => api.panel.webview.postMessage({ type: 'action', revision: api.lastEvent.revision, ...data });
  const first = await waitFor(e => e?.snapshot?.payload?.content === '开场。', 'offline WASM boot and first beat');
  assert.equal(first.version, document.version);
  assert.equal(first.snapshot.recentTags.length, 1);
  await action({ action: 'language', locale: 'en' });
  const translated = await waitFor(e => e?.snapshot?.payload?.content === 'Opening.', 'hot language switch');
  assert.equal(translated.snapshot.presentationId, first.snapshot.presentationId);
  assert.equal(translated.snapshot.recentTags.length, 1, 'language refresh does not replay tags');

  await action({ action: 'advance' });
  const choice = await waitFor(e => e?.snapshot?.status === 'AwaitingChoice', 'choice boundary');
  const choiceAction = { action: 'choice', id: choice.snapshot.choice.options[0].id, presentationId: choice.snapshot.presentationId };
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
  console.log('PASS: VS Code webview boots bundled C# WASM, reads dirty buffers, preserves choice/language boundaries, rejects stale input, reports and recovers from errors, and reopens cleanly.');
};
