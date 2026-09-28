let vscode;
try {
  vscode = require('vscode');
} catch {
  // Allow linter core logic to be tested in standard Node.js without vscode environment
}
const path = require('node:path');
const { pathToFileURL } = require('node:url');

let runtimePromise = null;
let lintBridge = null;

async function getLintBridge(frameworkDir) {
  if (lintBridge) return lintBridge;
  if (!runtimePromise) {
    runtimePromise = (async () => {
      const dotnetJsPath = path.join(frameworkDir, 'dotnet.js');
      const configPath = path.join(frameworkDir, 'blazor.boot.json');
      const { dotnet } = await import(pathToFileURL(dotnetJsPath).href);
      const runtime = await dotnet.withConfigSrc(configPath).create();
      const exports = await runtime.getAssemblyExports('Ktory.Wasm');
      return exports.Ktory.Wasm.KtoryLinterBridge;
    })();
  }
  lintBridge = await runtimePromise;
  return lintBridge;
}

async function lintScript(script, frameworkDir) {
  const bridge = await getLintBridge(frameworkDir);
  const json = bridge.Lint(script);
  return JSON.parse(json);
}

function toVsDiagnostic(item, document) {
  const line = Math.max(0, Math.min(document.lineCount - 1, (item.line || 1) - 1));
  const lineText = document.lineAt(line).text;
  const col = Math.max(0, Math.min(lineText.length, (item.column || 1) - 1));
  const endLine = Math.max(line, Math.min(document.lineCount - 1, (item.endLine || item.line || 1) - 1));
  const endLineText = document.lineAt(endLine).text;
  const endCol = item.endColumn > 0
    ? Math.max(col, Math.min(endLineText.length, item.endColumn - 1))
    : endLineText.length;

  const range = new vscode.Range(new vscode.Position(line, col), new vscode.Position(endLine, Math.max(col + 1, endCol)));

  let severity = vscode.DiagnosticSeverity.Error;
  if (item.severity === 2) severity = vscode.DiagnosticSeverity.Warning;
  else if (item.severity === 3) severity = vscode.DiagnosticSeverity.Information;
  else if (item.severity === 4) severity = vscode.DiagnosticSeverity.Hint;

  const diagnostic = new vscode.Diagnostic(range, item.message, severity);
  if (item.code) diagnostic.code = item.code;
  diagnostic.source = 'Ktory';
  return diagnostic;
}

function registerLinter(context, output, collectionName = 'ktory') {
  const diagnostics = vscode.languages.createDiagnosticCollection(collectionName);
  const frameworkDir = path.join(context.extensionUri.fsPath, 'reader', '_framework');
  const debounceTimers = new Map();

  function isKtoryDocument(doc) {
    if (!doc) return false;
    return doc.languageId === 'ktory' || /\.(ktr|ktory)$/i.test(doc.uri.path);
  }

  async function runLint(doc) {
    if (!isKtoryDocument(doc)) return;
    try {
      const items = await lintScript(doc.getText(), frameworkDir);
      if (doc.isClosed) {
        diagnostics.delete(doc.uri);
        return;
      }
      const vsDiagnostics = items.map(item => toVsDiagnostic(item, doc));
      diagnostics.set(doc.uri, vsDiagnostics);
    } catch (err) {
      if (output) {
        output.appendLine(`[Ktory Linter] Error analyzing ${doc.uri.fsPath}: ${err.message || err}`);
      }
    }
  }

  function scheduleLint(doc, delay = 300) {
    if (!isKtoryDocument(doc)) return;
    const uriStr = doc.uri.toString();
    if (debounceTimers.has(uriStr)) {
      clearTimeout(debounceTimers.get(uriStr));
    }
    if (delay <= 0) {
      debounceTimers.delete(uriStr);
      runLint(doc);
      return;
    }
    const timer = setTimeout(() => {
      debounceTimers.delete(uriStr);
      runLint(doc);
    }, delay);
    debounceTimers.set(uriStr, timer);
  }

  // Trigger on currently open documents
  if (Array.isArray(vscode.workspace.textDocuments)) {
    vscode.workspace.textDocuments.forEach(doc => {
      if (isKtoryDocument(doc)) scheduleLint(doc, 0);
    });
  }

  context.subscriptions.push(
    diagnostics,
    vscode.workspace.onDidOpenTextDocument(doc => scheduleLint(doc, 0)),
    vscode.workspace.onDidChangeTextDocument(event => {
      if (isKtoryDocument(event.document) && event.contentChanges.length) {
        scheduleLint(event.document, 300);
      }
    }),
    vscode.workspace.onDidSaveTextDocument(doc => scheduleLint(doc, 0)),
    vscode.workspace.onDidCloseTextDocument(doc => {
      const uriStr = doc.uri.toString();
      if (debounceTimers.has(uriStr)) {
        clearTimeout(debounceTimers.get(uriStr));
        debounceTimers.delete(uriStr);
      }
      diagnostics.delete(doc.uri);
    })
  );

  return { diagnostics, runLint, scheduleLint };
}

module.exports = {
  getLintBridge,
  lintScript,
  toVsDiagnostic,
  registerLinter
};
