# Ktory for VS Code

[English](README.md) | [简体中文](README.zh-CN.md) | [日本語](README.ja.md)

`.ktr` syntax highlighting and an offline reading panel powered by the Ktory C# core.

## What is Ktory

Ktory is an authoring and integration system for dialogue-driven games.

[Visit Ktory's website](https://ktory.ink) for more information.

## Install and read

1. Install the extension from marketplace.
2. Open a `.ktr` file and click **Open Preview to the Side** in the editor title, or run **Ktory: Open Preview to the Side**. The Explorer context menu also supports this command.
3. Click the reading area or press Space/Enter to reveal text and advance. Select choices using their buttons or number keys. The Reader also supports its existing AUTO, fast-forward and restart controls.
4. Use the script language dropdown in the preview toolbar to switch the dialogue language immediately. It includes zh/en/ja and language tags found in the loaded script. Missing translations follow the core's fallback rules.
5. After editing, click **Reload** or run **Ktory: Reload Preview from Editor**. This reads the latest editor buffer, including unsaved edits, and starts a new session. Typing does not silently restart a running preview. RESTART replays the loaded snapshot.

The extension's UI language (toolbar, statuses, notices) is configured strictly via VS Code's `ktory.preview.uiLanguage` setting (supports `auto` to match VS Code, `zh-cn`, `en`, and `ja`), completely separate from the script dialogue language in the preview toolbar.

One preview panel follows the document explicitly opened in it. Switching editor tabs does not silently replace the story. Closing the panel releases its runtime; reopening starts a new session. Parse errors appear in the panel and, when the core supplies a source location, VS Code's Problems list. Diagnostics refer to the version actually loaded and clear when that document changes.

The VSIX includes the .NET WebAssembly runtime, Core, bridge and Reader assets. End users do not need .NET, Unity, a local server or an online Reader. No script is uploaded. The extension currently targets desktop VS Code, including its normal remote extension host; `vscode.dev` is not a validated target.

The preview ignores unknown external choice conditions and skips unknown presentation handlers. It supports basic rich text and Ruby; executable HTML, resource-loading elements and arbitrary HTML attributes are removed inside the editor preview. It does not certify Unity rendering, resource bindings or game-state behavior. Syntax highlighting is a TextMate grammar, not a semantic validator or language server.

## License

This extension is proprietary software. The [license](LICENSE.txt) permits personal and commercial use, including authoring scripts for commercial games. Modification and redistribution of the extension require separate written permission. Your own scripts and other original content are not subject to these restrictions. Bundled third-party components retain their own licenses; see `reader/notices/` in the installed extension. This license does not grant redistribution rights for standalone Ktory Core or Unity runtime packages.

## Troubleshooting

If the offline runtime fails to load, inspect **Output → Ktory Preview**, then close and reopen the panel. If the package is missing runtime files, rebuild and reinstall the complete VSIX. The syntax contribution still works independently of the reading panel.