# Changelog

## 0.2.0

- Graduate extension from pre-release to stable channel.
- Add integrated offline linter check powered directly by the Core parser and validator via headless WebAssembly, requiring zero external .NET SDK dependencies.
- Report syntax errors and fatal semantic errors (invalid control flow, unresolved targets, option jump conflicts, forbidden markdown, dangling decorators) as real-time in-editor **Errors**.
- Introduce narrative script static quality analysis reporting **Warnings**.
- Native VS Code Problems panel integration with inline squiggles, diagnostic codes, and immediate recovery on edit.

## 0.1.3

- Synchronize offline WASM reader runtime and asset bundles for the VS Code preview.
- Update sample script character declaration naming conventions.

## 0.1.2

- Support multi-language UI in the VS Code extension for English, Simplified Chinese, and Japanese (`en`, `zh-cn`, `ja`).
- Decouple extension UI language from script dialogue language: UI language is strictly controlled via `ktory.preview.uiLanguage`, while dialogue language is switched in the preview toolbar.
- Streamline preview toolbar: remove entry section selector to default to the story start, and remove redundant version/disclaimer text.
- Rebuild toolbar layout: filename badge with dirty status indicator on the far left, script language dropdown on the left, and reload button on the right.
- Redesign toolbar visual styling with modern translucent capsule containers, vector icons, hover transitions, and VS Code theme integration.
- Add multilingual README documentation (`README.zh-CN.md` and `README.ja.md`).

## 0.1.1

- Support file-scoped speaker default decorators and whole-group per-dialogue overrides through the shared core.
- Unify portal, landing page, Reader and VS Code highlighting; distinguish file settings, speaker declarations, speaker identities, locales, anchors and decorators.
- Highlight anonymous `#` anchors and keep dots inside quoted filenames as strings.
- Add offline syntax highlighting to the Reader source editor.
- Add `package:patch` for local patch version increments after successful VSIX generation.


## 0.1.0 (Pre-release)

- Package the existing TextMate grammar as a VS Code language contribution; fix indented locale headers being mistaken for speakers.
- Add an offline WebAssembly Reader panel using the shared Ktory C# core and frontend.
- Read unsaved editor buffers, select entry sections, switch languages, reload explicitly and report positioned parse errors.
- Preserve choice boundaries and session isolation; reject executable/resource-loading HTML in editor previews.
- Adapt the preview toolbar to narrow panels and switch languages with one dropdown.
- Highlight speaker declarations with explicit IDs, including `@speaker alice:`.
- Publish under the `ktory` publisher with extension ID `ktory.ktory`.
- Include a proprietary license permitting commercial use of the extension while restricting modification and redistribution of the extension itself.
