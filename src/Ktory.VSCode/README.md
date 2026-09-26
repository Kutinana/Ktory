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

## Build and verify from this repository

Developers need .NET SDK 9, Node.js 20+ and pnpm 10. From this directory:

```sh
pnpm install --frozen-lockfile
pnpm build
pnpm test
pnpm test:integration
pnpm package
pnpm test:vsix
```

`test:integration` uses a separate VS Code profile and extensions directory. `test:vsix` installs the generated VSIX into another isolated profile and runs the same suite against the installed files. On macOS these commands detect the standard application path; elsewhere set `VSCODE_EXECUTABLE_PATH` to the VS Code executable (and `VSCODE_CLI_PATH` when the CLI is separate). Linux CI needs a display such as `xvfb-run`.

The `VS Code - Build VSIX` workflow uploads `ktory-vscode-vsix-<source-commit>`; extract its VSIX to install the extension. `package` builds and writes the VSIX to the repository's `artifacts/vscode` directory; it does not publish to the Marketplace. The repository does not currently declare a distribution license, so this local package remains `UNLICENSED`; packaging does not choose a project license or establish a Marketplace publisher account.

The extension manifest’s `version` in `package.json` is the single version source. `scripts/package.cjs` derives `ktory-vscode-<version>.vsix` from it, and `test:vsix` reads that same path. Ordinary builds do not increment versions; update the manifest version deliberately for a new release. The commit suffix on the Actions download identifies a build, not an extension version.

## Maintenance boundaries

- `syntaxes/ktory.tmLanguage.json` is the single maintained highlighting grammar. The portal imports it directly.
- `reader/` is generated from `src/Ktory.Web`, which references `src/Ktory.Core` and the shared frontend in `src/Ktory.Runner/wwwroot`. Never edit the generated runtime or fork the interpreter here.
- `extension.js` manages VS Code documents and panel lifecycle. `webview.js` adapts the shared Reader to editor messages and local resource URLs. Narrative execution remains in Core; time and input remain in the Reader.
- `reader/build-info.json` records the source commit, dirty flag and build time. A dirty build is a local candidate, not evidence of a clean release from that commit.
- Product guarantees follow the [design charter](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-design-charter.md); concrete language behavior follows the implementation specification.

## Troubleshooting

If the offline runtime fails to load, inspect **Output → Ktory Preview**, then close and reopen the panel. If the package is missing runtime files, rebuild and reinstall the complete VSIX. The syntax contribution still works independently of the reading panel.
