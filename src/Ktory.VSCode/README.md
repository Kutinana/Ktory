# Ktory for VS Code

`.ktr` / `.ktory` syntax highlighting and an offline reading panel powered by the same C# core as Ktory's Web Reader. Ktory is a dialogue-first, host-driven narrative language and runtime; the preview does not simulate your game's world state or presentation resources.

## Install and read

1. In VS Code 1.97 or newer, run **Extensions: Install from VSIX…** and select `ktory-vscode-<version>.vsix`.
2. Open a `.ktr` file and click **Open Preview to the Side** in the editor title, or run **Ktory: Open Preview to the Side**. The Explorer context menu also supports this command.
3. Click the reading area or press Space/Enter to reveal text and advance. Select choices using their buttons or number keys. The Reader also supports its existing AUTO, fast-forward and restart controls.
4. Choose a named entry section, or use the language dropdown beside the globe icon to switch immediately. It includes zh/en/ja and language tags found in the loaded script. Missing translations follow the core's fallback rules.
5. After editing, click **重新载入** or run **Ktory: Reload Preview from Editor**. This reads the latest editor buffer, including unsaved edits, and starts a new session. Typing does not silently restart a running preview. RESTART replays the loaded snapshot.

One preview panel follows the document explicitly opened in it. Switching editor tabs does not silently replace the story. Closing the panel releases its runtime; reopening starts a new session. Parse errors appear in the panel and, when the core supplies a source location, VS Code's Problems list. Diagnostics refer to the version actually loaded and clear when that document changes.

The VSIX includes the .NET WebAssembly runtime, Core, bridge and Reader assets. End users do not need .NET, Unity, a local server or an online Reader. No script is uploaded. The extension currently targets desktop VS Code, including its normal remote extension host; `vscode.dev` is not a validated target.

The preview ignores unknown external choice conditions and skips unknown presentation handlers. It supports basic rich text and Ruby; executable HTML, resource-loading elements and arbitrary HTML attributes are removed inside the editor preview. It does not certify Unity rendering, resource bindings or game-state behavior. Syntax highlighting is a TextMate grammar, not a semantic validator or language server.

## Development

Build, verification, packaging and maintenance instructions are maintained in the [repository workflow](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-workflow.md#vs-code-扩展开发与验证).

## Troubleshooting

If the offline runtime fails to load, inspect **Output → Ktory Preview**, then close and reopen the panel. If the package is missing runtime files, rebuild and reinstall the complete VSIX. The syntax contribution still works independently of the reading panel.
