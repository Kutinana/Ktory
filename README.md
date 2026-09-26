<p align="center">
  <img src=".github/assets/ktory_logo.webp" alt="Ktory Logo" width="180" style="border-radius: 50%; box-shadow: 0 8px 32px rgba(0, 0, 0, 0.35);" />
</p>

<h1 align="center">Ktory</h1>

<p align="center">
  A dialogue-first, host-driven narrative language and runtime, built around explicit beats.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-netstandard2.1%20%7C%20net9.0-512BD4?logo=dotnet" alt=".NET" />
  <img src="https://img.shields.io/badge/Unity-UPM%20Compatible-000000?logo=unity" alt="Unity UPM" />
  <img src="https://img.shields.io/badge/Core_Dependencies-Zero%20External-success" alt="Core: Zero External Dependencies" />
  <img src="https://img.shields.io/badge/Architecture-Discrete%20Step%20Driven-blue" alt="Architecture" />
</p>

---

Ktory combines structured dialogue and action beats, parameterized decorators, and translations in the same script. The C# core owns story flow; the host owns rendering, timing, world state, and external presentation gates.

Ink already supports host-driven continuation, choices, tags, external functions, and Unity integration. Ktory aims to standardize a more specific dialogue and localization workflow, reducing project-specific conventions. This is a design goal to validate in actual use, not a claim of capabilities that Ink cannot provide.

- [Try Ktory Web Reader](https://ktory.vercel.app)
- [VS Code syntax highlighting and offline preview](src/Ktory.VSCode/README.md)

The repository contains the shared core (`src/Ktory.Core`), local and WASM Reader hosts (`src/Ktory.Runner`, `src/Ktory.Web`), Unity package sources (`src/Ktory.Unity`), the VS Code extension (`src/Ktory.VSCode`), and the portal (`website`). The extension bundles the same WASM core and Reader frontend for offline use; its TextMate grammar is also used by the portal. Generated packages and deployed sites are distribution artifacts, not independently maintained core implementations.

Unity's generated package is `com.ktory.unity`, available through `https://github.com/Kutinana/Ktory.git#upm`. For reproducible integration, pin a generated package commit or immutable release tag and record its source commit; the main branch's Core directory is not itself a UPM package.

The Unity **Window → Ktory → Debugging** window observes registered live host sessions in Play Mode: source positions, input gates and timers, language refresh, bounded execution logs, choices and control flow. Existing players connect through a small Editor-only adapter; project presentation state remains project-owned. See [integration and Unity validation](docs/ktory-unity-debugging.md).

## Development

See the [design charter](docs/ktory-design-charter.md), [language specification](docs/ktory-implementation_v1.md), and [repository workflow](docs/ktory-workflow.md) for implementation, verification and distribution details.
