const vscode = require('vscode');
const fs = require('node:fs');
const path = require('node:path');

function escapeAttribute(value) {
  return String(value).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function previewHtml(webview, extensionUri) {
  const readerUri = vscode.Uri.joinPath(extensionUri, 'reader');
  const resource = (...parts) => escapeAttribute(webview.asWebviewUri(vscode.Uri.joinPath(extensionUri, ...parts)).toString());
  const csp = escapeAttribute(webview.cspSource);
  let html = fs.readFileSync(vscode.Uri.joinPath(readerUri, 'index.html').fsPath, 'utf8');
  // No CDN, server or second copy of the reader UI. All runtime assets ship in the VSIX.
  html = html.replace(/<link\b[^>]*href="https:[^>]*>/g, '');
  html = html.replace(/<script>[\s\S]*?<\/script>/g, '');
  html = html.replace(/\b(src|href)="(app\.js|style\.css|favicon\.ico|ktory_logo\.webp)"/g,
    (_, attribute, file) => `${attribute}="${resource('reader', file)}"`);
  html = html.replace('<head>', `<head>
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${csp} data:; style-src ${csp} 'unsafe-inline'; font-src ${csp}; script-src ${csp} 'wasm-unsafe-eval'; connect-src ${csp}; worker-src ${csp} blob:;">
    <script src="${resource('webview.js')}" data-reader-root="${resource('reader')}"></script>`);
  // Load host styles after the shared stylesheet.
  html = html.replace('</head>', `<link rel="stylesheet" href="${resource('webview.css')}"></head>`);
  html = html.replace('</body>', `<script src="${resource('reader', '_framework', 'blazor.webassembly.js')}" autostart="false"></script></body>`);
  return html;
}

function activate(context) {
  let panel;
  let document;
  let ready = false;
  let revision = 0;
  let lastEvent;
  let disposed = false;
  const output = vscode.window.createOutputChannel('Ktory Preview');
  const diagnostics = vscode.languages.createDiagnosticCollection('ktory-preview');

  async function sendDocument() {
    if (!panel || !document || !ready) return;
    if (document.isClosed && document.uri.scheme === 'untitled') {
      vscode.window.showInformationMessage('The unsaved source document is closed. Open a .ktr document to start another preview.');
      return;
    }
    // Reopen by URI so a closed editor, Save As or external edit cannot leave a stale buffer.
    const sourceUri = document.uri;
    const targetPanel = panel;
    const targetRevision = ++revision;
    const latest = await vscode.workspace.openTextDocument(sourceUri);
    if (disposed || panel !== targetPanel || revision !== targetRevision || document.uri.toString() !== sourceUri.toString()) return;
    document = latest;
    diagnostics.clear();
    await panel.webview.postMessage({
      type: 'document', revision: targetRevision, uri: document.uri.toString(),
      name: path.posix.basename(document.uri.path), version: document.version,
      dirty: document.isDirty, text: document.getText()
    });
  }

  async function openPreview(uri) {
    const candidate = uri || vscode.window.activeTextEditor?.document.uri || document?.uri;
    if (!candidate) { vscode.window.showInformationMessage('Open a .ktr file before starting Ktory Preview.'); return; }
    const next = await vscode.workspace.openTextDocument(candidate);
    if (next.languageId !== 'ktory' && !/\.(ktr|ktory)$/i.test(next.uri.path)) {
      vscode.window.showInformationMessage('Ktory Preview reads .ktr and .ktory documents.'); return;
    }
    document = next;
    if (panel) {
      panel.reveal(vscode.ViewColumn.Beside, true);
      await sendDocument();
      return;
    }
    panel = vscode.window.createWebviewPanel('ktory.preview', 'Ktory Preview',
      { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true }, {
        enableScripts: true, retainContextWhenHidden: true,
        localResourceRoots: [context.extensionUri], enableCommandUris: false
      });
    const thisPanel = panel;
    const receiver = thisPanel.webview.onDidReceiveMessage(async message => {
      if (panel !== thisPanel || !message || typeof message.type !== 'string') return;
      if (message.type === 'log') {
        output.appendLine(String(message.message));
        console.log(`[Ktory Preview] ${message.message}`);
      } else if (message.type === 'ready') {
        ready = true;
        await sendDocument();
      } else if (message.type === 'reload') {
        await sendDocument();
      } else if (message.type === 'reveal' && document) {
        await vscode.window.showTextDocument(document, { viewColumn: vscode.ViewColumn.One });
      } else if (message.type === 'state' || message.type === 'error') {
        if (message.revision !== revision && !(message.type === 'error' && !ready)) return;
        lastEvent = message;
        if (message.type === 'error' && document) {
          // Only attach a diagnostic to the exact version that was read.
          if (message.version !== document.version) return;
          const match = String(message.message).match(/Line: (\d+), Column: (\d+)/);
          if (match) {
            const line = Math.max(0, Math.min(document.lineCount - 1, Number(match[1]) - 1));
            const start = new vscode.Position(line, Math.max(0, Number(match[2]) - 1));
            const diagnostic = new vscode.Diagnostic(new vscode.Range(start, document.lineAt(line).range.end),
              String(message.message), vscode.DiagnosticSeverity.Error);
            diagnostic.source = 'Ktory Core';
            diagnostics.set(document.uri, [diagnostic]);
          }
        }
      }
    });
    thisPanel.onDidDispose(() => {
      receiver.dispose();
      if (panel === thisPanel) {
        panel = undefined; ready = false; lastEvent = undefined; revision++;
        if (!disposed) diagnostics.clear();
      }
    });
    thisPanel.webview.html = previewHtml(thisPanel.webview, context.extensionUri);
  }

  context.subscriptions.push(
    vscode.commands.registerCommand('ktory.openPreview', openPreview),
    vscode.commands.registerCommand('ktory.reloadPreview', () => panel ? sendDocument() : openPreview()),
    vscode.workspace.onDidChangeTextDocument(event => {
      if (document?.uri.toString() !== event.document.uri.toString() || !event.contentChanges.length) return;
      document = event.document;
      diagnostics.delete(document.uri);
      panel?.webview.postMessage({ type: 'changed', version: document.version });
    }),
    { dispose: () => { disposed = true; panel?.dispose(); diagnostics.dispose(); output.dispose(); } }
  );
  // The integration suite uses the same message channel as the actual panel.
  return { get panel() { return panel; }, get lastEvent() { return lastEvent; }, diagnostics };
}

module.exports = { activate, previewHtml };
