import fs from 'node:fs';
import { createRequire } from 'node:module';
import tm from 'vscode-textmate';
import onig from 'vscode-oniguruma';
import grammar from '../../../src/Ktory.VSCode/syntaxes/ktory.tmLanguage.json';
import { createKtoryHighlighter } from '../../../src/Ktory.Highlighting/renderer.mjs';

const require = createRequire(import.meta.url);
const wasm = fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm'));
await onig.loadWASM(wasm.buffer.slice(wasm.byteOffset, wasm.byteOffset + wasm.byteLength));
const highlighter = await createKtoryHighlighter(tm, onig, grammar);
export const highlightKtoryLines = (source: string): string[] => highlighter.highlightLines(source);
