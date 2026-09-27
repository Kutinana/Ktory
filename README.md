<p align="center">
  <img src=".github/assets/ktory_logo.webp" alt="Ktory Logo" width="180" style="border-radius: 50%; box-shadow: 0 8px 32px rgba(0, 0, 0, 0.35);" />
</p>

<h1 align="center">Ktory</h1>

<p align="center">
  Narrative authoring and integration for dialogue-driven games.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-netstandard2.1%20%7C%20net9.0-512BD4?logo=dotnet" alt=".NET" />
  <img src="https://img.shields.io/badge/Unity-UPM%20Compatible-000000?logo=unity" alt="Unity UPM" />
  <a href="https://marketplace.visualstudio.com/items?itemName=ktory.ktory"><img src="https://img.shields.io/badge/VS_Code-Ktory_Extension-blue" alt="VS Code Ktory Extension" /></a>
  <a href="https://open-vsx.org/extension/ktory/ktory"><img src="https://img.shields.io/badge/Open_VSX-Ktory_Extension-purple" alt="Open VSX Ktory Extension" /></a>
</p>

---

Ktory aims to keep text and story flow together in `.ktr`, reducing repeated presentation settings and scene-specific integration code. The recommended workflow keeps dialogue order, choices and branches in the script while the host provides rendering, world state and actual event detection.

- [Try Ktory Web Reader](https://reader.ktory.ink/)

The repository contains the shared core (`src/Ktory.Core`), local and WASM Reader hosts (`src/Ktory.Runner`, `src/Ktory.Web`), Unity package sources (`src/Ktory.Unity`), the VS Code extension (`src/Ktory.VSCode`), and the portal (`website`). The extension bundles the same WASM core and Reader frontend for offline use; its single TextMate grammar is also used by the portal, landing page and Reader source editor. Generated packages and deployed sites are distribution artifacts, not independently maintained core implementations.

## Installation

### VS Code

If you use Visual Studio Code or other derived versions (including Cursor, Antigravity IDE) as IDE, you can search for Ktory in the marketplace directly for installation.

### Unity

Package Manager -> Add package from Git URL...

```bash
https://github.com/Kutinana/Ktory.git#upm
```

For reproducible integration, pin a generated package commit or immutable release tag and record its source commit; the main branch's Core directory is not itself a UPM package.
