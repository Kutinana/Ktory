---
title: VS Code highlighting and preview
description: Install the VSIX and read the current Ktory document offline inside the editor
sidebar:
  order: 3
---

This page follows the [Ktory design principles](/en/01-overview/03-design-principles/). The extension uses the same C# core and Reader frontend, without another interpreter.

## Install and open

Use VS Code 1.97 or newer. Run **Extensions: Install from VSIX…**, then select the built `ktory-vscode-<version>.vsix`. Open a `.ktr` or `.ktory` file and click the preview icon in the editor title, or run **Ktory: Open Preview to the Side**.

The VSIX bundles the WebAssembly runtime. Reading needs no .NET installation, Unity, local server or online Reader. A local VSIX build does not imply a Marketplace release.

## Write and read

- Click the reading area or press Space/Enter to reveal and advance; use choice buttons or number keys to select a branch.
- Select an entry section or change the language. The core applies its default-language fallback rules.
- After editing, click **重新载入编辑器内容** or run **Ktory: Reload Preview from Editor**. Unsaved edits are included; reloading starts a new session from the entry.
- Typing does not automatically restart reading. The panel indicates when its snapshot is outdated. RESTART replays the loaded snapshot.
- Errors with source positions appear in the panel and Problems list; editing clears stale diagnostics.

The preview retains the Reader's typewriter and basic timing policies. Unknown external choice conditions are ignored and unknown presentation handlers skipped. It does not validate Unity resources, world state or presentation effects. Editor previews preserve basic formatting and Ruby while stripping executable HTML, resource-loading elements and arbitrary HTML attributes.

## Development and distribution

From `src/Ktory.VSCode`, run `pnpm install --frozen-lockfile`, `pnpm build`, `pnpm test`, `pnpm test:integration` and `pnpm package`. Building needs .NET SDK 9, Node.js 20+ and pnpm 10; end users do not need these tools.

The extension maintains the single TextMate grammar, imported directly by the portal. Generated `reader/` assets and VSIX files are not separate source trees. See the [extension README](https://github.com/Kutinana/Ktory/blob/main/src/Ktory.VSCode/README.md) for details.
