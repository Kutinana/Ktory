# Ktory for VS Code

`.ktr` / `.ktory` syntax highlighting and an offline reading panel powered by the same C# core as Ktory's Web Reader. Ktory is an authoring and integration system for dialogue-driven games; its current runtime uses host-driven beats. File-scoped speaker default decorators and per-line overrides are supported. Resource bindings and explicit signal dependencies remain pending. The preview does not simulate your game's world state or presentation resources; see the [design charter](../../docs/ktory-design-charter.md).

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

Run `pnpm package` to build a VSIX in `artifacts/vscode/packages/` at the repository root. The complete development extension is generated in `artifacts/vscode/extension/`; source files stay in this directory. `pnpm clean` removes all extension build outputs and packages.

Build, verification, packaging and maintenance instructions are maintained in the [repository workflow](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-workflow.md#vs-code-扩展开发与验证).

## License

This extension is proprietary software. The [license](LICENSE.txt) permits personal and commercial use, including authoring scripts for commercial games. Modification and redistribution of the extension require separate written permission. Your own scripts and other original content are not subject to these restrictions. Bundled third-party components retain their own licenses; see `reader/notices/` in the installed extension. This license does not grant redistribution rights for standalone Ktory Core or Unity runtime packages.

## Troubleshooting

If the offline runtime fails to load, inspect **Output → Ktory Preview**, then close and reopen the panel. If the package is missing runtime files, rebuild and reinstall the complete VSIX. The syntax contribution still works independently of the reading panel.

## Local patch packages

Run `pnpm --dir src/Ktory.VSCode package:patch` from the repository root. It builds the shared Reader and creates `artifacts/vscode/packages/ktory-vscode-<version>.vsix`, then updates the source `package.json` patch version only after packaging succeeds. Existing VSIX versions in the same major/minor line are skipped; a lock prevents simultaneous patch builds. A failed build keeps the source version unchanged. If the process crashes, check that no build is running before removing the reported lock file. Major/minor changes remain explicit edits to the source manifest.

`pnpm package` still builds the current version without incrementing it. Packages retain the existing pre-release channel. Neither command uploads to Marketplace or Open VSX. Automatic market publishing is deferred by the current decision; the generated VSIX can be uploaded to both markets.

## One highlighting grammar

`syntaxes/ktory.tmLanguage.json` is the only lexical rule source: VS Code consumes it natively, the documentation site imports it through Shiki, and the landing page and Reader use TextMate/Oniguruma with the same grammar. `../Ktory.Highlighting` only maps token scopes to HTML/colors. Reader assets, engines and license notices are copied during .NET builds and ship offline in the VSIX. Install extension dependencies before building either Reader host: `pnpm --dir src/Ktory.VSCode install --frozen-lockfile`.
