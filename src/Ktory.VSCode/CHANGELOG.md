# Changelog

## 0.1.0 (Pre-release)

- Package the existing TextMate grammar as a VS Code language contribution; fix indented locale headers being mistaken for speakers.
- Add an offline WebAssembly Reader panel using the shared Ktory C# core and frontend.
- Read unsaved editor buffers, select entry sections, switch languages, reload explicitly and report positioned parse errors.
- Preserve choice boundaries and session isolation; reject executable/resource-loading HTML in editor previews.
- Adapt the preview toolbar to narrow panels and switch languages with one dropdown.
- Highlight speaker declarations with explicit IDs, including `@speaker alice:`.
- Publish under the `ktory` publisher with extension ID `ktory.ktory`.
- Include a proprietary license permitting commercial use of the extension while restricting modification and redistribution of the extension itself.
